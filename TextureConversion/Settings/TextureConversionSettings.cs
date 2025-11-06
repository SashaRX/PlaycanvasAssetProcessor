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
        public int CompressionLevel { get; set; } = 1;
        public int QualityLevel { get; set; } = 128;
        public int UASTCQuality { get; set; } = 2;
        public bool UseUASTCRDO { get; set; } = true;
        public float UASTCRDOQuality { get; set; } = 1.0f;
        public bool PerceptualMode { get; set; } = true;
        public KTX2SupercompressionType KTX2Supercompression { get; set; } = KTX2SupercompressionType.Zstandard;
        public int KTX2ZstdLevel { get; set; } = 3;
        public bool UseETC1SRDO { get; set; } = true;
        public bool SeparateAlpha { get; set; } = false;
        public bool ForceAlphaChannel { get; set; } = false;
        public bool RemoveAlphaChannel { get; set; } = false;
        public bool ClampMipmaps { get; set; } = false;

        // Новая система Color Space
        public ColorSpace ColorSpace { get; set; } = ColorSpace.Auto;

        // Старые поля для обратной совместимости (deprecated)
        public bool TreatAsLinear { get; set; } = false;
        public bool TreatAsSRGB { get; set; } = false;

        // Новые настройки toktx
        public ToktxFilterType ToktxMipFilter { get; set; } = ToktxFilterType.Kaiser;
        public WrapMode WrapMode { get; set; } = WrapMode.Clamp;

        public bool UseLinearMipFiltering { get; set; } = false;
        public bool GenerateMipmaps { get; set; } = true;
        public bool UseCustomMipmaps { get; set; } = false;
        public bool ConvertToNormalMap { get; set; } = false;
        public bool NormalizeVectors { get; set; } = false;
        public bool KeepRGBLayout { get; set; } = false;
        public bool RemoveTemporaryMipmaps { get; set; } = true;

        // Histogram Analysis
        public HistogramSettings? HistogramAnalysis { get; set; } = null;

        /// <summary>
        /// Создает CompressionSettings из настроек с применением глобальных настроек
        /// </summary>
        public CompressionSettings ToCompressionSettings(GlobalTextureConversionSettings globalSettings) {
            // Определяем ColorSpace: новая система имеет приоритет, затем старые флаги для обратной совместимости
            var colorSpace = ColorSpace;
            if (colorSpace == ColorSpace.Auto) {
                if (TreatAsLinear) colorSpace = ColorSpace.Linear;
                else if (TreatAsSRGB) colorSpace = ColorSpace.SRGB;
            }

            return new CompressionSettings {
                CompressionFormat = CompressionFormat,
                OutputFormat = OutputFormat,
                CompressionLevel = CompressionLevel,
                QualityLevel = QualityLevel,
                UASTCQuality = UASTCQuality,
                UseUASTCRDO = UseUASTCRDO,
                UASTCRDOQuality = UASTCRDOQuality,
                GenerateMipmaps = GenerateMipmaps,
                UseMultithreading = globalSettings.UseMultithreading,
                ThreadCount = globalSettings.ThreadCount,
                PerceptualMode = PerceptualMode,
                UseSSE41 = globalSettings.UseSSE41,
                KTX2Supercompression = KTX2Supercompression,
                KTX2ZstdLevel = KTX2ZstdLevel,
                UseETC1SRDO = UseETC1SRDO,
                SeparateAlpha = SeparateAlpha,
                ForceAlphaChannel = ForceAlphaChannel,
                RemoveAlphaChannel = RemoveAlphaChannel,
                ClampMipmaps = ClampMipmaps,
                ColorSpace = colorSpace,
                ToktxMipFilter = ToktxMipFilter,
                WrapMode = WrapMode,
                UseLinearMipFiltering = UseLinearMipFiltering,
                UseCustomMipmaps = UseCustomMipmaps,
                ConvertToNormalMap = ConvertToNormalMap,
                NormalizeVectors = NormalizeVectors,
                KeepRGBLayout = KeepRGBLayout,
                RemoveTemporaryMipmaps = RemoveTemporaryMipmaps,
                HistogramAnalysis = HistogramAnalysis
            };
        }

        /// <summary>
        /// Создает настройки из CompressionSettings (без глобальных настроек)
        /// </summary>
        public static CompressionSettingsData FromCompressionSettings(CompressionSettings settings) {
            return new CompressionSettingsData {
                CompressionFormat = settings.CompressionFormat,
                OutputFormat = settings.OutputFormat,
                CompressionLevel = settings.CompressionLevel,
                QualityLevel = settings.QualityLevel,
                UASTCQuality = settings.UASTCQuality,
                UseUASTCRDO = settings.UseUASTCRDO,
                UASTCRDOQuality = settings.UASTCRDOQuality,
                PerceptualMode = settings.PerceptualMode,
                KTX2Supercompression = settings.KTX2Supercompression,
                KTX2ZstdLevel = settings.KTX2ZstdLevel,
                UseETC1SRDO = settings.UseETC1SRDO,
                SeparateAlpha = settings.SeparateAlpha,
                ForceAlphaChannel = settings.ForceAlphaChannel,
                RemoveAlphaChannel = settings.RemoveAlphaChannel,
                ClampMipmaps = settings.ClampMipmaps,
                ColorSpace = settings.ColorSpace,
                ToktxMipFilter = settings.ToktxMipFilter,
                WrapMode = settings.WrapMode,
                UseLinearMipFiltering = settings.UseLinearMipFiltering,
                GenerateMipmaps = settings.GenerateMipmaps,
                UseCustomMipmaps = settings.UseCustomMipmaps,
                ConvertToNormalMap = settings.ConvertToNormalMap,
                NormalizeVectors = settings.NormalizeVectors,
                KeepRGBLayout = settings.KeepRGBLayout,
                RemoveTemporaryMipmaps = settings.RemoveTemporaryMipmaps,
                HistogramAnalysis = settings.HistogramAnalysis
            };
        }
    }

    /// <summary>
    /// Глобальные настройки конвертации текстур
    /// </summary>
    public class GlobalTextureConversionSettings {
        /// <summary>
        /// Путь к toktx исполняемому файлу (KTX-Software)
        /// </summary>
        public string ToktxExecutablePath { get; set; } = "toktx";

        /// <summary>
        /// Путь к ktx исполняемому файлу (KTX-Software) для извлечения мипмапов
        /// </summary>
        public string KtxExecutablePath { get; set; } = "ktx";

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

        // Глобальные настройки производительности basisu/toktx
        /// <summary>
        /// Использовать SSE4.1 инструкции (глобально)
        /// </summary>
        public bool UseSSE41 { get; set; } = true;

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
