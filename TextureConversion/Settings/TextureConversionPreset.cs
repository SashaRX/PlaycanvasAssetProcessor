using AssetProcessor.TextureConversion.Core;

namespace AssetProcessor.TextureConversion.Settings {
    /// <summary>
    /// Пресет для конвертации текстур
    /// Содержит полный набор настроек компрессии и генерации мипмапов
    /// </summary>
    public class TextureConversionPreset {
        /// <summary>
        /// Уникальное имя пресета
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Описание пресета
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Встроенный пресет (нельзя удалить)
        /// </summary>
        public bool IsBuiltIn { get; set; } = false;

        /// <summary>
        /// Формат сжатия (ETC1S или UASTC)
        /// </summary>
        public CompressionFormat CompressionFormat { get; set; } = CompressionFormat.ETC1S;

        /// <summary>
        /// Формат выходного файла
        /// </summary>
        public OutputFormat OutputFormat { get; set; } = OutputFormat.KTX2;

        /// <summary>
        /// Уровень качества для ETC1S (0-255)
        /// </summary>
        public int QualityLevel { get; set; } = 128;

        /// <summary>
        /// Уровень качества для UASTC (0-4)
        /// </summary>
        public int UASTCQuality { get; set; } = 2;

        /// <summary>
        /// Применять RDO для UASTC
        /// </summary>
        public bool UseUASTCRDO { get; set; } = true;

        /// <summary>
        /// Уровень RDO для UASTC (0.1-10.0)
        /// </summary>
        public float UASTCRDOQuality { get; set; } = 1.0f;

        /// <summary>
        /// Использовать RDO для ETC1S
        /// </summary>
        public bool UseETC1SRDO { get; set; } = true;

        /// <summary>
        /// Уровень RDO lambda для ETC1S (0.0-10.0, по умолчанию 1.0)
        /// </summary>
        public float ETC1SRDOLambda { get; set; } = 1.0f;

        /// <summary>
        /// Генерировать мипмапы
        /// </summary>
        public bool GenerateMipmaps { get; set; } = true;

        /// <summary>
        /// Фильтр для генерации мипмапов
        /// </summary>
        public FilterType MipFilter { get; set; } = FilterType.Kaiser;

        /// <summary>
        /// Применять гамма-коррекцию
        /// </summary>
        public bool ApplyGammaCorrection { get; set; } = true;

        /// <summary>
        /// Нормализовать нормали (для normal maps)
        /// </summary>
        public bool NormalizeNormals { get; set; } = false;

        /// <summary>
        /// Использовать многопоточное сжатие
        /// </summary>
        public bool UseMultithreading { get; set; } = true;

        /// <summary>
        /// Перцептивный режим
        /// </summary>
        public bool PerceptualMode { get; set; } = true;

        /// <summary>
        /// KTX2 Supercompression
        /// </summary>
        public KTX2SupercompressionType KTX2Supercompression { get; set; } = KTX2SupercompressionType.Zstandard;

        /// <summary>
        /// Уровень Zstandard сжатия для KTX2 (1-22, по умолчанию 6)
        /// </summary>
        public int KTX2ZstdLevel { get; set; } = 6;

        /// <summary>
        /// Разделить RG на Color/Alpha
        /// </summary>
        public bool SeparateAlpha { get; set; } = false;

        /// <summary>
        /// Принудительно добавить альфа-канал
        /// </summary>
        public bool ForceAlphaChannel { get; set; } = false;

        /// <summary>
        /// Удалить альфа-канал
        /// </summary>
        public bool RemoveAlphaChannel { get; set; } = false;

        /// <summary>
        /// Обрабатывать как линейное цветовое пространство
        /// </summary>
        public bool ForceLinearColorSpace { get; set; } = false;

        /// <summary>
        /// Клампить края мипмапов
        /// </summary>
        public bool ClampMipmaps { get; set; } = false;

        /// <summary>
        /// Использовать линейный фильтр для мипов в basisu
        /// </summary>
        public bool UseLinearMipFiltering { get; set; } = false;

        /// <summary>
        /// Создает встроенный пресет "Default ETC1S"
        /// </summary>
        public static TextureConversionPreset CreateDefaultETC1S() {
            return new TextureConversionPreset {
                Name = "Default ETC1S",
                Description = "Balanced quality and size (ETC1S)",
                IsBuiltIn = true,
                CompressionFormat = CompressionFormat.ETC1S,
                OutputFormat = OutputFormat.KTX2,
                QualityLevel = 128,
                UseETC1SRDO = true,
                GenerateMipmaps = true,
                MipFilter = FilterType.Kaiser,
                ApplyGammaCorrection = true,
                UseMultithreading = true,
                PerceptualMode = true,
                KTX2Supercompression = KTX2SupercompressionType.Zstandard
            };
        }

        /// <summary>
        /// Создает встроенный пресет "Default UASTC"
        /// </summary>
        public static TextureConversionPreset CreateDefaultUASTC() {
            return new TextureConversionPreset {
                Name = "Default UASTC",
                Description = "High quality (UASTC)",
                IsBuiltIn = true,
                CompressionFormat = CompressionFormat.UASTC,
                OutputFormat = OutputFormat.KTX2,
                UASTCQuality = 2,
                UseUASTCRDO = true,
                UASTCRDOQuality = 1.0f,
                GenerateMipmaps = true,
                MipFilter = FilterType.Kaiser,
                ApplyGammaCorrection = true,
                UseMultithreading = true,
                PerceptualMode = true,
                KTX2Supercompression = KTX2SupercompressionType.Zstandard
            };
        }

        /// <summary>
        /// Создает встроенный пресет "High Quality"
        /// </summary>
        public static TextureConversionPreset CreateHighQuality() {
            return new TextureConversionPreset {
                Name = "High Quality",
                Description = "Maximum quality (UASTC Level 4)",
                IsBuiltIn = true,
                CompressionFormat = CompressionFormat.UASTC,
                OutputFormat = OutputFormat.KTX2,
                UASTCQuality = 4,
                UseUASTCRDO = true,
                UASTCRDOQuality = 0.5f,
                GenerateMipmaps = true,
                MipFilter = FilterType.Kaiser,
                ApplyGammaCorrection = true,
                UseMultithreading = true,
                PerceptualMode = true,
                KTX2Supercompression = KTX2SupercompressionType.Zstandard
            };
        }

        /// <summary>
        /// Создает встроенный пресет "Minimum Size"
        /// </summary>
        public static TextureConversionPreset CreateMinimumSize() {
            return new TextureConversionPreset {
                Name = "Minimum Size",
                Description = "Smallest file size (ETC1S Q64)",
                IsBuiltIn = true,
                CompressionFormat = CompressionFormat.ETC1S,
                OutputFormat = OutputFormat.KTX2,
                QualityLevel = 64,
                UseETC1SRDO = true,
                GenerateMipmaps = true,
                MipFilter = FilterType.Box,
                ApplyGammaCorrection = false,
                UseMultithreading = true,
                PerceptualMode = false,
                KTX2Supercompression = KTX2SupercompressionType.Zstandard
            };
        }

        /// <summary>
        /// Создает пресет для Normal Maps
        /// </summary>
        public static TextureConversionPreset CreateNormalMap() {
            return new TextureConversionPreset {
                Name = "Normal Map",
                Description = "Optimized for normal maps (UASTC, no gamma)",
                IsBuiltIn = true,
                CompressionFormat = CompressionFormat.UASTC,
                OutputFormat = OutputFormat.KTX2,
                UASTCQuality = 3,
                UseUASTCRDO = true,
                UASTCRDOQuality = 1.0f,
                GenerateMipmaps = true,
                MipFilter = FilterType.Kaiser,
                ApplyGammaCorrection = false,
                NormalizeNormals = true,
                UseMultithreading = true,
                PerceptualMode = false,
                KTX2Supercompression = KTX2SupercompressionType.Zstandard
            };
        }

        /// <summary>
        /// Возвращает список всех встроенных пресетов
        /// </summary>
        public static List<TextureConversionPreset> GetBuiltInPresets() {
            return new List<TextureConversionPreset> {
                CreateDefaultETC1S(),
                CreateDefaultUASTC(),
                CreateHighQuality(),
                CreateMinimumSize(),
                CreateNormalMap()
            };
        }

        /// <summary>
        /// Конвертирует пресет в CompressionSettings
        /// </summary>
        public CompressionSettings ToCompressionSettings() {
            return new CompressionSettings {
                CompressionFormat = this.CompressionFormat,
                OutputFormat = this.OutputFormat,
                QualityLevel = this.QualityLevel,
                UASTCQuality = this.UASTCQuality,
                UseUASTCRDO = this.UseUASTCRDO,
                UASTCRDOQuality = this.UASTCRDOQuality,
                UseETC1SRDO = this.UseETC1SRDO,
                ETC1SRDOLambda = this.ETC1SRDOLambda,
                GenerateMipmaps = this.GenerateMipmaps,
                UseMultithreading = this.UseMultithreading,
                PerceptualMode = this.PerceptualMode,
                KTX2Supercompression = this.KTX2Supercompression,
                KTX2ZstdLevel = this.KTX2ZstdLevel,
                SeparateAlpha = this.SeparateAlpha,
                ForceAlphaChannel = this.ForceAlphaChannel,
                RemoveAlphaChannel = this.RemoveAlphaChannel,
                ForceLinearColorSpace = this.ForceLinearColorSpace,
                ClampMipmaps = this.ClampMipmaps,
                UseLinearMipFiltering = this.UseLinearMipFiltering
            };
        }

        /// <summary>
        /// Конвертирует пресет в MipGenerationProfile
        /// </summary>
        public MipGenerationProfile ToMipGenerationProfile(TextureType textureType) {
            return new MipGenerationProfile {
                TextureType = textureType,
                Filter = this.MipFilter,
                ApplyGammaCorrection = this.ApplyGammaCorrection,
                NormalizeNormals = this.NormalizeNormals,
                Gamma = 2.2f,
                IncludeLastLevel = true,
                MinMipSize = 1
            };
        }
    }
}
