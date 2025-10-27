using System;
using System.Windows;
using System.Windows.Controls;
using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureConversion.Settings;

namespace AssetProcessor.Controls {
    public partial class TextureConversionSettingsPanel : UserControl {
        private bool _isLoading = false;
        private readonly PresetManager _presetManager = new();

        public event EventHandler? SettingsChanged;

        public TextureConversionSettingsPanel() {
            InitializeComponent();
            InitializePresets();
            InitializeDefaults();
        }

        private void InitializePresets() {
            // Загружаем все пресеты (встроенные + пользовательские)
            var presets = _presetManager.GetAllPresets();
            PresetComboBox.ItemsSource = presets;
            PresetComboBox.DisplayMemberPath = "Name";

            if (presets.Count > 0) {
                PresetComboBox.SelectedIndex = 0; // Выбираем первый пресет по умолчанию
            }
        }

        private void InitializeDefaults() {
            _isLoading = true;

            // Set default values
            CompressionFormatComboBox.SelectedItem = CompressionFormat.ETC1S;
            OutputFormatComboBox.SelectedItem = OutputFormat.KTX2;
            MipFilterComboBox.SelectedIndex = 5; // Kaiser
            KTX2SupercompressionComboBox.SelectedItem = KTX2SupercompressionType.Zstandard;
            UseUASTCRDOCheckBox.IsChecked = true;
            UASTCRDOLambdaSlider.Value = 1.0;
            UseETC1SRDOCheckBox.IsChecked = true;
            PerceptualModeCheckBox.IsChecked = true;
            SeparateAlphaCheckBox.IsChecked = false;
            ForceAlphaCheckBox.IsChecked = false;
            RemoveAlphaCheckBox.IsChecked = false;
            ForceLinearCheckBox.IsChecked = false;
            MipClampCheckBox.IsChecked = false;
            LinearMipFilterCheckBox.IsChecked = false;
            NormalizeNormalsCheckBox.IsChecked = false;

            UpdateCompressionPanels();
            UpdateOutputFormatPanels();

            _isLoading = false;
        }

        private void CompressionFormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (!_isLoading) {
                UpdateCompressionPanels();
                OnSettingsChanged();
            }
        }

        private void OutputFormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (!_isLoading) {
                UpdateOutputFormatPanels();
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

        private void UpdateOutputFormatPanels() {
            if (OutputFormatComboBox.SelectedItem == null) return;

            var output = (OutputFormat)OutputFormatComboBox.SelectedItem;

            if (output == OutputFormat.KTX2) {
                KTX2SupercompressionPanel.Visibility = Visibility.Visible;
            } else {
                KTX2SupercompressionPanel.Visibility = Visibility.Collapsed;
            }
        }

        public CompressionSettingsData GetCompressionSettings() {
            var format = CompressionFormatComboBox.SelectedItem != null
                ? (CompressionFormat)CompressionFormatComboBox.SelectedItem
                : CompressionFormat.ETC1S;

            var outputFormat = OutputFormatComboBox.SelectedItem != null
                ? (OutputFormat)OutputFormatComboBox.SelectedItem
                : OutputFormat.KTX2;

            var supercompression = KTX2SupercompressionComboBox.SelectedItem != null
                ? (KTX2SupercompressionType)KTX2SupercompressionComboBox.SelectedItem
                : KTX2SupercompressionType.Zstandard;

            return new CompressionSettingsData {
                CompressionFormat = format,
                OutputFormat = outputFormat,
                QualityLevel = (int)Math.Round(ETC1SQualitySlider.Value),
                UASTCQuality = (int)Math.Round(UASTCQualitySlider.Value),
                UseUASTCRDO = UseUASTCRDOCheckBox.IsChecked ?? true,
                UASTCRDOQuality = (float)Math.Round(UASTCRDOLambdaSlider.Value, 2),
                PerceptualMode = PerceptualModeCheckBox.IsChecked ?? true,
                KTX2Supercompression = supercompression,
                UseETC1SRDO = UseETC1SRDOCheckBox.IsChecked ?? true,
                SeparateAlpha = SeparateAlphaCheckBox.IsChecked ?? false,
                ForceAlphaChannel = ForceAlphaCheckBox.IsChecked ?? false,
                RemoveAlphaChannel = RemoveAlphaCheckBox.IsChecked ?? false,
                ClampMipmaps = MipClampCheckBox.IsChecked ?? false,
                ForceLinearColorSpace = ForceLinearCheckBox.IsChecked ?? false,
                UseLinearMipFiltering = LinearMipFilterCheckBox.IsChecked ?? false
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
                NormalizeNormals = NormalizeNormalsCheckBox.IsChecked ?? false
            };
        }

        public bool GenerateMipmaps => GenerateMipmapsCheckBox.IsChecked ?? true;
        public bool SaveSeparateMipmaps => SaveSeparateMipmapsCheckBox.IsChecked ?? false;
        public string? PresetName => (PresetComboBox.SelectedItem as TextureConversionPreset)?.Name;

        public void LoadSettings(CompressionSettingsData compression, MipProfileSettings mipProfile, bool generateMips, bool saveSeparateMips) {
            _isLoading = true;

            CompressionFormatComboBox.SelectedItem = compression.CompressionFormat;
            OutputFormatComboBox.SelectedItem = compression.OutputFormat;
            ETC1SQualitySlider.Value = compression.QualityLevel;
            UASTCQualitySlider.Value = compression.UASTCQuality;
            UseUASTCRDOCheckBox.IsChecked = compression.UseUASTCRDO;
            UASTCRDOLambdaSlider.Value = compression.UASTCRDOQuality;
            PerceptualModeCheckBox.IsChecked = compression.PerceptualMode;

            MipFilterComboBox.SelectedItem = mipProfile.Filter;
            ApplyGammaCorrectionCheckBox.IsChecked = mipProfile.ApplyGammaCorrection;
            GenerateMipmapsCheckBox.IsChecked = generateMips;
            SaveSeparateMipmapsCheckBox.IsChecked = saveSeparateMips;
            NormalizeNormalsCheckBox.IsChecked = mipProfile.NormalizeNormals;

            KTX2SupercompressionComboBox.SelectedItem = compression.KTX2Supercompression;
            UseETC1SRDOCheckBox.IsChecked = compression.UseETC1SRDO;
            SeparateAlphaCheckBox.IsChecked = compression.SeparateAlpha;
            ForceAlphaCheckBox.IsChecked = compression.ForceAlphaChannel;
            RemoveAlphaCheckBox.IsChecked = compression.RemoveAlphaChannel;
            ForceLinearCheckBox.IsChecked = compression.ForceLinearColorSpace;
            MipClampCheckBox.IsChecked = compression.ClampMipmaps;
            LinearMipFilterCheckBox.IsChecked = compression.UseLinearMipFiltering;

            UpdateCompressionPanels();
            UpdateOutputFormatPanels();

            _isLoading = false;
        }

        // OBSOLETE: This method is no longer used after preset system refactoring
        // Presets are now loaded through InitializePresets() using PresetManager
        [Obsolete("Use InitializePresets() instead")]
        public void LoadPresets(string[] presetNames, string? selectedPreset = null) {
            // Convert to list for ItemsSource compatibility
            var presetList = new List<object> { "(Custom)" };
            presetList.AddRange(presetNames);

            PresetComboBox.ItemsSource = presetList;

            if (!string.IsNullOrEmpty(selectedPreset) && presetList.Contains(selectedPreset)) {
                PresetComboBox.SelectedItem = selectedPreset;
            } else {
                PresetComboBox.SelectedIndex = 0; // Custom
            }
        }

        private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (!_isLoading && PresetComboBox.SelectedItem is TextureConversionPreset selectedPreset) {
                LoadPresetToUI(selectedPreset);
                OnSettingsChanged();
            }
        }

        private void LoadPresetToUI(TextureConversionPreset preset) {
            _isLoading = true;

            // Compression settings
            CompressionFormatComboBox.SelectedItem = preset.CompressionFormat;
            OutputFormatComboBox.SelectedItem = preset.OutputFormat;
            KTX2SupercompressionComboBox.SelectedItem = preset.KTX2Supercompression;

            // Quality settings
            ETC1SQualitySlider.Value = preset.QualityLevel;
            UASTCQualitySlider.Value = preset.UASTCQuality;
            UseUASTCRDOCheckBox.IsChecked = preset.UseUASTCRDO;
            UASTCRDOLambdaSlider.Value = preset.UASTCRDOQuality;
            UseETC1SRDOCheckBox.IsChecked = preset.UseETC1SRDO;

            // Mipmap settings
            GenerateMipmapsCheckBox.IsChecked = preset.GenerateMipmaps;
            MipFilterComboBox.SelectedItem = preset.MipFilter;
            LinearMipFilterCheckBox.IsChecked = preset.UseLinearMipFiltering;
            MipClampCheckBox.IsChecked = preset.ClampMipmaps;

            // Advanced settings
            PerceptualModeCheckBox.IsChecked = preset.PerceptualMode;
            SeparateAlphaCheckBox.IsChecked = preset.SeparateAlpha;
            ForceAlphaCheckBox.IsChecked = preset.ForceAlphaChannel;
            RemoveAlphaCheckBox.IsChecked = preset.RemoveAlphaChannel;
            ForceLinearCheckBox.IsChecked = preset.ForceLinearColorSpace;
            NormalizeNormalsCheckBox.IsChecked = preset.NormalizeNormals;

            UpdateCompressionPanels();
            UpdateOutputFormatPanels();

            _isLoading = false;
        }

        private void AutoDetectPreset_Click(object sender, RoutedEventArgs e) {
            // Вызываем событие, чтобы MainWindow мог передать имя файла
            OnAutoDetectRequested();
        }

        /// <summary>
        /// Автоматически выбирает пресет на основе имени файла
        /// </summary>
        public bool AutoDetectPresetByFileName(string fileName) {
            if (string.IsNullOrEmpty(fileName)) {
                return false;
            }

            var matchingPreset = _presetManager.FindPresetByFileName(fileName);
            if (matchingPreset != null) {
                PresetComboBox.SelectedItem = matchingPreset;
                LoadPresetToUI(matchingPreset);
                return true;
            }

            return false;
        }

        // Событие для запроса автоопределения
        public event EventHandler? AutoDetectRequested;

        private void OnAutoDetectRequested() {
            AutoDetectRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ManagePresets_Click(object sender, RoutedEventArgs e) {
            var presetsWindow = new Windows.PresetManagementWindow(_presetManager);
            presetsWindow.Owner = Window.GetWindow(this);
            presetsWindow.ShowDialog();

            // Обновляем список пресетов после закрытия окна
            var currentPreset = PresetComboBox.SelectedItem as TextureConversionPreset;
            InitializePresets();

            // Пытаемся восстановить выбранный пресет
            if (currentPreset != null) {
                var updatedPreset = _presetManager.GetPreset(currentPreset.Name);
                if (updatedPreset != null) {
                    PresetComboBox.SelectedItem = updatedPreset;
                }
            }
        }

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

        // Событие для запроса конвертации
        public event EventHandler? ConvertRequested;

        private void OnConvertRequested() {
            ConvertRequested?.Invoke(this, EventArgs.Empty);
        }

        private void CheckboxSettingChanged(object sender, RoutedEventArgs e) {
            if (!_isLoading) {
                OnSettingsChanged();
            }
        }

        private void ComboBoxSettingChanged(object sender, SelectionChangedEventArgs e) {
            if (!_isLoading) {
                OnSettingsChanged();
            }
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            if (!_isLoading) {
                OnSettingsChanged();
            }
        }

        private void KTX2SupercompressionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (!_isLoading) {
                OnSettingsChanged();
            }
        }

        private void ForceAlphaCheckBox_Checked(object sender, RoutedEventArgs e) {
            if (RemoveAlphaCheckBox.IsChecked == true) {
                RemoveAlphaCheckBox.IsChecked = false;
            }
            CheckboxSettingChanged(sender, e);
        }

        private void RemoveAlphaCheckBox_Checked(object sender, RoutedEventArgs e) {
            if (ForceAlphaCheckBox.IsChecked == true) {
                ForceAlphaCheckBox.IsChecked = false;
            }
            CheckboxSettingChanged(sender, e);
        }

        private void OnSettingsChanged() {
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
