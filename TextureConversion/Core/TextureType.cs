namespace AssetProcessor.TextureConversion.Core {
    /// <summary>
    /// Типы текстур для определения профиля генерации мипмапов
    /// </summary>
    public enum TextureType {
        /// <summary>
        /// Albedo/Diffuse карта (базовый цвет)
        /// </summary>
        Albedo,

        /// <summary>
        /// Normal map (карта нормалей)
        /// </summary>
        Normal,

        /// <summary>
        /// Roughness карта (шероховатость)
        /// </summary>
        Roughness,

        /// <summary>
        /// Metallic карта (металличность)
        /// </summary>
        Metallic,

        /// <summary>
        /// Ambient Occlusion (окклюзия)
        /// </summary>
        AmbientOcclusion,

        /// <summary>
        /// Emissive карта (излучение)
        /// </summary>
        Emissive,

        /// <summary>
        /// Height/Displacement карта
        /// </summary>
        Height,

        /// <summary>
        /// Gloss карта (глянец)
        /// </summary>
        Gloss,

        /// <summary>
        /// Общий тип (generic)
        /// </summary>
        Generic
    }
}
