namespace AssetProcessor.TextureConversion.KVD {
    /// <summary>
    /// Типы TLV блоков для KTX2 Key-Value Data
    /// CRITICAL: Только HIST_SCALAR (0x01) и HIST_PER_CHANNEL_3 (0x03)
    /// RGBA (4 канала) НЕ поддерживается!
    /// </summary>
    public enum TLVType : byte {
        /// <summary>
        /// HIST_SCALAR: общий scale/offset для всех каналов
        /// Payload: half scale, half offset (4 байта)
        /// Используется для HistogramChannelMode.AverageLuminance
        /// </summary>
        HIST_SCALAR = 0x01,

        /// <summary>
        /// HIST_PER_CHANNEL_3: поканально для RGB (3 канала)
        /// Payload: half3 scale, half3 offset (12 байт)
        /// Используется для HistogramChannelMode.PerChannel
        /// </summary>
        HIST_PER_CHANNEL_3 = 0x03,

        /// <summary>
        /// HIST_PARAMS: параметры анализа гистограммы (опционально)
        /// Payload: half pLow, half pHigh, half knee (6 байт)
        /// flags: биты [3:0] — режим (0=Percentile, 1=PercentileWithKnee, 2=LocalOutlierPatch)
        /// </summary>
        HIST_PARAMS = 0x10,

        /// <summary>
        /// NORMAL_LAYOUT: схема хранения нормалей
        /// flags (младшие 3 бита): 0=NONE, 1=RG, 2=GA, 3=RGB, 4=AG
        /// Payload: пустой (length=0)
        /// </summary>
        NORMAL_LAYOUT = 0x20,

        /// <summary>
        /// CHANNEL_SWIZZLE: явная свиззль каналов
        /// Payload: u8[4] (каждый индекс: 0=R,1=G,2=B,3=A,4=0,5=1,6=~R,7=~G,8=~B,9=~A)
        /// </summary>
        CHANNEL_SWIZZLE = 0x21
    }

    /// <summary>
    /// Схема хранения нормалей в текстуре
    /// </summary>
    public enum NormalLayout : byte {
        /// <summary>
        /// Нормали не хранятся / стандартный RGB формат
        /// </summary>
        NONE = 0,

        /// <summary>
        /// X в R, Y в G, Z вычисляется (BC5/UASTC normal maps)
        /// </summary>
        RG = 1,

        /// <summary>
        /// X в G, Y в A, Z вычисляется
        /// </summary>
        GA = 2,

        /// <summary>
        /// Полный XYZ в RGB
        /// </summary>
        RGB = 3,

        /// <summary>
        /// X в A, Y в G, Z вычисляется
        /// </summary>
        AG = 4,

        /// <summary>
        /// X в RGB (все каналы), Y в A, Z вычисляется (ETC1S normal maps в режиме RGB-X, A-Y)
        /// </summary>
        RGBxAy = 5
    }
}
