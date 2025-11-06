using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureConversion.Settings;

namespace AssetProcessor.Windows {
    public partial class PresetEditorWindow : Window {
        private TextureConversionPreset? _originalPreset;
        public TextureConversionPreset? EditedPreset { get; private set; }

        public PresetEditorWindow(TextureConversionPreset? preset) {
            InitializeComponent();
            _originalPreset = preset;
            LoadPresetData();
        }

        private void LoadPresetData() {
            if (_originalPreset != null) {
                // Editing existing preset
                NameTextBox.Text = _originalPreset.Name;
                DescriptionTextBox.Text = _originalPreset.Description ?? "";

                // Compression
                CompressionFormatComboBox.SelectedItem = _originalPreset.CompressionFormat;
                OutputFormatComboBox.SelectedItem = _originalPreset.OutputFormat;
                ColorSpaceComboBox.SelectedItem = _originalPreset.ColorSpace;
                KTX2SupercompressionComboBox.SelectedItem = _originalPreset.KTX2Supercompression;
                KTX2ZstdLevelSlider.Value = _originalPreset.KTX2ZstdLevel;

                // ETC1S
                ETC1SQualitySlider.Value = _originalPreset.QualityLevel;
                UseETC1SRDOCheckBox.IsChecked = _originalPreset.UseETC1SRDO;
                ETC1SRDOLambdaSlider.Value = _originalPreset.ETC1SRDOLambda;

                // UASTC
                UASTCQualitySlider.Value = _originalPreset.UASTCQuality;
                UseUASTCRDOCheckBox.IsChecked = _originalPreset.UseUASTCRDO;
                UASTCRDOLambdaSlider.Value = _originalPreset.UASTCRDOQuality;

                // Basic settings
                GenerateMipmapsCheckBox.IsChecked = _originalPreset.GenerateMipmaps;
                UseMultithreadingCheckBox.IsChecked = _originalPreset.UseMultithreading;
                PerceptualModeCheckBox.IsChecked = _originalPreset.PerceptualMode;

                // Mipmaps - Manual
                MipFilterComboBox.SelectedItem = _originalPreset.MipFilter;
                ApplyGammaCorrectionCheckBox.IsChecked = _originalPreset.ApplyGammaCorrection;
                NormalizeNormalsCheckBox.IsChecked = _originalPreset.NormalizeNormals;

                // Mipmaps - Automatic
                ToktxFilterComboBox.SelectedItem = _originalPreset.ToktxMipFilter;
                WrapModeComboBox.SelectedItem = _originalPreset.WrapMode;

                // Alpha
                ForceAlphaCheckBox.IsChecked = _originalPreset.ForceAlphaChannel;
                RemoveAlphaCheckBox.IsChecked = _originalPreset.RemoveAlphaChannel;

                // Normal Maps
                ConvertToNormalMapCheckBox.IsChecked = _originalPreset.ConvertToNormalMap;
                NormalizeVectorsCheckBox.IsChecked = _originalPreset.NormalizeVectors;

                // Advanced
                LinearMipFilterCheckBox.IsChecked = _originalPreset.UseLinearMipFiltering;
                RemoveTemporaryMipmapsCheckBox.IsChecked = _originalPreset.RemoveTemporaryMipmaps;

                // Toksvig Settings
                ToksvigEnabledCheckBox.IsChecked = _originalPreset.ToksvigSettings.Enabled;
                ToksvigCalculationModeComboBox.SelectedItem = _originalPreset.ToksvigSettings.CalculationMode;
                ToksvigCompositePowerSlider.Value = _originalPreset.ToksvigSettings.CompositePower;
                ToksvigMinMipLevelSlider.Value = _originalPreset.ToksvigSettings.MinToksvigMipLevel;
                ToksvigSmoothVarianceCheckBox.IsChecked = _originalPreset.ToksvigSettings.SmoothVariance;
                ToksvigVarianceThresholdSlider.Value = _originalPreset.ToksvigSettings.VarianceThreshold;
                ToksvigNormalMapPathTextBox.Text = _originalPreset.ToksvigSettings.NormalMapPath ?? "";

                // Load suffixes
                SuffixesListBox.ItemsSource = new System.Collections.ObjectModel.ObservableCollection<string>(_originalPreset.Suffixes);
            } else {
                // Creating new preset - use defaults
                CompressionFormatComboBox.SelectedItem = CompressionFormat.ETC1S;
                OutputFormatComboBox.SelectedItem = OutputFormat.KTX2;
                MipFilterComboBox.SelectedItem = FilterType.Kaiser;
                KTX2SupercompressionComboBox.SelectedItem = KTX2SupercompressionType.Zstandard;
                ColorSpaceComboBox.SelectedItem = ColorSpace.Auto;
                ToktxFilterComboBox.SelectedItem = ToktxFilterType.Kaiser;
                WrapModeComboBox.SelectedItem = WrapMode.Clamp;
                ToksvigCalculationModeComboBox.SelectedItem = ToksvigCalculationMode.Classic;

                // Empty suffixes list for new preset
                SuffixesListBox.ItemsSource = new System.Collections.ObjectModel.ObservableCollection<string>();
            }

            UpdatePanelVisibility();
            UpdateMipmapPanelsVisibility();
            UpdateToksvigPanelsVisibility();
        }

        private void AddSuffix_Click(object sender, RoutedEventArgs e) {
            var suffix = NewSuffixTextBox.Text.Trim();
            if (string.IsNullOrEmpty(suffix)) {
                MessageBox.Show("Please enter a suffix.", "Empty Suffix", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Add underscore if not present
            if (!suffix.StartsWith("_")) {
                suffix = "_" + suffix;
            }

            var suffixes = SuffixesListBox.ItemsSource as System.Collections.ObjectModel.ObservableCollection<string>;
            if (suffixes != null && !suffixes.Contains(suffix)) {
                suffixes.Add(suffix);
                NewSuffixTextBox.Clear();
            } else {
                MessageBox.Show("This suffix already exists.", "Duplicate", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void RemoveSuffix_Click(object sender, RoutedEventArgs e) {
            if (SuffixesListBox.SelectedItem is string selectedSuffix) {
                var suffixes = SuffixesListBox.ItemsSource as System.Collections.ObjectModel.ObservableCollection<string>;
                suffixes?.Remove(selectedSuffix);
            }
        }

        private void NewSuffixTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) {
            if (e.Key == System.Windows.Input.Key.Enter) {
                AddSuffix_Click(sender, e);
            }
        }

        private void CompressionFormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            UpdatePanelVisibility();
        }

        private void OutputFormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            UpdatePanelVisibility();
        }

        private void UpdatePanelVisibility() {
            if (CompressionFormatComboBox.SelectedItem == null || OutputFormatComboBox.SelectedItem == null)
                return;

            var compressionFormat = (CompressionFormat)CompressionFormatComboBox.SelectedItem;
            var outputFormat = (OutputFormat)OutputFormatComboBox.SelectedItem;

            // Show/hide compression-specific panels
            if (compressionFormat == CompressionFormat.ETC1S) {
                ETC1SPanel.Visibility = Visibility.Visible;
                UASTCPanel.Visibility = Visibility.Collapsed;
            } else {
                ETC1SPanel.Visibility = Visibility.Collapsed;
                UASTCPanel.Visibility = Visibility.Visible;
            }

            // Show/hide KTX2 panel
            if (outputFormat == OutputFormat.KTX2) {
                KTX2Panel.Visibility = Visibility.Visible;
            } else {
                KTX2Panel.Visibility = Visibility.Collapsed;
            }
        }

        private void CustomMipmapsCheckBox_Changed(object sender, RoutedEventArgs e) {
            UpdateMipmapPanelsVisibility();
        }

        private void UpdateMipmapPanelsVisibility() {
            bool useCustomMipmaps = CustomMipmapsCheckBox.IsChecked ?? false;

            // Show/hide panels based on mode
            ManualMipmapsPanel.Visibility = useCustomMipmaps ? Visibility.Visible : Visibility.Collapsed;
            AutomaticMipmapsPanel.Visibility = useCustomMipmaps ? Visibility.Collapsed : Visibility.Visible;

            // ВАЖНО: --normal_mode и --normalize работают ТОЛЬКО с автоматическими mipmaps (--genmipmap)!
            if (useCustomMipmaps) {
                // Disable options that only work with automatic mode
                ConvertToNormalMapCheckBox.IsChecked = false;
                ConvertToNormalMapCheckBox.IsEnabled = false;

                NormalizeVectorsCheckBox.IsChecked = false;
                NormalizeVectorsCheckBox.IsEnabled = false;
            } else {
                // Enable options for automatic mode
                ConvertToNormalMapCheckBox.IsEnabled = true;
                NormalizeVectorsCheckBox.IsEnabled = true;
            }
        }

        private void ToksvigEnabledCheckBox_Changed(object sender, RoutedEventArgs e) {
            UpdateToksvigPanelsVisibility();
        }

        private void ToksvigCalculationModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            UpdateToksvigPanelsVisibility();
        }

        private void UpdateToksvigPanelsVisibility() {
            if (ToksvigCalculationModeComboBox.SelectedItem == null)
                return;

            var mode = (ToksvigCalculationMode)ToksvigCalculationModeComboBox.SelectedItem;

            // Show Simplified settings only in Simplified mode
            ToksvigSimplifiedSettingsPanel.Visibility = mode == ToksvigCalculationMode.Simplified
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void BrowseToksvigNormalMap_Click(object sender, RoutedEventArgs e) {
            var dialog = new OpenFileDialog {
                Title = "Select Normal Map",
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.tga;*.bmp)|*.png;*.jpg;*.jpeg;*.tga;*.bmp|All Files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true) {
                ToksvigNormalMapPathTextBox.Text = dialog.FileName;
            }
        }

        private void UseUASTCRDOCheckBox_Changed(object sender, RoutedEventArgs e) {
            // Binding handles this automatically
        }

        private void UseETC1SRDOCheckBox_Changed(object sender, RoutedEventArgs e) {
            // Binding handles this automatically
        }

        private void AlphaCheckBox_Checked(object sender, RoutedEventArgs e) {
            if (sender == ForceAlphaCheckBox && ForceAlphaCheckBox.IsChecked == true) {
                RemoveAlphaCheckBox.IsChecked = false;
            } else if (sender == RemoveAlphaCheckBox && RemoveAlphaCheckBox.IsChecked == true) {
                ForceAlphaCheckBox.IsChecked = false;
            }
        }

        private void AlphaCheckBox_Unchecked(object sender, RoutedEventArgs e) {
            // Nothing to do
        }

        private void Save_Click(object sender, RoutedEventArgs e) {
            // Validate name
            string name = NameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) {
                MessageBox.Show("Please enter a preset name.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                NameTextBox.Focus();
                return;
            }

            // Get suffixes from ListBox
            var suffixesList = new List<string>();
            if (SuffixesListBox.ItemsSource is System.Collections.ObjectModel.ObservableCollection<string> suffixes) {
                suffixesList = suffixes.ToList();
            }

            // Create Toksvig settings
            var toksvigSettings = new ToksvigSettings {
                Enabled = ToksvigEnabledCheckBox.IsChecked ?? false,
                CalculationMode = (ToksvigCalculationMode)ToksvigCalculationModeComboBox.SelectedItem,
                CompositePower = (float)ToksvigCompositePowerSlider.Value,
                MinToksvigMipLevel = (int)Math.Round(ToksvigMinMipLevelSlider.Value),
                SmoothVariance = ToksvigSmoothVarianceCheckBox.IsChecked ?? true,
                VarianceThreshold = (float)ToksvigVarianceThresholdSlider.Value,
                NormalMapPath = string.IsNullOrWhiteSpace(ToksvigNormalMapPathTextBox.Text)
                    ? null
                    : ToksvigNormalMapPathTextBox.Text.Trim()
            };

            // Validate Toksvig settings
            if (toksvigSettings.Enabled && !toksvigSettings.Validate(out string? error)) {
                MessageBox.Show($"Invalid Toksvig settings: {error}", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Create preset from UI
            EditedPreset = new TextureConversionPreset {
                Name = name,
                Description = DescriptionTextBox.Text.Trim(),
                Suffixes = suffixesList,

                // Compression
                CompressionFormat = (CompressionFormat)CompressionFormatComboBox.SelectedItem,
                OutputFormat = (OutputFormat)OutputFormatComboBox.SelectedItem,
                ColorSpace = (ColorSpace)ColorSpaceComboBox.SelectedItem,
                QualityLevel = (int)Math.Round(ETC1SQualitySlider.Value),
                UASTCQuality = (int)Math.Round(UASTCQualitySlider.Value),
                UseUASTCRDO = UseUASTCRDOCheckBox.IsChecked ?? false,
                UASTCRDOQuality = (float)UASTCRDOLambdaSlider.Value,
                UseETC1SRDO = UseETC1SRDOCheckBox.IsChecked ?? true,
                ETC1SRDOLambda = (float)ETC1SRDOLambdaSlider.Value,
                UseMultithreading = UseMultithreadingCheckBox.IsChecked ?? true,
                PerceptualMode = PerceptualModeCheckBox.IsChecked ?? true,
                KTX2Supercompression = (KTX2SupercompressionType)KTX2SupercompressionComboBox.SelectedItem,
                KTX2ZstdLevel = (int)Math.Round(KTX2ZstdLevelSlider.Value),

                // Mipmaps
                GenerateMipmaps = GenerateMipmapsCheckBox.IsChecked ?? true,
                MipFilter = (FilterType)MipFilterComboBox.SelectedItem,
                ApplyGammaCorrection = ApplyGammaCorrectionCheckBox.IsChecked ?? true,
                NormalizeNormals = NormalizeNormalsCheckBox.IsChecked ?? false,
                ToktxMipFilter = (ToktxFilterType)ToktxFilterComboBox.SelectedItem,
                WrapMode = (WrapMode)WrapModeComboBox.SelectedItem,

                // Alpha
                ForceAlphaChannel = ForceAlphaCheckBox.IsChecked ?? false,
                RemoveAlphaChannel = RemoveAlphaCheckBox.IsChecked ?? false,

                // Normal Maps
                ConvertToNormalMap = ConvertToNormalMapCheckBox.IsChecked ?? false,
                NormalizeVectors = NormalizeVectorsCheckBox.IsChecked ?? false,

                // Advanced
                UseLinearMipFiltering = LinearMipFilterCheckBox.IsChecked ?? false,
                RemoveTemporaryMipmaps = RemoveTemporaryMipmapsCheckBox.IsChecked ?? true,

                // Toksvig
                ToksvigSettings = toksvigSettings,

                IsBuiltIn = false
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
