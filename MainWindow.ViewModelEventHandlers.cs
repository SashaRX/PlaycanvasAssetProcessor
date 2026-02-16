using AssetProcessor.Resources;
using AssetProcessor.TextureConversion.Settings;
using AssetProcessor.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace AssetProcessor {
    /// <summary>
    /// Partial class containing ViewModel event handlers:
    /// - ORMTextureViewModel events
    /// - TextureConversionSettingsViewModel events
    /// - AssetLoadingViewModel events
    /// - MaterialSelectionViewModel events
    /// </summary>
    public partial class MainWindow {

        #region ORMTextureViewModel Event Handlers

        private void OnORMCreated(object? sender, ORMCreatedEventArgs e) {
            // Select and scroll to the newly created ORM texture
            tabControl.SelectedItem = TexturesTabItem;
            TexturesDataGrid.SelectedItem = e.ORMTexture;
            TexturesDataGrid.ScrollIntoView(e.ORMTexture);

            // Show success message with details
            if (e.Mode.HasValue) {
                MessageBox.Show(
                    $"Created ORM texture:\n\n" +
                    $"Name: {e.ORMTexture.Name}\n" +
                    $"Mode: {e.Mode}\n" +
                    $"AO: {e.AOSource?.Name ?? "None"}\n" +
                    $"Gloss: {e.GlossSource?.Name ?? "None"}\n" +
                    $"Metallic: {e.MetallicSource?.Name ?? "None"}",
                    "ORM Texture Created", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void OnORMDeleted(object? sender, ORMDeletedEventArgs e) {
            logService.LogInfo($"ORM texture deleted: {e.ORMTexture.Name}");
        }

        private void OnORMConfirmationRequested(object? sender, ORMConfirmationRequestEventArgs e) {
            var result = MessageBox.Show(e.Message, "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes) {
                e.OnConfirmed?.Invoke();
            }
        }

        private void OnORMErrorOccurred(object? sender, ORMErrorEventArgs e) {
            MessageBox.Show(e.Message, e.Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnORMBatchCreationCompleted(object? sender, ORMBatchCreationCompletedEventArgs e) {
            var message = $"Batch ORM Creation Results:\n\n" +
                         $"Created: {e.Created}\n" +
                         $"Skipped: {e.Skipped}\n" +
                         $"Errors: {e.Errors.Count}";

            if (e.Errors.Count > 0) {
                message += $"\n\nErrors:\n{string.Join("\n", e.Errors.Take(5))}";
                if (e.Errors.Count > 5) {
                    message += $"\n... and {e.Errors.Count - 5} more";
                }
            }

            MessageBox.Show(message, "Batch ORM Creation Complete",
                MessageBoxButton.OK, e.Errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }

        #endregion

        #region TextureConversionSettingsViewModel Event Handlers

        private void OnConversionSettingsLoaded(object? sender, SettingsLoadedEventArgs e) {
            logService.LogInfo($"[OnConversionSettingsLoaded] Settings loaded for {e.Texture.Name}, Source: {e.Source}");

            // Set current texture path for auto-detect normal map
            ConversionSettingsPanel.SetCurrentTexturePath(e.Texture.Path);
            ConversionSettingsPanel.ClearNormalMapPath();

            switch (e.Source) {
                case SettingsSource.Saved:
                    // Load saved settings to UI
                    if (e.Settings != null) {
                        LoadSavedSettingsToUI(e.Settings);
                    }
                    break;

                case SettingsSource.AutoDetectedPreset:
                    // Set preset silently (without triggering change events)
                    if (!string.IsNullOrEmpty(e.PresetName)) {
                        var dropdownItems = ConversionSettingsPanel.PresetComboBox.Items.Cast<string>().ToList();
                        bool presetExistsInDropdown = dropdownItems.Contains(e.PresetName);

                        if (presetExistsInDropdown) {
                            ConversionSettingsPanel.SetPresetSilently(e.PresetName);
                        } else {
                            ConversionSettingsPanel.SetPresetSilently("Custom");
                        }
                    }
                    break;

                case SettingsSource.Default:
                    // Set to Custom preset and apply default settings
                    ConversionSettingsPanel.SetPresetSilently("Custom");

                    // Apply default settings based on texture type
                    var textureType = TextureResource.DetermineTextureType(e.Texture.Name ?? "");
                    var profile = TextureConversion.Core.MipGenerationProfile.CreateDefault(
                        MapTextureTypeToCore(textureType));
                    var compression = TextureConversion.Core.CompressionSettings.CreateETC1SDefault();
                    var compressionData = TextureConversion.Settings.CompressionSettingsData.FromCompressionSettings(compression);
                    var mipProfileData = TextureConversion.Settings.MipProfileSettings.FromMipGenerationProfile(profile);

                    ConversionSettingsPanel.LoadSettings(compressionData, mipProfileData, true, false);
                    break;
            }
        }

        private void OnConversionSettingsSaved(object? sender, SettingsSavedEventArgs e) {
            logService.LogInfo($"[OnConversionSettingsSaved] Settings saved for {e.Texture.Name}");
        }

        private void OnConversionSettingsError(object? sender, SettingsErrorEventArgs e) {
            logService.LogError($"[OnConversionSettingsError] {e.Title}: {e.Message}");
            MessageBox.Show(e.Message, e.Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        #endregion

        #region AssetLoadingViewModel Event Handlers

        // Track window active state to skip render loops when inactive (Alt+Tab fix)
        private volatile bool _isWindowActive = true;

        private void OnAssetsLoaded(object? sender, AssetsLoadedEventArgs e) {
            // Check if window is active - if not, defer loading to prevent UI freeze
            if (!_isWindowActive) {
                logger.Info("[OnAssetsLoaded] Window inactive, deferring asset loading");
                _pendingAssetsData = e;
                return;
            }

            ApplyAssetsToUI(e);
        }

        private void ApplyAssetsToUI(AssetsLoadedEventArgs e) {
            Dispatcher.Invoke(() => {
                viewModel.Textures = new ObservableCollection<TextureResource>(e.Textures);
                viewModel.Models = new ObservableCollection<ModelResource>(e.Models);
                viewModel.Materials = new ObservableCollection<MaterialResource>(e.Materials);
                folderPaths = new Dictionary<int, string>(e.FolderPaths);
                viewModel.RecalculateIndices();
                viewModel.SyncMaterialMasterMappings();

                ModelsDataGrid.SetBinding(System.Windows.Controls.ItemsControl.ItemsSourceProperty, new Binding("Models"));
                MaterialsDataGrid.SetBinding(System.Windows.Controls.ItemsControl.ItemsSourceProperty, new Binding("Materials"));
                TexturesDataGrid.SetBinding(System.Windows.Controls.ItemsControl.ItemsSourceProperty, new Binding("Textures"));

                ModelsDataGrid.Visibility = Visibility.Visible;
                MaterialsDataGrid.Visibility = Visibility.Visible;
                TexturesDataGrid.Visibility = Visibility.Visible;

                RestoreGridLayout(ModelsDataGrid);
                RestoreGridLayout(MaterialsDataGrid);
                RestoreGridLayout(TexturesDataGrid);

                ApplyTextureGroupingIfEnabled();

                viewModel.ProgressValue = viewModel.ProgressMaximum;
                viewModel.ProgressText = $"Ready ({e.Textures.Count} textures, {e.Models.Count} models, {e.Materials.Count} materials)";
                _ = ServerAssetsPanel.RefreshServerAssetsAsync();
            });
        }

        private void OnAssetLoadingProgressChanged(object? sender, AssetLoadProgressEventArgs e) {
            // Progress<T> already marshals to UI thread, no need for Dispatcher
            viewModel.ProgressMaximum = e.Total;
            viewModel.ProgressValue = e.Processed;
            // Show asset name being processed
            if (!string.IsNullOrEmpty(e.CurrentAsset)) {
                viewModel.ProgressText = $"{e.CurrentAsset} ({e.Processed}/{e.Total})";
            } else {
                viewModel.ProgressText = e.Total > 0 ? $"Loading... ({e.Processed}/{e.Total})" : "Loading...";
            }
        }

        /// <summary>
        /// Shows DataGrids and applies grouping with yield for heavy Textures grid.
        /// Split into phases to prevent UI freeze:
        /// 1. Show all DataGrids WITHOUT grouping (fast)
        /// 2. Defer grouping application to separate callback
        /// </summary>
        private void ShowDataGridsAndApplyGrouping(int textureCount, int modelCount, int materialCount) {
            viewModel.ProgressText = "Loading...";

            ModelsDataGrid.SetBinding(System.Windows.Controls.ItemsControl.ItemsSourceProperty,
                new System.Windows.Data.Binding("Models"));
            MaterialsDataGrid.SetBinding(System.Windows.Controls.ItemsControl.ItemsSourceProperty,
                new System.Windows.Data.Binding("Materials"));
            ModelsDataGrid.Visibility = Visibility.Visible;
            MaterialsDataGrid.Visibility = Visibility.Visible;
            RestoreGridLayout(ModelsDataGrid);
            RestoreGridLayout(MaterialsDataGrid);

            // Defer TexturesDataGrid to prevent UI freeze (heaviest DataGrid)
            var timer = new System.Windows.Threading.DispatcherTimer {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            timer.Tick += (s, e) => {
                timer.Stop();
                TexturesDataGrid.SetBinding(System.Windows.Controls.ItemsControl.ItemsSourceProperty,
                    new System.Windows.Data.Binding("Textures"));
                TexturesDataGrid.Visibility = Visibility.Visible;
                RestoreGridLayout(TexturesDataGrid);

                // Defer grouping to separate tick
                var groupTimer = new System.Windows.Threading.DispatcherTimer {
                    Interval = TimeSpan.FromMilliseconds(100)
                };
                groupTimer.Tick += (s2, e2) => {
                    groupTimer.Stop();
                    ApplyTextureGroupingIfEnabled();
                    viewModel.ProgressText = $"Ready ({textureCount} textures, {modelCount} models, {materialCount} materials)";
                    viewModel.ProgressValue = viewModel.ProgressMaximum;
                };
                groupTimer.Start();
            };
            timer.Start();
        }

        private void OnORMTexturesDetected(object? sender, ORMTexturesDetectedEventArgs e) {
            logService.LogInfo($"[OnORMTexturesDetected] Detected {e.DetectedCount} ORM textures, {e.Associations.Count} associations");
            // SubGroupName and ParentORMTexture are already set in AssetLoadingViewModel before AssetsLoaded
            // This event is for informational purposes only
        }

        private void OnVirtualORMTexturesGenerated(object? sender, VirtualORMTexturesGeneratedEventArgs e) {
            logService.LogInfo($"[OnVirtualORMTexturesGenerated] Generated {e.GeneratedCount} virtual ORM textures, {e.Associations.Count} associations");
            // SubGroupName, ParentORMTexture and sources are already set in AssetLoadingViewModel before AssetsLoaded
            // This event is for informational purposes only
        }

        private void OnUploadStatesRestored(object? sender, UploadStatesRestoredEventArgs e) {
            logService.LogInfo($"[OnUploadStatesRestored] Restored {e.RestoredCount} upload states");
            // Upload states are already applied in AssetLoadingViewModel before AssetsLoaded
            // This event is for informational purposes only
        }

        private void OnB2VerificationCompleted(object? sender, B2VerificationCompletedEventArgs e) {
            // Use BeginInvoke instead of Invoke to prevent potential deadlock
            // when this is called from background thread while UI thread is processing ApplyAssetsToUI
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, () => {
                if (!e.Success) {
                    logService.LogWarn($"[B2 Verification] Failed: {e.ErrorMessage}");
                    return;
                }

                if (e.NotFoundOnServer.Count > 0) {
                    logService.LogWarn($"[B2 Verification] {e.NotFoundOnServer.Count} files not found on server (status updated to 'Not on Server')");

                    // Refresh DataGrids to show updated status
                    TexturesDataGrid.Items.Refresh();
                    ModelsDataGrid.Items.Refresh();
                    MaterialsDataGrid.Items.Refresh();
                } else {
                    logService.LogInfo($"[B2 Verification] All {e.VerifiedCount} uploaded files verified on server");
                }
            });
        }

        private void OnAssetLoadingError(object? sender, AssetLoadErrorEventArgs e) {
            logService.LogError($"[OnAssetLoadingError] {e.Title}: {e.Message}");
            MessageBox.Show(e.Message, e.Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        #endregion

        #region MaterialSelectionViewModel Event Handlers

        private void OnMaterialParametersLoaded(object? sender, MaterialParametersLoadedEventArgs e) {
            // Use BeginInvoke to prevent potential deadlock when called from background thread
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, () => {
                DisplayMaterialParameters(e.Material);
            });
        }

        private void OnNavigateToTextureRequested(object? sender, NavigateToTextureEventArgs e) {
            Dispatcher.BeginInvoke(new Action(() => {
                if (TexturesTabItem != null) {
                    tabControl.SelectedItem = TexturesTabItem;
                }

                TextureResource? texture = viewModel.Textures.FirstOrDefault(t => t.ID == e.TextureId);
                if (texture != null) {
                    var view = CollectionViewSource.GetDefaultView(TexturesDataGrid.ItemsSource);
                    view?.MoveCurrentTo(texture);

                    TexturesDataGrid.SelectedItem = texture;
                    TexturesDataGrid.UpdateLayout();
                    TexturesDataGrid.ScrollIntoView(texture);
                    TexturesDataGrid.Focus();

                    logger.Info("Navigated to texture {TextureName} (ID {TextureId}) for {MapType}",
                        texture.Name, texture.ID, e.MapType);
                } else {
                    logger.Warn("Texture with ID {TextureId} not found for navigation", e.TextureId);
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void OnMaterialSelectionError(object? sender, MaterialErrorEventArgs e) {
            logService.LogError($"[OnMaterialSelectionError] {e.Message}");
        }

        #endregion
    }
}
