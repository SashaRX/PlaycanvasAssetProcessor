using AssetProcessor.Resources;
using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureConversion.MipGeneration;
using AssetProcessor.TextureConversion.Pipeline;
using AssetProcessor.TextureConversion.Settings;
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

            Logger.Info($"Initialize: availableTextures count = {availableTextures.Count}");

            // Обновляем ComboBox источниками (но не сбрасываем выбранные значения!)
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

            // CRITICAL: Use same list instance for all ComboBoxes so SelectedItem reference matching works
            AOSourceComboBox.ItemsSource = availableTextures;
            GlossSourceComboBox.ItemsSource = availableTextures;
            MetallicSourceComboBox.ItemsSource = availableTextures;
            HeightSourceComboBox.ItemsSource = availableTextures;

            // NOTE: DO NOT reset SelectedIndex here! LoadORMSettings will set it via Dispatcher.BeginInvoke
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

            // CRITICAL: Capture references IMMEDIATELY before UI changes trigger SaveORMSettings
            var aoSource = currentORMTexture.AOSource;
            var glossSource = currentORMTexture.GlossSource;
            var metallicSource = currentORMTexture.MetallicSource;
            var heightSource = currentORMTexture.HeightSource;
            var compressionFormat = currentORMTexture.CompressionFormat;
            var toksvigCalculationMode = currentORMTexture.GlossToksvigCalculationMode;
            var aoFilterType = currentORMTexture.AOFilterType;
            var glossFilterType = currentORMTexture.GlossFilterType;
            var metallicFilterType = currentORMTexture.MetallicFilterType;

            Logger.Info($"  Captured aoSource: {aoSource?.Name ?? "null"} (ID={aoSource?.ID})");
            Logger.Info($"  Captured glossSource: {glossSource?.Name ?? "null"} (ID={glossSource?.ID})");

            // Packing mode
            PackingModeComboBox.SelectedIndex = (int)currentORMTexture.PackingMode - 1;

            // AO settings (None=0, BiasedDarkening=1, Percentile=2)
            AOProcessingComboBox.SelectedIndex = currentORMTexture.AOProcessingMode switch {
                AOProcessingMode.None => 0,
                AOProcessingMode.BiasedDarkening => 1,
                AOProcessingMode.Percentile => 2,
                _ => 0
            };
            AOBiasSlider.Value = currentORMTexture.AOBias;
            AOPercentileSlider.Value = currentORMTexture.AOPercentile;

            // Gloss settings
            GlossToksvigCheckBox.IsChecked = currentORMTexture.GlossToksvigEnabled;
            GlossToksvigPowerSlider.Value = currentORMTexture.GlossToksvigPower;
            ToksvigMinMipLevelSlider.Value = currentORMTexture.GlossToksvigMinMipLevel;
            ToksvigEnergyPreservingCheckBox.IsChecked = currentORMTexture.GlossToksvigEnergyPreserving;
            ToksvigSmoothVarianceCheckBox.IsChecked = currentORMTexture.GlossToksvigSmoothVariance;

            // Metallic settings
            MetallicProcessingModeComboBox.SelectedIndex = currentORMTexture.MetallicProcessingMode switch {
                AOProcessingMode.None => 0,
                AOProcessingMode.BiasedDarkening => 1,
                AOProcessingMode.Percentile => 2,
                _ => 0
            };
            MetallicBiasSlider.Value = currentORMTexture.MetallicBias;
            MetallicPercentileSlider.Value = currentORMTexture.MetallicPercentile;

            // Compression settings
            CompressLevelSlider.Value = currentORMTexture.CompressLevel;
            QualityLevelSlider.Value = currentORMTexture.QualityLevel;
            UASTCQualitySlider.Value = currentORMTexture.UASTCQuality;

            // UASTC RDO settings
            EnableRDOCheckBox.IsChecked = currentORMTexture.EnableRDO;
            RDOLambdaSlider.Value = currentORMTexture.RDOLambda;

            // Perceptual
            PerceptualCheckBox.IsChecked = currentORMTexture.Perceptual;

            // Supercompression
            EnableSupercompressionCheckBox.IsChecked = currentORMTexture.EnableSupercompression;
            SupercompressionLevelSlider.Value = currentORMTexture.SupercompressionLevel;

            // CRITICAL: Use Dispatcher.BeginInvoke to set ALL ComboBox selections after UI is fully loaded
            // Variables were captured at the TOP of this method to avoid SaveORMSettings overwriting them
            Dispatcher.BeginInvoke(new Action(() => {
                Logger.Info("=== Dispatcher.BeginInvoke: Setting ComboBox selections ===");

                // Sources - find by ID and set
                if (aoSource != null) {
                    var found = availableTextures.FirstOrDefault(t => t.ID == aoSource.ID);
                    Logger.Info($"  AO: Looking for ID={aoSource.ID}, found={found?.Name ?? "null"}");
                    if (found != null) {
                        AOSourceComboBox.SelectedItem = found;
                        Logger.Info($"  AO: Set SelectedItem to {found.Name}, result={(AOSourceComboBox.SelectedItem as TextureResource)?.Name ?? "null"}");
                    }
                }

                if (glossSource != null) {
                    var found = availableTextures.FirstOrDefault(t => t.ID == glossSource.ID);
                    Logger.Info($"  Gloss: Looking for ID={glossSource.ID}, found={found?.Name ?? "null"}");
                    if (found != null) {
                        GlossSourceComboBox.SelectedItem = found;
                        Logger.Info($"  Gloss: Set SelectedItem to {found.Name}, result={(GlossSourceComboBox.SelectedItem as TextureResource)?.Name ?? "null"}");
                    }
                }

                if (metallicSource != null) {
                    var found = availableTextures.FirstOrDefault(t => t.ID == metallicSource.ID);
                    Logger.Info($"  Metallic: Looking for ID={metallicSource.ID}, found={found?.Name ?? "null"}");
                    if (found != null) {
                        MetallicSourceComboBox.SelectedItem = found;
                        Logger.Info($"  Metallic: Set SelectedItem to {found.Name}, result={(MetallicSourceComboBox.SelectedItem as TextureResource)?.Name ?? "null"}");
                    }
                }

                if (heightSource != null) {
                    var found = availableTextures.FirstOrDefault(t => t.ID == heightSource.ID);
                    if (found != null) HeightSourceComboBox.SelectedItem = found;
                }

                // Compression settings
                CompressionFormatComboBox.SelectedItem = compressionFormat;
                Logger.Info($"  CompressionFormat: Set to {compressionFormat}");

                // Toksvig calculation mode
                ToksvigCalculationModeComboBox.SelectedItem = toksvigCalculationMode;
                Logger.Info($"  ToksvigCalculationMode: Set to {toksvigCalculationMode}");

                // Filter types for each channel
                AOFilterTypeComboBox.SelectedItem = aoFilterType;
                GlossFilterTypeComboBox.SelectedItem = glossFilterType;
                MetallicFilterTypeComboBox.SelectedItem = metallicFilterType;

                Logger.Info("=== Dispatcher.BeginInvoke: DONE ===");
            }), System.Windows.Threading.DispatcherPriority.Loaded);

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
                MetallicProcessingModeComboBox == null || MetallicBiasSlider == null ||
                MetallicPercentileSlider == null ||
                CompressionFormatComboBox == null || QualityLevelSlider == null ||
                UASTCQualitySlider == null || ToksvigCalculationModeComboBox == null ||
                ToksvigMinMipLevelSlider == null || ToksvigEnergyPreservingCheckBox == null ||
                ToksvigSmoothVarianceCheckBox == null) return;
            if (currentORMTexture == null) return;

            // Sources
            currentORMTexture.AOSource = AOSourceComboBox.SelectedItem as TextureResource;
            currentORMTexture.GlossSource = GlossSourceComboBox.SelectedItem as TextureResource;
            currentORMTexture.MetallicSource = MetallicSourceComboBox.SelectedItem as TextureResource;
            currentORMTexture.HeightSource = HeightSourceComboBox.SelectedItem as TextureResource;

            // AO settings (None=0, BiasedDarkening=1, Percentile=2)
            currentORMTexture.AOProcessingMode = AOProcessingComboBox.SelectedIndex switch {
                0 => AOProcessingMode.None,
                1 => AOProcessingMode.BiasedDarkening,
                2 => AOProcessingMode.Percentile,
                _ => AOProcessingMode.None
            };
            currentORMTexture.AOBias = (float)AOBiasSlider.Value;
            currentORMTexture.AOPercentile = (float)AOPercentileSlider.Value;

            // Gloss settings
            currentORMTexture.GlossToksvigEnabled = GlossToksvigCheckBox.IsChecked ?? false;
            currentORMTexture.GlossToksvigPower = (float)GlossToksvigPowerSlider.Value;
            currentORMTexture.GlossToksvigMinMipLevel = (int)ToksvigMinMipLevelSlider.Value;
            currentORMTexture.GlossToksvigEnergyPreserving = ToksvigEnergyPreservingCheckBox.IsChecked ?? true;
            currentORMTexture.GlossToksvigSmoothVariance = ToksvigSmoothVarianceCheckBox.IsChecked ?? true;

            Logger.Info($"[SaveORMSettings] Gloss Toksvig: Enabled={currentORMTexture.GlossToksvigEnabled}, Power={currentORMTexture.GlossToksvigPower}, CheckBoxState={GlossToksvigCheckBox.IsChecked}");

            // Toksvig calculation mode - handle null case
            if (ToksvigCalculationModeComboBox.SelectedItem != null) {
                currentORMTexture.GlossToksvigCalculationMode = (ToksvigCalculationMode)ToksvigCalculationModeComboBox.SelectedItem;
            }

            // Metallic settings
            currentORMTexture.MetallicProcessingMode = MetallicProcessingModeComboBox.SelectedIndex switch {
                0 => AOProcessingMode.None,
                1 => AOProcessingMode.BiasedDarkening,
                2 => AOProcessingMode.Percentile,
                _ => AOProcessingMode.None
            };
            currentORMTexture.MetallicBias = (float)MetallicBiasSlider.Value;
            currentORMTexture.MetallicPercentile = (float)MetallicPercentileSlider.Value;

            // Compression settings
            if (CompressionFormatComboBox.SelectedItem != null) {
                currentORMTexture.CompressionFormat = (CompressionFormat)CompressionFormatComboBox.SelectedItem;
            }
            currentORMTexture.CompressLevel = (int)CompressLevelSlider.Value;
            currentORMTexture.QualityLevel = (int)QualityLevelSlider.Value;
            currentORMTexture.UASTCQuality = (int)UASTCQualitySlider.Value;

            // UASTC RDO settings
            currentORMTexture.EnableRDO = EnableRDOCheckBox.IsChecked ?? false;
            currentORMTexture.RDOLambda = (float)RDOLambdaSlider.Value;

            // Perceptual
            currentORMTexture.Perceptual = PerceptualCheckBox.IsChecked ?? false;

            // Supercompression
            currentORMTexture.EnableSupercompression = EnableSupercompressionCheckBox.IsChecked ?? false;
            currentORMTexture.SupercompressionLevel = (int)SupercompressionLevelSlider.Value;

            // Per-channel filter settings
            if (AOFilterTypeComboBox.SelectedItem != null) {
                currentORMTexture.AOFilterType = (FilterType)AOFilterTypeComboBox.SelectedItem;
            }
            if (GlossFilterTypeComboBox.SelectedItem != null) {
                currentORMTexture.GlossFilterType = (FilterType)GlossFilterTypeComboBox.SelectedItem;
            }
            if (MetallicFilterTypeComboBox.SelectedItem != null) {
                currentORMTexture.MetallicFilterType = (FilterType)MetallicFilterTypeComboBox.SelectedItem;
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
            if (MetallicPanel == null || HeightPanel == null) return;
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

            // None=hide both, BiasedDarkening=show bias, Percentile=show percentile
            AOBiasPanel.Visibility = mode == "BiasedDarkening" ? Visibility.Visible : Visibility.Collapsed;
            AOPercentilePanel.Visibility = mode == "Percentile" ? Visibility.Visible : Visibility.Collapsed;

            UpdateStatus();
        }

        private void MetallicProcessingModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            // Early return if controls not initialized yet
            if (MetallicBiasPanel == null || MetallicPercentilePanel == null) return;
            if (MetallicProcessingModeComboBox.SelectedItem == null) return;

            var mode = ((ComboBoxItem)MetallicProcessingModeComboBox.SelectedItem).Tag.ToString();

            // None=hide both, BiasedDarkening=show bias, Percentile=show percentile
            MetallicBiasPanel.Visibility = mode == "BiasedDarkening" ? Visibility.Visible : Visibility.Collapsed;
            MetallicPercentilePanel.Visibility = mode == "Percentile" ? Visibility.Visible : Visibility.Collapsed;

            UpdateStatus();
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

        private void EnableRDO_Changed(object sender, RoutedEventArgs e) {
            // Early return if controls not initialized yet
            if (RDOPanel == null || EnableRDOCheckBox == null) return;

            RDOPanel.Visibility = (EnableRDOCheckBox.IsChecked ?? false)
                ? Visibility.Visible : Visibility.Collapsed;
            UpdateStatus();
        }

        private void EnableSupercompression_Changed(object sender, RoutedEventArgs e) {
            // Early return if controls not initialized yet
            if (SupercompressionPanel == null || EnableSupercompressionCheckBox == null) return;

            SupercompressionPanel.Visibility = (EnableSupercompressionCheckBox.IsChecked ?? false)
                ? Visibility.Visible : Visibility.Collapsed;
            UpdateStatus();
        }

        // Pack & Convert button
        private async void PackConvert_Click(object sender, RoutedEventArgs e) {
            if (currentORMTexture == null || mainWindow == null) return;

            SaveORMSettings();

            Logger.Info($"[PackConvert_Click] Gloss Toksvig settings AFTER SaveORMSettings: Enabled={currentORMTexture.GlossToksvigEnabled}, Power={currentORMTexture.GlossToksvigPower}");

            try {
                PackConvertButton.IsEnabled = false;
                StatusText.Text = "Packing channels...";

                // Создаем ChannelPackingSettings
                var packingSettings = CreatePackingSettings();

                if (!packingSettings.Validate(out var error)) {
                    MessageBox.Show($"Invalid packing settings: {error}", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Генерируем путь для сохранения - в той же папке, что и исходные текстуры
                string? outputDir = null;
                if (!string.IsNullOrEmpty(currentORMTexture.AOSource?.Path)) {
                    outputDir = Path.GetDirectoryName(currentORMTexture.AOSource.Path);
                } else if (!string.IsNullOrEmpty(currentORMTexture.GlossSource?.Path)) {
                    outputDir = Path.GetDirectoryName(currentORMTexture.GlossSource.Path);
                } else if (!string.IsNullOrEmpty(currentORMTexture.MetallicSource?.Path)) {
                    outputDir = Path.GetDirectoryName(currentORMTexture.MetallicSource.Path);
                }

                if (string.IsNullOrEmpty(outputDir)) {
                    MessageBox.Show("Cannot determine output directory - no source textures specified", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var fileName = currentORMTexture.Name?.Replace("[ORM Texture - Not Packed]", "").Trim() ?? "orm_packed";
                var outputPath = Path.Combine(outputDir, $"{fileName}.ktx2");

                // Загружаем настройки для получения ktxPath
                var globalSettings = TextureConversionSettingsManager.LoadSettings();
                var ktxPath = string.IsNullOrWhiteSpace(globalSettings.KtxExecutablePath)
                    ? "ktx"
                    : globalSettings.KtxExecutablePath;

                // Создаем пайплайн с ktxPath
                var pipeline = new TextureConversionPipeline(ktxPath);

                // Create compression settings based on selected format
                var compressionSettings = currentORMTexture.CompressionFormat == CompressionFormat.UASTC
                    ? CompressionSettings.CreateUASTCDefault()
                    : CompressionSettings.CreateETC1SDefault();

                // Apply user settings
                compressionSettings.CompressionFormat = currentORMTexture.CompressionFormat;
                compressionSettings.CompressionLevel = currentORMTexture.CompressLevel; // ETC1S compress level (0-5)
                compressionSettings.QualityLevel = currentORMTexture.QualityLevel;
                compressionSettings.UASTCQuality = currentORMTexture.UASTCQuality;

                // RDO settings (different for UASTC vs ETC1S)
                if (currentORMTexture.CompressionFormat == CompressionFormat.UASTC) {
                    compressionSettings.UseUASTCRDO = currentORMTexture.EnableRDO;
                    compressionSettings.UASTCRDOQuality = currentORMTexture.RDOLambda;
                } else {
                    compressionSettings.UseETC1SRDO = currentORMTexture.EnableRDO;
                    compressionSettings.ETC1SRDOLambda = currentORMTexture.RDOLambda;
                }

                compressionSettings.PerceptualMode = currentORMTexture.Perceptual;

                // Supercompression
                if (currentORMTexture.EnableSupercompression) {
                    compressionSettings.KTX2Supercompression = KTX2SupercompressionType.Zstandard;
                    compressionSettings.KTX2ZstdLevel = currentORMTexture.SupercompressionLevel;
                } else {
                    compressionSettings.KTX2Supercompression = KTX2SupercompressionType.None;
                }

                compressionSettings.ColorSpace = ColorSpace.Linear; // КРИТИЧНО для ORM!

                // Mipmap settings
                compressionSettings.GenerateMipmaps = true;
                compressionSettings.UseCustomMipmaps = currentORMTexture.GlossToksvigEnabled; // Use custom mipmaps if Toksvig is enabled

                StatusText.Text = "Converting to KTX2...";

                var result = await pipeline.ConvertPackedTextureAsync(
                    packingSettings,
                    outputPath,
                    compressionSettings
                );

                if (result.Success) {
                    MessageBox.Show($"ORM texture packed successfully!\n\nOutput: {result.OutputPath}\nMip levels: {result.MipLevels}\nDuration: {result.Duration.TotalSeconds:F1}s",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                    StatusText.Text = "✓ Packing complete";
                    StatusText.Foreground = System.Windows.Media.Brushes.Green;

                    // Обновляем имя ORM текстуры и устанавливаем Status = "Converted"
                    currentORMTexture.Name = Path.GetFileNameWithoutExtension(outputPath);
                    currentORMTexture.Path = outputPath;
                    currentORMTexture.Status = "Converted";  // FIX: Update status to show green color in DataGrid
                    currentORMTexture.CompressionFormat = compressionSettings.CompressionFormat;
                    currentORMTexture.MipmapCount = result.MipLevels;

                    Logger.Info($"ORM texture updated: Status={currentORMTexture.Status}, MipLevels={result.MipLevels}");

                    // Refresh MainWindow to update preview and row color
                    Logger.Info("Calling MainWindow.RefreshCurrentTexture() to update preview and row color");
                    mainWindow?.RefreshCurrentTexture();
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

            // Set mipmap generation settings - use auto mipmap count
            settings.MipmapCount = -1; // Auto
            settings.MipGenerationProfile = new MipGenerationProfile {
                Filter = FilterType.Kaiser, // Default, will be overridden per-channel
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

                // Set filter via MipProfile
                if (settings.RedChannel.MipProfile == null) {
                    settings.RedChannel.MipProfile = MipGenerationProfile.CreateDefault(TextureType.AmbientOcclusion);
                }
                settings.RedChannel.MipProfile.Filter = currentORMTexture.AOFilterType;
            }

            // Gloss channel (Green or Alpha)
            if (settings.GreenChannel != null || settings.AlphaChannel != null) {
                var glossChannel = settings.GreenChannel ?? settings.AlphaChannel;
                if (glossChannel != null) {
                    glossChannel.SourcePath = currentORMTexture.GlossSource?.Path;
                    glossChannel.DefaultValue = 0.5f; // Medium gloss
                    glossChannel.ApplyToksvig = currentORMTexture.GlossToksvigEnabled;
                    glossChannel.AOProcessingMode = AOProcessingMode.None; // AO processing only for AO channel

                    Logger.Info($"  Gloss channel settings: ApplyToksvig={glossChannel.ApplyToksvig}, GlossToksvigEnabled={currentORMTexture.GlossToksvigEnabled}");

                    // Set filter via MipProfile
                    if (glossChannel.MipProfile == null) {
                        glossChannel.MipProfile = MipGenerationProfile.CreateDefault(TextureType.Gloss);
                    }
                    glossChannel.MipProfile.Filter = currentORMTexture.GlossFilterType;

                    if (glossChannel.ApplyToksvig) {
                        glossChannel.ToksvigSettings = new ToksvigSettings {
                            Enabled = true,
                            CompositePower = currentORMTexture.GlossToksvigPower,
                            CalculationMode = currentORMTexture.GlossToksvigCalculationMode,
                            MinToksvigMipLevel = currentORMTexture.GlossToksvigMinMipLevel,
                            UseEnergyPreserving = currentORMTexture.GlossToksvigEnergyPreserving,
                            SmoothVariance = currentORMTexture.GlossToksvigSmoothVariance,
                            VarianceThreshold = 0.002f
                        };
                        Logger.Info($"  Toksvig settings created: Power={glossChannel.ToksvigSettings.CompositePower}, Mode={glossChannel.ToksvigSettings.CalculationMode}");
                    } else {
                        Logger.Warn("  Toksvig is DISABLED - checkbox not checked!");
                    }
                }
            }

            // Metallic channel (Blue)
            if (settings.BlueChannel != null) {
                settings.BlueChannel.SourcePath = currentORMTexture.MetallicSource?.Path;
                settings.BlueChannel.DefaultValue = 0.0f; // Non-metallic by default
                settings.BlueChannel.AOProcessingMode = currentORMTexture.MetallicProcessingMode;
                settings.BlueChannel.AOBias = currentORMTexture.MetallicBias;
                settings.BlueChannel.AOPercentile = currentORMTexture.MetallicPercentile;

                // Set filter via MipProfile
                if (settings.BlueChannel.MipProfile == null) {
                    settings.BlueChannel.MipProfile = MipGenerationProfile.CreateDefault(TextureType.Metallic);
                }
                settings.BlueChannel.MipProfile.Filter = currentORMTexture.MetallicFilterType;
            }

            // Height channel (Alpha in OGMH mode)
            if (settings.AlphaChannel != null && currentORMTexture.PackingMode == ChannelPackingMode.OGMH) {
                settings.AlphaChannel.SourcePath = currentORMTexture.HeightSource?.Path;
                settings.AlphaChannel.DefaultValue = 0.5f; // Middle height
                settings.AlphaChannel.AOProcessingMode = AOProcessingMode.None; // AO processing only for AO channel
            }

            return settings;
        }
    }
}
