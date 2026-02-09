using System;
using System.Windows;
using AssetProcessor.Services;
using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureConversion.Settings;

namespace AssetProcessor.Controls {
    /// <summary>
    /// Partial class containing settings getters and loaders:
    /// - GetCompressionSettings, GetMipProfileSettings, GetToksvigSettings, GetHistogramSettings
    /// - LoadSettings, LoadToksvigSettings, LoadHistogramSettings
    /// - Preset name property
    /// </summary>
    public partial class TextureConversionSettingsPanel {

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

            var colorSpace = ColorSpaceComboBox.SelectedItem != null
                ? (ColorSpace)ColorSpaceComboBox.SelectedItem
                : ColorSpace.Auto;

            var toktxFilter = ToktxFilterComboBox.SelectedItem != null
                ? (ToktxFilterType)ToktxFilterComboBox.SelectedItem
                : ToktxFilterType.Kaiser;

            var wrapMode = WrapModeComboBox.SelectedItem != null
                ? (WrapMode)WrapModeComboBox.SelectedItem
                : WrapMode.Clamp;

            return new CompressionSettingsData {
                CompressionFormat = format,
                OutputFormat = outputFormat,
                CompressionLevel = (int)Math.Round(CompressionLevelSlider.Value),
                QualityLevel = (int)Math.Round(ETC1SQualitySlider.Value),
                UASTCQuality = (int)Math.Round(UASTCQualitySlider.Value),
                UseUASTCRDO = UseUASTCRDOCheckBox.IsChecked ?? true,
                UASTCRDOQuality = (float)Math.Round(UASTCRDOLambdaSlider.Value, 3),
                PerceptualMode = PerceptualModeCheckBox.IsChecked ?? true,
                KTX2Supercompression = supercompression,
                KTX2ZstdLevel = (int)Math.Round(ZstdLevelSlider.Value),
                UseETC1SRDO = UseETC1SRDOCheckBox.IsChecked ?? true,
                ForceAlphaChannel = ForceAlphaCheckBox.IsChecked ?? false,
                RemoveAlphaChannel = RemoveAlphaCheckBox.IsChecked ?? false,
                ColorSpace = colorSpace,
                ToktxMipFilter = toktxFilter,
                WrapMode = wrapMode,
                ClampMipmaps = false,
                UseLinearMipFiltering = false,
                GenerateMipmaps = GenerateMipmapsCheckBox.IsChecked ?? true,
                UseCustomMipmaps = CustomMipmapsCheckBox.IsChecked ?? false,
                ConvertToNormalMap = ConvertToNormalMapCheckBox.IsChecked ?? false,
                NormalizeVectors = NormalizeVectorsCheckBox.IsChecked ?? false,
                KeepRGBLayout = false,
                RemoveTemporaryMipmaps = !(RemoveTemporalMipmapsCheckBox.IsChecked ?? false)
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
            var mode = ToksvigCalculationModeComboBox.SelectedItem != null
                ? (ToksvigCalculationMode)ToksvigCalculationModeComboBox.SelectedItem
                : ToksvigCalculationMode.Classic;

            return new ToksvigSettings {
                Enabled = ToksvigEnabledCheckBox.IsChecked ?? false,
                CalculationMode = mode,
                CompositePower = (float)ToksvigCompositePowerSlider.Value,
                MinToksvigMipLevel = (int)ToksvigMinMipLevelSlider.Value,
                SmoothVariance = ToksvigSmoothVarianceCheckBox.IsChecked ?? true,
                UseEnergyPreserving = ToksvigUseEnergyPreservingCheckBox.IsChecked ?? true,
                VarianceThreshold = (float)ToksvigVarianceThresholdSlider.Value,
                NormalMapPath = string.IsNullOrWhiteSpace(NormalMapPathTextBox.Text) ? null : NormalMapPathTextBox.Text
            };
        }

        public HistogramSettings? GetHistogramSettings() {
            if (EnableHistogramCheckBox.IsChecked != true) {
                return null;
            }

            var quality = HistogramQualityComboBox.SelectedItem != null
                ? (HistogramQuality)HistogramQualityComboBox.SelectedItem
                : HistogramQuality.HighQuality;

            var channelMode = HistogramChannelModeComboBox.SelectedItem != null
                ? (HistogramChannelMode)HistogramChannelModeComboBox.SelectedItem
                : HistogramChannelMode.AverageLuminance;

            var settings = quality == HistogramQuality.HighQuality
                ? HistogramSettings.CreateHighQuality()
                : HistogramSettings.CreateFast();

            settings.ChannelMode = channelMode;
            settings.PercentileLow = (float)HistogramPercentileLowSlider.Value;
            settings.PercentileHigh = (float)HistogramPercentileHighSlider.Value;
            settings.KneeWidth = (float)HistogramKneeWidthSlider.Value;
            settings.MinRangeThreshold = (float)HistogramMinRangeThresholdSlider.Value;

            return settings;
        }

        public bool GenerateMipmaps => GenerateMipmapsCheckBox.IsChecked ?? true;
        public bool SaveSeparateMipmaps => SaveSeparateMipmapsCheckBox.IsChecked ?? false;

        /// <summary>
        /// Возвращает имя текущего выбранного пресета
        /// </summary>
        public string? PresetName {
            get {
                if (PresetComboBox.SelectedItem is string presetName) {
                    return presetName == "Custom" ? null : presetName;
                }
                if (PresetComboBox.SelectedItem is TextureConversionPreset preset) {
                    return preset.Name;
                }
                return null;
            }
        }

        ToksvigSettings ITextureConversionSettingsProvider.GetToksvigSettings(string texturePath) =>
            GetToksvigSettingsWithAutoDetect(texturePath);

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

            // Color Space
            ColorSpaceComboBox.SelectedItem = compression.ColorSpace;

            // Mipmaps
            MipFilterComboBox.SelectedItem = mipProfile.Filter;
            ToktxFilterComboBox.SelectedItem = compression.ToktxMipFilter;
            WrapModeComboBox.SelectedItem = compression.WrapMode;
            ApplyGammaCorrectionCheckBox.IsChecked = mipProfile.ApplyGammaCorrection;
            GenerateMipmapsCheckBox.IsChecked = generateMips;
            SaveSeparateMipmapsCheckBox.IsChecked = saveSeparateMips;
            CustomMipmapsCheckBox.IsChecked = false;

            // Normal Maps
            NormalizeNormalsCheckBox.IsChecked = mipProfile.NormalizeNormals;
            ConvertToNormalMapCheckBox.IsChecked = compression.ConvertToNormalMap;
            NormalizeVectorsCheckBox.IsChecked = compression.NormalizeVectors;

            // Toksvig (inverted logic)
            RemoveTemporalMipmapsCheckBox.IsChecked = !compression.RemoveTemporaryMipmaps;

            UpdateCompressionPanels();
            UpdateOutputFormatPanels();
            UpdateMipmapPanelsVisibility();

            _isLoading = false;
        }

        public void LoadToksvigSettings(ToksvigSettings settings, bool loadNormalMapPath = false) {
            _isLoading = true;

            ToksvigEnabledCheckBox.IsChecked = settings.Enabled;
            ToksvigCalculationModeComboBox.SelectedItem = settings.CalculationMode;
            ToksvigCompositePowerSlider.Value = settings.CompositePower;
            ToksvigMinMipLevelSlider.Value = settings.MinToksvigMipLevel;
            ToksvigSmoothVarianceCheckBox.IsChecked = settings.SmoothVariance;
            ToksvigUseEnergyPreservingCheckBox.IsChecked = settings.UseEnergyPreserving;
            ToksvigVarianceThresholdSlider.Value = settings.VarianceThreshold;

            if (settings.Enabled) {
                CustomMipmapsCheckBox.IsChecked = true;
            }

            if (loadNormalMapPath) {
                NormalMapPathTextBox.Text = settings.NormalMapPath ?? string.Empty;
            }

            UpdateToksvigCalculationModePanels();
            UpdateMipmapPanelsVisibility();

            _isLoading = false;
        }

        public void LoadHistogramSettings(HistogramSettings? settings) {
            _isLoading = true;

            if (settings != null && settings.Mode != HistogramMode.Off) {
                EnableHistogramCheckBox.IsChecked = true;
                HistogramQualityComboBox.SelectedItem = settings.Quality;
                HistogramChannelModeComboBox.SelectedItem = settings.ChannelMode;
                HistogramPercentileLowSlider.Value = settings.PercentileLow;
                HistogramPercentileHighSlider.Value = settings.PercentileHigh;
                HistogramKneeWidthSlider.Value = settings.KneeWidth;
                HistogramMinRangeThresholdSlider.Value = settings.MinRangeThreshold;
            } else {
                EnableHistogramCheckBox.IsChecked = false;
            }

            _isLoading = false;
        }
    }
}
