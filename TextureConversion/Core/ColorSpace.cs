namespace AssetProcessor.TextureConversion.Core {
    /// <summary>
    /// Цветовое пространство текстуры (OETF - Opto-Electronic Transfer Function)
    /// Используется для параметра --assign_oetf в toktx
    /// </summary>
    public enum ColorSpace {
        /// <summary>
        /// Автоопределение (по умолчанию)
        /// </summary>
        Auto,

        /// <summary>
        /// Линейное пространство (--assign_oetf linear)
        /// Для normal maps, roughness, metallic, AO
        /// </summary>
        Linear,

        /// <summary>
        /// sRGB пространство (--assign_oetf srgb)
        /// Для albedo, emissive
        /// </summary>
        SRGB
    }
}
