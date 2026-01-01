namespace AssetProcessor.ModelConversion.Core {
    /// <summary>
    /// Режим сжатия модели
    /// </summary>
    public enum CompressionMode {
        /// <summary>
        /// Без сжатия и квантования
        /// Максимальное качество, большой размер файла
        /// </summary>
        None,

        /// <summary>
        /// KHR_mesh_quantization
        /// Квантование вершин для уменьшения размера
        /// Совместимо со всеми редакторами и движками
        /// </summary>
        Quantization,

        /// <summary>
        /// EXT_meshopt_compression
        /// Максимальное сжатие для web runtime
        /// Требует meshoptimizer decoder в браузере
        /// Флаг: -c
        /// </summary>
        MeshOpt
    }

    /// <summary>
    /// Настройки квантования вершин
    /// </summary>
    public class QuantizationSettings {
        /// <summary>
        /// Биты для позиций (position) вершин
        /// Диапазон: 1-16, по умолчанию 14
        /// </summary>
        public int PositionBits { get; set; } = 14;

        /// <summary>
        /// Биты для текстурных координат (UV)
        /// Диапазон: 1-16, рекомендуется 16 для предотвращения ошибок денормализации
        /// ВАЖНО: gltfpack и Assimp денормализуют UNSIGNED_SHORT как 16-бит (делят на 65535)
        /// Поэтому используем 16 бит, чтобы избежать потери точности (12 бит → 16x ошибка!)
        /// </summary>
        public int TexCoordBits { get; set; } = 16;

        /// <summary>
        /// Биты для нормалей
        /// Диапазон: 1-16, по умолчанию 8
        /// </summary>
        public int NormalBits { get; set; } = 8;

        /// <summary>
        /// Биты для цветов вершин
        /// Диапазон: 1-16, по умолчанию 8
        /// </summary>
        public int ColorBits { get; set; } = 8;

        /// <summary>
        /// Создает настройки квантования по умолчанию
        /// Баланс между качеством и размером
        /// </summary>
        public static QuantizationSettings CreateDefault() {
            return new QuantizationSettings {
                PositionBits = 14,
                TexCoordBits = 16,  // 16 бит для корректной денормализации (избегаем 12-бит bug)
                NormalBits = 8,
                ColorBits = 8
            };
        }

        /// <summary>
        /// Создает настройки квантования для высокого качества
        /// </summary>
        public static QuantizationSettings CreateHighQuality() {
            return new QuantizationSettings {
                PositionBits = 16,
                TexCoordBits = 16,  // 16 бит для корректной денормализации
                NormalBits = 10,
                ColorBits = 10
            };
        }

        /// <summary>
        /// Создает настройки квантования для минимального размера
        /// </summary>
        public static QuantizationSettings CreateMinSize() {
            return new QuantizationSettings {
                PositionBits = 12,
                TexCoordBits = 16,  // 16 бит для корректной денормализации (даже для min size!)
                NormalBits = 8,
                ColorBits = 8
            };
        }
    }
}
