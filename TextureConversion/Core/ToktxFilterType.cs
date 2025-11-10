namespace AssetProcessor.TextureConversion.Core {
    /// <summary>
    /// Типы фильтров для ktx create --mipmap-filter (генерация мипмапов через ktx create --generate-mipmap)
    /// Эти фильтры используются когда ktx create сам генерирует мипмапы
    /// </summary>
    public enum ToktxFilterType {
        /// <summary>
        /// Box filter (быстрый, низкое качество)
        /// </summary>
        Box,

        /// <summary>
        /// Tent filter
        /// </summary>
        Tent,

        /// <summary>
        /// Bell filter
        /// </summary>
        Bell,

        /// <summary>
        /// B-spline filter
        /// </summary>
        BSpline,

        /// <summary>
        /// Mitchell filter (баланс между качеством и скоростью)
        /// </summary>
        Mitchell,

        /// <summary>
        /// Lanczos3 filter (высокое качество)
        /// </summary>
        Lanczos3,

        /// <summary>
        /// Lanczos4 filter
        /// </summary>
        Lanczos4,

        /// <summary>
        /// Lanczos6 filter
        /// </summary>
        Lanczos6,

        /// <summary>
        /// Lanczos12 filter (максимальное качество, очень медленно)
        /// </summary>
        Lanczos12,

        /// <summary>
        /// Blackman filter
        /// </summary>
        Blackman,

        /// <summary>
        /// Kaiser filter
        /// </summary>
        Kaiser,

        /// <summary>
        /// Gaussian filter
        /// </summary>
        Gaussian,

        /// <summary>
        /// Catmull-Rom filter
        /// </summary>
        CatmullRom,

        /// <summary>
        /// Quadratic interpolation
        /// </summary>
        QuadraticInterp,

        /// <summary>
        /// Quadratic approximation
        /// </summary>
        QuadraticApprox,

        /// <summary>
        /// Quadratic mix
        /// </summary>
        QuadraticMix
    }
}
