using System;
using System.Windows;
using System.Windows.Controls;
using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureConversion.Settings;

namespace AssetProcessor.Controls {
    public partial class TextureConversionSettingsPanel : UserControl {
        private bool _isLoading = false;

        public event EventHandler? SettingsChanged;

        public TextureConversionSettingsPanel() {
            InitializeComponent();
            InitializeDefaults();
        }

        private void InitializeDefaults() {
            _isLoading = true;

            // Set default values
            CompressionFormatComboBox.SelectedIndex = 0; // ETC1S
            OutputFormatComboBox.SelectedIndex = 1; // KTX2
            MipFilterComboBox.SelectedIndex = 5; // Kaiser

            UpdateCompressionPanels();

            _isLoading = false;
        }

        private void CompressionFormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (!_isLoading) {
                UpdateCompressionPanels();
                OnSettingsChanged();
            }
        }

        private void UpdateCompressionPanels() {
            if (CompressionFormatComboBox.SelectedItem == null) return;

            var format = (CompressionFormat)CompressionFormatComboBox.SelectedItem;

            if (format == CompressionFormat.ETC1S) {
                ETC1SPanel.Visibility = Visibility.Visible;
                UASTCPanel.Visibility = Visibility.Collapsed;
            } else {
                ETC1SPanel.Visibility = Visibility.Collapsed;
                UASTCPanel.Visibility = Visibility.Visible;
            }
        }

        public CompressionSettingsData GetCompressionSettings() {
            var format = CompressionFormatComboBox.SelectedItem != null
                ? (CompressionFormat)CompressionFormatComboBox.SelectedItem
                : CompressionFormat.ETC1S;

            var outputFormat = OutputFormatComboBox.SelectedItem != null
                ? (OutputFormat)OutputFormatComboBox.SelectedItem
                : OutputFormat.KTX2;

            return new CompressionSettingsData {
                CompressionFormat = format,
                OutputFormat = outputFormat,
                QualityLevel = (int)ETC1SQualitySlider.Value,
                UASTCQuality = (int)UASTCQualitySlider.Value,
                UseUASTCRDO = UseUASTCRDOCheckBox.IsChecked ?? true,
                UASTCRDOQuality = 1.0f,
                PerceptualMode = PerceptualModeCheckBox.IsChecked ?? true
            };
        }

        public MipProfileSettings GetMipProfileSettings() {
            var filter = MipFilterComboBox.SelectedItem != null
                ? (FilterType)MipFilterComboBox.SelectedItem
                : FilterType.Kaiser;

            return new MipProfileSettings {
                Filter = filter,
                ApplyGammaCorrection = ApplyGammaCorrectionCheckBox.IsChecked ?? true,
                Gamma = 2.2f,
                BlurRadius = 0.0f,
                IncludeLastLevel = true,
                MinMipSize = 1,
                NormalizeNormals = false
            };
        }

        public bool GenerateMipmaps => GenerateMipmapsCheckBox.IsChecked ?? true;
        public bool SaveSeparateMipmaps => SaveSeparateMipmapsCheckBox.IsChecked ?? false;
        public string? PresetName => PresetComboBox.SelectedItem as string;

        public void LoadSettings(CompressionSettingsData compression, MipProfileSettings mipProfile, bool generateMips, bool saveSeparateMips) {
            _isLoading = true;

            CompressionFormatComboBox.SelectedItem = compression.CompressionFormat;
            OutputFormatComboBox.SelectedItem = compression.OutputFormat;
            ETC1SQualitySlider.Value = compression.QualityLevel;
            UASTCQualitySlider.Value = compression.UASTCQuality;
            UseUASTCRDOCheckBox.IsChecked = compression.UseUASTCRDO;
            PerceptualModeCheckBox.IsChecked = compression.PerceptualMode;

            MipFilterComboBox.SelectedItem = mipProfile.Filter;
            ApplyGammaCorrectionCheckBox.IsChecked = mipProfile.ApplyGammaCorrection;
            GenerateMipmapsCheckBox.IsChecked = generateMips;
            SaveSeparateMipmapsCheckBox.IsChecked = saveSeparateMips;

            UpdateCompressionPanels();

            _isLoading = false;
        }

        public void LoadPresets(string[] presetNames, string? selectedPreset = null) {
            PresetComboBox.Items.Clear();
            PresetComboBox.Items.Add("(Custom)");

            foreach (var preset in presetNames) {
                PresetComboBox.Items.Add(preset);
            }

            if (!string.IsNullOrEmpty(selectedPreset) && PresetComboBox.Items.Contains(selectedPreset)) {
                PresetComboBox.SelectedItem = selectedPreset;
            } else {
                PresetComboBox.SelectedIndex = 0; // Custom
            }
        }

        private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (!_isLoading && PresetComboBox.SelectedItem != null) {
                var presetName = PresetComboBox.SelectedItem.ToString();
                if (presetName != "(Custom)") {
                    // TODO: Load preset from PresetManager
                    OnSettingsChanged();
                }
            }
        }

        private void ManagePresets_Click(object sender, RoutedEventArgs e) {
            // TODO: Open preset management window
            MessageBox.Show("Preset management coming soon!", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Apply_Click(object sender, RoutedEventArgs e) {
            OnSettingsChanged();
        }

        private void Reset_Click(object sender, RoutedEventArgs e) {
            InitializeDefaults();
            OnSettingsChanged();
        }

        private void OnSettingsChanged() {
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
