using AssetProcessor.Resources;
using AssetProcessor.Services.Models;
using AssetProcessor.ViewModels;
using CommunityToolkit.Mvvm.Input;
using NLog;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace AssetProcessor {
    /// <summary>
    /// Partial class containing texture processing and upload handlers:
    /// - Texture processing completion handlers
    /// - Texture preview loading
    /// - B2 upload functionality
    /// </summary>
    public partial class MainWindow {

        #region Central Control Box Handlers

        private async void ViewModel_TextureProcessingCompleted(object? sender, TextureProcessingCompletedEventArgs e) {
            try {
                await Dispatcher.InvokeAsync(() => {
                    TexturesDataGrid.Items.Refresh();
                    ProgressBar.Value = 0;
                    ProgressBar.Maximum = e.Result.SuccessCount + e.Result.ErrorCount;
                    ProgressTextBlock.Text = $"Completed: {e.Result.SuccessCount} success, {e.Result.ErrorCount} errors";
                });

                string resultMessage = BuildProcessingSummaryMessage(e.Result);
                MessageBoxImage icon = e.Result.ErrorCount == 0 && e.Result.SuccessCount > 0
                    ? MessageBoxImage.Information
                    : MessageBoxImage.Warning;

                // Показываем MessageBox в UI потоке
                await Dispatcher.InvokeAsync(() => {
                    MessageBox.Show(resultMessage, "Processing Complete", MessageBoxButton.OK, icon);
                });

                // Загружаем превью после закрытия MessageBox, чтобы не блокировать UI
                if (e.Result.PreviewTexture != null && viewModel.LoadKtxPreviewCommand is IAsyncRelayCommand<TextureResource?> command) {
                    try {
                        // Загружаем превью асинхронно (это уже async и не блокирует UI)
                        await command.ExecuteAsync(e.Result.PreviewTexture);

                        // Обновляем UI в UI потоке после загрузки
                        // Preview loading event will switch the viewer to KTX2 mode after the texture is loaded.
                    } catch (Exception ex) {
                        logger.Warn(ex, "Ошибка при загрузке превью KTX2");
                    }
                }
            } catch (Exception ex) {
                logger.Error(ex, "Ошибка при обработке завершения конвертации");
            }
        }

        private int _isLoadingTexture = 0; // 0 = false, 1 = true (используем int для Interlocked)

        private async void ViewModel_TexturePreviewLoaded(object? sender, TexturePreviewLoadedEventArgs e) {
            // Проверяем состояние и блокируем повторные загрузки
            // Используем CompareExchange для атомарной проверки и установки, чтобы избежать TOCTOU
            // Атомарно устанавливаем флаг в 1, если он был 0 (проверка+установка)
            int wasLoading = Interlocked.CompareExchange(ref _isLoadingTexture, 1, 0);
            if (wasLoading != 0) {
                logger.Warn("Texture loading already in progress, skipping duplicate load");
                // Важно: не сбрасываем флаг, так как другой поток уже загружает и сбросит
                // в своём finally блоке. Иначе можем, вернувшись до перехода, разрешить следующую загрузку.
                return;
            }

            try {

                // Обновляем UI свойства в UI потоке с помощью InvokeAsync
                bool rendererAvailable = false;
                await Dispatcher.InvokeAsync(() => {
                    texturePreviewService.CurrentLoadedTexturePath = e.Texture.Path;
                    texturePreviewService.CurrentLoadedKtx2Path = e.Preview.KtxPath;
                    texturePreviewService.IsKtxPreviewAvailable = true;
                    texturePreviewService.IsKtxPreviewActive = true;
                    texturePreviewService.CurrentKtxMipmaps?.Clear();
                    texturePreviewService.CurrentMipLevel = 0;

                    if (!texturePreviewService.IsUserPreviewSelection || texturePreviewService.CurrentPreviewSourceMode == TexturePreviewSourceMode.Ktx2) {
                        SetPreviewSourceMode(TexturePreviewSourceMode.Ktx2, initiatedByUser: false);
                    } else {
                        UpdatePreviewSourceControls();
                    }

                    // Проверяем доступность renderer в UI потоке
                    rendererAvailable = D3D11TextureViewer?.Renderer != null;
                    if (!rendererAvailable) {
                        logger.Warn("D3D11 viewer или renderer недоступен");
                    }
                });

                if (!rendererAvailable) {
                    return;
                }

                // Даём UI потоку возможность обработать другие сообщения перед загрузкой
                await Task.Yield();

                // Выполняем LoadTexture в UI потоке, но с низким приоритетом
                // чтобы не блокировать другие UI операции
                await Dispatcher.InvokeAsync(() => {
                    try {
                        if (D3D11TextureViewer?.Renderer == null) {
                            logger.Warn("D3D11 renderer стал null во время загрузки");
                            return;
                        }
                        D3D11TextureViewer.Renderer.LoadTexture(e.Preview.TextureData);
                    } catch (Exception ex) {
                        logger.Error(ex, "Ошибка при загрузке текстуры в D3D11");
                        return;
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);

                // Ещё раз даём UI потоку возможность обработать другие сообщения
                await Task.Yield();

                // Обновляем UI и выполняем Render
                await Dispatcher.InvokeAsync(() => {
                    try {
                        if (D3D11TextureViewer?.Renderer == null) {
                            logger.Warn("D3D11 renderer стал null во время обновления UI");
                            return;
                        }

                        UpdateHistogramCorrectionButtonState();

                        bool hasHistogram = D3D11TextureViewer.Renderer.HasHistogramMetadata();
                        if (TextureFormatTextBlock != null) {
                            string compressionFormat = e.Preview.TextureData.CompressionFormat ?? "Unknown";
                            string srgbInfo = compressionFormat.IndexOf("SRGB", StringComparison.OrdinalIgnoreCase) >= 0
                                ? " (sRGB)"
                                : compressionFormat.IndexOf("UNORM", StringComparison.OrdinalIgnoreCase) >= 0
                                    ? " (Linear)"
                                    : string.Empty;
                            string histInfo = hasHistogram ? " + Histogram" : string.Empty;
                            TextureFormatTextBlock.Text = $"Format: KTX2/{compressionFormat}{srgbInfo}{histInfo}";
                        }

                        D3D11TextureViewer.Renderer.Render();

                        if (e.Preview.ShouldEnableNormalReconstruction && D3D11TextureViewer.Renderer != null) {
                            texturePreviewService.CurrentActiveChannelMask = "Normal";
                            D3D11TextureViewer.Renderer.SetChannelMask(0x20);
                            D3D11TextureViewer.Renderer.Render();
                            UpdateChannelButtonsState();
                            if (!string.IsNullOrWhiteSpace(e.Preview.AutoEnableReason)) {
                                logService.LogInfo($"Auto-enabled Normal reconstruction mode for {e.Preview.AutoEnableReason}");
                            }
                        }
                    } catch (Exception ex) {
                        logger.Error(ex, "Ошибка при обновлении UI после загрузки текстуры");
                    }
                });
            } catch (Exception ex) {
                logger.Error(ex, "Ошибка при обработке превью KTX2");
            } finally {
                // Атомарно сбрасываем флаг
                Interlocked.Exchange(ref _isLoadingTexture, 0);
            }
        }

        private static string BuildProcessingSummaryMessage(TextureProcessingResult result) {
            var resultMessage = $"Processing completed!\n\nSuccess: {result.SuccessCount}\nErrors: {result.ErrorCount}";

            if (result.ErrorCount > 0 && result.ErrorMessages.Count > 0) {
                resultMessage += "\n\nError details:";
                var errorsToShow = result.ErrorMessages.Take(10).ToList();
                foreach (var error in errorsToShow) {
                    resultMessage += $"\n• {error}";
                }
                if (result.ErrorMessages.Count > 10) {
                    resultMessage += $"\n... and {result.ErrorMessages.Count - 10} more errors (see log file for details)";
                }
            } else if (result.SuccessCount > 0) {
                resultMessage += "\n\nConverted files saved next to source images.";
            }

            return resultMessage;
        }

        private async void UploadTexturesButton_Click(object sender, RoutedEventArgs e) {
            var projectName = ProjectName ?? "UnknownProject";
            var outputPath = Settings.AppSettings.Default.ProjectsFolderPath;

            if (string.IsNullOrEmpty(outputPath)) {
                MessageBox.Show(
                    "Не указана папка проектов. Откройте настройки и укажите Projects Folder Path.",
                    "Upload Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Проверяем настройки B2
            if (string.IsNullOrEmpty(Settings.AppSettings.Default.B2KeyId) ||
                string.IsNullOrEmpty(Settings.AppSettings.Default.B2BucketName)) {
                MessageBox.Show(
                    "Backblaze B2 credentials not configured. Go to Settings -> CDN/Upload to configure.",
                    "Upload Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Получаем текстуры для загрузки:
            // 1. Сначала пробуем выбранные в DataGrid
            // 2. Если ничего не выбрано - берём отмеченные для экспорта (ExportToServer = true)
            IEnumerable<TextureResource> texturesToUpload = TexturesDataGrid.SelectedItems.Cast<TextureResource>();

            if (!texturesToUpload.Any()) {
                // Используем текстуры, отмеченные для экспорта
                texturesToUpload = viewModel.Textures.Where(t => t.ExportToServer);
            }

            // Вычисляем путь к KTX2 из исходного Path (заменяем расширение на .ktx2)
            var selectedTextures = texturesToUpload
                .Where(t => !string.IsNullOrEmpty(t.Path))
                .Select(t => {
                    var sourceDir = System.IO.Path.GetDirectoryName(t.Path)!;
                    var fileName = System.IO.Path.GetFileNameWithoutExtension(t.Path);
                    var ktx2Path = System.IO.Path.Combine(sourceDir, fileName + ".ktx2");
                    return (Texture: t, Ktx2Path: ktx2Path);
                })
                .Where(x => System.IO.File.Exists(x.Ktx2Path))
                .ToList();

            if (!selectedTextures.Any()) {
                MessageBox.Show(
                    "No converted textures found.\n\nEither select textures in the list, or mark them for export (Mark Related), then process them to KTX2.",
                    "Upload Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try {
                UploadTexturesButton.IsEnabled = false;
                UploadTexturesButton.Content = "Uploading...";

                using var b2Service = new Upload.B2UploadService();
                using var uploadStateService = new Data.UploadStateService();
                var uploadCoordinator = new Services.AssetUploadCoordinator(b2Service, uploadStateService);

                var initialized = await uploadCoordinator.InitializeAsync();
                if (!initialized) {
                    MessageBox.Show(
                        "Failed to connect to Backblaze B2. Check your credentials in Settings.",
                        "Upload Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                // Подготавливаем список файлов для загрузки
                var files = selectedTextures
                    .Select(x => (LocalPath: x.Ktx2Path, RemotePath: $"{projectName}/textures/{System.IO.Path.GetFileName(x.Ktx2Path)}"))
                    .ToList();

                var result = await b2Service.UploadBatchAsync(
                    files,
                    progress: new Progress<Upload.B2UploadProgress>(p => {
                        Dispatcher.Invoke(() => {
                            ProgressBar.Value = p.PercentComplete;
                        });
                    })
                );

                // Обновляем статусы текстур и сохраняем в БД
                logger.Debug($"Results count: {result.Results.Count}, selectedTextures count: {selectedTextures.Count}");
                foreach (var item in selectedTextures) {
                    logger.Debug($"Looking for path: '{item.Ktx2Path}'");
                    foreach (var r in result.Results) {
                        logger.Debug($"  Result path: '{r.LocalPath}' Success={r.Success} Skipped={r.Skipped}");
                    }
                    var uploadResult = result.Results.FirstOrDefault(r =>
                        string.Equals(r.LocalPath, item.Ktx2Path, StringComparison.OrdinalIgnoreCase));
                    logger.Debug($"uploadResult found: {uploadResult != null}, Success: {uploadResult?.Success}");
                    if (uploadResult?.Success == true) {
                        item.Texture.UploadStatus = "Uploaded";
                        item.Texture.RemoteUrl = uploadResult.CdnUrl;
                        item.Texture.UploadedHash = uploadResult.ContentSha1;
                        item.Texture.LastUploadedAt = DateTime.UtcNow;

                        // Сохраняем в БД для персистентности между сессиями
                        var remotePath = $"{projectName}/textures/{System.IO.Path.GetFileName(item.Ktx2Path)}";
                        await uploadStateService.SaveUploadAsync(new Data.UploadRecord {
                            LocalPath = item.Ktx2Path,
                            RemotePath = remotePath,
                            ContentSha1 = uploadResult.ContentSha1 ?? "",
                            ContentLength = new System.IO.FileInfo(item.Ktx2Path).Length,
                            UploadedAt = DateTime.UtcNow,
                            CdnUrl = uploadResult.CdnUrl ?? "",
                            Status = "Uploaded",
                            FileId = uploadResult.FileId,
                            ProjectName = projectName
                        });
                    }
                }

                MessageBox.Show(
                    $"Upload completed!\n\n" +
                    $"Uploaded: {result.SuccessCount}\n" +
                    $"Skipped (already exists): {result.SkippedCount}\n" +
                    $"Failed: {result.FailedCount}\n" +
                    $"Duration: {result.Duration.TotalSeconds:F1}s",
                    "Upload Result",
                    MessageBoxButton.OK,
                    result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);

            } catch (Exception ex) {
                logger.Error(ex, "Texture upload failed");
                MessageBox.Show($"Upload failed: {ex.Message}", "Upload Error", MessageBoxButton.OK, MessageBoxImage.Error);
            } finally {
                UploadTexturesButton.IsEnabled = true;
                UploadTexturesButton.Content = "Upload";
                ProgressBar.Value = 0;
            }
        }

        /// <summary>
        /// Updates the unified export panel counts for all asset types
        /// </summary>
        private void UpdateExportCounts() {
            int markedModels = viewModel.Models.Count(m => m.ExportToServer);
            int markedMaterials = viewModel.Materials.Count(m => m.ExportToServer);
            int markedTextures = viewModel.Textures.Count(t => t.ExportToServer);

            MarkedModelsCountText.Text = markedModels.ToString();
            MarkedMaterialsCountText.Text = markedMaterials.ToString();
            MarkedTexturesCountText.Text = markedTextures.ToString();
        }

        #endregion
    }
}
