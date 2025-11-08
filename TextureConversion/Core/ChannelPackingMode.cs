namespace AssetProcessor.TextureConversion.Core {
    /// <summary>
    /// Режимы упаковки каналов для glTF/PlayCanvas ORM текстур
    /// </summary>
    public enum ChannelPackingMode {
        /// <summary>
        /// Без упаковки каналов (обычный режим)
        /// </summary>
        None,

        /// <summary>
        /// OG - 2 канала (для non-metallic материалов)
        /// RGB = Ambient Occlusion (AO)
        /// A = Gloss
        /// </summary>
        OG,

        /// <summary>
        /// OGM - 3 канала
        /// R = Ambient Occlusion (AO)
        /// G = Gloss
        /// B = Metallic
        /// </summary>
        OGM,

        /// <summary>
        /// OGMH - 4 канала (полная упаковка)
        /// R = Ambient Occlusion (AO)
        /// G = Gloss
        /// B = Metallic
        /// A = Height/Mask
        /// </summary>
        OGMH
    }

    /// <summary>
    /// Типы каналов для упаковки
    /// </summary>
    public enum ChannelType {
        /// <summary>
        /// Ambient Occlusion (затенение окружающей среды)
        /// </summary>
        AmbientOcclusion,

        /// <summary>
        /// Gloss (глянец, инвертированная roughness)
        /// </summary>
        Gloss,

        /// <summary>
        /// Metallic (металличность)
        /// </summary>
        Metallic,

        /// <summary>
        /// Height/Displacement (высота/смещение)
        /// </summary>
        Height
    }

    /// <summary>
    /// Режим обработки AO мипмапов
    /// </summary>
    public enum AOProcessingMode {
        /// <summary>
        /// Без специальной обработки (стандартная фильтрация)
        /// </summary>
        None,

        /// <summary>
        /// Lerp между средним и минимальным значением: lerp(mean, min, bias)
        /// </summary>
        BiasedDarkening,

        /// <summary>
        /// Percentile-based подход (например, 10-й перцентиль)
        /// </summary>
        Percentile
    }
}
