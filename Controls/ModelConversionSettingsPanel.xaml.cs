using System;
using System.Windows;
using System.Windows.Controls;
using AssetProcessor.ModelConversion.Core;
using NLog;

namespace AssetProcessor.Controls {
    public partial class ModelConversionSettingsPanel : UserControl {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private bool _isLoading = false;

        public event EventHandler? SettingsChanged;
        public event EventHandler? ProcessRequested;

        public ModelConversionSettingsPanel() {
            InitializeComponent();
            InitializeDefaults();
        }

        private void InitializeDefaults() {
            _isLoading = true;

            // Basic Settings
            GenerateLodsCheckBox.IsChecked = true;
            CompressionModeComboBox.SelectedItem = CompressionMode.Quantization;
            GenerateBothTracksCheckBox.IsChecked = true;
            ExcludeTexturesCheckBox.IsChecked = true;
            GenerateManifestCheckBox.IsChecked = true;
            GenerateQAReportCheckBox.IsChecked = true;
            CleanupIntermediateFilesCheckBox.IsChecked = true;

            // Quantization Settings (Default preset values)
            PositionBitsSlider.Value = 14;
            TexCoordBitsSlider.Value = 16;  // 16 бит для корректной денормализации (избегаем 12-бит bug)
            NormalBitsSlider.Value = 10;
            ColorBitsSlider.Value = 8;

            // LOD Settings
            LodHysteresisSlider.Value = 0.02;

            _isLoading = false;
        }

        /// <summary>
        /// Получает текущие настройки конвертации модели
        /// </summary>
        public ModelConversionSettings GetSettings() {
            var quantization = new QuantizationSettings {
                PositionBits = (int)PositionBitsSlider.Value,
                TexCoordBits = (int)TexCoordBitsSlider.Value,
                NormalBits = (int)NormalBitsSlider.Value,
                ColorBits = (int)ColorBitsSlider.Value
            };

            var compressionMode = CompressionModeComboBox.SelectedItem != null
                ? (CompressionMode)CompressionModeComboBox.SelectedItem
                : CompressionMode.Quantization;

            // Build LOD chain based on user settings
            var lodChain = new List<LodSettings>();

            // LOD0 always enabled
            lodChain.Add(LodSettings.CreateDefault(LodLevel.LOD0));

            if (Lod1EnabledCheckBox.IsChecked == true) {
                var lod1 = LodSettings.CreateDefault(LodLevel.LOD1);
                lod1.SimplificationRatio = (float)(Lod1RatioSlider.Value / 100.0);
                lodChain.Add(lod1);
            }

            if (Lod2EnabledCheckBox.IsChecked == true) {
                var lod2 = LodSettings.CreateDefault(LodLevel.LOD2);
                lod2.SimplificationRatio = (float)(Lod2RatioSlider.Value / 100.0);
                lodChain.Add(lod2);
            }

            if (Lod3EnabledCheckBox.IsChecked == true) {
                var lod3 = LodSettings.CreateDefault(LodLevel.LOD3);
                lod3.SimplificationRatio = (float)(Lod3RatioSlider.Value / 100.0);
                lodChain.Add(lod3);
            }

            return new ModelConversionSettings {
                GenerateLods = GenerateLodsCheckBox.IsChecked ?? true,
                LodChain = lodChain,
                CompressionMode = compressionMode,
                Quantization = quantization,
                LodHysteresis = (float)LodHysteresisSlider.Value,
                GenerateBothTracks = GenerateBothTracksCheckBox.IsChecked ?? true,
                CleanupIntermediateFiles = CleanupIntermediateFilesCheckBox.IsChecked ?? true,
                ExcludeTextures = ExcludeTexturesCheckBox.IsChecked ?? true,
                GenerateManifest = GenerateManifestCheckBox.IsChecked ?? true,
                GenerateQAReport = GenerateQAReportCheckBox.IsChecked ?? true
            };
        }

        /// <summary>
        /// Загружает настройки в UI
        /// </summary>
        public void LoadSettings(ModelConversionSettings settings) {
            _isLoading = true;

            GenerateLodsCheckBox.IsChecked = settings.GenerateLods;
            CompressionModeComboBox.SelectedItem = settings.CompressionMode;
            GenerateBothTracksCheckBox.IsChecked = settings.GenerateBothTracks;
            ExcludeTexturesCheckBox.IsChecked = settings.ExcludeTextures;
            GenerateManifestCheckBox.IsChecked = settings.GenerateManifest;
            GenerateQAReportCheckBox.IsChecked = settings.GenerateQAReport;
            CleanupIntermediateFilesCheckBox.IsChecked = settings.CleanupIntermediateFiles;
            LodHysteresisSlider.Value = settings.LodHysteresis;

            if (settings.Quantization != null) {
                PositionBitsSlider.Value = settings.Quantization.PositionBits;
                TexCoordBitsSlider.Value = settings.Quantization.TexCoordBits;
                NormalBitsSlider.Value = settings.Quantization.NormalBits;
                ColorBitsSlider.Value = settings.Quantization.ColorBits;
            }

            // Load LOD settings
            if (settings.LodChain != null) {
                // Reset all LOD checkboxes
                Lod1EnabledCheckBox.IsChecked = false;
                Lod2EnabledCheckBox.IsChecked = false;
                Lod3EnabledCheckBox.IsChecked = false;

                // Enable and configure based on LodChain
                foreach (var lod in settings.LodChain) {
                    switch (lod.Level) {
                        case LodLevel.LOD1:
                            Lod1EnabledCheckBox.IsChecked = true;
                            Lod1RatioSlider.Value = lod.SimplificationRatio * 100;
                            break;
                        case LodLevel.LOD2:
                            Lod2EnabledCheckBox.IsChecked = true;
                            Lod2RatioSlider.Value = lod.SimplificationRatio * 100;
                            break;
                        case LodLevel.LOD3:
                            Lod3EnabledCheckBox.IsChecked = true;
                            Lod3RatioSlider.Value = lod.SimplificationRatio * 100;
                            break;
                    }
                }
            }

            _isLoading = false;
        }

        // ============================================
        // PRESET HANDLERS
        // ============================================

        private void ApplyDefaultPreset_Click(object sender, RoutedEventArgs e) {
            LoadSettings(ModelConversionSettings.CreateDefault());
            OnSettingsChanged();
        }

        private void ApplyProductionPreset_Click(object sender, RoutedEventArgs e) {
            LoadSettings(ModelConversionSettings.CreateProduction());
            OnSettingsChanged();
        }

        private void ApplyHighQualityPreset_Click(object sender, RoutedEventArgs e) {
            LoadSettings(ModelConversionSettings.CreateHighQuality());
            OnSettingsChanged();
        }

        private void ApplyMinSizePreset_Click(object sender, RoutedEventArgs e) {
            LoadSettings(ModelConversionSettings.CreateMinSize());
            OnSettingsChanged();
        }

        // ============================================
        // ACTIONS
        // ============================================

        private void Process_Click(object sender, RoutedEventArgs e) {
            OnProcessRequested();
        }

        private void Apply_Click(object sender, RoutedEventArgs e) {
            OnSettingsChanged();
        }

        private void Reset_Click(object sender, RoutedEventArgs e) {
            InitializeDefaults();
            OnSettingsChanged();
        }

        // ============================================
        // EVENTS
        // ============================================

        private void OnSettingsChanged() {
            if (!_isLoading) {
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void OnProcessRequested() {
            ProcessRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
