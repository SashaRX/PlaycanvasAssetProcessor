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
        /// Конвертирует в ModelConversionSettings
        /// </summary>
        public ModelConversionSettings ToModelConversionSettings() {
            return new ModelConversionSettings {
                GenerateLods = GenerateLods,
                LodChain = LodChain.Select(l => l.ToLodSettings()).ToList(),
                CompressionMode = CompressionMode,
                Quantization = Quantization.ToQuantizationSettings(),
                LodHysteresis = LodHysteresis,
                GenerateBothTracks = GenerateBothTracks,
                CleanupIntermediateFiles = CleanupIntermediateFiles,
                GenerateManifest = GenerateManifest,
                GenerateQAReport = GenerateQAReport
            };
        }

        /// <summary>
        /// Создает из ModelConversionSettings
        /// </summary>
        public static ModelConversionSettingsData FromModelConversionSettings(ModelConversionSettings settings) {
            return new ModelConversionSettingsData {
                GenerateLods = settings.GenerateLods,
                LodChain = settings.LodChain.Select(l => LodSettingsData.FromLodSettings(l)).ToList(),
                CompressionMode = settings.CompressionMode,
                Quantization = QuantizationSettingsData.FromQuantizationSettings(settings.Quantization ?? QuantizationSettings.CreateDefault()),
                LodHysteresis = settings.LodHysteresis,
                GenerateBothTracks = settings.GenerateBothTracks,
                CleanupIntermediateFiles = settings.CleanupIntermediateFiles,
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
