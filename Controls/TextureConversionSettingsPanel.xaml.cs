using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureConversion.Settings;

namespace AssetProcessor.Controls {
    public partial class TextureConversionSettingsPanel : UserControl {
        private bool _isLoading = false;
        private readonly PresetManager _presetManager = new();

        /// <summary>
        /// Менеджер настроек конвертации (новая система параметров)
        /// </summary>
        private ConversionSettingsManager? _conversionSettingsManager;

        /// <summary>
        /// Путь к текущей обрабатываемой текстуре (для auto-detect normal map)
        /// </summary>
        private string? _currentTexturePath;

        public event EventHandler? SettingsChanged;
        public event EventHandler? ConvertRequested;
        public event EventHandler? AutoDetectRequested;

        public TextureConversionSettingsPanel() {
            InitializeComponent();
            // КРИТИЧНО: НЕ вызываем InitializePresets() здесь!
            // Пресеты будут загружены из PopulateConversionSettingsUI() в MainWindow
            // через новую систему ConversionSettingsSchema
            InitializeDefaults();
        }

        /// <summary>
        /// Устанавливает ConversionSettingsManager для использования новой системы пресетов
        /// </summary>
        public void SetConversionSettingsManager(ConversionSettingsManager manager) {
            _conversionSettingsManager = manager;

            // КРИТИЧНО: Когда установлен ConversionSettingsManager, загружаем пресеты из новой системы
            // Это гарантирует что ItemsSource заполнен ПОСЛЕ создания панели
            if (manager != null) {
                var presets = ConversionSettingsSchema.GetPredefinedPresets();
                var presetNames = new List<string> { "Custom" };
                presetNames.AddRange(presets.Select(p => p.Name));

                PresetComboBox.ItemsSource = presetNames;
                if (PresetComboBox.Items.Count > 0) {
                    PresetComboBox.SelectedIndex = 0; // "Custom"
                }

                System.Diagnostics.Debug.WriteLine($"[SetConversionSettingsManager] Loaded {presets.Count} presets from ConversionSettingsSchema");
            }
        }

        private void InitializePresets() {
            // Загружаем все пресеты (встроенные + пользовательские)
            var presets = _presetManager.GetAllPresets();
            System.Diagnostics.Debug.WriteLine($"[InitializePresets] Loaded {presets.Count} presets");
            foreach (var preset in presets) {
                System.Diagnostics.Debug.WriteLine($"  - {preset.Name} (IsBuiltIn: {preset.IsBuiltIn})");
            }

            PresetComboBox.ItemsSource = presets;
            PresetComboBox.DisplayMemberPath = "Name";

            if (presets.Count > 0) {
                PresetComboBox.SelectedIndex = 0; // Выбираем первый пресет по умолчанию
                System.Diagnostics.Debug.WriteLine($"[InitializePresets] Selected first preset: {presets[0].Name}");
            } else {
                System.Diagnostics.Debug.WriteLine($"[InitializePresets] WARNING: No presets loaded!");
            }
        }

        private void InitializeDefaults() {
            _isLoading = true;

            // Compression Settings
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

            // Alpha Options
            ForceAlphaCheckBox.IsChecked = false;
            RemoveAlphaCheckBox.IsChecked = false;

            // Color & Space (OETF)
            OETFAutoRadioButton.IsChecked = true;

            // Mipmaps
            GenerateMipmapsCheckBox.IsChecked = true;
            MipFilterComboBox.SelectedIndex = 5; // Kaiser
            // Removed - conflicted with Gamma Correction
            MipClampCheckBox.IsChecked = false;
            RemoveTemporalMipmapsCheckBox.IsChecked = true;
            ApplyGammaCorrectionCheckBox.IsChecked = true;
            SaveSeparateMipmapsCheckBox.IsChecked = false;

            // Normal Maps
            ConvertToNormalMapCheckBox.IsChecked = false;
            NormalizeVectorsCheckBox.IsChecked = false;
            NormalizeNormalsCheckBox.IsChecked = false;
            // Removed - unnecessary option

            // Toksvig
            ToksvigEnabledCheckBox.IsChecked = false;
            ToksvigCompositePowerSlider.Value = 1.0;
            ToksvigMinMipLevelSlider.Value = 1;
            ToksvigSmoothVarianceCheckBox.IsChecked = true;
            NormalMapPathTextBox.Text = string.Empty;

            UpdateCompressionPanels();
            UpdateOutputFormatPanels();

            _isLoading = false;
        }

        // ============================================
        // COMPRESSION FORMAT HANDLING
        // ============================================

        private void CompressionFormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (!_isLoading) {
                UpdateCompressionPanels();
                UpdateOutputFormatPanels(); // Обновляем также output панели (для скрытия суперкомпрессии)
                OnSettingsChanged();
            }
        }

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

        // ============================================
        // OUTPUT FORMAT HANDLING
        // ============================================

        private void OutputFormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (!_isLoading) {
                UpdateOutputFormatPanels();
                OnSettingsChanged();
            }
        }

        private void UpdateOutputFormatPanels() {
            if (OutputFormatComboBox.SelectedItem == null || CompressionFormatComboBox.SelectedItem == null) return;

            var output = (OutputFormat)OutputFormatComboBox.SelectedItem;
            var compression = (CompressionFormat)CompressionFormatComboBox.SelectedItem;

            // Показываем суперкомпрессию только для KTX2 и только НЕ для ETC1S
            // (т.к. --zcmp несовместим с ETC1S/BasisLZ)
            if (output == OutputFormat.KTX2 && compression != CompressionFormat.ETC1S) {
                KTX2SupercompressionPanel.Visibility = Visibility.Visible;
            } else {
                KTX2SupercompressionPanel.Visibility = Visibility.Collapsed;
            }
        }

        // ============================================
        // MUTUAL EXCLUSION CHECKBOXES
        // ============================================

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

        private void OETFRadioButton_Changed(object sender, RoutedEventArgs e) {
            if (!_isLoading) {
                OnSettingsChanged();
            }
        }

        private void ApplyGammaCorrectionCheckBox_Checked(object sender, RoutedEventArgs e) {
            CheckboxSettingChanged(sender, e);
        }

        private void ToksvigEnabledCheckBox_Changed(object sender, RoutedEventArgs e) {
            if (!_isLoading) {
                UpdateNormalMapAutoDetect();
                CheckboxSettingChanged(sender, e);
            }
        }

        private void NormalMapPathTextBox_TextChanged(object sender, TextChangedEventArgs e) {
            if (!_isLoading) {
                UpdateNormalMapAutoDetect();
                TextBoxSettingChanged(sender, e);
            }
        }

        private void UpdateNormalMapAutoDetect() {
            // КРИТИЧНО: Если Toksvig включен И путь пустой И есть текущая текстура - ИЩЕМ normal map!
            bool toksvigEnabled = ToksvigEnabledCheckBox.IsChecked ?? false;
            bool pathEmpty = string.IsNullOrWhiteSpace(NormalMapPathTextBox.Text);

            if (toksvigEnabled && pathEmpty && !string.IsNullOrWhiteSpace(_currentTexturePath)) {
                // АВТОПОИСК normal map прямо СЕЙЧАС!
                var normalMapPath = FindNormalMapForTexture(_currentTexturePath);
                if (!string.IsNullOrWhiteSpace(normalMapPath)) {
                    _isLoading = true; // Чтобы не вызывать событие изменения
                    NormalMapPathTextBox.Text = normalMapPath;
                    _isLoading = false;

                    var fileName = System.IO.Path.GetFileName(normalMapPath);
                    NormalMapStatusTextBlock.Text = $"⚙ Auto-detected: {fileName}";
                    NormalMapStatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;
                    return;
                }
            }

            // Обновляем статус auto-detect для normal map
            if (string.IsNullOrWhiteSpace(NormalMapPathTextBox.Text)) {
                NormalMapStatusTextBlock.Text = "(auto-detect from filename)";
                NormalMapStatusTextBlock.Foreground = System.Windows.Media.Brushes.Gray;
            } else {
                var fileName = System.IO.Path.GetFileName(NormalMapPathTextBox.Text);
                if (System.IO.File.Exists(NormalMapPathTextBox.Text)) {
                    NormalMapStatusTextBlock.Text = $"✓ Using: {fileName}";
                    NormalMapStatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;
                } else {
                    NormalMapStatusTextBlock.Text = $"⚠ Not found: {fileName}";
                    NormalMapStatusTextBlock.Foreground = System.Windows.Media.Brushes.OrangeRed;
                }
            }
        }

        // ============================================
        // SETTINGS GETTERS
        // ============================================

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
                CompressionLevel = (int)Math.Round(CompressionLevelSlider.Value),
                QualityLevel = (int)Math.Round(ETC1SQualitySlider.Value),
                UASTCQuality = (int)Math.Round(UASTCQualitySlider.Value),
                UseUASTCRDO = UseUASTCRDOCheckBox.IsChecked ?? true,
                UASTCRDOQuality = (float)Math.Round(UASTCRDOLambdaSlider.Value, 2),
                PerceptualMode = PerceptualModeCheckBox.IsChecked ?? true,
                KTX2Supercompression = supercompression,
                KTX2ZstdLevel = (int)Math.Round(ZstdLevelSlider.Value),
                UseETC1SRDO = UseETC1SRDOCheckBox.IsChecked ?? true,
                // Alpha options
                ForceAlphaChannel = ForceAlphaCheckBox.IsChecked ?? false,
                RemoveAlphaChannel = RemoveAlphaCheckBox.IsChecked ?? false,
                // OETF (Color Space)
                TreatAsLinear = OETFLinearRadioButton.IsChecked ?? false,
                TreatAsSRGB = OETFSRGBRadioButton.IsChecked ?? false,
                // Mipmaps
                ClampMipmaps = MipClampCheckBox.IsChecked ?? false,
                UseLinearMipFiltering = false, // Removed from UI
                GenerateMipmaps = GenerateMipmapsCheckBox.IsChecked ?? true,
                ConvertToNormalMap = ConvertToNormalMapCheckBox.IsChecked ?? false,
                NormalizeVectors = NormalizeVectorsCheckBox.IsChecked ?? false,
                KeepRGBLayout = false, // Removed from UI
                RemoveTemporaryMipmaps = RemoveTemporalMipmapsCheckBox.IsChecked ?? true
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

        public ToksvigSettings GetToksvigSettings() {
            return new ToksvigSettings {
                Enabled = ToksvigEnabledCheckBox.IsChecked ?? false,
                CompositePower = (float)ToksvigCompositePowerSlider.Value,
                MinToksvigMipLevel = (int)ToksvigMinMipLevelSlider.Value,
                SmoothVariance = ToksvigSmoothVarianceCheckBox.IsChecked ?? true,
                NormalMapPath = string.IsNullOrWhiteSpace(NormalMapPathTextBox.Text) ? null : NormalMapPathTextBox.Text
            };
        }

        /// <summary>
        /// Устанавливает путь текущей текстуры (для auto-detect normal map)
        /// </summary>
        public void SetCurrentTexturePath(string? texturePath) {
            _currentTexturePath = texturePath;
        }

        /// <summary>
        /// Очищает путь к normal map (для auto-detect при выборе новой текстуры)
        /// </summary>
        public void ClearNormalMapPath() {
            _isLoading = true;
            NormalMapPathTextBox.Text = string.Empty;
            UpdateNormalMapAutoDetect();
            _isLoading = false;
        }

        /// <summary>
        /// Пытается автоматически найти normal map для текстуры gloss
        /// </summary>
        public ToksvigSettings GetToksvigSettingsWithAutoDetect(string glossTexturePath) {
            var settings = GetToksvigSettings();

            // Если NormalMapPath уже указан вручную, не делаем автопоиск
            if (!string.IsNullOrWhiteSpace(settings.NormalMapPath)) {
                return settings;
            }

            // Автопоиск normal map если Toksvig включен
            if (settings.Enabled) {
                var normalMapPath = FindNormalMapForTexture(glossTexturePath);
                if (!string.IsNullOrWhiteSpace(normalMapPath)) {
                    settings.NormalMapPath = normalMapPath;

                    // Обновляем UI с найденным путем (серым цветом)
                    Dispatcher.BeginInvoke(() => {
                        NormalMapStatusTextBlock.Text = $"⚙ Auto-detected: {System.IO.Path.GetFileName(normalMapPath)}";
                        NormalMapStatusTextBlock.Foreground = System.Windows.Media.Brushes.Gray;
                    });
                }
            }

            return settings;
        }

        /// <summary>
        /// Sanitizes file path by removing newlines and whitespace
        /// </summary>
        private static string SanitizePath(string? path) {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            return path.Replace("\r", "").Replace("\n", "").Trim();
        }

        /// <summary>
        /// Ищет normal map по имени файла gloss текстуры
        /// </summary>
        private string? FindNormalMapForTexture(string texturePath) {
            if (string.IsNullOrWhiteSpace(texturePath)) return null;

            try {
                // КРИТИЧНО: Sanitize path перед использованием File.Exists!
                texturePath = SanitizePath(texturePath);

                var directory = System.IO.Path.GetDirectoryName(texturePath);
                if (string.IsNullOrEmpty(directory)) return null;

                var fileNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(texturePath);

                // Убираем "_gloss", "_glossiness", "_smoothness" из имени
                var glossSuffixes = new[] { "_gloss", "_glossiness", "_smoothness", "_sm", "_gls" };
                string baseName = fileNameWithoutExt;
                foreach (var suffix in glossSuffixes) {
                    if (baseName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) {
                        baseName = baseName.Substring(0, baseName.Length - suffix.Length);
                        break;
                    }
                }

                // Ищем файлы с "_normal", "_norm", "_nrm", "_n"
                var normalSuffixes = new[] { "_normal", "_norm", "_nrm", "_n", "_normals" };
                var extensions = new[] { ".png", ".jpg", ".jpeg", ".tga", ".bmp", ".tif", ".tiff" };

                foreach (var normalSuffix in normalSuffixes) {
                    foreach (var ext in extensions) {
                        var normalMapPath = System.IO.Path.Combine(directory, baseName + normalSuffix + ext);

                        // КРИТИЧНО: Case-insensitive проверка существования файла!
                        // Файл может быть "oldMailBox_Normal.png" или "oldMailBox_normal.png"
                        if (System.IO.File.Exists(normalMapPath)) {
                            return normalMapPath;
                        }

                        // Попробуем с заглавной буквы (например "_Normal")
                        if (normalSuffix.Length > 0) {
                            string capitalizedSuffix = "_" + char.ToUpper(normalSuffix[1]) + normalSuffix.Substring(2);
                            var capitalizedPath = System.IO.Path.Combine(directory, baseName + capitalizedSuffix + ext);
                            if (System.IO.File.Exists(capitalizedPath)) {
                                return capitalizedPath;
                            }
                        }
                    }
                }
            } catch {
                // Игнорируем ошибки автопоиска
            }

            return null;
        }

        public bool GenerateMipmaps => GenerateMipmapsCheckBox.IsChecked ?? true;
        public bool SaveSeparateMipmaps => SaveSeparateMipmapsCheckBox.IsChecked ?? false;
        public string? PresetName => (PresetComboBox.SelectedItem as TextureConversionPreset)?.Name;

        // ============================================
        // SETTINGS LOADERS
        // ============================================

        public void LoadSettings(CompressionSettingsData compression, MipProfileSettings mipProfile, bool generateMips, bool saveSeparateMips) {
            _isLoading = true;

            // Compression
            CompressionFormatComboBox.SelectedItem = compression.CompressionFormat;
            OutputFormatComboBox.SelectedItem = compression.OutputFormat;
            CompressionLevelSlider.Value = compression.CompressionLevel;
            ETC1SQualitySlider.Value = compression.QualityLevel;
            UASTCQualitySlider.Value = compression.UASTCQuality;
            UseUASTCRDOCheckBox.IsChecked = compression.UseUASTCRDO;
            UASTCRDOLambdaSlider.Value = compression.UASTCRDOQuality;
            PerceptualModeCheckBox.IsChecked = compression.PerceptualMode;
            KTX2SupercompressionComboBox.SelectedItem = compression.KTX2Supercompression;
            ZstdLevelSlider.Value = compression.KTX2ZstdLevel;
            UseETC1SRDOCheckBox.IsChecked = compression.UseETC1SRDO;

            // Alpha
            ForceAlphaCheckBox.IsChecked = compression.ForceAlphaChannel;
            RemoveAlphaCheckBox.IsChecked = compression.RemoveAlphaChannel;

            // Color & Space (OETF) - RadioButtons
            if (compression.TreatAsLinear) {
                OETFLinearRadioButton.IsChecked = true;
            } else if (compression.TreatAsSRGB) {
                OETFSRGBRadioButton.IsChecked = true;
            } else {
                OETFAutoRadioButton.IsChecked = true;
            }

            // Mipmaps
            MipFilterComboBox.SelectedItem = mipProfile.Filter;
            ApplyGammaCorrectionCheckBox.IsChecked = mipProfile.ApplyGammaCorrection;
            GenerateMipmapsCheckBox.IsChecked = generateMips;
            SaveSeparateMipmapsCheckBox.IsChecked = saveSeparateMips;
            MipClampCheckBox.IsChecked = compression.ClampMipmaps;

            // Normal Maps
            NormalizeNormalsCheckBox.IsChecked = mipProfile.NormalizeNormals;
            ConvertToNormalMapCheckBox.IsChecked = compression.ConvertToNormalMap;
            NormalizeVectorsCheckBox.IsChecked = compression.NormalizeVectors;

            // Toksvig (moved RemoveTemporaryMipmaps here, inverted logic)
            RemoveTemporalMipmapsCheckBox.IsChecked = !compression.RemoveTemporaryMipmaps;

            UpdateCompressionPanels();
            UpdateOutputFormatPanels();

            _isLoading = false;
        }

        public void LoadToksvigSettings(ToksvigSettings settings, bool loadNormalMapPath = false) {
            _isLoading = true;

            ToksvigEnabledCheckBox.IsChecked = settings.Enabled;
            ToksvigCompositePowerSlider.Value = settings.CompositePower;
            ToksvigMinMipLevelSlider.Value = settings.MinToksvigMipLevel;
            ToksvigSmoothVarianceCheckBox.IsChecked = settings.SmoothVariance;

            // КРИТИЧНО: НЕ загружаем NormalMapPath по умолчанию!
            // Это позволяет auto-detect работать для каждой новой текстуры
            // Загружаем только если явно указано (например, при загрузке сохраненных настроек)
            if (loadNormalMapPath) {
                NormalMapPathTextBox.Text = settings.NormalMapPath ?? string.Empty;
            }

            _isLoading = false;
        }

        // ============================================
        // PRESET HANDLING
        // ============================================

        private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (_isLoading) return;

            // Новая система: строковые имена пресетов (ConversionSettingsManager)
            if (PresetComboBox.SelectedItem is string presetName) {
                if (_conversionSettingsManager != null && presetName != "Custom") {
                    // Применяем пресет из ConversionSettingsSchema
                    var presets = ConversionSettingsSchema.GetPredefinedPresets();
                    var preset = presets.FirstOrDefault(p => p.Name == presetName);
                    if (preset != null) {
                        LoadConversionPresetToUI(preset);
                        OnSettingsChanged();
                    }
                }
            }
            // Старая система: объекты TextureConversionPreset (PresetManager)
            else if (PresetComboBox.SelectedItem is TextureConversionPreset selectedPreset) {
                LoadPresetToUI(selectedPreset);
                OnSettingsChanged();
            }
        }

        /// <summary>
        /// Загружает ConversionPreset (новая система) в UI
        /// </summary>
        private void LoadConversionPresetToUI(ConversionPreset preset) {
            _isLoading = true;

            try {
                // Применяем значения параметров из пресета
                foreach (var param in preset.ParameterValues) {
                    switch (param.Key) {
                        case "compressionFormat":
                            if (Enum.TryParse<CompressionFormat>(param.Value?.ToString(), true, out var format)) {
                                CompressionFormatComboBox.SelectedItem = format;
                            }
                            break;

                        case "outputFormat":
                            if (Enum.TryParse<OutputFormat>(param.Value?.ToString(), true, out var outputFormat)) {
                                OutputFormatComboBox.SelectedItem = outputFormat;
                            }
                            break;

                        case "qualityLevel":
                            if (param.Value is int qualityInt) {
                                ETC1SQualitySlider.Value = qualityInt;
                            }
                            break;

                        case "uastcQuality":
                            if (param.Value is int uastcQuality) {
                                UASTCQualitySlider.Value = uastcQuality;
                            }
                            break;

                        case "uastcRDOLambda":
                            if (param.Value is double rdoLambda) {
                                UASTCRDOLambdaSlider.Value = rdoLambda;
                            }
                            break;

                        case "treatAsSRGB":
                            if (param.Value is bool srgb && srgb) {
                                OETFSRGBRadioButton.IsChecked = true;
                            }
                            break;

                        case "treatAsLinear":
                            if (param.Value is bool linear && linear) {
                                OETFLinearRadioButton.IsChecked = true;
                            }
                            break;

                        case "mipFilter":
                            if (Enum.TryParse<FilterType>(param.Value?.ToString(), true, out var filter)) {
                                MipFilterComboBox.SelectedItem = filter;
                            }
                            break;

                        case "perceptualMode":
                            if (param.Value is bool perceptual) {
                                PerceptualModeCheckBox.IsChecked = perceptual;
                            }
                            break;

                        case "normalizeVectors":
                            if (param.Value is bool normalize) {
                                NormalizeVectorsCheckBox.IsChecked = normalize;
                            }
                            break;

                        case "enableToksvig":
                            if (param.Value is bool enableToksvig) {
                                ToksvigEnabledCheckBox.IsChecked = enableToksvig;
                            }
                            break;

                        case "compositePower":
                            if (param.Value is double compositePower) {
                                ToksvigCompositePowerSlider.Value = compositePower;
                            }
                            break;
                    }
                }

                UpdateCompressionPanels();
                UpdateOutputFormatPanels();

            } finally {
                _isLoading = false;
            }
        }

        private void LoadPresetToUI(TextureConversionPreset preset) {
            _isLoading = true;

            // Compression settings
            CompressionFormatComboBox.SelectedItem = preset.CompressionFormat;
            OutputFormatComboBox.SelectedItem = preset.OutputFormat;
            CompressionLevelSlider.Value = preset.CompressionLevel;
            KTX2SupercompressionComboBox.SelectedItem = preset.KTX2Supercompression;
            ZstdLevelSlider.Value = preset.KTX2ZstdLevel;

            // Quality settings
            ETC1SQualitySlider.Value = preset.QualityLevel;
            UASTCQualitySlider.Value = preset.UASTCQuality;
            UseUASTCRDOCheckBox.IsChecked = preset.UseUASTCRDO;
            UASTCRDOLambdaSlider.Value = preset.UASTCRDOQuality;
            UseETC1SRDOCheckBox.IsChecked = preset.UseETC1SRDO;

            // Mipmap settings
            GenerateMipmapsCheckBox.IsChecked = preset.GenerateMipmaps;
            MipFilterComboBox.SelectedItem = preset.MipFilter;
            MipClampCheckBox.IsChecked = preset.ClampMipmaps;
            ApplyGammaCorrectionCheckBox.IsChecked = preset.ApplyGammaCorrection;

            // Advanced settings
            PerceptualModeCheckBox.IsChecked = preset.PerceptualMode;
            ForceAlphaCheckBox.IsChecked = preset.ForceAlphaChannel;
            RemoveAlphaCheckBox.IsChecked = preset.RemoveAlphaChannel;

            // Color Space (OETF) - RadioButtons
            if (preset.TreatAsLinear) {
                OETFLinearRadioButton.IsChecked = true;
            } else if (preset.TreatAsSRGB) {
                OETFSRGBRadioButton.IsChecked = true;
            } else {
                OETFAutoRadioButton.IsChecked = true;
            }

            // Normal Maps
            NormalizeNormalsCheckBox.IsChecked = preset.NormalizeNormals;
            ConvertToNormalMapCheckBox.IsChecked = preset.ConvertToNormalMap;
            NormalizeVectorsCheckBox.IsChecked = preset.NormalizeVectors;

            // Toksvig
            LoadToksvigSettings(preset.ToksvigSettings);

            UpdateCompressionPanels();
            UpdateOutputFormatPanels();

            _isLoading = false;
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

        private void AutoDetectPreset_Click(object sender, RoutedEventArgs e) {
            OnAutoDetectRequested();
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
        // EVENT HANDLERS
        // ============================================

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

        private void TextBoxSettingChanged(object sender, TextChangedEventArgs e) {
            if (!_isLoading) {
                OnSettingsChanged();
            }
        }

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
