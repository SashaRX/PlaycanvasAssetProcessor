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
        UASTC,

        /// <summary>
        /// XUASTC LDR - новый формат с DCT-трансформ сжатием весовой сетки ASTC.
        /// Поддерживает все 14 размеров блоков ASTC (от 4x4 до 12x12).
        /// Прямой транскод в BC7 и ASTC LDR.
        /// Требует basisu CLI с поддержкой XUASTC LDR (basis_universal master).
        /// </summary>
        XUASTC_LDR
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

    /// <summary>
    /// Размер блока ASTC для XUASTC LDR.
    /// Определяет соотношение качество/размер в GPU памяти.
    /// Меньший блок = выше качество, больше bpp.
    /// </summary>
    public enum XuastcBlockSize {
        /// <summary>
        /// 4x4 - 8.00 bpp в GPU. Максимальное качество. Прямой транскод в BC7.
        /// </summary>
        Block4x4,

        /// <summary>
        /// 5x4 - 6.40 bpp в GPU.
        /// </summary>
        Block5x4,

        /// <summary>
        /// 5x5 - 5.12 bpp в GPU.
        /// </summary>
        Block5x5,

        /// <summary>
        /// 6x5 - 4.27 bpp в GPU.
        /// </summary>
        Block6x5,

        /// <summary>
        /// 6x6 - 3.56 bpp в GPU. Хороший баланс качество/размер. Прямой транскод в BC7.
        /// Рекомендуется для Albedo и ORM текстур.
        /// </summary>
        Block6x6,

        /// <summary>
        /// 8x5 - 3.20 bpp в GPU.
        /// </summary>
        Block8x5,

        /// <summary>
        /// 8x6 - 2.67 bpp в GPU. Прямой транскод в BC7.
        /// Рекомендуется для текстур с умеренными требованиями к качеству.
        /// </summary>
        Block8x6,

        /// <summary>
        /// 8x8 - 2.00 bpp в GPU.
        /// </summary>
        Block8x8,

        /// <summary>
        /// 10x5 - 2.56 bpp в GPU.
        /// </summary>
        Block10x5,

        /// <summary>
        /// 10x6 - 2.13 bpp в GPU.
        /// </summary>
        Block10x6,

        /// <summary>
        /// 10x8 - 1.60 bpp в GPU.
        /// </summary>
        Block10x8,

        /// <summary>
        /// 10x10 - 1.28 bpp в GPU.
        /// </summary>
        Block10x10,

        /// <summary>
        /// 12x10 - 1.07 bpp в GPU.
        /// </summary>
        Block12x10,

        /// <summary>
        /// 12x12 - 0.89 bpp в GPU. Минимальный размер, минимальное качество.
        /// </summary>
        Block12x12
    }

    /// <summary>
    /// Профиль суперкомпрессии XUASTC LDR.
    /// Определяет алгоритм сжатия DCT-данных.
    /// </summary>
    public enum XuastcSupercompressionProfile {
        /// <summary>
        /// Zstandard - быстрый декод, хорошее сжатие.
        /// Рекомендуется для стриминга (быстрый декод на клиенте).
        /// </summary>
        Zstd,

        /// <summary>
        /// Arithmetic - максимальное сжатие, более медленный декод.
        /// </summary>
        Arithmetic,

        /// <summary>
        /// Hybrid - Zstd для DCT данных, Arithmetic для метаданных.
        /// Баланс между скоростью декода и размером файла.
        /// </summary>
        Hybrid
    }

    /// <summary>
    /// Вспомогательные методы для работы с XuastcBlockSize
    /// </summary>
    public static class XuastcBlockSizeExtensions {
        /// <summary>
        /// Возвращает строковое представление размера блока для CLI (например "6x6")
        /// </summary>
        public static string ToCliString(this XuastcBlockSize blockSize) {
            return blockSize switch {
                XuastcBlockSize.Block4x4 => "4x4",
                XuastcBlockSize.Block5x4 => "5x4",
                XuastcBlockSize.Block5x5 => "5x5",
                XuastcBlockSize.Block6x5 => "6x5",
                XuastcBlockSize.Block6x6 => "6x6",
                XuastcBlockSize.Block8x5 => "8x5",
                XuastcBlockSize.Block8x6 => "8x6",
                XuastcBlockSize.Block8x8 => "8x8",
                XuastcBlockSize.Block10x5 => "10x5",
                XuastcBlockSize.Block10x6 => "10x6",
                XuastcBlockSize.Block10x8 => "10x8",
                XuastcBlockSize.Block10x10 => "10x10",
                XuastcBlockSize.Block12x10 => "12x10",
                XuastcBlockSize.Block12x12 => "12x12",
                _ => "6x6"
            };
        }

        /// <summary>
        /// Возвращает bpp в GPU памяти для данного размера блока (128 бит на блок)
        /// </summary>
        public static float GetGpuBitsPerPixel(this XuastcBlockSize blockSize) {
            return blockSize switch {
                XuastcBlockSize.Block4x4 => 8.00f,
                XuastcBlockSize.Block5x4 => 6.40f,
                XuastcBlockSize.Block5x5 => 5.12f,
                XuastcBlockSize.Block6x5 => 4.27f,
                XuastcBlockSize.Block6x6 => 3.56f,
                XuastcBlockSize.Block8x5 => 3.20f,
                XuastcBlockSize.Block8x6 => 2.67f,
                XuastcBlockSize.Block8x8 => 2.00f,
                XuastcBlockSize.Block10x5 => 2.56f,
                XuastcBlockSize.Block10x6 => 2.13f,
                XuastcBlockSize.Block10x8 => 1.60f,
                XuastcBlockSize.Block10x10 => 1.28f,
                XuastcBlockSize.Block12x10 => 1.07f,
                XuastcBlockSize.Block12x12 => 0.89f,
                _ => 3.56f
            };
        }

        /// <summary>
        /// Возвращает bpp для BC7 транскода (всегда 8 bpp, независимо от размера XUASTC блока)
        /// </summary>
        public static float GetBc7BitsPerPixel(this XuastcBlockSize blockSize) {
            return 8.00f; // BC7 = 128 bits per 4x4 block = 8 bpp
        }

        /// <summary>
        /// Поддерживает ли данный размер блока прямой транскод в BC7
        /// </summary>
        public static bool SupportsBc7Transcode(this XuastcBlockSize blockSize) {
            return blockSize switch {
                XuastcBlockSize.Block4x4 => true,
                XuastcBlockSize.Block6x6 => true,
                XuastcBlockSize.Block8x6 => true,
                _ => false // Другие размеры могут требовать промежуточной декомпрессии
            };
        }

        /// <summary>
        /// Парсит строку размера блока (например "6x6") в enum
        /// </summary>
        public static XuastcBlockSize Parse(string blockSizeString) {
            return blockSizeString.ToLowerInvariant() switch {
                "4x4" => XuastcBlockSize.Block4x4,
                "5x4" => XuastcBlockSize.Block5x4,
                "5x5" => XuastcBlockSize.Block5x5,
                "6x5" => XuastcBlockSize.Block6x5,
                "6x6" => XuastcBlockSize.Block6x6,
                "8x5" => XuastcBlockSize.Block8x5,
                "8x6" => XuastcBlockSize.Block8x6,
                "8x8" => XuastcBlockSize.Block8x8,
                "10x5" => XuastcBlockSize.Block10x5,
                "10x6" => XuastcBlockSize.Block10x6,
                "10x8" => XuastcBlockSize.Block10x8,
                "10x10" => XuastcBlockSize.Block10x10,
                "12x10" => XuastcBlockSize.Block12x10,
                "12x12" => XuastcBlockSize.Block12x12,
                _ => throw new ArgumentException($"Unknown XUASTC block size: {blockSizeString}")
            };
        }
    }
}
