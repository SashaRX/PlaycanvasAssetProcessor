using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureConversion.Pipeline;

namespace AssetProcessor.TextureConversion.Examples {
    /// <summary>
    /// Примеры использования анализа гистограммы для оптимизации сжатия текстур
    /// </summary>
    public static class HistogramAnalysisExample {
        /// <summary>
        /// Базовый пример: конвертация текстуры с анализом гистограммы
        /// </summary>
        public static async Task BasicHistogramAnalysis() {
            var pipeline = new TextureConversionPipeline();

            // Настройки сжатия с анализом гистограммы (High Quality режим)
            var settings = new CompressionSettings {
                CompressionFormat = CompressionFormat.ETC1S,
                QualityLevel = 128,
                GenerateMipmaps = true,

                // Включаем анализ гистограммы (High Quality: PercentileWithKnee 0.5%, 99.5%)
                HistogramAnalysis = HistogramSettings.CreateHighQuality()
            };

            var mipProfile = MipGenerationProfile.CreateDefault(TextureType.Albedo);

            var result = await pipeline.ConvertTextureAsync(
                inputPath: "input_albedo.png",
                outputPath: "output_albedo.ktx2",
                mipProfile: mipProfile,
                compressionSettings: settings
            );

            if (result.Success && result.HistogramAnalysisResult != null) {
                Console.WriteLine($"Histogram analysis applied:");
                Console.WriteLine($"  Scale: {result.HistogramAnalysisResult.Scale[0]:F4}");
                Console.WriteLine($"  Offset: {result.HistogramAnalysisResult.Offset[0]:F4}");
                Console.WriteLine($"  Range: [{result.HistogramAnalysisResult.RangeLow:F4} - {result.HistogramAnalysisResult.RangeHigh:F4}]");
            }
        }

        /// <summary>
        /// Продвинутый пример: анализ с мягким коленом (soft-knee)
        /// </summary>
        public static async Task HistogramWithSoftKnee() {
            var pipeline = new TextureConversionPipeline();

            var settings = new CompressionSettings {
                CompressionFormat = CompressionFormat.UASTC,
                UASTCQuality = 3,
                UseUASTCRDO = true,

                // Анализ с мягким коленом (High Quality: PercentileWithKnee 0.5%, 99.5%, knee=2%)
                HistogramAnalysis = HistogramSettings.CreateHighQuality()
            };

            var mipProfile = MipGenerationProfile.CreateDefault(TextureType.Albedo);

            await pipeline.ConvertTextureAsync(
                inputPath: "hdr_texture.png",
                outputPath: "hdr_texture.ktx2",
                mipProfile: mipProfile,
                compressionSettings: settings
            );
        }

        /// <summary>
        /// Поканальный анализ RGB
        /// </summary>
        public static async Task PerChannelAnalysis() {
            var pipeline = new TextureConversionPipeline();

            var settings = new CompressionSettings {
                CompressionFormat = CompressionFormat.ETC1S,
                QualityLevel = 192,

                // Поканальный анализ для RGB (High Quality с PerChannel)
                HistogramAnalysis = new HistogramSettings {
                    Quality = HistogramQuality.HighQuality,
                    Mode = HistogramMode.PercentileWithKnee,
                    ChannelMode = HistogramChannelMode.PerChannel, // Отдельные scale/offset для R, G, B
                    PercentileLow = 0.5f,
                    PercentileHigh = 99.5f,
                    KneeWidth = 0.02f
                }
            };

            var mipProfile = MipGenerationProfile.CreateDefault(TextureType.Albedo);

            var result = await pipeline.ConvertTextureAsync(
                inputPath: "colored_texture.png",
                outputPath: "colored_texture.ktx2",
                mipProfile: mipProfile,
                compressionSettings: settings
            );

            if (result.Success && result.HistogramAnalysisResult != null) {
                Console.WriteLine($"Per-channel analysis:");
                Console.WriteLine($"  R: scale={result.HistogramAnalysisResult.Scale[0]:F4}, offset={result.HistogramAnalysisResult.Offset[0]:F4}");
                Console.WriteLine($"  G: scale={result.HistogramAnalysisResult.Scale[1]:F4}, offset={result.HistogramAnalysisResult.Offset[1]:F4}");
                Console.WriteLine($"  B: scale={result.HistogramAnalysisResult.Scale[2]:F4}, offset={result.HistogramAnalysisResult.Offset[2]:F4}");
            }
        }

        /// <summary>
        /// Анализ с пользовательскими параметрами
        /// </summary>
        public static async Task CustomHistogramSettings() {
            var pipeline = new TextureConversionPipeline();

            var settings = new CompressionSettings {
                CompressionFormat = CompressionFormat.ETC1S,
                QualityLevel = 128,

                // Пользовательские настройки анализа (Fast режим с модификациями)
                HistogramAnalysis = new HistogramSettings {
                    Quality = HistogramQuality.Fast,  // Или HighQuality для soft-knee
                    Mode = HistogramMode.PercentileWithKnee,
                    ChannelMode = HistogramChannelMode.AverageLuminance,

                    // Более агрессивное отсечение выбросов
                    PercentileLow = 1.0f,    // 1% нижний
                    PercentileHigh = 99.0f,  // 99% верхний

                    // Более широкое колено для сглаживания
                    KneeWidth = 0.05f,       // 5% колено

                    // Порог для предупреждений
                    TailThreshold = 0.01f,   // 1% порог хвостов

                    // Минимальный диапазон для нормализации
                    MinRangeThreshold = 0.02f  // Игнорировать почти константные текстуры
                }
            };

            var mipProfile = MipGenerationProfile.CreateDefault(TextureType.Albedo);

            await pipeline.ConvertTextureAsync(
                inputPath: "noisy_texture.png",
                outputPath: "noisy_texture.ktx2",
                mipProfile: mipProfile,
                compressionSettings: settings
            );
        }

        /// <summary>
        /// Отключение анализа гистограммы
        /// </summary>
        public static async Task DisabledHistogramAnalysis() {
            var pipeline = new TextureConversionPipeline();

            var settings = new CompressionSettings {
                CompressionFormat = CompressionFormat.UASTC,
                UASTCQuality = 2,

                // Анализ отключён (по умолчанию)
                HistogramAnalysis = null  // или HistogramSettings.CreateDefault()
            };

            var mipProfile = MipGenerationProfile.CreateDefault(TextureType.Normal);

            await pipeline.ConvertTextureAsync(
                inputPath: "normal_map.png",
                outputPath: "normal_map.ktx2",
                mipProfile: mipProfile,
                compressionSettings: settings
            );
        }

        /// <summary>
        /// Рекомендуемые настройки для различных типов текстур
        /// </summary>
        public static class RecommendedSettings {
            /// <summary>
            /// Настройки для HDR текстур с широким динамическим диапазоном
            /// </summary>
            public static HistogramSettings ForHDR() {
                return new HistogramSettings {
                    Quality = HistogramQuality.HighQuality,
                    Mode = HistogramMode.PercentileWithKnee,
                    ChannelMode = HistogramChannelMode.AverageLuminance,
                    PercentileLow = 0.1f,      // Очень консервативное отсечение
                    PercentileHigh = 99.9f,
                    KneeWidth = 0.03f,         // Мягкое колено для HDR
                    MinRangeThreshold = 0.05f  // Больший порог для HDR
                };
            }

            /// <summary>
            /// Настройки для albedo текстур с потенциальными выбросами
            /// </summary>
            public static HistogramSettings ForAlbedo() {
                // Эквивалентно CreateHighQuality()
                return HistogramSettings.CreateHighQuality();
            }

            /// <summary>
            /// Настройки для roughness/metallic карт (обычно узкий диапазон)
            /// </summary>
            public static HistogramSettings ForPBRMaps() {
                // Эквивалентно CreateFast() с небольшими модификациями
                return new HistogramSettings {
                    Quality = HistogramQuality.Fast,
                    Mode = HistogramMode.Percentile,  // Без колена для точности
                    ChannelMode = HistogramChannelMode.AverageLuminance,
                    PercentileLow = 0.5f,
                    PercentileHigh = 99.5f,
                    MinRangeThreshold = 0.005f  // Низкий порог для узких диапазонов
                };
            }

            /// <summary>
            /// Настройки для emissive карт (могут иметь яркие выбросы)
            /// </summary>
            public static HistogramSettings ForEmissive() {
                return new HistogramSettings {
                    Quality = HistogramQuality.Fast,
                    Mode = HistogramMode.PercentileWithKnee,
                    ChannelMode = HistogramChannelMode.PerChannel, // Поканально для цветных источников света
                    PercentileLow = 1.0f,          // Агрессивное отсечение тёмных областей
                    PercentileHigh = 99.0f,
                    KneeWidth = 0.05f,             // Широкое колено для ярких пикселей
                    MinRangeThreshold = 0.02f
                };
            }
        }
    }
}
