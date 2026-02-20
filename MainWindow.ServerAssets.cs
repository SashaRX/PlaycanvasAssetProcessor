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
            viewModel.SelectedServerAsset = asset;
        }

        private void CopyServerUrlButton_Click(object sender, RoutedEventArgs e) {
            var asset = viewModel.SelectedServerAsset;
            if (asset != null && !string.IsNullOrEmpty(asset.CdnUrl)) {
                if (Helpers.ClipboardHelper.SetText(asset.CdnUrl)) {
                    logService.LogInfo($"Copied CDN URL: {asset.CdnUrl}");
                }
            }
        }

        private async void DeleteServerFileButton_Click(object sender, RoutedEventArgs e) {
            if (viewModel.SelectedServerAsset == null) return;

            var selectedAsset = viewModel.SelectedServerAsset;
            var confirmation = MessageBox.Show(
                $"Are you sure you want to delete '{selectedAsset.FileName}' from the server?\n\nThis action cannot be undone.",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmation != MessageBoxResult.Yes) return;

            try {
                var deleteResult = await assetWorkflowCoordinator.DeleteServerAssetAsync(
                    selectedAsset,
                    AppSettings.Default.B2KeyId,
                    AppSettings.Default.B2BucketName,
                    AppSettings.Default.B2BucketId,
                    getApplicationKey: () => AppSettings.Default.TryGetDecryptedB2ApplicationKey(out var key) ? key : null,
                    createB2Service: () => new Upload.B2UploadService(),
                    refreshServerAssetsAsync: () => ServerAssetsPanel.RefreshServerAssetsAsync(),
                    onInfo: logService.LogInfo,
                    onError: logService.LogError);

                if (deleteResult.Success) {
                    UpdateServerFileInfo(null);
                }
            } catch (Exception ex) {
                logService.LogError($"Error deleting file: {ex.Message}");
            }
        }

        #endregion

        #region Resource Navigation

        private void OnNavigateToResourceRequested(object? sender, string fileName) {
            var navigation = assetWorkflowCoordinator.ResolveNavigationTarget(fileName, viewModel.Textures, viewModel.Models, viewModel.Materials);

            if (navigation.Texture != null) {
                tabControl.SelectedItem = TexturesTabItem;
                TexturesDataGrid.SelectedItem = navigation.Texture;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, () => {
                    TexturesDataGrid.ScrollIntoView(navigation.Texture);
                });
                return;
            }

            if (navigation.IsOrmFile) {
                tabControl.SelectedItem = TexturesTabItem;
                TexturesDataGrid.SelectedItems.Clear();

                var textureInGroup = navigation.OrmGroupTexture;
                if (textureInGroup != null) {
                    SelectedORMSubGroupName = textureInGroup.SubGroupName;

                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, () => {
                        TexturesDataGrid.ScrollIntoView(textureInGroup);
                    });

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

            if (navigation.Model != null) {
                logger.Debug($"[Navigation] Found model: {navigation.Model.Name}");
                tabControl.SelectedItem = ModelsTabItem;
                ModelsDataGrid.SelectedItem = navigation.Model;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, () => {
                    ModelsDataGrid.ScrollIntoView(navigation.Model);
                });
                return;
            }

            if (navigation.Material != null) {
                logger.Debug($"[Navigation] Found material: {navigation.Material.Name}");
                tabControl.SelectedItem = MaterialsTabItem;
                MaterialsDataGrid.SelectedItem = navigation.Material;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, () => {
                    MaterialsDataGrid.ScrollIntoView(navigation.Material);
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
            var syncResult = assetWorkflowCoordinator.SyncDeletedPaths(
                deletedPaths,
                viewModel.Textures,
                viewModel.Materials,
                viewModel.Models);
            if (!syncResult.HasDeletedPaths) {
                return;
            }

            logger.Info($"Reset upload status for resources matching {syncResult.DeletedPathCount} deleted server paths. Reset: {syncResult.ResetCount}");
        }

        /// <summary>
        /// Обработчик события обновления списка файлов на сервере - верифицирует статусы ресурсов
        /// </summary>
        private void OnServerAssetsRefreshed(HashSet<string> serverPaths) {
            var syncResult = assetWorkflowCoordinator.SyncStatusesWithServer(
                serverPaths,
                viewModel.Textures,
                viewModel.Materials,
                viewModel.Models);

            if (syncResult.ServerWasEmpty) {
                if (syncResult.ResetCount > 0) {
                    logger.Info($"Server empty - reset {syncResult.ResetCount} upload statuses");
                }
                return;
            }

            if (syncResult.ResetCount > 0) {
                logger.Info($"Server status verification reset {syncResult.ResetCount} stale statuses");
            }
        }

        #endregion
    }
}
