namespace AssetProcessor.ModelConversion.Core {
    /// <summary>
    /// Режим сжатия модели
    /// </summary>
    public enum CompressionMode {
        /// <summary>
        /// Без сжатия (только упрощение геометрии)
        /// </summary>
        None,

        /// <summary>
        /// Квантование (KHR_mesh_quantization)
        /// Уменьшает размер без потери совместимости
        /// Флаги: -kn -km
        /// </summary>
        Quantization,

        /// <summary>
        /// EXT_meshopt_compression
        /// Максимальное сжатие для web runtime
        /// Флаг: -c
        /// </summary>
        MeshOpt,

        /// <summary>
        /// EXT_meshopt_compression с дополнительным сжатием
        /// Флаг: -cc
        /// </summary>
        MeshOptAggressive
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
        /// Диапазон: 1-16, по умолчанию 12
        /// </summary>
        public int TexCoordBits { get; set; } = 12;

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
                TexCoordBits = 12,
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
                TexCoordBits = 14,
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
                TexCoordBits = 10,
                NormalBits = 8,
                ColorBits = 8
            };
        }
    }
}
