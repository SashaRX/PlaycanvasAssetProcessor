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
            var emptyItem = new TextureResource { Name = "[None - Use Default Value]" };

            AOSourceComboBox.ItemsSource = new[] { emptyItem }.Concat(availableTextures).ToList();
            GlossSourceComboBox.ItemsSource = new[] { emptyItem }.Concat(availableTextures).ToList();
            MetallicSourceComboBox.ItemsSource = new[] { emptyItem }.Concat(availableTextures).ToList();
            HeightSourceComboBox.ItemsSource = new[] { emptyItem }.Concat(availableTextures).ToList();

            AOSourceComboBox.SelectedIndex = 0;
            GlossSourceComboBox.SelectedIndex = 0;
            MetallicSourceComboBox.SelectedIndex = 0;
            HeightSourceComboBox.SelectedIndex = 0;
        }

        /// <summary>
        /// Загружает настройки из ORM текстуры
        /// </summary>
        private void LoadORMSettings() {
            if (currentORMTexture == null) return;

            // Packing mode
            PackingModeComboBox.SelectedIndex = (int)currentORMTexture.PackingMode - 1;

            // Sources
            if (currentORMTexture.AOSource != null)
                AOSourceComboBox.SelectedItem = currentORMTexture.AOSource;
            if (currentORMTexture.GlossSource != null)
                GlossSourceComboBox.SelectedItem = currentORMTexture.GlossSource;
            if (currentORMTexture.MetallicSource != null)
                MetallicSourceComboBox.SelectedItem = currentORMTexture.MetallicSource;
            if (currentORMTexture.HeightSource != null)
                HeightSourceComboBox.SelectedItem = currentORMTexture.HeightSource;

            // AO settings
            AOProcessingComboBox.SelectedIndex = (int)currentORMTexture.AOProcessingMode;
            AOBiasSlider.Value = currentORMTexture.AOBias;
            AOPercentileSlider.Value = currentORMTexture.AOPercentile;
            AODefaultSlider.Value = currentORMTexture.AODefaultValue;

            // Gloss settings
            GlossToksvigCheckBox.IsChecked = currentORMTexture.GlossToksvigEnabled;
            GlossToksvigPowerSlider.Value = currentORMTexture.GlossToksvigPower;
            GlossDefaultSlider.Value = currentORMTexture.GlossDefaultValue;

            // Metallic/Height defaults
            MetallicDefaultSlider.Value = currentORMTexture.MetallicDefaultValue;
            HeightDefaultSlider.Value = currentORMTexture.HeightDefaultValue;

            UpdateStatus();
        }

        /// <summary>
        /// Сохраняет настройки в ORM текстуру
        /// </summary>
        private void SaveORMSettings() {
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
            currentORMTexture.AODefaultValue = (float)AODefaultSlider.Value;

            // Gloss settings
            currentORMTexture.GlossToksvigEnabled = GlossToksvigCheckBox.IsChecked ?? false;
            currentORMTexture.GlossToksvigPower = (float)GlossToksvigPowerSlider.Value;
            currentORMTexture.GlossDefaultValue = (float)GlossDefaultSlider.Value;

            // Metallic/Height defaults
            currentORMTexture.MetallicDefaultValue = (float)MetallicDefaultSlider.Value;
            currentORMTexture.HeightDefaultValue = (float)HeightDefaultSlider.Value;
        }

        /// <summary>
        /// Обновляет статус готовности
        /// </summary>
        private void UpdateStatus() {
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
            if (AOProcessingComboBox.SelectedItem == null) return;

            var mode = ((ComboBoxItem)AOProcessingComboBox.SelectedItem).Tag.ToString();

            AOBiasPanel.Visibility = mode == "BiasedDarkening" ? Visibility.Visible : Visibility.Collapsed;
            AOPercentilePanel.Visibility = mode == "Percentile" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AOBias_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            UpdateStatus();
        }

        private void GlossToksvig_Changed(object sender, RoutedEventArgs e) {
            GlossToksvigPanel.Visibility = (GlossToksvigCheckBox.IsChecked ?? false)
                ? Visibility.Visible : Visibility.Collapsed;
            UpdateStatus();
        }

        // Auto-detect handlers
        private void AutoDetectAO_Click(object sender, RoutedEventArgs e) {
            AutoDetectChannel(ChannelType.AmbientOcclusion, AOSourceComboBox);
        }

        private void AutoDetectGloss_Click(object sender, RoutedEventArgs e) {
            AutoDetectChannel(ChannelType.Gloss, GlossSourceComboBox);
        }

        private void AutoDetectMetallic_Click(object sender, RoutedEventArgs e) {
            AutoDetectChannel(ChannelType.Metallic, MetallicSourceComboBox);
        }

        private void AutoDetectHeight_Click(object sender, RoutedEventArgs e) {
            AutoDetectChannel(ChannelType.Height, HeightSourceComboBox);
        }

        private void AutoDetectChannel(ChannelType channelType, ComboBox targetComboBox) {
            if (availableTextures.Count == 0) {
                MessageBox.Show("No textures available for detection", "Auto-Detect", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Используем первую попавшуюся текстуру как базу для поиска
            var basePath = availableTextures[0].LocalPath;

            if (string.IsNullOrEmpty(basePath)) {
                MessageBox.Show("Base texture path is not available", "Auto-Detect", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var detector = new ORMTextureDetector();
            var foundPath = detector.FindTextureByType(basePath, channelType, validateDimensions: false);

            if (!string.IsNullOrEmpty(foundPath)) {
                // Ищем в списке текстуру с таким путем
                var foundTexture = availableTextures.FirstOrDefault(t => t.LocalPath == foundPath);

                if (foundTexture != null) {
                    targetComboBox.SelectedItem = foundTexture;
                    MessageBox.Show($"Found: {foundTexture.Name}", "Auto-Detect", MessageBoxButton.OK, MessageBoxImage.Information);
                } else {
                    MessageBox.Show($"Texture found but not in list: {Path.GetFileName(foundPath)}", "Auto-Detect", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            } else {
                MessageBox.Show($"No {channelType} texture found with common naming patterns", "Auto-Detect", MessageBoxButton.OK, MessageBoxImage.Information);
            }
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
                    FileName = $"{currentORMTexture.Name.Replace("[ORM Texture - Not Packed]", "packed_orm")}.ktx2"
                };

                if (saveDialog.ShowDialog() != true) {
                    StatusText.Text = "Canceled";
                    return;
                }

                // Создаем пайплайн и упаковываем
                var pipeline = new TextureConversionPipeline();
                var compressionSettings = CompressionSettings.CreateETC1SDefault();
                compressionSettings.QualityLevel = 192; // Высокое качество для ORM
                compressionSettings.ColorSpace = ColorSpace.Linear; // КРИТИЧНО!

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
                    currentORMTexture.LocalPath = saveDialog.FileName;
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

            // AO channel
            if (settings.RedChannel != null) {
                settings.RedChannel.SourcePath = currentORMTexture.AOSource?.LocalPath;
                settings.RedChannel.DefaultValue = currentORMTexture.AODefaultValue;
                settings.RedChannel.AOProcessingMode = currentORMTexture.AOProcessingMode;
                settings.RedChannel.AOBias = currentORMTexture.AOBias;
                settings.RedChannel.AOPercentile = currentORMTexture.AOPercentile;
            }

            // Gloss channel
            if (settings.GreenChannel != null || settings.AlphaChannel != null) {
                var glossChannel = settings.GreenChannel ?? settings.AlphaChannel;
                if (glossChannel != null) {
                    glossChannel.SourcePath = currentORMTexture.GlossSource?.LocalPath;
                    glossChannel.DefaultValue = currentORMTexture.GlossDefaultValue;
                    glossChannel.ApplyToksvig = currentORMTexture.GlossToksvigEnabled;

                    if (glossChannel.ApplyToksvig) {
                        glossChannel.ToksvigSettings = new ToksvigSettings {
                            Enabled = true,
                            CompositePower = currentORMTexture.GlossToksvigPower,
                            CalculationMode = ToksvigCalculationMode.Simplified
                        };
                    }
                }
            }

            // Metallic channel
            if (settings.BlueChannel != null) {
                settings.BlueChannel.SourcePath = currentORMTexture.MetallicSource?.LocalPath;
                settings.BlueChannel.DefaultValue = currentORMTexture.MetallicDefaultValue;
            }

            // Height channel
            if (settings.AlphaChannel != null && currentORMTexture.PackingMode == ChannelPackingMode.OGMH) {
                settings.AlphaChannel.SourcePath = currentORMTexture.HeightSource?.LocalPath;
                settings.AlphaChannel.DefaultValue = currentORMTexture.HeightDefaultValue;
            }

            return settings;
        }
    }
}
