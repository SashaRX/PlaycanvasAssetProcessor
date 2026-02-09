using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AssetProcessor.Services;
using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureConversion.Settings;
using NLog;

namespace AssetProcessor.Controls {
    /// <summary>
    /// UserControl for texture conversion settings.
    /// Split into partial classes:
    /// - TextureConversionSettingsPanel.xaml.cs (core: init, visibility, event handlers)
    /// - TextureConversionSettingsPanel.Presets.cs (preset loading and management)
    /// - TextureConversionSettingsPanel.Getters.cs (settings getters and loaders)
    /// - TextureConversionSettingsPanel.NormalMap.cs (normal map auto-detection)
    /// </summary>
    public partial class TextureConversionSettingsPanel : UserControl, ITextureConversionSettingsProvider {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private bool _isLoading = false;
        private readonly PresetManager _presetManager = new();
        private ConversionSettingsManager? _conversionSettingsManager;
        private string? _currentTexturePath;

        public event EventHandler? SettingsChanged;
        public event EventHandler? ConvertRequested;
        public event EventHandler? AutoDetectRequested;

        public TextureConversionSettingsPanel() {
            InitializeComponent();
            InitializeDefaults();
        }

        public void SetConversionSettingsManager(ConversionSettingsManager manager) {
            _conversionSettingsManager = manager;

            if (manager != null) {
                var presets = ConversionSettingsSchema.GetPredefinedPresets();
                var presetNames = new List<string> { "Custom" };
                presetNames.AddRange(presets.Select(p => p.Name));

                PresetComboBox.ItemsSource = presetNames;
                if (PresetComboBox.Items.Count > 0) {
                    PresetComboBox.SelectedIndex = 0;
                }
            }
        }

        public void BeginLoadingSettings() {
            _isLoading = true;
        }

        public void EndLoadingSettings() {
            _isLoading = false;
        }

        // ============================================
        // INITIALIZATION
        // ============================================

        private void InitializePresets() {
            var presets = _presetManager.GetAllPresets();
            PresetComboBox.ItemsSource = presets;
            PresetComboBox.DisplayMemberPath = "Name";
            if (presets.Count > 0) {
                PresetComboBox.SelectedIndex = 0;
            }
        }

        private void InitializeDefaults() {
            _isLoading = true;

            // Compression
            CompressionFormatComboBox.SelectedItem = CompressionFormat.ETC1S;
            OutputFormatComboBox.SelectedItem = OutputFormat.KTX2;
            CompressionLevelSlider.Value = 1;
            ETC1SQualitySlider.Value = 128;
            UASTCQualitySlider.Value = 2;
            UASTCRDOLambdaSlider.Value = 1.0;
            UseETC1SRDOCheckBox.IsChecked = true;
            UseUASTCRDOCheckBox.IsChecked = true;
            PerceptualModeCheckBox.IsChecked = true;
            KTX2SupercompressionComboBox.SelectedItem = KTX2SupercompressionType.Zstandard;
            ZstdLevelSlider.Value = 3;

            // Alpha
            ForceAlphaCheckBox.IsChecked = false;
            RemoveAlphaCheckBox.IsChecked = false;

            // Color Space
            ColorSpaceComboBox.SelectedItem = ColorSpace.Auto;

            // Mipmaps
            GenerateMipmapsCheckBox.IsChecked = true;
            CustomMipmapsCheckBox.IsChecked = false;
            MipFilterComboBox.SelectedIndex = 5; // Kaiser
            ToktxFilterComboBox.SelectedItem = ToktxFilterType.Kaiser;
            WrapModeComboBox.SelectedItem = WrapMode.Clamp;
            RemoveTemporalMipmapsCheckBox.IsChecked = true;
            ApplyGammaCorrectionCheckBox.IsChecked = true;
            SaveSeparateMipmapsCheckBox.IsChecked = false;
            UpdateMipmapPanelsVisibility();

            // Normal Maps
            ConvertToNormalMapCheckBox.IsChecked = false;
            NormalizeVectorsCheckBox.IsChecked = false;
            NormalizeNormalsCheckBox.IsChecked = false;

            // Toksvig
            ToksvigEnabledCheckBox.IsChecked = false;
            ToksvigCalculationModeComboBox.SelectedItem = ToksvigCalculationMode.Classic;
            ToksvigCompositePowerSlider.Value = 1.0;
            ToksvigMinMipLevelSlider.Value = 0;
            ToksvigSmoothVarianceCheckBox.IsChecked = true;
            ToksvigVarianceThresholdSlider.Value = 0.002;
            NormalMapPathTextBox.Text = string.Empty;

            // Histogram
            EnableHistogramCheckBox.IsChecked = false;
            HistogramQualityComboBox.SelectedItem = HistogramQuality.HighQuality;
            HistogramChannelModeComboBox.SelectedItem = HistogramChannelMode.AverageLuminance;
            HistogramPercentileLowSlider.Value = 0.5;
            HistogramPercentileHighSlider.Value = 99.5;
            HistogramKneeWidthSlider.Value = 0.02;
            HistogramMinRangeThresholdSlider.Value = 0.01;

            UpdateCompressionPanels();
            UpdateOutputFormatPanels();
            UpdateToksvigCalculationModePanels();

            _isLoading = false;
        }

        // ============================================
        // PANEL VISIBILITY UPDATES
        // ============================================

        private void UpdateCompressionPanels() {
            if (CompressionFormatComboBox.SelectedItem == null) return;

            var format = (CompressionFormat)CompressionFormatComboBox.SelectedItem;

            if (format == CompressionFormat.ETC1S) {
                CompressionLevelPanel.Visibility = Visibility.Visible;
                ETC1SQualityPanel.Visibility = Visibility.Visible;
                UASTCQualityPanel.Visibility = Visibility.Collapsed;
            } else {
                CompressionLevelPanel.Visibility = Visibility.Collapsed;
                ETC1SQualityPanel.Visibility = Visibility.Collapsed;
                UASTCQualityPanel.Visibility = Visibility.Visible;
            }
        }

        private void UpdateOutputFormatPanels() {
            if (OutputFormatComboBox.SelectedItem == null || CompressionFormatComboBox.SelectedItem == null) return;

            var output = (OutputFormat)OutputFormatComboBox.SelectedItem;
            var compression = (CompressionFormat)CompressionFormatComboBox.SelectedItem;

            // Zstd supercompression only for KTX2 + UASTC (incompatible with ETC1S/BasisLZ)
            KTX2SupercompressionPanel.Visibility =
                (output == OutputFormat.KTX2 && compression != CompressionFormat.ETC1S)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private void UpdateMipmapPanelsVisibility() {
            bool useCustomMipmaps = CustomMipmapsCheckBox.IsChecked ?? false;

            ManualMipmapsPanel.Visibility = useCustomMipmaps ? Visibility.Visible : Visibility.Collapsed;
            AutomaticMipmapsPanel.Visibility = useCustomMipmaps ? Visibility.Collapsed : Visibility.Visible;
            ToksvigExpander.Visibility = useCustomMipmaps ? Visibility.Visible : Visibility.Collapsed;

            // --normal_mode and --normalize only work with automatic mipmaps
            ConvertToNormalMapCheckBox.Visibility = useCustomMipmaps ? Visibility.Collapsed : Visibility.Visible;
            NormalizeVectorsCheckBox.Visibility = useCustomMipmaps ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateToksvigCalculationModePanels() {
            if (ToksvigCalculationModeComboBox.SelectedItem == null) return;

            var mode = (ToksvigCalculationMode)ToksvigCalculationModeComboBox.SelectedItem;

            if (mode == ToksvigCalculationMode.Simplified) {
                ToksvigSimplifiedSettingsPanel.Visibility = Visibility.Visible;
                ToksvigSmoothVarianceCheckBox.IsEnabled = false;
            } else {
                ToksvigSimplifiedSettingsPanel.Visibility = Visibility.Collapsed;
                ToksvigSmoothVarianceCheckBox.IsEnabled = true;
            }
        }

        // ============================================
        // UI EVENT HANDLERS
        // ============================================

        private void CompressionFormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (!_isLoading) {
                UpdateCompressionPanels();
                UpdateOutputFormatPanels();
                OnSettingsChanged();
            }
        }

        private void OutputFormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (!_isLoading) {
                UpdateOutputFormatPanels();
                OnSettingsChanged();
            }
        }

        private void ToksvigCalculationModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (!_isLoading) {
                UpdateToksvigCalculationModePanels();
                OnSettingsChanged();
            }
        }

        private void ForceAlphaCheckBox_Checked(object sender, RoutedEventArgs e) {
            if (RemoveAlphaCheckBox.IsChecked == true) RemoveAlphaCheckBox.IsChecked = false;
            CheckboxSettingChanged(sender, e);
        }

        private void RemoveAlphaCheckBox_Checked(object sender, RoutedEventArgs e) {
            if (ForceAlphaCheckBox.IsChecked == true) ForceAlphaCheckBox.IsChecked = false;
            CheckboxSettingChanged(sender, e);
        }

        private void ApplyGammaCorrectionCheckBox_Checked(object sender, RoutedEventArgs e) {
            CheckboxSettingChanged(sender, e);
        }

        private void CustomMipmapsCheckBox_Changed(object sender, RoutedEventArgs e) {
            if (!_isLoading) {
                UpdateMipmapPanelsVisibility();
                OnSettingsChanged();
            }
        }

        private void ToksvigEnabledCheckBox_Changed(object sender, RoutedEventArgs e) {
            if (!_isLoading) {
                UpdateNormalMapAutoDetect();
                CheckboxSettingChanged(sender, e);
            }
        }

        private void EnableHistogramCheckBox_Changed(object sender, RoutedEventArgs e) {
            if (!_isLoading) {
                CheckboxSettingChanged(sender, e);
            }
        }

        private void NormalMapPathTextBox_TextChanged(object sender, TextChangedEventArgs e) {
            if (!_isLoading) {
                UpdateNormalMapAutoDetect();
                TextBoxSettingChanged(sender, e);
            }
        }

        private void HistogramQualityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (_isLoading) return;

            var quality = HistogramQualityComboBox.SelectedItem != null
                ? (HistogramQuality)HistogramQualityComboBox.SelectedItem
                : HistogramQuality.HighQuality;

            _isLoading = true;
            try {
                if (quality == HistogramQuality.HighQuality) {
                    HistogramPercentileLowSlider.Value = 0.5;
                    HistogramPercentileHighSlider.Value = 99.5;
                    HistogramKneeWidthSlider.Value = 0.02;
                } else {
                    HistogramPercentileLowSlider.Value = 1.0;
                    HistogramPercentileHighSlider.Value = 99.0;
                    HistogramKneeWidthSlider.Value = 0.0;
                }
            } finally {
                _isLoading = false;
            }

            OnSettingsChanged();
        }

        private void CheckboxSettingChanged(object sender, RoutedEventArgs e) {
            if (!_isLoading) OnSettingsChanged();
        }

        private void ComboBoxSettingChanged(object sender, SelectionChangedEventArgs e) {
            if (!_isLoading) OnSettingsChanged();
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            if (!_isLoading) OnSettingsChanged();
        }

        private void KTX2SupercompressionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (!_isLoading) OnSettingsChanged();
        }

        private void TextBoxSettingChanged(object sender, TextChangedEventArgs e) {
            if (!_isLoading) OnSettingsChanged();
        }

        // ============================================
        // ACTIONS
        // ============================================

        private void Convert_Click(object sender, RoutedEventArgs e) {
            OnConvertRequested();
        }

        private void Apply_Click(object sender, RoutedEventArgs e) {
            OnSettingsChanged();
        }

        private void Reset_Click(object sender, RoutedEventArgs e) {
            InitializeDefaults();
            OnSettingsChanged();
        }

        private void BrowseNormalMap_Click(object sender, RoutedEventArgs e) {
            var dialog = new Microsoft.Win32.OpenFileDialog {
                Title = "Select Normal Map",
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.tga;*.bmp)|*.png;*.jpg;*.jpeg;*.tga;*.bmp|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true) {
                NormalMapPathTextBox.Text = dialog.FileName;
            }
        }

        // ============================================
        // EVENT RAISERS
        // ============================================

        private void OnSettingsChanged() {
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnConvertRequested() {
            ConvertRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnAutoDetectRequested() {
            AutoDetectRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
