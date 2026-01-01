namespace AssetProcessor.ModelConversion.Core {
    /// <summary>
    /// Уровень детализации (Level of Detail) для модели
    /// </summary>
    public enum LodLevel {
        /// <summary>
        /// LOD0 - максимальная детализация (100%)
        /// </summary>
        LOD0 = 0,

        /// <summary>
        /// LOD1 - высокая детализация (60%)
        /// </summary>
        LOD1 = 1,

        /// <summary>
        /// LOD2 - средняя детализация (40%)
        /// </summary>
        LOD2 = 2,

        /// <summary>
        /// LOD3 - низкая детализация (20%)
        /// </summary>
        LOD3 = 3
    }

    /// <summary>
    /// Настройки для уровня LOD
    /// </summary>
    public class LodSettings {
        /// <summary>
        /// Уровень LOD
        /// </summary>
        public LodLevel Level { get; set; }

        /// <summary>
        /// Коэффициент упрощения геометрии (0.0-1.0)
        /// 1.0 = без упрощения, 0.12 = 12% от оригинального количества треугольников
        /// </summary>
        public float SimplificationRatio { get; set; }

        /// <summary>
        /// Агрессивное упрощение (флаг -sa для gltfpack)
        /// Позволяет более агрессивное упрощение с возможной потерей топологии
        /// </summary>
        public bool AggressiveSimplification { get; set; }

        /// <summary>
        /// Порог переключения LOD (screen coverage или расстояние)
        /// Доля экрана (0.0-1.0) при которой происходит переключение на этот LOD
        /// </summary>
        public float SwitchThreshold { get; set; }

        /// <summary>
        /// Создает стандартные настройки для указанного уровня LOD
        /// </summary>
        public static LodSettings CreateDefault(LodLevel level) {
            return level switch {
                LodLevel.LOD0 => new LodSettings {
                    Level = LodLevel.LOD0,
                    SimplificationRatio = 1.0f,
                    AggressiveSimplification = false,
                    SwitchThreshold = 0.25f
                },
                LodLevel.LOD1 => new LodSettings {
                    Level = LodLevel.LOD1,
                    SimplificationRatio = 0.6f,
                    AggressiveSimplification = false, // Отключено для сохранения UV seams
                    SwitchThreshold = 0.10f
                },
                LodLevel.LOD2 => new LodSettings {
                    Level = LodLevel.LOD2,
                    SimplificationRatio = 0.4f,
                    AggressiveSimplification = false, // Отключено - UV seams важны для текстур
                    SwitchThreshold = 0.04f
                },
                LodLevel.LOD3 => new LodSettings {
                    Level = LodLevel.LOD3,
                    SimplificationRatio = 0.2f,
                    AggressiveSimplification = false, // Отключено - UV seams важны для текстур
                    SwitchThreshold = 0.02f
                },
                _ => throw new ArgumentOutOfRangeException(nameof(level))
            };
        }

        /// <summary>
        /// Создает полный набор LOD настроек (LOD0-LOD3)
        /// </summary>
        public static List<LodSettings> CreateFullChain() {
            return new List<LodSettings> {
                CreateDefault(LodLevel.LOD0),
                CreateDefault(LodLevel.LOD1),
                CreateDefault(LodLevel.LOD2),
                CreateDefault(LodLevel.LOD3)
            };
        }
    }
}
