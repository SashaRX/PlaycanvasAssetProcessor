namespace AssetProcessor.TextureConversion.Core {
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
        /// Диапазон: 0.5 - 2.0
        /// По умолчанию: 1.0
        /// </summary>
        public float CompositePower { get; set; } = 1.0f;

        /// <summary>
        /// Минимальный уровень мипмапа для применения Toksvig
        /// По умолчанию: 1 (не трогаем уровень 0)
        /// </summary>
        public int MinToksvigMipLevel { get; set; } = 1;

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
        /// Создаёт настройки Toksvig по умолчанию
        /// </summary>
        public static ToksvigSettings CreateDefault() {
            return new ToksvigSettings {
                Enabled = false,
                CompositePower = 1.0f,
                MinToksvigMipLevel = 1,
                SmoothVariance = true,
                NormalMapPath = null
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
                NormalMapPath = NormalMapPath
            };
        }

        /// <summary>
        /// Валидирует настройки
        /// </summary>
        public bool Validate(out string? error) {
            if (CompositePower < 0.5f || CompositePower > 2.0f) {
                error = "CompositePower должен быть в диапазоне 0.5-2.0";
                return false;
            }

            if (MinToksvigMipLevel < 0) {
                error = "MinToksvigMipLevel не может быть отрицательным";
                return false;
            }

            error = null;
            return true;
        }
    }
}
