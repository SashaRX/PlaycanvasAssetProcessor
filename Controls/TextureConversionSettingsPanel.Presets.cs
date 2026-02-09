using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureConversion.Settings;

namespace AssetProcessor.Controls {
    /// <summary>
    /// Partial class containing preset loading and management:
    /// - ConversionPreset (new system) loading via ParameterValues dictionary
    /// - TextureConversionPreset (legacy system) loading via typed properties
    /// - Preset selection, auto-detect, and management window
    /// </summary>
    public partial class TextureConversionSettingsPanel {

        private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (_isLoading) return;

            // Новая система: строковые имена пресетов (ConversionSettingsManager)
            if (PresetComboBox.SelectedItem is string presetName) {
                if (_conversionSettingsManager != null && presetName != "Custom") {
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
                foreach (var param in preset.ParameterValues) {
                    ApplyPresetParameter(param.Key, param.Value);
                }

                UpdateCompressionPanels();
                UpdateOutputFormatPanels();

            } finally {
                _isLoading = false;
            }
        }

        private void ApplyPresetParameter(string key, object? value) {
            switch (key) {
                case "compressionFormat":
                    if (Enum.TryParse<CompressionFormat>(value?.ToString(), true, out var format))
                        CompressionFormatComboBox.SelectedItem = format;
                    break;

                case "outputFormat":
                    if (Enum.TryParse<OutputFormat>(value?.ToString(), true, out var outputFormat))
                        OutputFormatComboBox.SelectedItem = outputFormat;
                    break;

                case "qualityLevel":
                    if (value is int qualityInt) ETC1SQualitySlider.Value = qualityInt;
                    break;

                case "uastcQuality":
                    if (value is int uastcQuality) UASTCQualitySlider.Value = uastcQuality;
                    break;

                case "uastcRDOLambda":
                    if (value is double rdoLambda) UASTCRDOLambdaSlider.Value = rdoLambda;
                    break;

                case "treatAsSRGB":
                    if (value is bool srgb && srgb) ColorSpaceComboBox.SelectedItem = ColorSpace.SRGB;
                    break;

                case "treatAsLinear":
                    if (value is bool linear && linear) ColorSpaceComboBox.SelectedItem = ColorSpace.Linear;
                    break;

                case "colorSpace":
                    if (Enum.TryParse<ColorSpace>(value?.ToString(), true, out var colorSpace))
                        ColorSpaceComboBox.SelectedItem = colorSpace;
                    break;

                case "mipFilter":
                    if (Enum.TryParse<FilterType>(value?.ToString(), true, out var filter))
                        MipFilterComboBox.SelectedItem = filter;
                    break;

                case "perceptualMode":
                    if (value is bool perceptual) PerceptualModeCheckBox.IsChecked = perceptual;
                    break;

                case "normalizeVectors":
                    if (value is bool normalize) NormalizeVectorsCheckBox.IsChecked = normalize;
                    break;

                case "enableToksvig":
                    if (value is bool enableToksvig) ToksvigEnabledCheckBox.IsChecked = enableToksvig;
                    break;

                case "compositePower":
                    if (value is double compositePower) ToksvigCompositePowerSlider.Value = compositePower;
                    break;

                case "enableHistogram":
                    if (value is bool enableHistogram) EnableHistogramCheckBox.IsChecked = enableHistogram;
                    break;

                case "histogramQuality":
                    if (Enum.TryParse<HistogramQuality>(value?.ToString(), true, out var histQuality))
                        HistogramQualityComboBox.SelectedItem = histQuality;
                    break;

                case "histogramChannelMode":
                    if (Enum.TryParse<HistogramChannelMode>(value?.ToString(), true, out var histChannel))
                        HistogramChannelModeComboBox.SelectedItem = histChannel;
                    break;

                case "histogramPercentileLow":
                    if (value is double percLow) HistogramPercentileLowSlider.Value = percLow;
                    break;

                case "histogramPercentileHigh":
                    if (value is double percHigh) HistogramPercentileHighSlider.Value = percHigh;
                    break;

                case "histogramKneeWidth":
                    if (value is double kneeWidth) HistogramKneeWidthSlider.Value = kneeWidth;
                    break;

                case "histogramMinRangeThreshold":
                    if (value is double minRange) HistogramMinRangeThresholdSlider.Value = minRange;
                    break;
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
            ApplyGammaCorrectionCheckBox.IsChecked = preset.ApplyGammaCorrection;

            // Advanced settings
            PerceptualModeCheckBox.IsChecked = preset.PerceptualMode;
            ForceAlphaCheckBox.IsChecked = preset.ForceAlphaChannel;
            RemoveAlphaCheckBox.IsChecked = preset.RemoveAlphaChannel;

            // Color Space
            if (preset.TreatAsLinear) {
                ColorSpaceComboBox.SelectedItem = ColorSpace.Linear;
            } else if (preset.TreatAsSRGB) {
                ColorSpaceComboBox.SelectedItem = ColorSpace.SRGB;
            } else {
                ColorSpaceComboBox.SelectedItem = ColorSpace.Auto;
            }

            // Normal Maps
            NormalizeNormalsCheckBox.IsChecked = preset.NormalizeNormals;
            ConvertToNormalMapCheckBox.IsChecked = preset.ConvertToNormalMap;
            NormalizeVectorsCheckBox.IsChecked = preset.NormalizeVectors;

            // Toksvig
            LoadToksvigSettings(preset.ToksvigSettings);

            // Histogram Analysis
            LoadHistogramSettings(preset.HistogramSettings);

            UpdateCompressionPanels();
            UpdateOutputFormatPanels();

            _isLoading = false;
        }

        /// <summary>
        /// Программно устанавливает пресет БЕЗ триггера событий SettingsChanged
        /// </summary>
        public void SetPresetSilently(string presetName) {
            _isLoading = true;
            try {
                if (PresetComboBox.Items.Cast<string>().Contains(presetName)) {
                    PresetComboBox.SelectedItem = presetName;
                } else {
                    PresetComboBox.SelectedIndex = 0; // "Custom"
                }
            } finally {
                _isLoading = false;
            }
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

            if (_conversionSettingsManager != null) {
                // Новая система — обновляем список строк
                var presets = ConversionSettingsSchema.GetPredefinedPresets();
                var currentSelectedName = PresetComboBox.SelectedItem as string;

                var presetNames = new List<string> { "Custom" };
                presetNames.AddRange(presets.Select(p => p.Name));

                PresetComboBox.ItemsSource = presetNames;

                if (!string.IsNullOrEmpty(currentSelectedName) && presetNames.Contains(currentSelectedName)) {
                    PresetComboBox.SelectedItem = currentSelectedName;
                } else {
                    PresetComboBox.SelectedIndex = 0;
                }
            } else {
                // Старая система — обновляем список объектов
                var currentPreset = PresetComboBox.SelectedItem as TextureConversionPreset;
                InitializePresets();

                if (currentPreset != null) {
                    var updatedPreset = _presetManager.GetPreset(currentPreset.Name);
                    if (updatedPreset != null) {
                        PresetComboBox.SelectedItem = updatedPreset;
                    }
                }
            }
        }
    }
}
