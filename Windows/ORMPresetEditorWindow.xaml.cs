using System.Windows;
using System.Windows.Controls;
using AssetProcessor.TextureConversion.Core;

namespace AssetProcessor.Windows {
    public partial class ORMPresetEditorWindow : Window {
        private ORMSettings? _originalPreset;
        public ORMSettings? EditedPreset { get; private set; }

        public ORMPresetEditorWindow(ORMSettings? preset) {
            InitializeComponent();
            _originalPreset = preset;
            LoadPresetData();
        }

        private void LoadPresetData() {
            if (_originalPreset != null) {
                // Editing existing preset
                NameTextBox.Text = _originalPreset.Name;
                DescriptionTextBox.Text = _originalPreset.Description ?? "";

                // Packing Mode
                PackingModeComboBox.SelectedItem = _originalPreset.PackingMode;

                // AO Channel
                AOFilterComboBox.SelectedItem = _originalPreset.AOFilter;
                AOProcessingComboBox.SelectedItem = _originalPreset.AOProcessing;
                AOBiasSlider.Value = _originalPreset.AOBias;

                // Gloss Channel
                GlossFilterComboBox.SelectedItem = _originalPreset.GlossFilter;
                ToksvigEnabledCheckBox.IsChecked = _originalPreset.ToksvigEnabled;
                ToksvigModeComboBox.SelectedItem = _originalPreset.ToksvigMode;
                ToksvigPowerSlider.Value = _originalPreset.ToksvigPower;
                ToksvigMinMipSlider.Value = _originalPreset.ToksvigMinMip;
                ToksvigEnergyPreservingCheckBox.IsChecked = _originalPreset.ToksvigEnergyPreserving;
                ToksvigSmoothVarianceCheckBox.IsChecked = _originalPreset.ToksvigSmoothVariance;

                // Metallic Channel
                MetallicFilterComboBox.SelectedItem = _originalPreset.MetallicFilter;
                MetallicProcessingComboBox.SelectedItem = _originalPreset.MetallicProcessing;

                // Compression
                CompressionFormatComboBox.SelectedItem = _originalPreset.CompressionFormat;
                ETC1SCompressLevelSlider.Value = _originalPreset.ETC1SCompressLevel;
                ETC1SQualitySlider.Value = _originalPreset.ETC1SQuality;
                ETC1SPerceptualCheckBox.IsChecked = _originalPreset.ETC1SPerceptual;
                UASTCQualitySlider.Value = _originalPreset.UASTCQuality;
                UASTCRDOCheckBox.IsChecked = _originalPreset.UASTCRDO;
                UASTCRDOLambdaSlider.Value = _originalPreset.UASTCRDOLambda;
                UASTCZstdCheckBox.IsChecked = _originalPreset.UASTCZstd;
                UASTCZstdLevelSlider.Value = _originalPreset.UASTCZstdLevel;
            } else {
                // Creating new preset - use defaults
                PackingModeComboBox.SelectedItem = ChannelPackingMode.Auto;
                AOFilterComboBox.SelectedItem = FilterType.Kaiser;
                AOProcessingComboBox.SelectedItem = AOProcessingMode.BiasedDarkening;
                GlossFilterComboBox.SelectedItem = FilterType.Kaiser;
                ToksvigModeComboBox.SelectedItem = ToksvigCalculationMode.Classic;
                MetallicFilterComboBox.SelectedItem = FilterType.Box;
                MetallicProcessingComboBox.SelectedItem = AOProcessingMode.None;
                CompressionFormatComboBox.SelectedItem = CompressionFormat.ETC1S;
            }

            UpdatePanelVisibility();
            UpdateToksvigVisibility();
        }

        private void CompressionFormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            UpdatePanelVisibility();
        }

        private void UpdatePanelVisibility() {
            if (CompressionFormatComboBox.SelectedItem == null)
                return;

            var format = (CompressionFormat)CompressionFormatComboBox.SelectedItem;

            if (format == CompressionFormat.ETC1S) {
                ETC1SPanel.Visibility = Visibility.Visible;
                UASTCPanel.Visibility = Visibility.Collapsed;
            } else {
                ETC1SPanel.Visibility = Visibility.Collapsed;
                UASTCPanel.Visibility = Visibility.Visible;
            }
        }

        private void ToksvigEnabledCheckBox_Changed(object sender, RoutedEventArgs e) {
            UpdateToksvigVisibility();
        }

        private void UpdateToksvigVisibility() {
            bool enabled = ToksvigEnabledCheckBox.IsChecked ?? false;
            ToksvigSettingsPanel.IsEnabled = enabled;
            ToksvigSettingsPanel.Opacity = enabled ? 1.0 : 0.5;
        }

        private void Save_Click(object sender, RoutedEventArgs e) {
            // Validate name
            string name = NameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) {
                MessageBox.Show("Please enter a preset name.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                NameTextBox.Focus();
                return;
            }

            // Create preset from UI
            EditedPreset = new ORMSettings {
                Name = name,
                Description = DescriptionTextBox.Text.Trim(),
                IsBuiltIn = false,
                Enabled = true,

                // Packing Mode
                PackingMode = (ChannelPackingMode)PackingModeComboBox.SelectedItem,

                // AO Channel
                AOFilter = (FilterType)AOFilterComboBox.SelectedItem,
                AOProcessing = (AOProcessingMode)AOProcessingComboBox.SelectedItem,
                AOBias = (float)AOBiasSlider.Value,

                // Gloss Channel
                GlossFilter = (FilterType)GlossFilterComboBox.SelectedItem,
                ToksvigEnabled = ToksvigEnabledCheckBox.IsChecked ?? false,
                ToksvigMode = (ToksvigCalculationMode)ToksvigModeComboBox.SelectedItem,
                ToksvigPower = (float)ToksvigPowerSlider.Value,
                ToksvigMinMip = (int)ToksvigMinMipSlider.Value,
                ToksvigEnergyPreserving = ToksvigEnergyPreservingCheckBox.IsChecked ?? true,
                ToksvigSmoothVariance = ToksvigSmoothVarianceCheckBox.IsChecked ?? true,

                // Metallic Channel
                MetallicFilter = (FilterType)MetallicFilterComboBox.SelectedItem,
                MetallicProcessing = (AOProcessingMode)MetallicProcessingComboBox.SelectedItem,

                // Compression
                CompressionFormat = (CompressionFormat)CompressionFormatComboBox.SelectedItem,
                ETC1SCompressLevel = (int)ETC1SCompressLevelSlider.Value,
                ETC1SQuality = (int)ETC1SQualitySlider.Value,
                ETC1SPerceptual = ETC1SPerceptualCheckBox.IsChecked ?? false,
                UASTCQuality = (int)UASTCQualitySlider.Value,
                UASTCRDO = UASTCRDOCheckBox.IsChecked ?? false,
                UASTCRDOLambda = (float)UASTCRDOLambdaSlider.Value,
                UASTCZstd = UASTCZstdCheckBox.IsChecked ?? true,
                UASTCZstdLevel = (int)UASTCZstdLevelSlider.Value
            };

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) {
            DialogResult = false;
            Close();
        }
    }
}
