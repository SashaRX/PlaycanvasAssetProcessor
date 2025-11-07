namespace AssetProcessor.TextureConversion.Core {
    /// <summary>
    /// Режимы анализа гистограммы для оптимизации сжатия
    /// </summary>
    public enum HistogramMode {
        /// <summary>
        /// Анализ отключён (scale=1, offset=0)
        /// </summary>
        Off = 0,

        /// <summary>
        /// Использование перцентилей для устойчивости к выбросам
        /// Вычисляет lo = P(pLow), hi = P(pHigh) и применяет жёсткое клампирование
        /// </summary>
        Percentile = 1,

        /// <summary>
        /// Перцентили с мягким коленом (soft knee)
        /// Применяет smoothstep-сглаживание для выбросов вместо жёсткого клампирования
        /// </summary>
        PercentileWithKnee = 2,

        /// <summary>
        /// Локальный анализ выбросов (зарезервировано для будущего расширения)
        /// Детектирует и подавляет локальные аномалии на основе пространственного анализа
        /// </summary>
        LocalOutlierPatch = 3
    }

    /// <summary>
    /// Режим анализа каналов
    /// </summary>
    public enum HistogramChannelMode {
        /// <summary>
        /// Усреднённая яркость (R+G+B)/3 - один общий scale/offset
        /// </summary>
        AverageLuminance = 0,

        /// <summary>
        /// Поканальный анализ RGB - отдельные scale/offset для каждого канала
        /// </summary>
        PerChannel = 1,

        /// <summary>
        /// Анализ только RGB, игнорируя альфа-канал
        /// </summary>
        RGBOnly = 2,

        /// <summary>
        /// Поканальный анализ RGBA (включая альфа)
        /// </summary>
        PerChannelRGBA = 3
    }

    /// <summary>
    /// Режим качества preprocessing гистограммы
    /// Текстура всегда нормализуется перед сжатием, scale/offset записываются в KVD для восстановления на GPU
    /// </summary>
    public enum HistogramQuality {
        /// <summary>
        /// Высокое качество (рекомендуется)
        /// PercentileWithKnee (0.5%, 99.5%), knee=2%, soft-knee сглаживание
        /// Минимальные потери, лучшее распределение гистограммы
        /// </summary>
        HighQuality = 0,

        /// <summary>
        /// Быстрый режим (грубая обработка)
        /// Percentile (1%, 99%), жёсткое клампирование без soft-knee
        /// Быстрее, но возможны артефакты на выбросах
        /// </summary>
        Fast = 1
    }

    /// <summary>
    /// Формат квантования метаданных (внутренний, не показывается в UI)
    /// Всегда используется Half16 для упрощения API
    /// </summary>
    public enum HistogramQuantization {
        /// <summary>
        /// Half float (16-bit IEEE 754) - используется по умолчанию
        /// Диапазон: ±65504, точность: ~3 десятичных знака
        /// Размер: 4 байта (scale + offset)
        /// </summary>
        Half16 = 0,

        /// <summary>
        /// Packed uint32 (2×16-bit unsigned normalized)
        /// scale и offset упакованы в один uint32
        /// Диапазон: [0.0, 1.0], точность: 1/65535
        /// Размер: 4 байта (более компактно для позитивных значений)
        /// </summary>
        PackedUInt32 = 1,

        /// <summary>
        /// Float32 (32-bit IEEE 754) - максимальная точность
        /// Диапазон: ±3.4e38, точность: ~7 десятичных знаков
        /// Размер: 8 байт (scale + offset)
        /// </summary>
        Float32 = 2
    }
}
