using AssetProcessor.Services;
using AssetProcessor.Settings;
using AssetProcessor.TextureConversion.Settings;
using NLog;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace AssetProcessor {
    /// <summary>
    /// Partial class containing helper methods:
    /// - KTX2 scanning
    /// - Format validation
    /// - Settings management
    /// - Window lifecycle handlers
    /// - ConversionSettings initialization
    /// </summary>
    public partial class MainWindow {

        #region Helper Methods

        /// <summary>
        /// Scans all textures for existing KTX2 files and populates CompressionFormat, MipmapCount, and CompressedSize.
        /// Runs in background to avoid blocking UI.
        /// </summary>
        private void ScanKtx2InfoForAllTextures() {
            // Take a snapshot of textures to process
            var texturesToScan = viewModel.Textures.ToList();

            if (texturesToScan.Count == 0) {
                return;
            }

            // Run scanning in background, apply results on UI thread
            Task.Run(() => {
                var results = ktx2InfoService.ScanTextures(texturesToScan);

                foreach (var result in results) {
                    Dispatcher.InvokeAsync(() => {
                        result.Texture.CompressedSize = result.Info.FileSize;
                        if (result.Info.MipmapCount > 0) {
                            result.Texture.MipmapCount = result.Info.MipmapCount;
                        }
                        if (result.Info.CompressionFormat != null) {
                            result.Texture.CompressionFormat = result.Info.CompressionFormat;
                        }
                    });
                }
            });
        }

        private bool IsSupportedTextureFormat(string extension) {
            return supportedFormats.Contains(extension) && !excludedFormats.Contains(extension);
        }

        private bool IsSupportedModelFormat(string extension) {
            return supportedModelFormats.Contains(extension) && !excludedFormats.Contains(extension);
        }

        private void SaveCurrentSettings() {
            if (!string.IsNullOrEmpty(viewModel.SelectedProjectId)) {
                AppSettings.Default.LastSelectedProjectId = viewModel.SelectedProjectId;
            }

            if (!string.IsNullOrEmpty(viewModel.SelectedBranchId)) {
                Branch? selectedBranch = viewModel.Branches.FirstOrDefault(b => b.Id == viewModel.SelectedBranchId);
                if (selectedBranch != null) {
                    AppSettings.Default.LastSelectedBranchName = selectedBranch.Name;
                }
            }

            AppSettings.Default.Save();
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs? e) {
            try {
                // Stop file watcher and unsubscribe
                projectFileWatcherService.FilesDeletionDetected -= OnFilesDeletionDetected;
                StopFileWatcher();

                // Останавливаем billboard обновление
                StopBillboardUpdate();

                cancellationTokenSource?.Cancel();
                textureLoadCancellation?.Cancel();

                // Thread.Sleep removed - CancellationToken.Cancel() is instant

                cancellationTokenSource?.Dispose();
                textureLoadCancellation?.Dispose();

                playCanvasService?.Dispose();
            } catch (Exception ex) {
                logger.Error(ex, "Error canceling operations during window closing");
            }

            // Cleanup DataGrid layout service timers and state
            dataGridLayoutService.CleanupAll();
            _sortDirections.Clear();
            _previousColumnWidths.Clear();

            // Cleanup GLB viewer resources
            CleanupGlbViewer();

            viewModel.ProjectSelectionChanged -= ViewModel_ProjectSelectionChanged;
            viewModel.BranchSelectionChanged -= ViewModel_BranchSelectionChanged;
            SaveCurrentSettings();
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e) {
            cancellationTokenSource?.Cancel();
            cancellationTokenSource = new CancellationTokenSource();
        }

        private void Setting(object? sender, RoutedEventArgs e) {
            SettingsWindow settingsWindow = new();
            settingsWindow.OnPreviewRendererChanged += HandlePreviewRendererChanged;
            settingsWindow.ShowDialog();
            settingsWindow.OnPreviewRendererChanged -= HandlePreviewRendererChanged;
        }

        /// <summary>
        /// Инициализирует ConversionSettings менеджер и UI
        /// </summary>
        private void InitializeConversionSettings() {
            try {
                // Загружаем глобальные настройки
                globalTextureSettings ??= TextureConversionSettingsManager.LoadSettings();

                // Создаём менеджер настроек конвертации
                conversionSettingsManager = new ConversionSettingsManager(globalTextureSettings);

                // Заполняем UI элементы для ConversionSettings
                PopulateConversionSettingsUI();

                logger.Info("ConversionSettings initialized successfully");
            } catch (Exception ex) {
                logger.Error(ex, "Error initializing ConversionSettings");
                MessageBox.Show($"Error initializing conversion settings: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Заполняет UI элементы ConversionSettings (пресеты и параметры слайдеры)
        /// </summary>
        private void PopulateConversionSettingsUI() {
            if (conversionSettingsManager == null) {
                logger.Warn("ConversionSettingsManager not initialized");
                return;
            }

            try {
                // Передаём ConversionSettingsManager в панель настроек конвертации
                // Важно: панель сама загрузит пресеты из ConversionSettingsSchema
                // Только SetConversionSettingsManager() - он выполнит всё сам!
                if (ConversionSettingsPanel != null) {
                    ConversionSettingsPanel.SetConversionSettingsManager(conversionSettingsManager);

                    // Логируем для отладки
                    logger.Info($"ConversionSettingsManager passed to panel. PresetComboBox items count: {ConversionSettingsPanel.PresetComboBox.Items.Count}");
                }

            } catch (Exception ex) {
                logger.Error(ex, "Error populating ConversionSettings UI");
                throw;
            }
        }

        #endregion
    }
}
