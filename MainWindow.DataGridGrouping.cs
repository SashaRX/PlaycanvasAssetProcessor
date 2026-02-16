using AssetProcessor.Resources;
using AssetProcessor.Settings;
using NLog;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

namespace AssetProcessor {
    /// <summary>
    /// Partial class containing DataGrid grouping and ORM texture management:
    /// - Texture grouping controls (Group/Collapse checkboxes)
    /// - ORM subgroup header click handling
    /// - ORM texture preview loading
    /// </summary>
    public partial class MainWindow {

        #region Column Visibility Management

        private void GroupTexturesCheckBox_Changed(object sender, RoutedEventArgs e) {
            AppSettings.Default.Save();
            if (TexturesDataGrid?.ItemsSource == null || viewModel?.Textures == null) return;
            ApplyTextureGroupingIfEnabled();
        }

        private void CollapseGroupsCheckBox_Changed(object sender, RoutedEventArgs e) {
            // Save the preference when changed
            AppSettings.Default.Save();
        }

        /// <summary>
        /// Applies texture grouping if the GroupTextures checkbox is checked.
        /// Called after loading assets to apply default grouping.
        /// </summary>
        private void ApplyTextureGroupingIfEnabled() {
            var view = CollectionViewSource.GetDefaultView(TexturesDataGrid.ItemsSource);
            if (view == null || !view.CanGroup) return;

            if (Settings.AppSettings.Default.GroupTexturesByType) {
                using (view.DeferRefresh()) {
                    view.GroupDescriptions.Clear();
                    view.GroupDescriptions.Add(new PropertyGroupDescription("GroupName"));
                    view.GroupDescriptions.Add(new PropertyGroupDescription("SubGroupName"));
                }
            } else {
                if (view.GroupDescriptions.Count > 0) {
                    view.GroupDescriptions.Clear();
                }
            }
        }

        /// <summary>
        /// Обработчик клика на заголовок ORM подгруппы - показывает настройки ORM
        /// </summary>
        private void ORMSubGroupHeader_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            // Останавливаем событие чтобы Expander не сворачивался
            e.Handled = true;

            if (sender is FrameworkElement element && element.DataContext is CollectionViewGroup group) {
                // Получаем имя подгруппы (это имя ORM текстуры)
                string? subGroupName = group.Name?.ToString();
                if (string.IsNullOrEmpty(subGroupName)) return;

                // Ищем любую текстуру из этой подгруппы чтобы получить ParentORMTexture
                var textureInGroup = viewModel.Textures.FirstOrDefault(t => t.SubGroupName == subGroupName);
                if (textureInGroup?.ParentORMTexture != null) {
                    var ormTexture = textureInGroup.ParentORMTexture;
                    logService.LogInfo($"ORM subgroup clicked: {ormTexture.Name} ({ormTexture.PackingMode})");

                    // Устанавливаем выбранную подгруппу для визуального выделения
                    SelectedORMSubGroupName = subGroupName;

                    // Снимаем выделение с обычных строк DataGrid
                    TexturesDataGrid.SelectedItem = null;

                    // Показываем ORM панель настроек (как при выборе ORM в DataGrid)
                    viewModel.IsConversionSettingsVisible = false;
                    viewModel.IsORMPanelVisible = true;

                    // Инициализируем ORM панель с доступными текстурами (исключаем ORM текстуры)
                    var availableTextures = viewModel.Textures.Where(t => !(t is ORMTextureResource)).ToList();
                    ORMPanel.Initialize(this, availableTextures);
                    ORMPanel.SetORMTexture(ormTexture);

                    // Обновляем информацию о текстуре в preview панели
                    viewModel.TextureInfoName = "Texture Name: " + ormTexture.Name;
                    viewModel.TextureInfoColorSpace = "Color Space: Linear (ORM)";

                    // Если ORM уже упакована - загружаем preview
                    if (!string.IsNullOrEmpty(ormTexture.Path) && File.Exists(ormTexture.Path)) {
                        viewModel.TextureInfoResolution = ormTexture.Resolution != null && ormTexture.Resolution.Length >= 2
                            ? $"Resolution: {ormTexture.Resolution[0]}x{ormTexture.Resolution[1]}"
                            : "Resolution: Unknown";
                        viewModel.TextureInfoFormat = "Format: KTX2 (packed)";

                        // Загружаем preview асинхронно
                        _ = LoadORMPreviewAsync(ormTexture);
                    } else {
                        viewModel.TextureInfoResolution = "Resolution: Not packed yet";
                        viewModel.TextureInfoFormat = "Format: Not packed";
                        ResetPreviewState();
                        ClearD3D11Viewer();
                    }

                    // Обновляем ViewModel.SelectedTexture
                    viewModel.SelectedTexture = ormTexture;
                }
            }
        }

        /// <summary>
        /// Загружает preview для упакованной ORM текстуры
        /// </summary>
        private async Task LoadORMPreviewAsync(ORMTextureResource ormTexture) {
            // Cancel any pending texture load
            textureLoadCancellation?.Cancel();
            textureLoadCancellation = new CancellationTokenSource();
            var cancellationToken = textureLoadCancellation.Token;

            try {
                bool ktxLoaded = false;

                if (texturePreviewService.IsUsingD3D11Renderer) {
                    // D3D11 MODE: Try native KTX2 loading
                    logger.Info($"[LoadORMPreviewAsync] Loading packed ORM to D3D11: {ormTexture.Name}");
                    ktxLoaded = await TryLoadKtx2ToD3D11Async(ormTexture, cancellationToken);

                    if (ktxLoaded && !cancellationToken.IsCancellationRequested) {
                        // Extract mip0 bitmap for histogram calculation (fire-and-forget with timeout)
                        string? ormPath = ormTexture.Path;
                        string ormName = ormTexture.Name ?? "unknown";
                        logger.Info($"[LoadORMPreviewAsync] Starting histogram extraction for: {ormName}, path: {ormPath}");

                        // Small delay to let LoadTexture complete first (prevents concurrent execution issues)
                        _ = Task.Run(async () => {
                            try {
                                // Wait for LoadTexture to complete (queued via BeginInvoke)
                                await Task.Delay(200, cancellationToken).ConfigureAwait(false);

                                if (string.IsNullOrEmpty(ormPath)) {
                                    logger.Warn($"[LoadORMPreviewAsync] ORM path is empty for: {ormName}");
                                    return;
                                }

                                logger.Info($"[LoadORMPreviewAsync] Extracting mipmaps from: {ormPath}");

                                // Add timeout to prevent hanging
                                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                                var mipmaps = await texturePreviewService.LoadKtx2MipmapsAsync(ormPath, linkedCts.Token).ConfigureAwait(false);
                                logger.Info($"[LoadORMPreviewAsync] Extracted {mipmaps.Count} mipmaps for: {ormName}");

                                if (mipmaps.Count > 0 && !linkedCts.Token.IsCancellationRequested) {
                                    var mip0Bitmap = mipmaps[0].Bitmap;
                                    logger.Info($"[LoadORMPreviewAsync] Got mip0 bitmap {mip0Bitmap.PixelWidth}x{mip0Bitmap.PixelHeight} for: {ormName}");

                                    // Use BeginInvoke to avoid deadlock
                                    _ = Dispatcher.BeginInvoke(new Action(() => {
                                        if (!cancellationToken.IsCancellationRequested) {
                                            texturePreviewService.OriginalFileBitmapSource = mip0Bitmap;
                                            UpdateHistogram(mip0Bitmap);
                                            logger.Info($"[LoadORMPreviewAsync] Histogram updated for ORM: {ormName}");
                                        } else {
                                            logger.Info($"[LoadORMPreviewAsync] Cancelled before histogram update: {ormName}");
                                        }
                                    }));
                                } else {
                                    logger.Warn($"[LoadORMPreviewAsync] No mipmaps or cancelled for: {ormName}");
                                }
                            } catch (OperationCanceledException) {
                                logger.Info($"[LoadORMPreviewAsync] Histogram extraction cancelled/timeout for: {ormName}");
                            } catch (Exception ex) {
                                logger.Warn(ex, $"[LoadORMPreviewAsync] Failed to extract bitmap for histogram: {ormName}");
                            }
                        });
                    } else {
                        logger.Info($"[LoadORMPreviewAsync] Histogram skipped - ktxLoaded={ktxLoaded}, cancelled={cancellationToken.IsCancellationRequested}");
                    }

                    if (!ktxLoaded) {
                        // Fallback: Try extracting PNG from KTX2
                        logger.Info($"[LoadORMPreviewAsync] D3D11 native loading failed, trying PNG extraction: {ormTexture.Name}");
                        ktxLoaded = await TryLoadKtx2PreviewAsync(ormTexture, cancellationToken);
                    }
                } else {
                    // WPF MODE: Extract PNG from KTX2
                    ktxLoaded = await TryLoadKtx2PreviewAsync(ormTexture, cancellationToken);
                }

                if (!ktxLoaded && !cancellationToken.IsCancellationRequested) {
                    await Dispatcher.InvokeAsync(() => {
                        texturePreviewService.IsKtxPreviewAvailable = false;
                        viewModel.TextureInfoFormat = "Format: KTX2 (preview unavailable)";
                        logService.LogWarn($"Failed to load preview for packed ORM texture: {ormTexture.Name}");
                    });
                }
            } catch (OperationCanceledException) {
                logService.LogInfo($"[LoadORMPreviewAsync] Cancelled for ORM: {ormTexture.Name}");
            } catch (Exception ex) {
                logService.LogError($"Error loading packed ORM texture {ormTexture.Name}: {ex.Message}");
                ResetPreviewState();
                ClearD3D11Viewer();
            }
        }

        #endregion
    }
}
