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
}
