using AssetProcessor.Resources;
using AssetProcessor.Settings;
using AssetProcessor.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace AssetProcessor {
    /// <summary>
    /// Partial class containing server assets related handlers:
    /// - Viewer type management
    /// - Server file info display
    /// - Navigation to resources
    /// - Server sync status handlers
    /// </summary>
    public partial class MainWindow {

        #region Viewer Management

        private void ShowViewer(ViewerType viewerType) {
            viewModel.ActiveViewerType = viewerType;
        }

        #endregion

        #region Server File Info

        private ViewModels.ServerAssetViewModel? _selectedServerAsset;

        /// <summary>
        /// Wires event handlers for ServerFileInfoPanel controls.
        /// Called from MainWindow constructor after InitializeComponent.
        /// </summary>
        private void InitializeServerFileInfoPanel() {
            serverFileInfoPanel.CopyServerUrlButton.Click += CopyServerUrlButton_Click;
            serverFileInfoPanel.DeleteServerFileButton.Click += DeleteServerFileButton_Click;
        }

        /// <summary>
        /// Updates the server file info panel with the selected asset
        /// </summary>
        public void UpdateServerFileInfo(ViewModels.ServerAssetViewModel? asset) {
            _selectedServerAsset = asset;

            // Safety check - panel controls may not be ready
            if (serverFileInfoPanel?.ServerFileNameText == null) return;

            if (asset == null) {
                serverFileInfoPanel.ServerFileNameText.Text = "-";
                serverFileInfoPanel.ServerFileTypeText.Text = "-";
                serverFileInfoPanel.ServerFileSizeText.Text = "-";
                serverFileInfoPanel.ServerFileSyncStatusText.Text = "-";
                serverFileInfoPanel.ServerFileSyncStatusText.Foreground = System.Windows.Media.Brushes.Gray;
                serverFileInfoPanel.ServerFileUploadedText.Text = "-";
                serverFileInfoPanel.ServerFileSha1Text.Text = "-";
                serverFileInfoPanel.ServerFileRemotePathText.Text = "-";
                serverFileInfoPanel.ServerFileCdnUrlText.Text = "-";
                serverFileInfoPanel.ServerFileLocalPathText.Text = "-";
                return;
            }

            serverFileInfoPanel.ServerFileNameText.Text = asset.FileName;
            serverFileInfoPanel.ServerFileTypeText.Text = asset.FileType;
            serverFileInfoPanel.ServerFileSizeText.Text = asset.SizeDisplay;
            serverFileInfoPanel.ServerFileSyncStatusText.Text = asset.SyncStatus;
            serverFileInfoPanel.ServerFileSyncStatusText.Foreground = asset.SyncStatusColor;
            serverFileInfoPanel.ServerFileUploadedText.Text = asset.UploadedAtDisplay;
            serverFileInfoPanel.ServerFileSha1Text.Text = asset.ContentSha1;
            serverFileInfoPanel.ServerFileRemotePathText.Text = asset.RemotePath;
            serverFileInfoPanel.ServerFileCdnUrlText.Text = asset.CdnUrl ?? "-";
            serverFileInfoPanel.ServerFileLocalPathText.Text = asset.LocalPath ?? "Not found locally";
        }

        private void CopyServerUrlButton_Click(object sender, RoutedEventArgs e) {
            if (_selectedServerAsset != null && !string.IsNullOrEmpty(_selectedServerAsset.CdnUrl)) {
                if (Helpers.ClipboardHelper.SetText(_selectedServerAsset.CdnUrl)) {
                    logService.LogInfo($"Copied CDN URL: {_selectedServerAsset.CdnUrl}");
                }
            }
        }

        private async void DeleteServerFileButton_Click(object sender, RoutedEventArgs e) {
            if (_selectedServerAsset == null) return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete '{_selectedServerAsset.FileName}' from the server?\n\nThis action cannot be undone.",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try {
                using var b2Service = new Upload.B2UploadService();

                if (!AppSettings.Default.TryGetDecryptedB2ApplicationKey(out var appKey) || string.IsNullOrEmpty(appKey)) {
                    logService.LogError("Failed to decrypt B2 application key.");
                    return;
                }

                var settings = new Upload.B2UploadSettings {
                    KeyId = AppSettings.Default.B2KeyId,
                    ApplicationKey = appKey,
                    BucketName = AppSettings.Default.B2BucketName,
                    BucketId = AppSettings.Default.B2BucketId
                };

                await b2Service.AuthorizeAsync(settings);
                var success = await b2Service.DeleteFileAsync(_selectedServerAsset.RemotePath);

                if (success) {
                    logService.LogInfo($"Deleted: {_selectedServerAsset.RemotePath}");
                    UpdateServerFileInfo(null);
                    // Refresh the server assets panel
                    await ServerAssetsPanel.RefreshServerAssetsAsync();
                } else {
                    logService.LogError($"Failed to delete: {_selectedServerAsset.RemotePath}");
                }
            } catch (Exception ex) {
                logService.LogError($"Error deleting file: {ex.Message}");
            }
        }

        #endregion

        #region Resource Navigation

        private void OnNavigateToResourceRequested(object? sender, string fileName) {
            string baseName = System.IO.Path.GetFileNameWithoutExtension(fileName);

            // Strip ORM suffix patterns for better matching (_og, _ogm, _ogmh)
            string baseNameWithoutSuffix = baseName;
            bool isOrmFile = false;
            if (baseName.EndsWith("_og", StringComparison.OrdinalIgnoreCase)) {
                baseNameWithoutSuffix = baseName[..^3];
                isOrmFile = true;
            } else if (baseName.EndsWith("_ogm", StringComparison.OrdinalIgnoreCase)) {
                baseNameWithoutSuffix = baseName[..^4];
                isOrmFile = true;
            } else if (baseName.EndsWith("_ogmh", StringComparison.OrdinalIgnoreCase)) {
                baseNameWithoutSuffix = baseName[..^5];
                isOrmFile = true;
            }

            // Try textures (including ORM textures)
            var texture = viewModel.Textures.FirstOrDefault(t => {
                // Direct name match (most common case - check first)
                if (t.Name?.Equals(baseName, StringComparison.OrdinalIgnoreCase) == true)
                    return true;
                // Path ends with filename
                if (t.Path != null && t.Path.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                    return true;
                // For ORM textures, try additional matching
                if (t is ORMTextureResource orm) {
                    // Match against SettingsKey
                    if (!string.IsNullOrEmpty(orm.SettingsKey)) {
                        var settingsKeyBase = orm.SettingsKey.StartsWith("orm_", StringComparison.OrdinalIgnoreCase)
                            ? orm.SettingsKey[4..]
                            : orm.SettingsKey;
                        if (baseName.Equals(orm.SettingsKey, StringComparison.OrdinalIgnoreCase) ||
                            baseName.Equals(settingsKeyBase, StringComparison.OrdinalIgnoreCase) ||
                            baseNameWithoutSuffix.Equals(settingsKeyBase, StringComparison.OrdinalIgnoreCase) ||
                            settingsKeyBase.Contains(baseNameWithoutSuffix, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    // Match file name from Path property (packed ORM)
                    if (!string.IsNullOrEmpty(orm.Path)) {
                        var ormPathBaseName = System.IO.Path.GetFileNameWithoutExtension(orm.Path);
                        if (baseName.Equals(ormPathBaseName, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    // Match ORM texture name patterns
                    var cleanName = t.Name?.Replace("[ORM Texture - Not Packed]", "").Trim();
                    if (!string.IsNullOrEmpty(cleanName)) {
                        if (cleanName.Equals(baseName, StringComparison.OrdinalIgnoreCase) ||
                            cleanName.Equals(baseNameWithoutSuffix, StringComparison.OrdinalIgnoreCase) ||
                            baseNameWithoutSuffix.Contains(cleanName, StringComparison.OrdinalIgnoreCase) ||
                            cleanName.Contains(baseNameWithoutSuffix, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    // Match against source texture names
                    if (orm.AOSource?.Name != null && baseNameWithoutSuffix.Contains(orm.AOSource.Name.Replace("_ao", "").Replace("_AO", ""), StringComparison.OrdinalIgnoreCase))
                        return true;
                    if (orm.GlossSource?.Name != null && baseNameWithoutSuffix.Contains(orm.GlossSource.Name.Replace("_gloss", "").Replace("_Gloss", "").Replace("_roughness", "").Replace("_Roughness", ""), StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            });
            if (texture != null) {
                tabControl.SelectedItem = TexturesTabItem;
                TexturesDataGrid.SelectedItem = texture;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, () => {
                    TexturesDataGrid.ScrollIntoView(texture);
                });
                return;
            }

            // For ORM files, highlight the ORM group header and show ORM panel
            if (isOrmFile) {
                tabControl.SelectedItem = TexturesTabItem;
                TexturesDataGrid.SelectedItems.Clear();

                // Ищем текстуру по GroupName = baseNameWithoutSuffix (например "oldMailBox")
                var textureInGroup = viewModel.Textures.FirstOrDefault(t =>
                    !string.IsNullOrEmpty(t.GroupName) &&
                    t.GroupName.Equals(baseNameWithoutSuffix, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(t.SubGroupName));

                if (textureInGroup != null) {
                    SelectedORMSubGroupName = textureInGroup.SubGroupName;

                    // Скролл к текстуре в группе
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, () => {
                        TexturesDataGrid.ScrollIntoView(textureInGroup);
                    });

                    // Показываем ORM панель если есть ParentORMTexture
                    if (textureInGroup.ParentORMTexture != null) {
                        var ormTexture = textureInGroup.ParentORMTexture;

                        viewModel.IsConversionSettingsVisible = false;
                        viewModel.IsORMPanelVisible = true;

                        var availableTextures = viewModel.Textures.Where(t => !(t is ORMTextureResource)).ToList();
                        ORMPanel.Initialize(this, availableTextures);
                        ORMPanel.SetORMTexture(ormTexture);

                        viewModel.TextureInfoName = "Texture Name: " + ormTexture.Name;
                        viewModel.TextureInfoColorSpace = "Color Space: Linear (ORM)";

                        if (!string.IsNullOrEmpty(ormTexture.Path) && System.IO.File.Exists(ormTexture.Path)) {
                            viewModel.TextureInfoResolution = ormTexture.Resolution != null && ormTexture.Resolution.Length >= 2
                                ? $"Resolution: {ormTexture.Resolution[0]}x{ormTexture.Resolution[1]}"
                                : "Resolution: Unknown";
                            viewModel.TextureInfoFormat = "Format: KTX2 (packed)";
                            _ = LoadORMPreviewAsync(ormTexture);
                        } else {
                            viewModel.TextureInfoResolution = "Resolution: Not packed yet";
                            viewModel.TextureInfoFormat = "Format: Not packed";
                        }

                        viewModel.SelectedTexture = ormTexture;
                    }
                }
                return;
            }

            // Try models
            var model = viewModel.Models.FirstOrDefault(m =>
                m.Name?.Equals(baseName, StringComparison.OrdinalIgnoreCase) == true ||
                (m.Path != null && m.Path.EndsWith(fileName, StringComparison.OrdinalIgnoreCase)));
            if (model != null) {
                logger.Debug($"[Navigation] Found model: {model.Name}");
                tabControl.SelectedItem = ModelsTabItem;
                ModelsDataGrid.SelectedItem = model;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, () => {
                    ModelsDataGrid.ScrollIntoView(model);
                });
                return;
            }

            // Try materials
            var material = viewModel.Materials.FirstOrDefault(m =>
                m.Name?.Equals(baseName, StringComparison.OrdinalIgnoreCase) == true ||
                (m.Path != null && m.Path.EndsWith(fileName, StringComparison.OrdinalIgnoreCase)));
            if (material != null) {
                logger.Debug($"[Navigation] Found material: {material.Name}");
                tabControl.SelectedItem = MaterialsTabItem;
                MaterialsDataGrid.SelectedItem = material;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, () => {
                    MaterialsDataGrid.ScrollIntoView(material);
                });
                return;
            }

            logger.Debug($"[Navigation] Resource not found: {fileName}");
        }

        #endregion

        #region Server Sync Status

        /// <summary>
        /// Обработчик события удаления файлов с сервера - сбрасывает статусы ресурсов
        /// </summary>
        private void OnServerFilesDeleted(List<string> deletedPaths) {
            if (deletedPaths == null || deletedPaths.Count == 0) return;

            // Нормализуем пути для сравнения
            var normalizedPaths = deletedPaths
                .Select(p => p.Replace('\\', '/').ToLowerInvariant())
                .ToHashSet();

            // Сбрасываем статусы текстур
            foreach (var texture in viewModel.Textures) {
                if (!string.IsNullOrEmpty(texture.RemoteUrl)) {
                    // Извлекаем относительный путь из RemoteUrl (убираем базовый CDN URL)
                    var remotePath = ExtractRelativePathFromUrl(texture.RemoteUrl);
                    if (remotePath != null && normalizedPaths.Contains(remotePath.ToLowerInvariant())) {
                        texture.UploadStatus = null;
                        texture.UploadedHash = null;
                        texture.RemoteUrl = null;
                        texture.LastUploadedAt = null;
                    }
                }
            }

            // Сбрасываем статусы материалов
            foreach (var material in viewModel.Materials) {
                if (!string.IsNullOrEmpty(material.RemoteUrl)) {
                    var remotePath = ExtractRelativePathFromUrl(material.RemoteUrl);
                    if (remotePath != null && normalizedPaths.Contains(remotePath.ToLowerInvariant())) {
                        material.UploadStatus = null;
                        material.UploadedHash = null;
                        material.RemoteUrl = null;
                        material.LastUploadedAt = null;
                    }
                }
            }

            // Сбрасываем статусы моделей
            foreach (var model in viewModel.Models) {
                if (!string.IsNullOrEmpty(model.RemoteUrl)) {
                    var remotePath = ExtractRelativePathFromUrl(model.RemoteUrl);
                    if (remotePath != null && normalizedPaths.Contains(remotePath.ToLowerInvariant())) {
                        model.UploadStatus = null;
                        model.UploadedHash = null;
                        model.RemoteUrl = null;
                        model.LastUploadedAt = null;
                    }
                }
            }

            logger.Info($"Reset upload status for resources matching {deletedPaths.Count} deleted server paths");
        }

        /// <summary>
        /// Извлекает относительный путь из полного CDN URL
        /// </summary>
        private string? ExtractRelativePathFromUrl(string url) {
            if (string.IsNullOrEmpty(url)) return null;

            // CDN URL обычно имеет формат: https://cdn.example.com/bucket/path/to/file.ext
            // или просто путь: content/path/to/file.ext
            try {
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) {
                    // Убираем начальный слэш из пути
                    return uri.AbsolutePath.TrimStart('/');
                }
                // Если это уже относительный путь
                return url.Replace('\\', '/').TrimStart('/');
            } catch {
                return url.Replace('\\', '/').TrimStart('/');
            }
        }

        /// <summary>
        /// Обработчик события обновления списка файлов на сервере - верифицирует статусы ресурсов
        /// </summary>
        private void OnServerAssetsRefreshed(HashSet<string> serverPaths) {
            if (serverPaths == null || serverPaths.Count == 0) {
                // Сервер пустой - сбрасываем все upload статусы
                ResetAllUploadStatuses();
                return;
            }

            int verified = 0;
            int notFound = 0;

            // Проверяем текстуры
            foreach (var texture in viewModel.Textures) {
                if (texture.UploadStatus == "Uploaded" && !string.IsNullOrEmpty(texture.RemoteUrl)) {
                    var remotePath = ExtractRelativePathFromUrl(texture.RemoteUrl);
                    if (remotePath != null && serverPaths.Contains(remotePath)) {
                        verified++;
                    } else {
                        texture.UploadStatus = null;
                        texture.UploadedHash = null;
                        texture.RemoteUrl = null;
                        texture.LastUploadedAt = null;
                        notFound++;
                    }
                }
            }

            // Проверяем материалы
            foreach (var material in viewModel.Materials) {
                if (material.UploadStatus == "Uploaded" && !string.IsNullOrEmpty(material.RemoteUrl)) {
                    var remotePath = ExtractRelativePathFromUrl(material.RemoteUrl);
                    if (remotePath != null && serverPaths.Contains(remotePath)) {
                        verified++;
                    } else {
                        material.UploadStatus = null;
                        material.UploadedHash = null;
                        material.RemoteUrl = null;
                        material.LastUploadedAt = null;
                        notFound++;
                    }
                }
            }

            // Проверяем модели
            foreach (var model in viewModel.Models) {
                if (model.UploadStatus == "Uploaded" && !string.IsNullOrEmpty(model.RemoteUrl)) {
                    var remotePath = ExtractRelativePathFromUrl(model.RemoteUrl);
                    if (remotePath != null && serverPaths.Contains(remotePath)) {
                        verified++;
                    } else {
                        model.UploadStatus = null;
                        model.UploadedHash = null;
                        model.RemoteUrl = null;
                        model.LastUploadedAt = null;
                        notFound++;
                    }
                }
            }

            if (notFound > 0) {
                logger.Info($"Server status verification: {verified} verified, {notFound} not found (status reset)");
            } else if (verified > 0) {
                logger.Info($"Server status verification: {verified} files verified");
            }
        }

        /// <summary>
        /// Сбрасывает все upload статусы (когда сервер пустой)
        /// </summary>
        private void ResetAllUploadStatuses() {
            int reset = 0;

            foreach (var texture in viewModel.Textures) {
                if (!string.IsNullOrEmpty(texture.UploadStatus)) {
                    texture.UploadStatus = null;
                    texture.UploadedHash = null;
                    texture.RemoteUrl = null;
                    texture.LastUploadedAt = null;
                    reset++;
                }
            }

            foreach (var material in viewModel.Materials) {
                if (!string.IsNullOrEmpty(material.UploadStatus)) {
                    material.UploadStatus = null;
                    material.UploadedHash = null;
                    material.RemoteUrl = null;
                    material.LastUploadedAt = null;
                    reset++;
                }
            }

            foreach (var model in viewModel.Models) {
                if (!string.IsNullOrEmpty(model.UploadStatus)) {
                    model.UploadStatus = null;
                    model.UploadedHash = null;
                    model.RemoteUrl = null;
                    model.LastUploadedAt = null;
                    reset++;
                }
            }

            if (reset > 0) {
                logger.Info($"Server empty - reset {reset} upload statuses");
            }
        }

        #endregion
    }
}
