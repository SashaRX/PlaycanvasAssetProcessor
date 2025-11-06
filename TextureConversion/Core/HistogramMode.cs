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
}
