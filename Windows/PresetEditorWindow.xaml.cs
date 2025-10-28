using System.Windows;
using System.Windows.Controls;
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
                CompressionFormatComboBox.SelectedItem = _originalPreset.CompressionFormat;
                OutputFormatComboBox.SelectedItem = _originalPreset.OutputFormat;
                KTX2SupercompressionComboBox.SelectedItem = _originalPreset.KTX2Supercompression;
                KTX2ZstdLevelSlider.Value = _originalPreset.KTX2ZstdLevel;
                ETC1SQualitySlider.Value = _originalPreset.QualityLevel;
                UASTCQualitySlider.Value = _originalPreset.UASTCQuality;
                UseUASTCRDOCheckBox.IsChecked = _originalPreset.UseUASTCRDO;
                UASTCRDOLambdaSlider.Value = _originalPreset.UASTCRDOQuality;
                UseETC1SRDOCheckBox.IsChecked = _originalPreset.UseETC1SRDO;
                ETC1SRDOLambdaSlider.Value = _originalPreset.ETC1SRDOLambda;
                GenerateMipmapsCheckBox.IsChecked = _originalPreset.GenerateMipmaps;
                MipFilterComboBox.SelectedItem = _originalPreset.MipFilter;
                ApplyGammaCorrectionCheckBox.IsChecked = _originalPreset.ApplyGammaCorrection;
                NormalizeNormalsCheckBox.IsChecked = _originalPreset.NormalizeNormals;
                UseMultithreadingCheckBox.IsChecked = _originalPreset.UseMultithreading;
                PerceptualModeCheckBox.IsChecked = _originalPreset.PerceptualMode;
                SeparateAlphaCheckBox.IsChecked = _originalPreset.SeparateAlpha;
                ForceAlphaCheckBox.IsChecked = _originalPreset.ForceAlphaChannel;
                RemoveAlphaCheckBox.IsChecked = _originalPreset.RemoveAlphaChannel;
                ForceLinearCheckBox.IsChecked = _originalPreset.TreatAsLinear;
                ClampMipmapsCheckBox.IsChecked = _originalPreset.ClampMipmaps;
                LinearMipFilterCheckBox.IsChecked = _originalPreset.UseLinearMipFiltering;

                // Load suffixes
                SuffixesListBox.ItemsSource = new System.Collections.ObjectModel.ObservableCollection<string>(_originalPreset.Suffixes);
            } else {
                // Creating new preset - use defaults
                CompressionFormatComboBox.SelectedItem = CompressionFormat.ETC1S;
                OutputFormatComboBox.SelectedItem = OutputFormat.KTX2;
                MipFilterComboBox.SelectedItem = FilterType.Kaiser;
                KTX2SupercompressionComboBox.SelectedItem = KTX2SupercompressionType.Zstandard;

                // Empty suffixes list for new preset
                SuffixesListBox.ItemsSource = new System.Collections.ObjectModel.ObservableCollection<string>();
            }

            UpdatePanelVisibility();
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

            // Create preset from UI
            EditedPreset = new TextureConversionPreset {
                Name = name,
                Description = DescriptionTextBox.Text.Trim(),
                Suffixes = suffixesList,
                CompressionFormat = (CompressionFormat)CompressionFormatComboBox.SelectedItem,
                OutputFormat = (OutputFormat)OutputFormatComboBox.SelectedItem,
                QualityLevel = (int)Math.Round(ETC1SQualitySlider.Value),
                UASTCQuality = (int)Math.Round(UASTCQualitySlider.Value),
                UseUASTCRDO = UseUASTCRDOCheckBox.IsChecked ?? false,
                UASTCRDOQuality = (float)UASTCRDOLambdaSlider.Value,
                UseETC1SRDO = UseETC1SRDOCheckBox.IsChecked ?? true,
                ETC1SRDOLambda = (float)ETC1SRDOLambdaSlider.Value,
                GenerateMipmaps = GenerateMipmapsCheckBox.IsChecked ?? true,
                MipFilter = (FilterType)MipFilterComboBox.SelectedItem,
                ApplyGammaCorrection = ApplyGammaCorrectionCheckBox.IsChecked ?? true,
                NormalizeNormals = NormalizeNormalsCheckBox.IsChecked ?? false,
                UseMultithreading = UseMultithreadingCheckBox.IsChecked ?? true,
                PerceptualMode = PerceptualModeCheckBox.IsChecked ?? true,
                KTX2Supercompression = (KTX2SupercompressionType)KTX2SupercompressionComboBox.SelectedItem,
                KTX2ZstdLevel = (int)Math.Round(KTX2ZstdLevelSlider.Value),
                SeparateAlpha = SeparateAlphaCheckBox.IsChecked ?? false,
                ForceAlphaChannel = ForceAlphaCheckBox.IsChecked ?? false,
                RemoveAlphaChannel = RemoveAlphaCheckBox.IsChecked ?? false,
                TreatAsLinear = ForceLinearCheckBox.IsChecked ?? false,
                ClampMipmaps = ClampMipmapsCheckBox.IsChecked ?? false,
                UseLinearMipFiltering = LinearMipFilterCheckBox.IsChecked ?? false,
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
