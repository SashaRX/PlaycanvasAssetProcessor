namespace AssetProcessor.TextureConversion.Core {
    /// <summary>
    /// Режим сэмплирования пикселей на границах изображения (toktx --wmode)
    /// </summary>
    public enum WrapMode {
        /// <summary>
        /// Clamp - пиксели на границе повторяются (по умолчанию)
        /// </summary>
        Clamp,

        /// <summary>
        /// Wrap - изображение повторяется (tiling)
        /// </summary>
        Wrap
    }
}
