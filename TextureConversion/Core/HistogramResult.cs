namespace AssetProcessor.TextureConversion.Core {
    /// <summary>
    /// Результат анализа гистограммы
    /// </summary>
    public class HistogramResult {
        /// <summary>
        /// Успешно ли выполнен анализ
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Режим анализа, который был применён
        /// </summary>
        public HistogramMode Mode { get; set; }

        /// <summary>
        /// Режим анализа каналов
        /// </summary>
        public HistogramChannelMode ChannelMode { get; set; }

        /// <summary>
        /// Scale (масштаб) для нормализации
        /// Для поканального режима - массив из 3 или 4 значений
        /// </summary>
        public float[] Scale { get; set; } = new[] { 1.0f };

        /// <summary>
        /// Offset (смещение) для нормализации
        /// Для поканального режима - массив из 3 или 4 значений
        /// </summary>
        public float[] Offset { get; set; } = new[] { 0.0f };

        /// <summary>
        /// Нижняя граница диапазона (после анализа перцентилей)
        /// Для AverageLuminance - одно значение, для PerChannel - среднее по каналам
        /// </summary>
        public float RangeLow { get; set; }

        /// <summary>
        /// Верхняя граница диапазона (после анализа перцентилей)
        /// Для AverageLuminance - одно значение, для PerChannel - среднее по каналам
        /// </summary>
        public float RangeHigh { get; set; }

        /// <summary>
        /// Нижние границы для каждого канала (используется при PerChannel режиме)
        /// </summary>
        public float[] RangeLowPerChannel { get; set; } = new[] { 0.0f, 0.0f, 0.0f };

        /// <summary>
        /// Верхние границы для каждого канала (используется при PerChannel режиме)
        /// </summary>
        public float[] RangeHighPerChannel { get; set; } = new[] { 1.0f, 1.0f, 1.0f };

        /// <summary>
        /// Доля пикселей в хвостах распределения (за пределами перцентилей)
        /// </summary>
        public float TailFraction { get; set; }

        /// <summary>
        /// Индикатор, что используется поканальный режим (RGB per-channel)
        /// </summary>
        public bool IsPerChannel => ChannelMode == HistogramChannelMode.PerChannel;

        /// <summary>
        /// Было ли применено мягкое колено (soft knee)
        /// </summary>
        public bool KneeApplied { get; set; }

        /// <summary>
        /// Общее количество пикселей, участвовавших в анализе
        /// </summary>
        public long TotalPixels { get; set; }

        /// <summary>
        /// Сообщение об ошибке (если есть)
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// Предупреждения (например, о высокой доле выбросов)
        /// </summary>
        public List<string> Warnings { get; set; } = new();

        /// <summary>
        /// Возвращает результат "без нормализации" (scale=1, offset=0)
        /// </summary>
        public static HistogramResult CreateIdentity() {
            return new HistogramResult {
                Success = true,
                Mode = HistogramMode.Off,
                Scale = [1.0f],
                Offset = [0.0f],
                RangeLow = 0.0f,
                RangeHigh = 1.0f
            };
        }
    }
}
