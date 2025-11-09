using AssetProcessor.Resources;
using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureConversion.MipGeneration;
using AssetProcessor.TextureConversion.Pipeline;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace AssetProcessor.Controls {
    public partial class ORMPackingPanel : UserControl {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private ORMTextureResource? currentORMTexture;
        private List<TextureResource> availableTextures = new();
        private MainWindow? mainWindow;

        public ORMPackingPanel() {
            InitializeComponent();
        }

        /// <summary>
        /// Инициализация панели с доступными текстурами
        /// </summary>
        public void Initialize(MainWindow window, List<TextureResource> textures) {
            mainWindow = window;
            availableTextures = textures;

            // Заполняем ComboBox источниками
            RefreshSourceLists();
        }

        /// <summary>
        /// Устанавливает текущую ORM текстуру для редактирования
        /// </summary>
        public void SetORMTexture(ORMTextureResource ormTexture) {
            currentORMTexture = ormTexture;

            if (ormTexture == null) {
                return;
            }

            // Загружаем настройки из ORM текстуры
            LoadORMSettings();
        }

        /// <summary>
        /// Обновляет списки источников
        /// </summary>
        private void RefreshSourceLists() {
            // No default/empty item - all channels must have actual texture sources
            Logger.Info($"RefreshSourceLists: availableTextures count = {availableTextures.Count}");
            if (availableTextures.Count > 0) {
                Logger.Info($"First 3 textures: {string.Join(", ", availableTextures.Take(3).Select(t => $"{t.Name}(ID={t.ID})"))}");
            }

            AOSourceComboBox.ItemsSource = availableTextures.ToList();
            GlossSourceComboBox.ItemsSource = availableTextures.ToList();
            MetallicSourceComboBox.ItemsSource = availableTextures.ToList();
            HeightSourceComboBox.ItemsSource = availableTextures.ToList();

            // Set to -1 (no selection) by default
            AOSourceComboBox.SelectedIndex = -1;
            GlossSourceComboBox.SelectedIndex = -1;
            MetallicSourceComboBox.SelectedIndex = -1;
            HeightSourceComboBox.SelectedIndex = -1;
        }

        /// <summary>
        /// Загружает настройки из ORM текстуры
        /// </summary>
        private void LoadORMSettings() {
            if (currentORMTexture == null) return;

            Logger.Info($"LoadORMSettings: ORM Name = {currentORMTexture.Name}");
            Logger.Info($"  AOSource: {currentORMTexture.AOSource?.Name ?? "null"} (ID={currentORMTexture.AOSource?.ID})");
            Logger.Info($"  GlossSource: {currentORMTexture.GlossSource?.Name ?? "null"} (ID={currentORMTexture.GlossSource?.ID})");
            Logger.Info($"  MetallicSource: {currentORMTexture.MetallicSource?.Name ?? "null"} (ID={currentORMTexture.MetallicSource?.ID})");

            // Packing mode
            PackingModeComboBox.SelectedIndex = (int)currentORMTexture.PackingMode - 1;

            // Sources - find by ID (more reliable than reference comparison)
            if (currentORMTexture.AOSource != null) {
                var found = availableTextures.FirstOrDefault(t => t.ID == currentORMTexture.AOSource.ID);
                Logger.Info($"  Searching for AO ID={currentORMTexture.AOSource.ID}: found={found?.Name ?? "null"}");
                if (found != null) {
                    AOSourceComboBox.SelectedItem = found;
                    Logger.Info($"  Set AOSourceComboBox.SelectedItem to {found.Name}");
                }
            }
            if (currentORMTexture.GlossSource != null) {
                var found = availableTextures.FirstOrDefault(t => t.ID == currentORMTexture.GlossSource.ID);
                Logger.Info($"  Searching for Gloss ID={currentORMTexture.GlossSource.ID}: found={found?.Name ?? "null"}");
                if (found != null) {
                    GlossSourceComboBox.SelectedItem = found;
                    Logger.Info($"  Set GlossSourceComboBox.SelectedItem to {found.Name}");
                }
            }
            if (currentORMTexture.MetallicSource != null) {
                var found = availableTextures.FirstOrDefault(t => t.ID == currentORMTexture.MetallicSource.ID);
                Logger.Info($"  Searching for Metallic ID={currentORMTexture.MetallicSource.ID}: found={found?.Name ?? "null"}");
                if (found != null) {
                    MetallicSourceComboBox.SelectedItem = found;
                    Logger.Info($"  Set MetallicSourceComboBox.SelectedItem to {found.Name}");
                }
            }
            if (currentORMTexture.HeightSource != null) {
                var found = availableTextures.FirstOrDefault(t => t.ID == currentORMTexture.HeightSource.ID);
                Logger.Info($"  Searching for Height ID={currentORMTexture.HeightSource.ID}: found={found?.Name ?? "null"}");
                if (found != null) {
                    HeightSourceComboBox.SelectedItem = found;
                    Logger.Info($"  Set HeightSourceComboBox.SelectedItem to {found.Name}");
                }
            }

            // AO settings
            AOProcessingComboBox.SelectedIndex = (int)currentORMTexture.AOProcessingMode;
            AOBiasSlider.Value = currentORMTexture.AOBias;
            AOPercentileSlider.Value = currentORMTexture.AOPercentile;

            // Gloss settings
            GlossToksvigCheckBox.IsChecked = currentORMTexture.GlossToksvigEnabled;
            GlossToksvigPowerSlider.Value = currentORMTexture.GlossToksvigPower;
            ToksvigMinMipLevelSlider.Value = currentORMTexture.GlossToksvigMinMipLevel;
            ToksvigEnergyPreservingCheckBox.IsChecked = currentORMTexture.GlossToksvigEnergyPreserving;

            // Use Dispatcher.BeginInvoke to set ComboBox selected items after binding is complete
            Dispatcher.BeginInvoke(new Action(() => {
                // Compression settings
                CompressionFormatComboBox.SelectedItem = currentORMTexture.CompressionFormat;
                Logger.Info($"  Set CompressionFormatComboBox to {currentORMTexture.CompressionFormat}");

                // Toksvig calculation mode
                ToksvigCalculationModeComboBox.SelectedItem = currentORMTexture.GlossToksvigCalculationMode;
                Logger.Info($"  Set ToksvigCalculationModeComboBox to {currentORMTexture.GlossToksvigCalculationMode}");

                // Filter type
                FilterTypeComboBox.SelectedItem = currentORMTexture.FilterType;
                Logger.Info($"  Set FilterTypeComboBox to {currentORMTexture.FilterType}");
            }), System.Windows.Threading.DispatcherPriority.Background);

            // Compression quality settings
            QualityLevelSlider.Value = currentORMTexture.QualityLevel;
            UASTCQualitySlider.Value = currentORMTexture.UASTCQuality;

            // Mipmap settings
            MipmapCountSlider.Value = currentORMTexture.MipmapCount;

            UpdateStatus();
        }

        /// <summary>
        /// Сохраняет настройки в ORM текстуру
        /// </summary>
        private void SaveORMSettings() {
            // Early return if controls not initialized yet
            if (AOSourceComboBox == null || GlossSourceComboBox == null ||
                MetallicSourceComboBox == null || HeightSourceComboBox == null ||
                AOProcessingComboBox == null || AOBiasSlider == null ||
                AOPercentileSlider == null ||
                GlossToksvigCheckBox == null || GlossToksvigPowerSlider == null ||
                CompressionFormatComboBox == null || QualityLevelSlider == null ||
                UASTCQualitySlider == null || MipmapCountSlider == null ||
                FilterTypeComboBox == null || ToksvigCalculationModeComboBox == null ||
                ToksvigMinMipLevelSlider == null || ToksvigEnergyPreservingCheckBox == null) return;
            if (currentORMTexture == null) return;

            // Sources
            currentORMTexture.AOSource = AOSourceComboBox.SelectedItem as TextureResource;
            currentORMTexture.GlossSource = GlossSourceComboBox.SelectedItem as TextureResource;
            currentORMTexture.MetallicSource = MetallicSourceComboBox.SelectedItem as TextureResource;
            currentORMTexture.HeightSource = HeightSourceComboBox.SelectedItem as TextureResource;

            // AO settings
            currentORMTexture.AOProcessingMode = (AOProcessingMode)AOProcessingComboBox.SelectedIndex;
            currentORMTexture.AOBias = (float)AOBiasSlider.Value;
            currentORMTexture.AOPercentile = (float)AOPercentileSlider.Value;

            // Gloss settings
            currentORMTexture.GlossToksvigEnabled = GlossToksvigCheckBox.IsChecked ?? false;
            currentORMTexture.GlossToksvigPower = (float)GlossToksvigPowerSlider.Value;
            currentORMTexture.GlossToksvigMinMipLevel = (int)ToksvigMinMipLevelSlider.Value;
            currentORMTexture.GlossToksvigEnergyPreserving = ToksvigEnergyPreservingCheckBox.IsChecked ?? true;

            // Toksvig calculation mode - handle null case
            if (ToksvigCalculationModeComboBox.SelectedItem != null) {
                currentORMTexture.GlossToksvigCalculationMode = (ToksvigCalculationMode)ToksvigCalculationModeComboBox.SelectedItem;
            }

            // Compression settings
            if (CompressionFormatComboBox.SelectedItem != null) {
                currentORMTexture.CompressionFormat = (CompressionFormat)CompressionFormatComboBox.SelectedItem;
            }
            currentORMTexture.QualityLevel = (int)QualityLevelSlider.Value;
            currentORMTexture.UASTCQuality = (int)UASTCQualitySlider.Value;

            // Mipmap settings
            currentORMTexture.MipmapCount = (int)MipmapCountSlider.Value;
            if (FilterTypeComboBox.SelectedItem != null) {
                currentORMTexture.FilterType = (FilterType)FilterTypeComboBox.SelectedItem;
            }
        }

        /// <summary>
        /// Обновляет статус готовности
        /// </summary>
        private void UpdateStatus() {
            // Early return if controls not initialized yet
            if (StatusText == null || MissingChannelsText == null || PackConvertButton == null) return;
            if (currentORMTexture == null) return;

            SaveORMSettings();

            var missing = currentORMTexture.GetMissingChannels();

            if (missing.Count == 0) {
                StatusText.Text = "✓ All required channels configured";
                StatusText.Foreground = System.Windows.Media.Brushes.Green;
                MissingChannelsText.Text = "";
                PackConvertButton.IsEnabled = true;
            } else {
                StatusText.Text = "⚠ Some channels use default values";
                StatusText.Foreground = System.Windows.Media.Brushes.Orange;
                MissingChannelsText.Text = $"Missing sources: {string.Join(", ", missing)}";
                PackConvertButton.IsEnabled = true; // Разрешаем упаковку с константами
            }
        }

        // Event handlers
        private void PackingModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            // Early return if controls not initialized yet (happens during XAML parsing with IsSelected="True")
            if (MetallicPanel == null || HeightPanel == null || ModeDescription == null) return;
            if (currentORMTexture == null || PackingModeComboBox.SelectedItem == null) return;

            var tag = ((ComboBoxItem)PackingModeComboBox.SelectedItem).Tag.ToString();
            currentORMTexture.PackingMode = tag switch {
                "OG" => ChannelPackingMode.OG,
                "OGM" => ChannelPackingMode.OGM,
                "OGMH" => ChannelPackingMode.OGMH,
                _ => ChannelPackingMode.OGM
            };

            // Update UI visibility
            MetallicPanel.Visibility = currentORMTexture.PackingMode >= ChannelPackingMode.OGM
                ? Visibility.Visible : Visibility.Collapsed;
            HeightPanel.Visibility = currentORMTexture.PackingMode == ChannelPackingMode.OGMH
                ? Visibility.Visible : Visibility.Collapsed;

            ModeDescription.Text = currentORMTexture.PackingModeDescription;

            UpdateStatus();
        }

        private void SourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            UpdateStatus();
        }

        private void AOProcessing_Changed(object sender, SelectionChangedEventArgs e) {
            // Early return if controls not initialized yet
            if (AOBiasPanel == null || AOPercentilePanel == null) return;
            if (AOProcessingComboBox.SelectedItem == null) return;

            var mode = ((ComboBoxItem)AOProcessingComboBox.SelectedItem).Tag.ToString();

            AOBiasPanel.Visibility = mode == "BiasedDarkening" ? Visibility.Visible : Visibility.Collapsed;
            AOPercentilePanel.Visibility = mode == "Percentile" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AOBias_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            UpdateStatus();
        }

        private void GlossToksvig_Changed(object sender, RoutedEventArgs e) {
            // Early return if controls not initialized yet
            if (GlossToksvigPanel == null || GlossToksvigCheckBox == null) return;

            GlossToksvigPanel.Visibility = (GlossToksvigCheckBox.IsChecked ?? false)
                ? Visibility.Visible : Visibility.Collapsed;
            UpdateStatus();
        }

        private void CompressionFormat_Changed(object sender, SelectionChangedEventArgs e) {
            // Early return if controls not initialized yet
            if (ETC1SPanel == null || UASTCPanel == null || CompressionFormatComboBox == null) return;
            if (CompressionFormatComboBox.SelectedItem == null) return;

            var format = (CompressionFormat)CompressionFormatComboBox.SelectedItem;

            // Show/hide panels based on compression format
            if (format == CompressionFormat.ETC1S) {
                ETC1SPanel.Visibility = Visibility.Visible;
                UASTCPanel.Visibility = Visibility.Collapsed;
                Logger.Info("Switched to ETC1S compression format");
            } else if (format == CompressionFormat.UASTC) {
                ETC1SPanel.Visibility = Visibility.Collapsed;
                UASTCPanel.Visibility = Visibility.Visible;
                Logger.Info("Switched to UASTC compression format");
            }

            UpdateStatus();
        }

        // Pack & Convert button
        private async void PackConvert_Click(object sender, RoutedEventArgs e) {
            if (currentORMTexture == null || mainWindow == null) return;

            SaveORMSettings();

            try {
                PackConvertButton.IsEnabled = false;
                StatusText.Text = "Packing channels...";

                // Создаем ChannelPackingSettings
                var packingSettings = CreatePackingSettings();

                if (!packingSettings.Validate(out var error)) {
                    MessageBox.Show($"Invalid packing settings: {error}", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Создаем диалог сохранения
                var saveDialog = new Microsoft.Win32.SaveFileDialog {
                    Filter = "KTX2 files (*.ktx2)|*.ktx2",
                    FileName = $"{(currentORMTexture.Name ?? "orm_texture").Replace("[ORM Texture - Not Packed]", "packed_orm")}.ktx2"
                };

                if (saveDialog.ShowDialog() != true) {
                    StatusText.Text = "Canceled";
                    return;
                }

                // Создаем пайплайн и упаковываем
                var pipeline = new TextureConversionPipeline();

                // Create compression settings based on selected format
                var compressionSettings = currentORMTexture.CompressionFormat == CompressionFormat.UASTC
                    ? CompressionSettings.CreateUASTCDefault()
                    : CompressionSettings.CreateETC1SDefault();

                // Apply user settings
                compressionSettings.CompressionFormat = currentORMTexture.CompressionFormat;
                compressionSettings.QualityLevel = currentORMTexture.QualityLevel;
                compressionSettings.UASTCQuality = currentORMTexture.UASTCQuality;
                compressionSettings.ColorSpace = ColorSpace.Linear; // КРИТИЧНО для ORM!

                // Mipmap settings
                compressionSettings.GenerateMipmaps = true;
                compressionSettings.UseCustomMipmaps = currentORMTexture.GlossToksvigEnabled; // Use custom mipmaps if Toksvig is enabled

                StatusText.Text = "Converting to KTX2...";

                var result = await pipeline.ConvertPackedTextureAsync(
                    packingSettings,
                    saveDialog.FileName,
                    compressionSettings
                );

                if (result.Success) {
                    MessageBox.Show($"ORM texture packed successfully!\n\nOutput: {result.OutputPath}\nMip levels: {result.MipLevels}\nDuration: {result.Duration.TotalSeconds:F1}s",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                    StatusText.Text = "✓ Packing complete";
                    StatusText.Foreground = System.Windows.Media.Brushes.Green;

                    // Обновляем имя ORM текстуры
                    currentORMTexture.Name = Path.GetFileNameWithoutExtension(saveDialog.FileName);
                    currentORMTexture.Path = saveDialog.FileName;
                } else {
                    MessageBox.Show($"Packing failed: {result.Error}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusText.Text = "✗ Packing failed";
                    StatusText.Foreground = System.Windows.Media.Brushes.Red;
                }
            } catch (Exception ex) {
                Logger.Error(ex, "ORM packing failed");
                MessageBox.Show($"Error: {ex.Message}", "Packing Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "✗ Error occurred";
                StatusText.Foreground = System.Windows.Media.Brushes.Red;
            } finally {
                PackConvertButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// Создает ChannelPackingSettings из текущих настроек
        /// </summary>
        private ChannelPackingSettings CreatePackingSettings() {
            var settings = ChannelPackingSettings.CreateDefault(currentORMTexture!.PackingMode);

            // Set mipmap generation settings
            settings.MipmapCount = currentORMTexture.MipmapCount == 0 ? -1 : currentORMTexture.MipmapCount; // 0 = auto = -1
            settings.MipGenerationProfile = new MipGenerationProfile {
                Filter = currentORMTexture.FilterType,
                ApplyGammaCorrection = false, // Linear space for ORM
                IncludeLastLevel = true,
                MinMipSize = 1
            };

            // AO channel (Red or RGB)
            if (settings.RedChannel != null) {
                settings.RedChannel.SourcePath = currentORMTexture.AOSource?.Path;
                settings.RedChannel.DefaultValue = 1.0f; // White (no occlusion)
                settings.RedChannel.AOProcessingMode = currentORMTexture.AOProcessingMode;
                settings.RedChannel.AOBias = currentORMTexture.AOBias;
                settings.RedChannel.AOPercentile = currentORMTexture.AOPercentile;
            }

            // Gloss channel (Green or Alpha)
            if (settings.GreenChannel != null || settings.AlphaChannel != null) {
                var glossChannel = settings.GreenChannel ?? settings.AlphaChannel;
                if (glossChannel != null) {
                    glossChannel.SourcePath = currentORMTexture.GlossSource?.Path;
                    glossChannel.DefaultValue = 0.5f; // Medium gloss
                    glossChannel.ApplyToksvig = currentORMTexture.GlossToksvigEnabled;

                    if (glossChannel.ApplyToksvig) {
                        glossChannel.ToksvigSettings = new ToksvigSettings {
                            Enabled = true,
                            CompositePower = currentORMTexture.GlossToksvigPower,
                            CalculationMode = currentORMTexture.GlossToksvigCalculationMode,
                            MinToksvigMipLevel = currentORMTexture.GlossToksvigMinMipLevel,
                            UseEnergyPreserving = currentORMTexture.GlossToksvigEnergyPreserving,
                            SmoothVariance = true,
                            VarianceThreshold = 0.002f
                        };
                    }
                }
            }

            // Metallic channel (Blue)
            if (settings.BlueChannel != null) {
                settings.BlueChannel.SourcePath = currentORMTexture.MetallicSource?.Path;
                settings.BlueChannel.DefaultValue = 0.0f; // Non-metallic by default
            }

            // Height channel (Alpha in OGMH mode)
            if (settings.AlphaChannel != null && currentORMTexture.PackingMode == ChannelPackingMode.OGMH) {
                settings.AlphaChannel.SourcePath = currentORMTexture.HeightSource?.Path;
                settings.AlphaChannel.DefaultValue = 0.5f; // Middle height
            }

            return settings;
        }
    }
}
