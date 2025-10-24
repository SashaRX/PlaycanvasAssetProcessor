namespace AssetProcessor.TextureConversion.Core {
    /// <summary>
    /// Типы фильтров для ресэмплинга при генерации мипмапов
    /// </summary>
    public enum FilterType {
        /// <summary>
        /// Box filter (быстрый, низкое качество)
        /// </summary>
        Box,

        /// <summary>
        /// Bilinear filter
        /// </summary>
        Bilinear,

        /// <summary>
        /// Bicubic filter (высокое качество)
        /// </summary>
        Bicubic,

        /// <summary>
        /// Lanczos filter (очень высокое качество, медленнее)
        /// </summary>
        Lanczos3,

        /// <summary>
        /// Mitchell filter (баланс между качеством и скоростью)
        /// </summary>
        Mitchell,

        /// <summary>
        /// Kaiser filter
        /// </summary>
        Kaiser
    }
}
