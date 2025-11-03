namespace AssetProcessor.TextureConversion.Core {
    /// <summary>
    /// Режимы расчёта дисперсии для Toksvig
    /// </summary>
    public enum ToksvigCalculationMode {
        /// <summary>
        /// Классический режим: 3x3 окно без нормализации, GaussianBlur, k^1.5
        /// </summary>
        Classic,

        /// <summary>
        /// Упрощённый режим: нормализация нормалей, Box 2x2, линейный CompositePower, порог дисперсии
        /// </summary>
        Simplified
    }

    /// <summary>
    /// Настройки для Toksvig mipmap generation
    /// Используется для уменьшения specular aliasing ("искр") в PBR материалах
    /// путём коррекции gloss/roughness карт на основе дисперсии normal map
    /// </summary>
    public class ToksvigSettings {
        /// <summary>
        /// Включить Toksvig mipmap generation
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Composite Power (k) - вес влияния дисперсии нормалей на roughness
        /// Диапазон: 0.5 - 8.0
        /// По умолчанию: 1.0
        /// </summary>
        public float CompositePower { get; set; } = 1.0f;

        /// <summary>
        /// Минимальный уровень мипмапа для применения Toksvig
        /// По умолчанию: 0 (применяем начиная с базового уровня)
        /// </summary>
        public int MinToksvigMipLevel { get; set; } = 0;

        /// <summary>
        /// Применять ли сглаживание дисперсии (3x3 blur)
        /// По умолчанию: true
        /// </summary>
        public bool SmoothVariance { get; set; } = true;

        /// <summary>
        /// Путь к соответствующей normal map (если известен)
        /// null = автоматический поиск
        /// </summary>
        public string? NormalMapPath { get; set; }

        /// <summary>
        /// Режим расчёта дисперсии
        /// По умолчанию: Classic
        /// </summary>
        public ToksvigCalculationMode CalculationMode { get; set; } = ToksvigCalculationMode.Classic;

        /// <summary>
        /// Порог дисперсии (dead zone) - дисперсия ниже этого значения обнуляется
        /// Применяется только в Simplified режиме
        /// По умолчанию: 0.002
        /// </summary>
        public float VarianceThreshold { get; set; } = 0.002f;

        /// <summary>
        /// Создаёт настройки Toksvig по умолчанию
        /// </summary>
        public static ToksvigSettings CreateDefault() {
            return new ToksvigSettings {
                Enabled = false,
                CompositePower = 1.0f,
                MinToksvigMipLevel = 0,
                SmoothVariance = true,
                NormalMapPath = null,
                CalculationMode = ToksvigCalculationMode.Classic,
                VarianceThreshold = 0.002f
            };
        }

        /// <summary>
        /// Клонирует настройки
        /// </summary>
        public ToksvigSettings Clone() {
            return new ToksvigSettings {
                Enabled = Enabled,
                CompositePower = CompositePower,
                MinToksvigMipLevel = MinToksvigMipLevel,
                SmoothVariance = SmoothVariance,
                NormalMapPath = NormalMapPath,
                CalculationMode = CalculationMode,
                VarianceThreshold = VarianceThreshold
            };
        }

        /// <summary>
        /// Валидирует настройки
        /// </summary>
        public bool Validate(out string? error) {
            if (CompositePower < 0.5f || CompositePower > 8.0f) {
                error = "CompositePower должен быть в диапазоне 0.5-8.0";
                return false;
            }

            if (MinToksvigMipLevel < 0) {
                error = "MinToksvigMipLevel не может быть отрицательным";
                return false;
            }

            if (VarianceThreshold < 0f || VarianceThreshold > 1.0f) {
                error = "VarianceThreshold должен быть в диапазоне 0.0-1.0";
                return false;
            }

            error = null;
            return true;
        }
    }
}
