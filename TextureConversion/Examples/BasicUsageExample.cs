using System.IO;
using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureConversion.Pipeline;

namespace AssetProcessor.TextureConversion.Examples {
    /// <summary>
    /// Примеры использования системы конвертации текстур
    /// </summary>
    public static class BasicUsageExample {
        /// <summary>
        /// Пример 1: Базовая конвертация одного файла
        /// </summary>
        public static async Task Example1_BasicConversion() {
            Console.WriteLine("=== Example 1: Basic Conversion ===");

            var pipeline = new TextureConversionPipeline();

            // Создаем профиль для albedo текстуры
            var mipProfile = MipGenerationProfile.CreateDefault(TextureType.Albedo);

            // Настройки сжатия ETC1S
            var compressionSettings = CompressionSettings.CreateETC1SDefault();

            // Конвертируем
            var result = await pipeline.ConvertTextureAsync(
                inputPath: "input/wood_albedo.png",
                outputPath: "output/wood_albedo.ktx2",
                mipProfile: mipProfile,
                compressionSettings: compressionSettings
            );

            if (result.Success) {
                Console.WriteLine($"✓ Success! Generated {result.MipLevels} mip levels");
                Console.WriteLine($"  Duration: {result.Duration.TotalSeconds:F2}s");
                Console.WriteLine($"  Output: {result.OutputPath}");
            } else {
                Console.WriteLine($"✗ Failed: {result.Error}");
            }
        }

        /// <summary>
        /// Пример 2: Normal map с нормализацией
        /// </summary>
        public static async Task Example2_NormalMap() {
            Console.WriteLine("\n=== Example 2: Normal Map ===");

            var pipeline = new TextureConversionPipeline();

            // Профиль для normal map
            var profile = MipGenerationProfile.CreateDefault(TextureType.Normal);
            profile.Filter = FilterType.Lanczos3; // Высокое качество
            profile.NormalizeNormals = true; // Важно для normal maps

            // UASTC для лучшего качества normal maps
            var settings = CompressionSettings.CreateUASTCDefault();

            var result = await pipeline.ConvertTextureAsync(
                inputPath: "input/wall_normal.png",
                outputPath: "output/wall_normal.ktx2",
                mipProfile: profile,
                compressionSettings: settings
            );

            Console.WriteLine(result.Success ? "✓ Normal map converted" : $"✗ Failed: {result.Error}");
        }

        /// <summary>
        /// Пример 3: Пакетная обработка директории
        /// </summary>
        public static async Task Example3_BatchProcessing() {
            Console.WriteLine("\n=== Example 3: Batch Processing ===");

            var pipeline = new TextureConversionPipeline();
            var batchProcessor = new BatchProcessor(pipeline);

            // Автоматический выбор профиля по имени файла
            var profileSelector = BatchProcessor.CreateNameBasedProfileSelector();

            var compressionSettings = CompressionSettings.CreateETC1SDefault();

            // Прогресс
            var progress = new Progress<BatchProgress>(p => {
                Console.WriteLine($"  [{p.CurrentFile}/{p.TotalFiles}] {p.CurrentFileName} - {p.PercentComplete:F1}%");
            });

            var batchResult = await batchProcessor.ProcessDirectoryAsync(
                inputDirectory: "input_textures",
                outputDirectory: "output_compressed",
                profileSelector: profileSelector,
                compressionSettings: compressionSettings,
                saveSeparateMipmaps: false,
                progress: progress,
                maxParallelism: 4
            );

            Console.WriteLine($"\n✓ Batch complete: {batchResult.SuccessCount} succeeded, {batchResult.FailureCount} failed");
            Console.WriteLine($"  Total duration: {batchResult.Duration.TotalSeconds:F2}s");
        }

        /// <summary>
        /// Пример 4: Только генерация мипмапов без сжатия
        /// </summary>
        public static async Task Example4_MipmapsOnly() {
            Console.WriteLine("\n=== Example 4: Mipmaps Only ===");

            var pipeline = new TextureConversionPipeline();

            var profile = MipGenerationProfile.CreateDefault(TextureType.Roughness);
            profile.Filter = FilterType.Kaiser;
            profile.MinMipSize = 4; // Не генерировать мипы меньше 4x4

            var mipmapPaths = await pipeline.GenerateMipmapsOnlyAsync(
                inputPath: "input/material_roughness.png",
                outputDirectory: "output/mipmaps",
                profile: profile
            );

            Console.WriteLine($"✓ Generated {mipmapPaths.Count} mipmaps:");
            foreach (var path in mipmapPaths) {
                Console.WriteLine($"  - {path}");
            }
        }

        /// <summary>
        /// Пример 5: Кастомный профиль с настройками
        /// </summary>
        public static async Task Example5_CustomProfile() {
            Console.WriteLine("\n=== Example 5: Custom Profile ===");

            var pipeline = new TextureConversionPipeline();

            // Создаем кастомный профиль
            var customProfile = new MipGenerationProfile {
                TextureType = TextureType.Emissive,
                Filter = FilterType.Mitchell,
                ApplyGammaCorrection = true,
                Gamma = 2.2f,
                BlurRadius = 0.3f, // Небольшой blur для эффекта glow
                IncludeLastLevel = true,
                MinMipSize = 1
            };

            var settings = new CompressionSettings {
                CompressionFormat = CompressionFormat.UASTC,
                OutputFormat = OutputFormat.KTX2,
                UASTCQuality = 3,
                UseUASTCRDO = true,
                UASTCRDOQuality = 1.0f,
                UseMultithreading = true,
                ThreadCount = 8
            };

            var result = await pipeline.ConvertTextureAsync(
                inputPath: "input/neon_emissive.png",
                outputPath: "output/neon_emissive.ktx2",
                mipProfile: customProfile,
                compressionSettings: settings
            );

            Console.WriteLine(result.Success ? "✓ Custom profile applied" : $"✗ Failed: {result.Error}");
        }

        /// <summary>
        /// Пример 6: Сохранение отдельных мипмапов для стриминга
        /// </summary>
        public static async Task Example6_SeparateMipmaps() {
            Console.WriteLine("\n=== Example 6: Separate Mipmaps for Streaming ===");

            var pipeline = new TextureConversionPipeline();
            var profile = MipGenerationProfile.CreateDefault(TextureType.Albedo);
            var settings = CompressionSettings.CreateHighQuality();

            var result = await pipeline.ConvertTextureAsync(
                inputPath: "input/terrain_albedo.png",
                outputPath: "output/terrain_albedo.ktx2",
                mipProfile: profile,
                compressionSettings: settings,
                saveSeparateMipmaps: true, // Сохраняем отдельно
                mipmapOutputDir: "output/mipmaps/terrain_albedo"
            );

            if (result.Success) {
                Console.WriteLine($"✓ Texture converted with separate mipmaps");
                Console.WriteLine($"  Mipmaps saved to: output/mipmaps/terrain_albedo/");
            }
        }

        /// <summary>
        /// Пример 7: Toksvig коррекция для gloss/roughness текстур
        /// </summary>
        public static async Task Example7_ToksvigCorrection() {
            Console.WriteLine("\n=== Example 7: Toksvig Correction ===");

            var pipeline = new TextureConversionPipeline();

            // Профиль для roughness текстуры
            var profile = MipGenerationProfile.CreateDefault(TextureType.Roughness);

            // Настройки Toksvig
            var toksvigSettings = new ToksvigSettings {
                Enabled = true,
                CompositePower = 1.0f, // Стандартный вес
                MinToksvigMipLevel = 1, // Не трогаем уровень 0
                SmoothVariance = true, // Сглаживание дисперсии
                NormalMapPath = null // Автоматический поиск
            };

            var compressionSettings = CompressionSettings.CreateETC1SDefault();

            // Конвертируем с Toksvig коррекцией
            var result = await pipeline.ConvertTextureAsync(
                inputPath: "input/metal_roughness.png",
                outputPath: "output/metal_roughness.ktx2",
                mipProfile: profile,
                compressionSettings: compressionSettings,
                toksvigSettings: toksvigSettings
            );

            if (result.Success) {
                Console.WriteLine($"✓ Toksvig correction applied!");
                Console.WriteLine($"  Normal map used: {result.NormalMapUsed ?? "не найдена"}");
                Console.WriteLine($"  Toksvig applied: {result.ToksvigApplied}");
                Console.WriteLine($"  Duration: {result.Duration.TotalSeconds:F2}s");
            } else {
                Console.WriteLine($"✗ Failed: {result.Error}");
            }
        }

        /// <summary>
        /// Пример 8: Toksvig с указанием конкретной normal map
        /// </summary>
        public static async Task Example8_ToksvigWithSpecificNormalMap() {
            Console.WriteLine("\n=== Example 8: Toksvig with Specific Normal Map ===");

            var pipeline = new TextureConversionPipeline();

            // Профиль для gloss текстуры
            var profile = MipGenerationProfile.CreateDefault(TextureType.Gloss);

            // Настройки Toksvig с указанием конкретной normal map
            var toksvigSettings = new ToksvigSettings {
                Enabled = true,
                CompositePower = 1.5f, // Более сильный эффект
                MinToksvigMipLevel = 1,
                SmoothVariance = true,
                NormalMapPath = "input/metal_normal.png" // Конкретный путь
            };

            var compressionSettings = CompressionSettings.CreateUASTCDefault();

            var result = await pipeline.ConvertTextureAsync(
                inputPath: "input/metal_gloss.png",
                outputPath: "output/metal_gloss.ktx2",
                mipProfile: profile,
                compressionSettings: compressionSettings,
                toksvigSettings: toksvigSettings
            );

            if (result.Success) {
                Console.WriteLine($"✓ Gloss texture with Toksvig converted");
                Console.WriteLine($"  Normal map: {result.NormalMapUsed}");
            } else {
                Console.WriteLine($"✗ Failed: {result.Error}");
            }
        }

        /// <summary>
        /// Пример 9: Сравнение с и без Toksvig
        /// </summary>
        public static async Task Example9_ToksvigComparison() {
            Console.WriteLine("\n=== Example 9: Toksvig Comparison ===");

            var pipeline = new TextureConversionPipeline();
            var profile = MipGenerationProfile.CreateDefault(TextureType.Roughness);
            var settings = CompressionSettings.CreateETC1SDefault();

            // Без Toksvig
            var withoutToksvig = await pipeline.ConvertTextureAsync(
                "input/test_roughness.png",
                "output/test_roughness_no_toksvig.ktx2",
                profile,
                settings,
                toksvigSettings: null
            );

            // С Toksvig
            var toksvigSettings = ToksvigSettings.CreateDefault();
            toksvigSettings.Enabled = true;
            var withToksvig = await pipeline.ConvertTextureAsync(
                "input/test_roughness.png",
                "output/test_roughness_with_toksvig.ktx2",
                profile,
                settings,
                toksvigSettings: toksvigSettings
            );

            if (withoutToksvig.Success && withToksvig.Success) {
                Console.WriteLine("✓ Comparison:");
                Console.WriteLine($"  Without Toksvig: {withoutToksvig.Duration.TotalSeconds:F2}s");
                Console.WriteLine($"  With Toksvig: {withToksvig.Duration.TotalSeconds:F2}s (normal: {withToksvig.NormalMapUsed ?? "N/A"})");
                Console.WriteLine($"  Note: Load both textures in engine to see visual difference (reduced specular aliasing)");
            }
        }

        /// <summary>
        /// Пример 10: Сравнение качества ETC1S vs UASTC
        /// </summary>
        public static async Task Example10_QualityComparison() {
            Console.WriteLine("\n=== Example 7: Quality Comparison ===");

            var pipeline = new TextureConversionPipeline();
            var profile = MipGenerationProfile.CreateDefault(TextureType.Albedo);

            // ETC1S
            var etc1sSettings = CompressionSettings.CreateETC1SDefault();
            var etc1sResult = await pipeline.ConvertTextureAsync(
                "input/test.png",
                "output/test_etc1s.ktx2",
                profile,
                etc1sSettings
            );

            // UASTC
            var uastcSettings = CompressionSettings.CreateUASTCDefault();
            var uastcResult = await pipeline.ConvertTextureAsync(
                "input/test.png",
                "output/test_uastc.ktx2",
                profile,
                uastcSettings
            );

            if (etc1sResult.Success && uastcResult.Success) {
                var etc1sSize = new FileInfo(etc1sResult.OutputPath).Length;
                var uastcSize = new FileInfo(uastcResult.OutputPath).Length;

                Console.WriteLine("✓ Comparison:");
                Console.WriteLine($"  ETC1S: {etc1sSize / 1024}KB in {etc1sResult.Duration.TotalSeconds:F2}s");
                Console.WriteLine($"  UASTC: {uastcSize / 1024}KB in {uastcResult.Duration.TotalSeconds:F2}s");
                Console.WriteLine($"  Size difference: {(uastcSize - etc1sSize) * 100 / etc1sSize:F1}%");
            }
        }

        /// <summary>
        /// Запускает все примеры
        /// </summary>
        public static async Task RunAllExamples() {
            Console.WriteLine("╔════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  Texture Conversion Pipeline - Usage Examples         ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════╝");

            try {
                await Example1_BasicConversion();
                await Example2_NormalMap();
                await Example3_BatchProcessing();
                await Example4_MipmapsOnly();
                await Example5_CustomProfile();
                await Example6_SeparateMipmaps();
                await Example7_ToksvigCorrection();
                await Example8_ToksvigWithSpecificNormalMap();
                await Example9_ToksvigComparison();
                await Example10_QualityComparison();

                Console.WriteLine("\n✓ All examples completed!");
            } catch (Exception ex) {
                Console.WriteLine($"\n✗ Error: {ex.Message}");
                Console.WriteLine($"Note: Make sure basisu is installed and input files exist");
            }
        }
    }
}
