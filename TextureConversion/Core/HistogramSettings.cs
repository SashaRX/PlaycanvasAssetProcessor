namespace AssetProcessor.TextureConversion.Core {
    /// <summary>
    /// Настройки анализа гистограммы
    /// </summary>
    public class HistogramSettings {
        /// <summary>
        /// Режим анализа гистограммы
        /// </summary>
        public HistogramMode Mode { get; set; } = HistogramMode.Off;

        /// <summary>
        /// Режим анализа каналов
        /// </summary>
        public HistogramChannelMode ChannelMode { get; set; } = HistogramChannelMode.AverageLuminance;

        /// <summary>
        /// Нижний перцентиль для устойчивого анализа (0.0-100.0, по умолчанию 0.5%)
        /// Используется для определения минимального значения диапазона с учётом выбросов
        /// </summary>
        public float PercentileLow { get; set; } = 0.5f;

        /// <summary>
        /// Верхний перцентиль для устойчивого анализа (0.0-100.0, по умолчанию 99.5%)
        /// Используется для определения максимального значения диапазона с учётом выбросов
        /// </summary>
        public float PercentileHigh { get; set; } = 99.5f;

        /// <summary>
        /// Ширина мягкого колена (knee) в долях диапазона (0.0-1.0, по умолчанию 0.02 = 2%)
        /// Применяется только в режиме PercentileWithKnee
        /// Определяет ширину зоны сглаживания для выбросов
        /// </summary>
        public float KneeWidth { get; set; } = 0.02f;

        /// <summary>
        /// Порог доли хвостов гистограммы для автоматического включения soft-knee (0.0-1.0, по умолчанию 0.005 = 0.5%)
        /// Если доля пикселей за пределами перцентилей превышает этот порог, включается предупреждение
        /// </summary>
        public float TailThreshold { get; set; } = 0.005f;

        /// <summary>
        /// Минимальный порог применения нормализации
        /// Если диапазон (hi - lo) меньше этого значения, нормализация не применяется
        /// Предотвращает усиление шума на почти константных текстурах
        /// </summary>
        public float MinRangeThreshold { get; set; } = 0.01f;

        /// <summary>
        /// Создаёт настройки по умолчанию (анализ отключён)
        /// </summary>
        public static HistogramSettings CreateDefault() {
            return new HistogramSettings {
                Mode = HistogramMode.Off
            };
        }

        /// <summary>
        /// Создаёт настройки с перцентилями (устойчивый анализ)
        /// </summary>
        public static HistogramSettings CreatePercentile(float pLow = 0.5f, float pHigh = 99.5f) {
            return new HistogramSettings {
                Mode = HistogramMode.Percentile,
                PercentileLow = pLow,
                PercentileHigh = pHigh
            };
        }

        /// <summary>
        /// Создаёт настройки с мягким коленом (рекомендуется для большинства случаев)
        /// </summary>
        public static HistogramSettings CreateWithKnee(float pLow = 0.5f, float pHigh = 99.5f, float knee = 0.02f) {
            return new HistogramSettings {
                Mode = HistogramMode.PercentileWithKnee,
                PercentileLow = pLow,
                PercentileHigh = pHigh,
                KneeWidth = knee
            };
        }
    }
}
