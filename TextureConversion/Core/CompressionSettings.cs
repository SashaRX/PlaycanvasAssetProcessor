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
        /// Уровень качества для ETC1S (0-255, по умолчанию 128)
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
        /// Уровень RDO для UASTC (0.1-10.0, по умолчанию 1.0)
        /// Выше = больше сжатие, ниже качество
        /// </summary>
        public float UASTCRDOQuality { get; set; } = 1.0f;

        /// <summary>
        /// Использовать ли RDO для ETC1S
        /// </summary>
        public bool UseETC1SRDO { get; set; } = false;

        /// <summary>
        /// Значение ETC1S RDO lambda (чем выше, тем меньше размер)
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
        /// Сжать альфа-канал отдельно (для ETC1S)
        /// </summary>
        public bool SeparateAlpha { get; set; } = false;

        /// <summary>
        /// Использовать SSE4.1 инструкции для ускорения
        /// </summary>
        public bool UseSSE41 { get; set; } = true;

        /// <summary>
        /// Использовать OpenCL для GPU ускорения
        /// </summary>
        public bool UseOpenCL { get; set; } = false;

        /// <summary>
        /// Тип KTX2 supercompression (только для KTX2)
        /// </summary>
        public KTX2SupercompressionType KTX2Supercompression { get; set; } = KTX2SupercompressionType.Zstandard;

        /// <summary>
        /// Создает настройки по умолчанию для ETC1S
        /// </summary>
        public static CompressionSettings CreateETC1SDefault() {
            return new CompressionSettings {
                CompressionFormat = CompressionFormat.ETC1S,
                OutputFormat = OutputFormat.KTX2,
                QualityLevel = 128,
                GenerateMipmaps = true,
                UseMultithreading = true,
                PerceptualMode = true,
                KTX2Supercompression = KTX2SupercompressionType.Zstandard,
                UseETC1SRDO = false,
                ETC1SRDOLambda = 1.0f
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
                UseETC1SRDO = false,
                ETC1SRDOLambda = 1.0f
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
                UseETC1SRDO = false,
                ETC1SRDOLambda = 1.0f
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
                ETC1SRDOLambda = 50.0f
            };
        }
    }
}
