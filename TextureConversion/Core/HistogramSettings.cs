namespace AssetProcessor.TextureConversion.Core {
    /// <summary>
    /// Упрощённые настройки анализа гистограммы
    /// Текстура всегда нормализуется перед сжатием (preprocessing), scale/offset записываются в KVD
    /// </summary>
    public class HistogramSettings {
        /// <summary>
        /// Режим анализа гистограммы
        /// Off = отключено, PercentileWithKnee = включён soft-knee (устанавливается автоматически из Quality)
        /// </summary>
        public HistogramMode Mode { get; set; } = HistogramMode.Off;

        /// <summary>
        /// Режим качества preprocessing (HighQuality или Fast)
        /// Автоматически настраивает перцентили, knee, и другие параметры
        /// </summary>
        public HistogramQuality Quality { get; set; } = HistogramQuality.HighQuality;

        /// <summary>
        /// Режим анализа каналов (по умолчанию - усреднённая яркость)
        /// </summary>
        public HistogramChannelMode ChannelMode { get; set; } = HistogramChannelMode.AverageLuminance;

        /// <summary>
        /// Нижний перцентиль (автоматически устанавливается из Quality, можно переопределить)
        /// </summary>
        public float PercentileLow { get; set; } = 0.5f;

        /// <summary>
        /// Верхний перцентиль (автоматически устанавливается из Quality, можно переопределить)
        /// </summary>
        public float PercentileHigh { get; set; } = 99.5f;

        /// <summary>
        /// Ширина мягкого колена (автоматически устанавливается из Quality, можно переопределить)
        /// </summary>
        public float KneeWidth { get; set; } = 0.02f;

        /// <summary>
        /// Минимальный порог применения нормализации (предотвращает усиление шума)
        /// </summary>
        public float MinRangeThreshold { get; set; } = 0.01f;

        /// <summary>
        /// Порог доли хвостов гистограммы для предупреждений
        /// </summary>
        public float TailThreshold { get; set; } = 0.005f;

        /// <summary>
        /// Создаёт настройки по умолчанию (анализ отключён)
        /// </summary>
        public static HistogramSettings CreateDefault() {
            return new HistogramSettings {
                Mode = HistogramMode.Off
            };
        }

        /// <summary>
        /// Создаёт настройки высокого качества (рекомендуется)
        /// PercentileWithKnee (0.5%, 99.5%), knee=2%, soft-knee сглаживание
        /// </summary>
        public static HistogramSettings CreateHighQuality() {
            return new HistogramSettings {
                Mode = HistogramMode.PercentileWithKnee,
                Quality = HistogramQuality.HighQuality,
                PercentileLow = 0.5f,
                PercentileHigh = 99.5f,
                KneeWidth = 0.02f,
                ChannelMode = HistogramChannelMode.AverageLuminance
            };
        }

        /// <summary>
        /// Создаёт настройки быстрого режима (грубая обработка)
        /// Percentile (1%, 99%), жёсткое клампирование
        /// </summary>
        public static HistogramSettings CreateFast() {
            return new HistogramSettings {
                Mode = HistogramMode.Percentile,
                Quality = HistogramQuality.Fast,
                PercentileLow = 1.0f,
                PercentileHigh = 99.0f,
                KneeWidth = 0.0f,
                ChannelMode = HistogramChannelMode.AverageLuminance
            };
        }

        /// <summary>
        /// Применяет пресет качества к текущим настройкам
        /// </summary>
        public void ApplyQualityPreset(HistogramQuality quality) {
            Quality = quality;

            if (quality == HistogramQuality.HighQuality) {
                Mode = HistogramMode.PercentileWithKnee;
                PercentileLow = 0.5f;
                PercentileHigh = 99.5f;
                KneeWidth = 0.02f;
            } else if (quality == HistogramQuality.Fast) {
                Mode = HistogramMode.Percentile;
                PercentileLow = 1.0f;
                PercentileHigh = 99.0f;
                KneeWidth = 0.0f;
            }
        }
    }
}
