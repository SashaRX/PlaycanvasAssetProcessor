using System;
using System.IO;
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

            // Simplification Settings
            SimplificationErrorSlider.Value = 0.01;
            PermissiveSimplificationCheckBox.IsChecked = false;
            LockBorderVerticesCheckBox.IsChecked = false;

            // Vertex Attributes
            PositionFormatComboBox.SelectedItem = VertexPositionFormat.Integer;
            FloatTexCoordsCheckBox.IsChecked = false;
            FloatNormalsCheckBox.IsChecked = false;
            InterleavedAttributesCheckBox.IsChecked = false;
            KeepVertexAttributesCheckBox.IsChecked = true;
            FlipUVsCheckBox.IsChecked = false;

            // Animation Settings
            AnimTranslationBitsSlider.Value = 16;
            AnimRotationBitsSlider.Value = 12;
            AnimScaleBitsSlider.Value = 16;
            AnimFrameRateSlider.Value = 30;
            KeepConstantTracksCheckBox.IsChecked = false;

            // Scene Options
            KeepNamedNodesCheckBox.IsChecked = false;
            KeepNamedMaterialsCheckBox.IsChecked = false;
            KeepExtrasCheckBox.IsChecked = false;
            MergeMeshInstancesCheckBox.IsChecked = false;
            UseGpuInstancingCheckBox.IsChecked = false;
            CompressedWithFallbackCheckBox.IsChecked = false;
            DisableQuantizationCheckBox.IsChecked = false;

            _isLoading = false;
        }

        /// <summary>
        /// Получает текущие настройки конвертации модели
        /// </summary>
        /// <param name="filePath">Путь к файлу модели (опционально, для определения типа источника)</param>
        public ModelConversionSettings GetSettings(string? filePath = null) {
            var quantization = new QuantizationSettings {
                PositionBits = (int)PositionBitsSlider.Value,
                TexCoordBits = (int)TexCoordBitsSlider.Value,
                NormalBits = (int)NormalBitsSlider.Value,
                ColorBits = (int)ColorBitsSlider.Value
            };

            var advancedSettings = new GltfPackSettings {
                // Simplification
                SimplificationError = (float)SimplificationErrorSlider.Value,
                PermissiveSimplification = PermissiveSimplificationCheckBox.IsChecked ?? false,
                LockBorderVertices = LockBorderVerticesCheckBox.IsChecked ?? false,

                // Vertex Attributes
                // Безопасное приведение типа с проверкой: используем as для защиты от InvalidCastException
                PositionFormat = PositionFormatComboBox.SelectedItem is VertexPositionFormat format
                    ? format
                    : VertexPositionFormat.Integer,
                FloatTexCoords = FloatTexCoordsCheckBox.IsChecked ?? false,
                FloatNormals = FloatNormalsCheckBox.IsChecked ?? false,
                InterleavedAttributes = InterleavedAttributesCheckBox.IsChecked ?? false,
                KeepVertexAttributes = KeepVertexAttributesCheckBox.IsChecked ?? true,
                FlipUVs = FlipUVsCheckBox.IsChecked ?? false,

                // Animation
                AnimationTranslationBits = (int)AnimTranslationBitsSlider.Value,
                AnimationRotationBits = (int)AnimRotationBitsSlider.Value,
                AnimationScaleBits = (int)AnimScaleBitsSlider.Value,
                AnimationFrameRate = (int)AnimFrameRateSlider.Value,
                KeepConstantAnimationTracks = KeepConstantTracksCheckBox.IsChecked ?? false,

                // Scene
                KeepNamedNodes = KeepNamedNodesCheckBox.IsChecked ?? false,
                KeepNamedMaterials = KeepNamedMaterialsCheckBox.IsChecked ?? false,
                KeepExtras = KeepExtrasCheckBox.IsChecked ?? false,
                MergeMeshInstances = MergeMeshInstancesCheckBox.IsChecked ?? false,
                UseGpuInstancing = UseGpuInstancingCheckBox.IsChecked ?? false,

                // Misc
                CompressedWithFallback = CompressedWithFallbackCheckBox.IsChecked ?? false,
                DisableQuantization = DisableQuantizationCheckBox.IsChecked ?? false
            };

            // Определяем тип источника по расширению файла
            var sourceType = ModelSourceType.FBX; // По умолчанию FBX
            if (!string.IsNullOrEmpty(filePath)) {
                var extension = Path.GetExtension(filePath).ToLowerInvariant();
                if (extension == ".glb" || extension == ".gltf") {
                    sourceType = ModelSourceType.GLB;
                } else if (extension == ".fbx") {
                    sourceType = ModelSourceType.FBX;
                }
                // Если расширение неизвестно, остаётся FBX по умолчанию
            }

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
                SourceType = sourceType,
                GenerateLods = GenerateLodsCheckBox.IsChecked ?? true,
                LodChain = lodChain,
                CompressionMode = compressionMode,
                Quantization = quantization,
                AdvancedSettings = advancedSettings,
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

            // Basic Settings
            GenerateLodsCheckBox.IsChecked = settings.GenerateLods;
            CompressionModeComboBox.SelectedItem = settings.CompressionMode;
            GenerateBothTracksCheckBox.IsChecked = settings.GenerateBothTracks;
            ExcludeTexturesCheckBox.IsChecked = settings.ExcludeTextures;
            GenerateManifestCheckBox.IsChecked = settings.GenerateManifest;
            GenerateQAReportCheckBox.IsChecked = settings.GenerateQAReport;
            CleanupIntermediateFilesCheckBox.IsChecked = settings.CleanupIntermediateFiles;
            LodHysteresisSlider.Value = settings.LodHysteresis;

            // Quantization Settings
            if (settings.Quantization != null) {
                PositionBitsSlider.Value = settings.Quantization.PositionBits;
                TexCoordBitsSlider.Value = settings.Quantization.TexCoordBits;
                NormalBitsSlider.Value = settings.Quantization.NormalBits;
                ColorBitsSlider.Value = settings.Quantization.ColorBits;
            }

            // Advanced Settings
            if (settings.AdvancedSettings != null) {
                var adv = settings.AdvancedSettings;

                // Simplification
                SimplificationErrorSlider.Value = adv.SimplificationError ?? 0.01;
                PermissiveSimplificationCheckBox.IsChecked = adv.PermissiveSimplification;
                LockBorderVerticesCheckBox.IsChecked = adv.LockBorderVertices;

                // Vertex Attributes
                PositionFormatComboBox.SelectedItem = adv.PositionFormat;
                FloatTexCoordsCheckBox.IsChecked = adv.FloatTexCoords;
                FloatNormalsCheckBox.IsChecked = adv.FloatNormals;
                InterleavedAttributesCheckBox.IsChecked = adv.InterleavedAttributes;
                KeepVertexAttributesCheckBox.IsChecked = adv.KeepVertexAttributes;
                FlipUVsCheckBox.IsChecked = adv.FlipUVs;

                // Animation
                AnimTranslationBitsSlider.Value = adv.AnimationTranslationBits;
                AnimRotationBitsSlider.Value = adv.AnimationRotationBits;
                AnimScaleBitsSlider.Value = adv.AnimationScaleBits;
                AnimFrameRateSlider.Value = adv.AnimationFrameRate;
                KeepConstantTracksCheckBox.IsChecked = adv.KeepConstantAnimationTracks;

                // Scene
                KeepNamedNodesCheckBox.IsChecked = adv.KeepNamedNodes;
                KeepNamedMaterialsCheckBox.IsChecked = adv.KeepNamedMaterials;
                KeepExtrasCheckBox.IsChecked = adv.KeepExtras;
                MergeMeshInstancesCheckBox.IsChecked = adv.MergeMeshInstances;
                UseGpuInstancingCheckBox.IsChecked = adv.UseGpuInstancing;

                // Misc
                CompressedWithFallbackCheckBox.IsChecked = adv.CompressedWithFallback;
                DisableQuantizationCheckBox.IsChecked = adv.DisableQuantization;
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
