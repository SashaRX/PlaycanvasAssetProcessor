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
        /// Список постфиксов для автоматического выбора пресета
        /// Например: "_albedo", "_diffuse", "_color"
        /// </summary>
        public List<string> Suffixes { get; set; } = new List<string>();

        /// <summary>
        /// Формат сжатия (ETC1S или UASTC)
        /// </summary>
        public CompressionFormat CompressionFormat { get; set; } = CompressionFormat.ETC1S;

        /// <summary>
        /// Формат выходного файла
        /// </summary>
        public OutputFormat OutputFormat { get; set; } = OutputFormat.KTX2;

        /// <summary>
        /// Уровень компрессии для ETC1S (0-5)
        /// </summary>
        public int CompressionLevel { get; set; } = 1;

        /// <summary>
        /// Уровень качества для ETC1S (1-255)
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
        /// Уровень Zstandard сжатия для KTX2 (1-22, по умолчанию 3)
        /// </summary>
        public int KTX2ZstdLevel { get; set; } = 3;

        /// <summary>
        /// Разделить RG на Color/Alpha (для normal maps)
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
        /// Цветовое пространство (новая система)
        /// </summary>
        public ColorSpace ColorSpace { get; set; } = ColorSpace.Auto;

        /// <summary>
        /// [DEPRECATED] Трактовать как линейное цветовое пространство
        /// Используйте ColorSpace вместо этого
        /// </summary>
        public bool TreatAsLinear { get; set; } = false;

        /// <summary>
        /// [DEPRECATED] Трактовать как sRGB цветовое пространство
        /// Используйте ColorSpace вместо этого
        /// </summary>
        public bool TreatAsSRGB { get; set; } = false;

        /// <summary>
        /// Клампить края мипмапов
        /// </summary>
        public bool ClampMipmaps { get; set; } = false;

        /// <summary>
        /// Фильтр toktx для автогенерации мипмапов
        /// </summary>
        public ToktxFilterType ToktxMipFilter { get; set; } = ToktxFilterType.Kaiser;

        /// <summary>
        /// Режим сэмплирования на границах (Clamp/Wrap)
        /// </summary>
        public WrapMode WrapMode { get; set; } = WrapMode.Clamp;

        /// <summary>
        /// Использовать линейный фильтр для мипов
        /// </summary>
        public bool UseLinearMipFiltering { get; set; } = false;

        /// <summary>
        /// Конвертировать в XY(RGB/A) Normal Map
        /// </summary>
        public bool ConvertToNormalMap { get; set; } = false;

        /// <summary>
        /// Нормализовать векторы
        /// </summary>
        public bool NormalizeVectors { get; set; } = false;

        /// <summary>
        /// Оставить RGB структуру без преобразования
        /// </summary>
        public bool KeepRGBLayout { get; set; } = false;

        /// <summary>
        /// Удалять временные мипмапы
        /// </summary>
        public bool RemoveTemporaryMipmaps { get; set; } = true;

        /// <summary>
        /// Настройки Toksvig для Gloss текстур
        /// </summary>
        public ToksvigSettings ToksvigSettings { get; set; } = new();

        /// <summary>
        /// Настройки анализа гистограммы
        /// </summary>
        public HistogramSettings? HistogramSettings { get; set; } = null;

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
                CompressionLevel = 1,
                QualityLevel = 128,
                UseETC1SRDO = true,
                GenerateMipmaps = true,
                MipFilter = FilterType.Kaiser,
                ApplyGammaCorrection = true,
                UseMultithreading = true,
                PerceptualMode = true,
                KTX2Supercompression = KTX2SupercompressionType.Zstandard,
                KTX2ZstdLevel = 3,
                ColorSpace = ColorSpace.Auto,
                ToktxMipFilter = ToktxFilterType.Kaiser,
                WrapMode = WrapMode.Clamp
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
                KTX2Supercompression = KTX2SupercompressionType.Zstandard,
                ColorSpace = ColorSpace.Auto,
                ToktxMipFilter = ToktxFilterType.Kaiser,
                WrapMode = WrapMode.Clamp
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
                KTX2Supercompression = KTX2SupercompressionType.Zstandard,
                ColorSpace = ColorSpace.Auto,
                ToktxMipFilter = ToktxFilterType.Kaiser,
                WrapMode = WrapMode.Clamp
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
                KTX2Supercompression = KTX2SupercompressionType.Zstandard,
                ColorSpace = ColorSpace.Auto,
                ToktxMipFilter = ToktxFilterType.Box,
                WrapMode = WrapMode.Clamp
            };
        }

        /// <summary>
        /// Создает пресет для Normal Maps
        /// </summary>
        public static TextureConversionPreset CreateNormalMap() {
            return new TextureConversionPreset {
                Name = "Normal (Linear)",
                Description = "Optimized for normal maps (UASTC, linear, normalize)",
                IsBuiltIn = true,
                Suffixes = new List<string> { "_normal", "_norm", "_nrm", "_n", "_normals" },
                CompressionFormat = CompressionFormat.UASTC,
                OutputFormat = OutputFormat.KTX2,
                UASTCQuality = 3,
                UseUASTCRDO = true,
                UASTCRDOQuality = 1.0f,
                GenerateMipmaps = true,
                MipFilter = FilterType.Kaiser,
                ApplyGammaCorrection = false,
                ColorSpace = ColorSpace.Linear,
                NormalizeNormals = true,
                NormalizeVectors = false, // ОТКЛЮЧЕНО: --normalize конфликтует с --mipmap
                ConvertToNormalMap = false, // ОТКЛЮЧЕНО: --normal_mode может конфликтовать с pre-generated mipmaps
                UseMultithreading = true,
                PerceptualMode = false,
                KTX2Supercompression = KTX2SupercompressionType.Zstandard,
                KTX2ZstdLevel = 3,
                ToktxMipFilter = ToktxFilterType.Kaiser,
                WrapMode = WrapMode.Clamp
            };
        }

        /// <summary>
        /// Создает пресет для Albedo/Diffuse текстур
        /// </summary>
        public static TextureConversionPreset CreateAlbedo() {
            return new TextureConversionPreset {
                Name = "Albedo/Color (sRGB)",
                Description = "Optimized for albedo/diffuse maps with gamma correction",
                IsBuiltIn = true,
                Suffixes = new List<string> { "_albedo", "_diffuse", "_color", "_basecolor", "_diff", "_base" },
                CompressionFormat = CompressionFormat.ETC1S,
                OutputFormat = OutputFormat.KTX2,
                CompressionLevel = 1,
                QualityLevel = 128,
                UseETC1SRDO = true,
                GenerateMipmaps = true,
                MipFilter = FilterType.Kaiser,
                ApplyGammaCorrection = true,
                ColorSpace = ColorSpace.SRGB,
                UseMultithreading = true,
                PerceptualMode = true,
                KTX2Supercompression = KTX2SupercompressionType.Zstandard,
                KTX2ZstdLevel = 3,
                ToktxMipFilter = ToktxFilterType.Kaiser,
                WrapMode = WrapMode.Clamp
            };
        }

        /// <summary>
        /// Создает пресет для Roughness/Metallic/AO текстур
        /// </summary>
        public static TextureConversionPreset CreateRoughness() {
            return new TextureConversionPreset {
                Name = "Roughness/Metallic/AO",
                Description = "Optimized for roughness/metallic/AO maps (Linear)",
                IsBuiltIn = true,
                Suffixes = new List<string> { "_roughness", "_rough", "_metallic", "_metal", "_ao", "_ambient", "_occlusion" },
                CompressionFormat = CompressionFormat.ETC1S,
                OutputFormat = OutputFormat.KTX2,
                CompressionLevel = 1,
                QualityLevel = 128,
                UseETC1SRDO = true,
                GenerateMipmaps = true,
                MipFilter = FilterType.Kaiser,
                ApplyGammaCorrection = false,
                ColorSpace = ColorSpace.Linear,
                UseMultithreading = true,
                PerceptualMode = false,
                KTX2Supercompression = KTX2SupercompressionType.Zstandard,
                KTX2ZstdLevel = 3,
                ToktxMipFilter = ToktxFilterType.Kaiser,
                WrapMode = WrapMode.Clamp
            };
        }

        /// <summary>
        /// Создает пресет для Gloss текстур с Toksvig
        /// </summary>
        public static TextureConversionPreset CreateGloss() {
            return new TextureConversionPreset {
                Name = "Gloss (Linear + Toksvig)",
                Description = "Optimized for gloss maps with Toksvig anti-aliasing",
                IsBuiltIn = true,
                Suffixes = new List<string> { "_gloss", "_glossiness", "_smoothness" },
                CompressionFormat = CompressionFormat.ETC1S,
                OutputFormat = OutputFormat.KTX2,
                CompressionLevel = 1,
                QualityLevel = 128,
                UseETC1SRDO = true,
                GenerateMipmaps = true,
                MipFilter = FilterType.Kaiser,
                ApplyGammaCorrection = false,
                ColorSpace = ColorSpace.Linear,
                UseMultithreading = true,
                PerceptualMode = false,
                KTX2Supercompression = KTX2SupercompressionType.Zstandard,
                KTX2ZstdLevel = 3,
                ToktxMipFilter = ToktxFilterType.Kaiser,
                WrapMode = WrapMode.Clamp,
                ToksvigSettings = new ToksvigSettings {
                    Enabled = true,
                    CompositePower = 1.0f,
                    MinToksvigMipLevel = 0,
                    SmoothVariance = true,
                    UseEnergyPreserving = true,
                    NormalMapPath = null // Автоопределение
                }
            };
        }

        /// <summary>
        /// Создает пресет для Height текстур
        /// </summary>
        public static TextureConversionPreset CreateHeight() {
            return new TextureConversionPreset {
                Name = "Height (Linear with Clamp)",
                Description = "Optimized for height/displacement maps",
                IsBuiltIn = true,
                Suffixes = new List<string> { "_height", "_displacement", "_disp", "_bump" },
                CompressionFormat = CompressionFormat.ETC1S,
                OutputFormat = OutputFormat.KTX2,
                CompressionLevel = 1,
                QualityLevel = 128,
                UseETC1SRDO = true,
                GenerateMipmaps = true,
                MipFilter = FilterType.Kaiser,
                ApplyGammaCorrection = false,
                ColorSpace = ColorSpace.Linear,
                ClampMipmaps = true,
                UseMultithreading = true,
                PerceptualMode = false,
                KTX2Supercompression = KTX2SupercompressionType.Zstandard,
                KTX2ZstdLevel = 3,
                ToktxMipFilter = ToktxFilterType.Kaiser,
                WrapMode = WrapMode.Clamp
            };
        }

        /// <summary>
        /// Создает пресет для Emissive текстур
        /// </summary>
        public static TextureConversionPreset CreateEmissive() {
            return new TextureConversionPreset {
                Name = "Emissive",
                Description = "Optimized for emissive/glow maps",
                IsBuiltIn = true,
                Suffixes = new List<string> { "_emissive", "_emission", "_glow", "_light" },
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
        /// Возвращает список всех встроенных пресетов
        /// </summary>
        public static List<TextureConversionPreset> GetBuiltInPresets() {
            return new List<TextureConversionPreset> {
                CreateAlbedo(),
                CreateNormalMap(),
                CreateRoughness(),
                CreateGloss(),
                CreateHeight(),
                CreateEmissive(),
                CreateDefaultETC1S(),
                CreateDefaultUASTC(),
                CreateHighQuality(),
                CreateMinimumSize()
            };
        }

        /// <summary>
        /// Конвертирует пресет в CompressionSettings
        /// </summary>
        public CompressionSettings ToCompressionSettings() {
            // Определяем ColorSpace: новая система имеет приоритет, затем старые флаги для обратной совместимости
            var colorSpace = ColorSpace;
            if (colorSpace == ColorSpace.Auto) {
                if (TreatAsLinear) colorSpace = ColorSpace.Linear;
                else if (TreatAsSRGB) colorSpace = ColorSpace.SRGB;
            }

            return new CompressionSettings {
                CompressionFormat = this.CompressionFormat,
                OutputFormat = this.OutputFormat,
                CompressionLevel = this.CompressionLevel,
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
                ColorSpace = colorSpace,
                ClampMipmaps = this.ClampMipmaps,
                ToktxMipFilter = this.ToktxMipFilter,
                WrapMode = this.WrapMode,
                UseLinearMipFiltering = this.UseLinearMipFiltering,
                ConvertToNormalMap = this.ConvertToNormalMap,
                NormalizeVectors = this.NormalizeVectors,
                KeepRGBLayout = this.KeepRGBLayout,
                RemoveTemporaryMipmaps = this.RemoveTemporaryMipmaps,
                HistogramAnalysis = this.HistogramSettings
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

        /// <summary>
        /// Проверяет, соответствует ли имя файла постфиксам этого пресета (без учета регистра)
        /// </summary>
        public bool MatchesFileName(string fileName) {
            if (Suffixes == null || Suffixes.Count == 0) {
                return false;
            }

            // Убираем расширение и приводим к нижнему регистру для сравнения без учета регистра
            var nameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();

            foreach (var suffix in Suffixes) {
                // Сравниваем без учета регистра
                if (nameWithoutExtension.EndsWith(suffix.ToLowerInvariant())) {
                    return true;
                }
            }

            return false;
        }
    }
}
