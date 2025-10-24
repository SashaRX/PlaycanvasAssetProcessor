namespace AssetProcessor.TextureConversion.Core {
    /// <summary>
    /// Форматы сжатия Basis Universal
    /// </summary>
    public enum CompressionFormat {
        /// <summary>
        /// ETC1S - меньший размер, более широкая поддержка
        /// </summary>
        ETC1S,

        /// <summary>
        /// UASTC - высокое качество, больший размер
        /// </summary>
        UASTC
    }

    /// <summary>
    /// Формат выходного файла
    /// </summary>
    public enum OutputFormat {
        /// <summary>
        /// Basis Universal .basis файл
        /// </summary>
        Basis,

        /// <summary>
        /// Khronos KTX2 файл
        /// </summary>
        KTX2
    }

    /// <summary>
    /// Тип supercompression для KTX2
    /// </summary>
    public enum KTX2SupercompressionType {
        /// <summary>
        /// Без дополнительного сжатия
        /// </summary>
        None,

        /// <summary>
        /// Zstandard supercompression (лучшее сжатие, рекомендуется)
        /// </summary>
        Zstandard,

        /// <summary>
        /// ZLIB supercompression (для совместимости)
        /// </summary>
        ZLIB
    }
}
