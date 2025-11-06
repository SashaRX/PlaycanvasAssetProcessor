namespace AssetProcessor.TextureConversion.Core {
    /// <summary>
    /// Настройки сжатия Basis Universal
    /// </summary>
    public class CompressionSettings {
        /// <summary>
        /// Формат сжатия (ETC1S или UASTC)
        /// </summary>
        public CompressionFormat CompressionFormat { get; set; } = CompressionFormat.ETC1S;

        /// <summary>
        /// Формат выходного файла
        /// </summary>
        public OutputFormat OutputFormat { get; set; } = OutputFormat.KTX2;

        /// <summary>
        /// Уровень компрессии для ETC1S (0-5, по умолчанию 1)
        /// 0 = fastest, 5 = slowest but best compression
        /// </summary>
        public int CompressionLevel { get; set; } = 1;

        /// <summary>
        /// Уровень качества для ETC1S (1-255, по умолчанию 128)
        /// Выше = лучше качество, больше размер файла
        /// </summary>
        public int QualityLevel { get; set; } = 128;

        /// <summary>
        /// Уровень качества для UASTC (0-4, по умолчанию 2)
        /// 0 = fastest, 4 = slowest but best quality
        /// </summary>
        public int UASTCQuality { get; set; } = 2;

        /// <summary>
        /// Применять RDO (Rate-Distortion Optimization) для UASTC
        /// </summary>
        public bool UseUASTCRDO { get; set; } = true;

        /// <summary>
        /// Уровень RDO lambda для UASTC (0.001-10.0, по умолчанию 1.0)
        /// Выше = больше сжатие, ниже качество
        /// Рекомендуется: [0.25-10] для обычных текстур, [0.25-0.75] для normal maps
        /// </summary>
        public float UASTCRDOQuality { get; set; } = 1.0f;

        /// <summary>
        /// Использовать ли RDO для ETC1S
        /// </summary>
        public bool UseETC1SRDO { get; set; } = true;

        /// <summary>
        /// Уровень RDO lambda для ETC1S (0.0-10.0, по умолчанию 1.0)
        /// Выше = больше сжатие, ниже качество
        /// </summary>
        public float ETC1SRDOLambda { get; set; } = 1.0f;

        /// <summary>
        /// Масштаб мипмапов (1.0 = без изменений)
        /// </summary>
        public float MipScale { get; set; } = 1.0f;

        /// <summary>
        /// Минимальный уровень мипмапа для включения (0 = все уровни)
        /// </summary>
        public int MipSmallestDimension { get; set; } = 1;

        /// <summary>
        /// Генерировать ли мипмапы (если они не были сгенерированы ранее)
        /// </summary>
        public bool GenerateMipmaps { get; set; } = true;

        /// <summary>
        /// Использовать ручную генерацию мипмапов (через MipGenerator) вместо автоматической (toktx --genmipmap)
        /// Когда false, toktx сам генерирует мипмапы, что необходимо для работы --normal_mode и --normalize
        /// </summary>
        public bool UseCustomMipmaps { get; set; } = false;

        /// <summary>
        /// Использовать многопоточное сжатие
        /// </summary>
        public bool UseMultithreading { get; set; } = true;

        /// <summary>
        /// Количество потоков (0 = автоопределение)
        /// </summary>
        public int ThreadCount { get; set; } = 0;

        /// <summary>
        /// Включить перцептивное сравнение при сжатии
        /// </summary>
        public bool PerceptualMode { get; set; } = true;

        /// <summary>
        /// Сжать альфа-канал отдельно (для ETC1S) - Separate RG to Color/Alpha
        /// </summary>
        public bool SeparateAlpha { get; set; } = false;

        /// <summary>
        /// Принудительно добавлять альфа-канал (--target_type RGBA)
        /// </summary>
        public bool ForceAlphaChannel { get; set; } = false;

        /// <summary>
        /// Удалять альфа-канал (--target_type RGB)
        /// </summary>
        public bool RemoveAlphaChannel { get; set; } = false;

        /// <summary>
        /// Клампить края мипмапов (-mip_clamp)
        /// </summary>
        public bool ClampMipmaps { get; set; } = false;

        /// <summary>
        /// Цветовое пространство (--assign_oetf <linear|srgb>)
        /// </summary>
        public ColorSpace ColorSpace { get; set; } = ColorSpace.Auto;

        /// <summary>
        /// [DEPRECATED] Трактовать как линейное пространство (--assign_oetf linear)
        /// Используйте ColorSpace = ColorSpace.Linear вместо этого
        /// </summary>
        [System.Obsolete("Use ColorSpace property instead")]
        public bool TreatAsLinear {
            get => ColorSpace == ColorSpace.Linear;
            set => ColorSpace = value ? ColorSpace.Linear : ColorSpace.Auto;
        }

        /// <summary>
        /// [DEPRECATED] Трактовать как sRGB пространство (--assign_oetf srgb)
        /// Используйте ColorSpace = ColorSpace.SRGB вместо этого
        /// </summary>
        [System.Obsolete("Use ColorSpace property instead")]
        public bool TreatAsSRGB {
            get => ColorSpace == ColorSpace.SRGB;
            set => ColorSpace = value ? ColorSpace.SRGB : ColorSpace.Auto;
        }

        /// <summary>
        /// Фильтр для автоматической генерации мипмапов (toktx --filter)
        /// Используется только когда GenerateMipmaps = true и toktx сам генерирует мипы
        /// </summary>
        public ToktxFilterType ToktxMipFilter { get; set; } = ToktxFilterType.Kaiser;

        /// <summary>
        /// Режим сэмплирования на границах изображения (toktx --wmode)
        /// Clamp (по умолчанию) или Wrap
        /// </summary>
        public WrapMode WrapMode { get; set; } = WrapMode.Clamp;

        /// <summary>
        /// Использовать линейный фильтр для генерации мипов (-mip_linear)
        /// </summary>
        public bool UseLinearMipFiltering { get; set; } = false;

        /// <summary>
        /// Использовать SSE4.1 инструкции для ускорения
        /// </summary>
        public bool UseSSE41 { get; set; } = true;

        /// <summary>
        /// Тип KTX2 supercompression (только для KTX2)
        /// </summary>
        public KTX2SupercompressionType KTX2Supercompression { get; set; } = KTX2SupercompressionType.Zstandard;

        /// <summary>
        /// Уровень Zstandard сжатия для KTX2 (1-22, по умолчанию 3)
        /// Выше = лучше сжатие, медленнее. --zcmp flag
        /// </summary>
        public int KTX2ZstdLevel { get; set; } = 3;

        /// <summary>
        /// Конвертировать в XY(RGB/A) Normal Map (--normal_mode)
        /// </summary>
        public bool ConvertToNormalMap { get; set; } = false;

        /// <summary>
        /// Нормализовать векторы нормалей (--normalize)
        /// </summary>
        public bool NormalizeVectors { get; set; } = false;

        /// <summary>
        /// Оставить RGB структуру без преобразования (--input_swizzle rgb1)
        /// </summary>
        public bool KeepRGBLayout { get; set; } = false;

        /// <summary>
        /// Удалять временные мипмапы после конвертации
        /// </summary>
        public bool RemoveTemporaryMipmaps { get; set; } = true;

        /// <summary>
        /// Настройки анализа гистограммы для оптимизации сжатия
        /// </summary>
        public HistogramSettings? HistogramAnalysis { get; set; } = null;

        /// <summary>
        /// Записывать параметры анализа гистограммы в KTX2 метаданные
        /// </summary>
        public bool WriteHistogramParams { get; set; } = true;

        /// <summary>
        /// Создает настройки по умолчанию для ETC1S
        /// </summary>
        public static CompressionSettings CreateETC1SDefault() {
            return new CompressionSettings {
                CompressionFormat = CompressionFormat.ETC1S,
                OutputFormat = OutputFormat.KTX2,
                CompressionLevel = 1,
                QualityLevel = 128,
                GenerateMipmaps = true,
                UseMultithreading = true,
                PerceptualMode = true,
                KTX2Supercompression = KTX2SupercompressionType.Zstandard,
                KTX2ZstdLevel = 3,
                UseETC1SRDO = true,
                ColorSpace = ColorSpace.Auto
            };
        }

        /// <summary>
        /// Создает настройки по умолчанию для UASTC
        /// </summary>
        public static CompressionSettings CreateUASTCDefault() {
            return new CompressionSettings {
                CompressionFormat = CompressionFormat.UASTC,
                OutputFormat = OutputFormat.KTX2,
                UASTCQuality = 2,
                UseUASTCRDO = true,
                UASTCRDOQuality = 1.0f,
                GenerateMipmaps = true,
                UseMultithreading = true,
                KTX2Supercompression = KTX2SupercompressionType.Zstandard,
                KTX2ZstdLevel = 3,
                UseETC1SRDO = true,
                ColorSpace = ColorSpace.Auto
            };
        }

        /// <summary>
        /// Создает настройки для максимального качества
        /// </summary>
        public static CompressionSettings CreateHighQuality() {
            return new CompressionSettings {
                CompressionFormat = CompressionFormat.UASTC,
                OutputFormat = OutputFormat.KTX2,
                UASTCQuality = 4,
                UseUASTCRDO = true,
                UASTCRDOQuality = 0.5f,
                GenerateMipmaps = true,
                UseMultithreading = true,
                PerceptualMode = true,
                KTX2Supercompression = KTX2SupercompressionType.Zstandard,
                UseETC1SRDO = true,
                ColorSpace = ColorSpace.Auto
            };
        }

        /// <summary>
        /// Создает настройки для минимального размера
        /// </summary>
        public static CompressionSettings CreateMinSize() {
            return new CompressionSettings {
                CompressionFormat = CompressionFormat.ETC1S,
                OutputFormat = OutputFormat.KTX2,
                QualityLevel = 64,
                GenerateMipmaps = true,
                UseMultithreading = true,
                PerceptualMode = false,
                KTX2Supercompression = KTX2SupercompressionType.Zstandard,
                UseETC1SRDO = true,
                ColorSpace = ColorSpace.Auto
            };
        }
    }
}
