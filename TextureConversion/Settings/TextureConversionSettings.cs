using System.Text.Json.Serialization;
using AssetProcessor.TextureConversion.Core;

namespace AssetProcessor.TextureConversion.Settings {
    /// <summary>
    /// Настройки для конвертации одной текстуры
    /// </summary>
    public class TextureConversionSettings {
        /// <summary>
        /// Путь к текстуре
        /// </summary>
        public string TexturePath { get; set; } = string.Empty;

        /// <summary>
        /// Тип текстуры
        /// </summary>
        public TextureType TextureType { get; set; } = TextureType.Albedo;

        /// <summary>
        /// Профиль генерации мипмапов
        /// </summary>
        public MipProfileSettings MipProfile { get; set; } = new();

        /// <summary>
        /// Настройки сжатия
        /// </summary>
        public CompressionSettingsData Compression { get; set; } = new();

        /// <summary>
        /// Сохранять ли отдельные мипмапы
        /// </summary>
        public bool SaveSeparateMipmaps { get; set; } = false;

        /// <summary>
        /// Включена ли обработка этой текстуры
        /// </summary>
        public bool IsEnabled { get; set; } = true;
    }

    /// <summary>
    /// Упрощенные настройки профиля мипмапов для сериализации
    /// </summary>
    public class MipProfileSettings {
        public FilterType Filter { get; set; } = FilterType.Kaiser;
        public bool ApplyGammaCorrection { get; set; } = true;
        public float Gamma { get; set; } = 2.2f;
        public float BlurRadius { get; set; } = 0.0f;
        public bool IncludeLastLevel { get; set; } = true;
        public int MinMipSize { get; set; } = 1;
        public bool NormalizeNormals { get; set; } = false;

        /// <summary>
        /// Создает MipGenerationProfile из настроек
        /// </summary>
        public MipGenerationProfile ToMipGenerationProfile(TextureType textureType) {
            return new MipGenerationProfile {
                TextureType = textureType,
                Filter = Filter,
                ApplyGammaCorrection = ApplyGammaCorrection,
                Gamma = Gamma,
                BlurRadius = BlurRadius,
                IncludeLastLevel = IncludeLastLevel,
                MinMipSize = MinMipSize,
                NormalizeNormals = NormalizeNormals
            };
        }

        /// <summary>
        /// Создает настройки из MipGenerationProfile
        /// </summary>
        public static MipProfileSettings FromMipGenerationProfile(MipGenerationProfile profile) {
            return new MipProfileSettings {
                Filter = profile.Filter,
                ApplyGammaCorrection = profile.ApplyGammaCorrection,
                Gamma = profile.Gamma,
                BlurRadius = profile.BlurRadius,
                IncludeLastLevel = profile.IncludeLastLevel,
                MinMipSize = profile.MinMipSize,
                NormalizeNormals = profile.NormalizeNormals
            };
        }
    }

    /// <summary>
    /// Настройки сжатия для сериализации (per-texture)
    /// </summary>
    public class CompressionSettingsData {
        public CompressionFormat CompressionFormat { get; set; } = CompressionFormat.ETC1S;
        public OutputFormat OutputFormat { get; set; } = OutputFormat.KTX2;
        public int QualityLevel { get; set; } = 128;
        public int UASTCQuality { get; set; } = 2;
        public bool UseUASTCRDO { get; set; } = true;
        public float UASTCRDOQuality { get; set; } = 1.0f;
        public bool PerceptualMode { get; set; } = true;
        public KTX2SupercompressionType KTX2Supercompression { get; set; } = KTX2SupercompressionType.Zstandard;
        public int KTX2ZstdLevel { get; set; } = 18;
        public bool UseETC1SRDO { get; set; } = false;
        public float ETC1SRDOLambda { get; set; } = 1.0f;

        /// <summary>
        /// Создает CompressionSettings из настроек с применением глобальных настроек
        /// </summary>
        public CompressionSettings ToCompressionSettings(GlobalTextureConversionSettings globalSettings) {
            var zstdLevel = KTX2ZstdLevel <= 0 ? 18 : KTX2ZstdLevel;

            return new CompressionSettings {
                CompressionFormat = CompressionFormat,
                OutputFormat = OutputFormat,
                QualityLevel = QualityLevel,
                UASTCQuality = UASTCQuality,
                UseUASTCRDO = UseUASTCRDO,
                UASTCRDOQuality = UASTCRDOQuality,
                GenerateMipmaps = false, // Мы генерируем отдельно
                UseMultithreading = globalSettings.UseMultithreading,
                ThreadCount = globalSettings.ThreadCount,
                PerceptualMode = PerceptualMode,
                UseSSE41 = globalSettings.UseSSE41,
                UseOpenCL = globalSettings.UseOpenCL,
                KTX2Supercompression = KTX2Supercompression,
                KTX2ZstdLevel = zstdLevel,
                UseETC1SRDO = UseETC1SRDO,
                ETC1SRDOLambda = ETC1SRDOLambda
            };
        }

        /// <summary>
        /// Создает настройки из CompressionSettings (без глобальных настроек)
        /// </summary>
        public static CompressionSettingsData FromCompressionSettings(CompressionSettings settings) {
            return new CompressionSettingsData {
                CompressionFormat = settings.CompressionFormat,
                OutputFormat = settings.OutputFormat,
                QualityLevel = settings.QualityLevel,
                UASTCQuality = settings.UASTCQuality,
                UseUASTCRDO = settings.UseUASTCRDO,
                UASTCRDOQuality = settings.UASTCRDOQuality,
                PerceptualMode = settings.PerceptualMode,
                KTX2Supercompression = settings.KTX2Supercompression,
                KTX2ZstdLevel = settings.KTX2ZstdLevel,
                UseETC1SRDO = settings.UseETC1SRDO,
                ETC1SRDOLambda = settings.ETC1SRDOLambda
            };
        }
    }

    /// <summary>
    /// Глобальные настройки конвертации текстур
    /// </summary>
    public class GlobalTextureConversionSettings {
        /// <summary>
        /// Путь к basisu исполняемому файлу
        /// </summary>
        public string BasisUExecutablePath { get; set; } = "basisu";

        /// <summary>
        /// Выходная директория по умолчанию
        /// </summary>
        public string DefaultOutputDirectory { get; set; } = "output_textures";

        /// <summary>
        /// Настройки для отдельных текстур
        /// </summary>
        public List<TextureConversionSettings> TextureSettings { get; set; } = new();

        /// <summary>
        /// Пресет по умолчанию для новых текстур
        /// </summary>
        public string DefaultPreset { get; set; } = "Balanced";

        /// <summary>
        /// Максимальное количество параллельных задач
        /// </summary>
        public int MaxParallelTasks { get; set; } = 4;

        // Глобальные настройки производительности basisu
        /// <summary>
        /// Использовать SSE4.1 инструкции (глобально)
        /// </summary>
        public bool UseSSE41 { get; set; } = true;

        /// <summary>
        /// Использовать OpenCL для GPU ускорения (глобально)
        /// </summary>
        public bool UseOpenCL { get; set; } = false;

        /// <summary>
        /// Использовать многопоточность (глобально)
        /// </summary>
        public bool UseMultithreading { get; set; } = true;

        /// <summary>
        /// Количество потоков (0 = автоопределение)
        /// </summary>
        public int ThreadCount { get; set; } = 0;
    }
}
