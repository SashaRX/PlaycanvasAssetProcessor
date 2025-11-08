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
        /// Режим анализа каналов (по умолчанию - поканальный для правильной обработки цветных текстур)
        /// </summary>
        public HistogramChannelMode ChannelMode { get; set; } = HistogramChannelMode.PerChannel;

        /// <summary>
        /// Нижний перцентиль (автоматически устанавливается из Quality, можно переопределить)
        /// </summary>
        public float PercentileLow { get; set; } = 5.0f;

        /// <summary>
        /// Верхний перцентиль (автоматически устанавливается из Quality, можно переопределить)
        /// </summary>
        public float PercentileHigh { get; set; } = 95.0f;

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
        /// CRITICAL: Using Percentile (hard clamping) instead of PercentileWithKnee
        /// because soft-knee is NON-LINEAR and cannot be inverted by GPU's linear formula.
        /// GPU can only apply: v_original = v_normalized * scale + offset
        /// Percentile (5%, 95%), hard clamping - wider range to preserve more data
        /// </summary>
        public static HistogramSettings CreateHighQuality() {
            return new HistogramSettings {
                Mode = HistogramMode.Percentile,  // CRITICAL: Must be linear for GPU inversion
                Quality = HistogramQuality.HighQuality,
                PercentileLow = 5.0f,   // Wider range to avoid excessive clipping
                PercentileHigh = 95.0f,
                KneeWidth = 0.0f,  // No knee for linear transformation
                ChannelMode = HistogramChannelMode.PerChannel  // Per-channel for correct color handling
            };
        }

        /// <summary>
        /// Создаёт настройки быстрого режима (грубая обработка)
        /// Percentile (10%, 90%), жёсткое клампирование - conservative range
        /// </summary>
        public static HistogramSettings CreateFast() {
            return new HistogramSettings {
                Mode = HistogramMode.Percentile,
                Quality = HistogramQuality.Fast,
                PercentileLow = 10.0f,  // Conservative range
                PercentileHigh = 90.0f,
                KneeWidth = 0.0f,
                ChannelMode = HistogramChannelMode.PerChannel  // Per-channel for correct color handling
            };
        }

        /// <summary>
        /// Применяет пресет качества к текущим настройкам
        /// CRITICAL: Both modes use Percentile (hard clamping) because soft-knee
        /// is non-linear and cannot be inverted by GPU
        /// </summary>
        public void ApplyQualityPreset(HistogramQuality quality) {
            Quality = quality;

            if (quality == HistogramQuality.HighQuality) {
                Mode = HistogramMode.Percentile;  // CRITICAL: Must be linear for GPU inversion
                PercentileLow = 5.0f;   // Wider range to preserve more data
                PercentileHigh = 95.0f;
                KneeWidth = 0.0f;  // No knee for linear transformation
            } else if (quality == HistogramQuality.Fast) {
                Mode = HistogramMode.Percentile;
                PercentileLow = 10.0f;  // Conservative range
                PercentileHigh = 90.0f;
                KneeWidth = 0.0f;
            }
        }
    }
}
