using AssetProcessor.ModelConversion.Core;

namespace AssetProcessor.ModelConversion.Settings {
    /// <summary>
    /// Настройки конвертации для одной модели (сериализуемый формат)
    /// </summary>
    public class ModelConversionSettingsData {
        /// <summary>
        /// Путь к FBX файлу
        /// </summary>
        public string ModelPath { get; set; } = string.Empty;

        /// <summary>
        /// Включена ли модель для обработки
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Тип источника модели (FBX или GLB)
        /// </summary>
        public ModelSourceType SourceType { get; set; } = ModelSourceType.FBX;

        /// <summary>
        /// Генерировать LOD цепочку
        /// </summary>
        public bool GenerateLods { get; set; } = true;

        /// <summary>
        /// Режим сжатия
        /// </summary>
        public CompressionMode CompressionMode { get; set; } = CompressionMode.Quantization;

        /// <summary>
        /// Генерировать оба трека (glb + meshopt)
        /// </summary>
        public bool GenerateBothTracks { get; set; } = false;

        /// <summary>
        /// Настройки квантования
        /// </summary>
        public QuantizationSettingsData Quantization { get; set; } = QuantizationSettingsData.CreateDefault();

        /// <summary>
        /// Расширенные настройки gltfpack
        /// </summary>
        public GltfPackSettingsData AdvancedSettings { get; set; } = GltfPackSettingsData.CreateDefault();

        /// <summary>
        /// Настройки LOD уровней
        /// </summary>
        public List<LodSettingsData> LodChain { get; set; } = LodSettingsData.CreateDefaultChain();

        /// <summary>
        /// Гистерезис LOD
        /// </summary>
        public float LodHysteresis { get; set; } = 0.02f;

        /// <summary>
        /// Удалять промежуточные файлы
        /// </summary>
        public bool CleanupIntermediateFiles { get; set; } = true;

        /// <summary>
        /// Генерировать манифест
        /// </summary>
        public bool GenerateManifest { get; set; } = true;

        /// <summary>
        /// Генерировать QA отчет
        /// </summary>
        public bool GenerateQAReport { get; set; } = true;

        /// <summary>
        /// Исключить текстуры из GLB (экспортировать только геометрию, материалы, анимации)
        /// ВАЖНО: Текстуры должны обрабатываться отдельно через TextureConversion пайплайн!
        /// </summary>
        public bool ExcludeTextures { get; set; } = true;

        /// <summary>
        /// Конвертирует в ModelConversionSettings
        /// </summary>
        public ModelConversionSettings ToModelConversionSettings() {
            return new ModelConversionSettings {
                SourceType = SourceType,
                GenerateLods = GenerateLods,
                LodChain = LodChain.Select(l => l.ToLodSettings()).ToList(),
                CompressionMode = CompressionMode,
                Quantization = Quantization.ToQuantizationSettings(),
                AdvancedSettings = AdvancedSettings.ToGltfPackSettings(),
                LodHysteresis = LodHysteresis,
                GenerateBothTracks = GenerateBothTracks,
                CleanupIntermediateFiles = CleanupIntermediateFiles,
                ExcludeTextures = ExcludeTextures,
                GenerateManifest = GenerateManifest,
                GenerateQAReport = GenerateQAReport
            };
        }

        /// <summary>
        /// Создает из ModelConversionSettings
        /// </summary>
        public static ModelConversionSettingsData FromModelConversionSettings(ModelConversionSettings settings) {
            return new ModelConversionSettingsData {
                SourceType = settings.SourceType,
                GenerateLods = settings.GenerateLods,
                LodChain = settings.LodChain.Select(l => LodSettingsData.FromLodSettings(l)).ToList(),
                CompressionMode = settings.CompressionMode,
                Quantization = QuantizationSettingsData.FromQuantizationSettings(settings.Quantization ?? QuantizationSettings.CreateDefault()),
                AdvancedSettings = GltfPackSettingsData.FromGltfPackSettings(settings.AdvancedSettings ?? GltfPackSettings.CreateDefault()),
                LodHysteresis = settings.LodHysteresis,
                GenerateBothTracks = settings.GenerateBothTracks,
                CleanupIntermediateFiles = settings.CleanupIntermediateFiles,
                ExcludeTextures = settings.ExcludeTextures,
                GenerateManifest = settings.GenerateManifest,
                GenerateQAReport = settings.GenerateQAReport
            };
        }
    }

    /// <summary>
    /// Настройки квантования (сериализуемый формат)
    /// </summary>
    public class QuantizationSettingsData {
        public int PositionBits { get; set; } = 14;
        public int TexCoordBits { get; set; } = 12;
        public int NormalBits { get; set; } = 8;
        public int ColorBits { get; set; } = 8;

        public QuantizationSettings ToQuantizationSettings() {
            return new QuantizationSettings {
                PositionBits = PositionBits,
                TexCoordBits = TexCoordBits,
                NormalBits = NormalBits,
                ColorBits = ColorBits
            };
        }

        public static QuantizationSettingsData FromQuantizationSettings(QuantizationSettings settings) {
            return new QuantizationSettingsData {
                PositionBits = settings.PositionBits,
                TexCoordBits = settings.TexCoordBits,
                NormalBits = settings.NormalBits,
                ColorBits = settings.ColorBits
            };
        }

        public static QuantizationSettingsData CreateDefault() {
            return FromQuantizationSettings(QuantizationSettings.CreateDefault());
        }
    }

    /// <summary>
    /// Настройки LOD уровня (сериализуемый формат)
    /// </summary>
    public class LodSettingsData {
        public LodLevel Level { get; set; }
        public float SimplificationRatio { get; set; }
        public bool AggressiveSimplification { get; set; }
        public float SwitchThreshold { get; set; }

        public LodSettings ToLodSettings() {
            return new LodSettings {
                Level = Level,
                SimplificationRatio = SimplificationRatio,
                AggressiveSimplification = AggressiveSimplification,
                SwitchThreshold = SwitchThreshold
            };
        }

        public static LodSettingsData FromLodSettings(LodSettings settings) {
            return new LodSettingsData {
                Level = settings.Level,
                SimplificationRatio = settings.SimplificationRatio,
                AggressiveSimplification = settings.AggressiveSimplification,
                SwitchThreshold = settings.SwitchThreshold
            };
        }

        public static List<LodSettingsData> CreateDefaultChain() {
            return LodSettings.CreateFullChain()
                .Select(l => FromLodSettings(l))
                .ToList();
        }
    }

    /// <summary>
    /// Расширенные настройки gltfpack (сериализуемый формат)
    /// </summary>
    public class GltfPackSettingsData {
        // Simplification
        public float? SimplificationError { get; set; }
        public bool PermissiveSimplification { get; set; }
        public bool LockBorderVertices { get; set; }

        // Vertex Position Format
        public VertexPositionFormat PositionFormat { get; set; } = VertexPositionFormat.Integer;

        // Vertex Attributes
        public bool FloatTexCoords { get; set; }
        public bool FloatNormals { get; set; }
        public bool InterleavedAttributes { get; set; }
        public bool KeepVertexAttributes { get; set; } = true;

        // Animation
        public int AnimationTranslationBits { get; set; } = 16;
        public int AnimationRotationBits { get; set; } = 12;
        public int AnimationScaleBits { get; set; } = 16;
        public int AnimationFrameRate { get; set; } = 30;
        public bool KeepConstantAnimationTracks { get; set; }

        // Scene
        public bool KeepNamedNodes { get; set; }
        public bool KeepNamedMaterials { get; set; }
        public bool KeepExtras { get; set; }
        public bool MergeMeshInstances { get; set; }
        public bool UseGpuInstancing { get; set; }

        // Misc
        public bool CompressedWithFallback { get; set; }
        public bool DisableQuantization { get; set; }
        public bool FlipUVs { get; set; }

        public GltfPackSettings ToGltfPackSettings() {
            return new GltfPackSettings {
                SimplificationError = SimplificationError,
                PermissiveSimplification = PermissiveSimplification,
                LockBorderVertices = LockBorderVertices,
                PositionFormat = PositionFormat,
                FloatTexCoords = FloatTexCoords,
                FloatNormals = FloatNormals,
                InterleavedAttributes = InterleavedAttributes,
                KeepVertexAttributes = KeepVertexAttributes,
                AnimationTranslationBits = AnimationTranslationBits,
                AnimationRotationBits = AnimationRotationBits,
                AnimationScaleBits = AnimationScaleBits,
                AnimationFrameRate = AnimationFrameRate,
                KeepConstantAnimationTracks = KeepConstantAnimationTracks,
                KeepNamedNodes = KeepNamedNodes,
                KeepNamedMaterials = KeepNamedMaterials,
                KeepExtras = KeepExtras,
                MergeMeshInstances = MergeMeshInstances,
                UseGpuInstancing = UseGpuInstancing,
                CompressedWithFallback = CompressedWithFallback,
                DisableQuantization = DisableQuantization,
                FlipUVs = FlipUVs
            };
        }

        public static GltfPackSettingsData FromGltfPackSettings(GltfPackSettings settings) {
            return new GltfPackSettingsData {
                SimplificationError = settings.SimplificationError,
                PermissiveSimplification = settings.PermissiveSimplification,
                LockBorderVertices = settings.LockBorderVertices,
                PositionFormat = settings.PositionFormat,
                FloatTexCoords = settings.FloatTexCoords,
                FloatNormals = settings.FloatNormals,
                InterleavedAttributes = settings.InterleavedAttributes,
                KeepVertexAttributes = settings.KeepVertexAttributes,
                AnimationTranslationBits = settings.AnimationTranslationBits,
                AnimationRotationBits = settings.AnimationRotationBits,
                AnimationScaleBits = settings.AnimationScaleBits,
                AnimationFrameRate = settings.AnimationFrameRate,
                KeepConstantAnimationTracks = settings.KeepConstantAnimationTracks,
                KeepNamedNodes = settings.KeepNamedNodes,
                KeepNamedMaterials = settings.KeepNamedMaterials,
                KeepExtras = settings.KeepExtras,
                MergeMeshInstances = settings.MergeMeshInstances,
                UseGpuInstancing = settings.UseGpuInstancing,
                CompressedWithFallback = settings.CompressedWithFallback,
                DisableQuantization = settings.DisableQuantization,
                FlipUVs = settings.FlipUVs
            };
        }

        public static GltfPackSettingsData CreateDefault() {
            return FromGltfPackSettings(GltfPackSettings.CreateDefault());
        }
    }

    /// <summary>
    /// Глобальные настройки для конвертации моделей
    /// </summary>
    public class GlobalModelConversionSettings {
        /// <summary>
        /// Путь к FBX2glTF.exe
        /// </summary>
        public string FBX2glTFExecutablePath { get; set; } = "FBX2glTF-windows-x64.exe";

        /// <summary>
        /// Путь к gltfpack.exe
        /// </summary>
        public string GltfPackExecutablePath { get; set; } = "gltfpack.exe";

        /// <summary>
        /// Директория вывода по умолчанию
        /// </summary>
        public string DefaultOutputDirectory { get; set; } = "output_models";

        /// <summary>
        /// Настройки для отдельных моделей
        /// </summary>
        public List<ModelConversionSettingsData> ModelSettings { get; set; } = new();
    }
}
