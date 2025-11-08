using System;
using System.IO;
using System.Threading.Tasks;
using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureConversion.MipGeneration;
using AssetProcessor.TextureConversion.Pipeline;

namespace AssetProcessor.TextureConversion.Examples {
    /// <summary>
    /// Примеры использования ORM Channel Packing для glTF/PlayCanvas
    /// </summary>
    public static class ChannelPackingExample {
        /// <summary>
        /// Пример 1: Автоматическое обнаружение и упаковка в OGM режим
        /// </summary>
        public static async Task Example1_AutoDetectAndPack() {
            Console.WriteLine("=== Example 1: Auto-detect and pack OGM ===");

            // Путь к любой текстуре материала (например, albedo)
            string basePath = @"C:\Textures\material_albedo.png";

            // Автоматический поиск AO, Gloss, Metallic текстур
            var detector = new ORMTextureDetector();
            var detection = detector.DetectORMTextures(basePath, validateDimensions: true);

            Console.WriteLine($"Found textures: {detection}");
            Console.WriteLine($"Recommended mode: {detection.GetRecommendedPackingMode()}");

            // Создаем настройки на основе найденных текстур
            var packingSettings = detector.CreateSettingsFromDetection(basePath);

            if (packingSettings == null) {
                Console.WriteLine("Not enough textures found for packing");
                return;
            }

            // Настраиваем параметры обработки
            // AO: используем BiasedDarkening с bias 0.5
            if (packingSettings.RedChannel != null) {
                packingSettings.RedChannel.AOProcessingMode = AOProcessingMode.BiasedDarkening;
                packingSettings.RedChannel.AOBias = 0.5f; // Средний bias
            }

            // Gloss: включаем Toksvig коррекцию
            if (packingSettings.GreenChannel != null) {
                packingSettings.GreenChannel.ApplyToksvig = true;
                packingSettings.GreenChannel.ToksvigSettings = new ToksvigSettings {
                    Enabled = true,
                    CompositePower = 4.0f, // Средняя сила коррекции
                    CalculationMode = ToksvigCalculationMode.Simplified
                };
            }

            // Создаем пайплайн и упаковываем
            var pipeline = new TextureConversionPipeline();
            var compressionSettings = CompressionSettings.CreateETC1SDefault();
            compressionSettings.QualityLevel = 192; // Высокое качество для ORM
            compressionSettings.ColorSpace = ColorSpace.Linear; // КРИТИЧНО для ORM!

            var outputPath = @"C:\Textures\material_orm.ktx2";
            var result = await pipeline.ConvertPackedTextureAsync(
                packingSettings,
                outputPath,
                compressionSettings
            );

            if (result.Success) {
                Console.WriteLine($"✓ ORM texture packed successfully: {outputPath}");
                Console.WriteLine($"  Mip levels: {result.MipLevels}");
                Console.WriteLine($"  Duration: {result.Duration.TotalSeconds:F2}s");
            } else {
                Console.WriteLine($"✗ Failed: {result.Error}");
            }
        }

        /// <summary>
        /// Пример 2: Ручная настройка OGMH (4 канала) с кастомными параметрами
        /// </summary>
        public static async Task Example2_ManualOGMHPacking() {
            Console.WriteLine("=== Example 2: Manual OGMH packing ===");

            // Создаем настройки вручную для OGMH режима
            var packingSettings = ChannelPackingSettings.CreateDefault(ChannelPackingMode.OGMH);

            // R канал = AO с агрессивным затемнением
            packingSettings.RedChannel!.SourcePath = @"C:\Textures\rock_ao.png";
            packingSettings.RedChannel.AOProcessingMode = AOProcessingMode.BiasedDarkening;
            packingSettings.RedChannel.AOBias = 0.7f; // Более темные мипы (bias ближе к min)

            // G канал = Gloss с Toksvig коррекцией
            packingSettings.GreenChannel!.SourcePath = @"C:\Textures\rock_gloss.png";
            packingSettings.GreenChannel.ApplyToksvig = true;
            packingSettings.GreenChannel.ToksvigSettings = new ToksvigSettings {
                Enabled = true,
                CompositePower = 8.0f, // Высокая сила для сильного эффекта
                CalculationMode = ToksvigCalculationMode.Classic,
                SmoothVariance = true
            };

            // B канал = Metallic (бинарный, Box фильтр)
            packingSettings.BlueChannel!.SourcePath = @"C:\Textures\rock_metallic.png";
            packingSettings.BlueChannel.MipProfile = MipGenerationProfile.CreateDefault(TextureType.Metallic);

            // A канал = Height
            packingSettings.AlphaChannel!.SourcePath = @"C:\Textures\rock_height.png";

            // Валидация настроек
            if (!packingSettings.Validate(out var error)) {
                Console.WriteLine($"Invalid settings: {error}");
                return;
            }

            // Настройки компрессии UASTC для максимального качества
            var compressionSettings = CompressionSettings.CreateUASTCDefault();
            compressionSettings.UASTCQuality = 4; // Максимальное качество
            compressionSettings.UseUASTCRDO = true;
            compressionSettings.UASTCRDOQuality = 0.5f; // Low RDO lambda = выше качество
            compressionSettings.ColorSpace = ColorSpace.Linear;

            var pipeline = new TextureConversionPipeline();
            var result = await pipeline.ConvertPackedTextureAsync(
                packingSettings,
                @"C:\Textures\rock_orm_highq.ktx2",
                compressionSettings,
                saveSeparateMipmaps: true, // Сохраняем мипмапы для отладки
                mipmapOutputDir: @"C:\Textures\rock_orm_mipmaps"
            );

            if (result.Success) {
                Console.WriteLine($"✓ High quality OGMH texture created");
                Console.WriteLine($"  Mipmaps saved to: {result.MipmapsSavedPath}");
            }
        }

        /// <summary>
        /// Пример 3: OG режим (2 канала) для non-metallic материалов
        /// </summary>
        public static async Task Example3_OGModeForNonMetallic() {
            Console.WriteLine("=== Example 3: OG mode (RGB=AO, A=Gloss) ===");

            var packingSettings = ChannelPackingSettings.CreateDefault(ChannelPackingMode.OG);

            // RGB = AO с percentile-based обработкой
            packingSettings.RedChannel!.SourcePath = @"C:\Textures\wood_ao.png";
            packingSettings.RedChannel.AOProcessingMode = AOProcessingMode.Percentile;
            packingSettings.RedChannel.AOPercentile = 10.0f; // 10-й перцентиль

            // A = Gloss без Toksvig (для простых материалов)
            packingSettings.AlphaChannel!.SourcePath = @"C:\Textures\wood_gloss.png";
            packingSettings.AlphaChannel.ApplyToksvig = false;

            // Компрессия ETC1S для минимального размера
            var compressionSettings = CompressionSettings.CreateMinSize();
            compressionSettings.ColorSpace = ColorSpace.Linear;

            var pipeline = new TextureConversionPipeline();
            var result = await pipeline.ConvertPackedTextureAsync(
                packingSettings,
                @"C:\Textures\wood_og_minsize.ktx2",
                compressionSettings
            );

            Console.WriteLine($"Result: {(result.Success ? "Success" : "Failed")}");
        }

        /// <summary>
        /// Пример 4: Использование констант для недостающих каналов
        /// </summary>
        public static async Task Example4_MissingChannelsWithDefaults() {
            Console.WriteLine("=== Example 4: Missing channels with default values ===");

            var packingSettings = ChannelPackingSettings.CreateDefault(ChannelPackingMode.OGM);

            // R = AO (есть текстура)
            packingSettings.RedChannel!.SourcePath = @"C:\Textures\metal_ao.png";

            // G = Gloss (нет текстуры, используем константу 0.8 = высокий глянец)
            packingSettings.GreenChannel!.SourcePath = null;
            packingSettings.GreenChannel.DefaultValue = 0.8f;

            // B = Metallic (есть текстура)
            packingSettings.BlueChannel!.SourcePath = @"C:\Textures\metal_metallic.png";

            var pipeline = new TextureConversionPipeline();
            var compressionSettings = CompressionSettings.CreateETC1SDefault();
            compressionSettings.ColorSpace = ColorSpace.Linear;

            var result = await pipeline.ConvertPackedTextureAsync(
                packingSettings,
                @"C:\Textures\metal_orm_partial.ktx2",
                compressionSettings
            );

            Console.WriteLine($"Result: {(result.Success ? "Success" : "Failed")}");
            Console.WriteLine("Green channel filled with constant value 0.8 (high gloss)");
        }

        /// <summary>
        /// Пример 5: Только упаковка каналов без компрессии (для отладки)
        /// </summary>
        public static async Task Example5_PackOnlyWithoutCompression() {
            Console.WriteLine("=== Example 5: Pack channels without compression ===");

            var detector = new ORMTextureDetector();
            var packingSettings = detector.CreateSettingsFromDetection(
                @"C:\Textures\material_albedo.png"
            );

            if (packingSettings == null) {
                Console.WriteLine("Cannot create packing settings");
                return;
            }

            // Используем ChannelPackingPipeline напрямую для получения PNG мипмапов
            var channelPacker = new ChannelPackingPipeline();

            var savedPaths = await channelPacker.PackAndSaveAsync(
                packingSettings,
                outputDirectory: @"C:\Textures\debug_packed",
                baseName: "material_orm_debug"
            );

            Console.WriteLine($"✓ Saved {savedPaths.Count} mipmap levels as PNG:");
            foreach (var path in savedPaths) {
                Console.WriteLine($"  - {path}");
            }
        }

        /// <summary>
        /// Пример 6: Batch обработка нескольких материалов
        /// </summary>
        public static async Task Example6_BatchProcessing() {
            Console.WriteLine("=== Example 6: Batch ORM packing ===");

            string[] materials = {
                @"C:\Textures\material01_albedo.png",
                @"C:\Textures\material02_albedo.png",
                @"C:\Textures\material03_albedo.png"
            };

            var detector = new ORMTextureDetector();
            var pipeline = new TextureConversionPipeline();

            foreach (var materialPath in materials) {
                Console.WriteLine($"\nProcessing: {Path.GetFileName(materialPath)}");

                var detection = detector.DetectORMTextures(materialPath);
                if (detection.FoundCount == 0) {
                    Console.WriteLine("  No ORM textures found, skipping");
                    continue;
                }

                var packingSettings = detector.CreateSettingsFromDetection(materialPath);
                if (packingSettings == null) continue;

                // Стандартные настройки для батча
                var compressionSettings = CompressionSettings.CreateETC1SDefault();
                compressionSettings.QualityLevel = 128;
                compressionSettings.ColorSpace = ColorSpace.Linear;

                var baseName = Path.GetFileNameWithoutExtension(materialPath)
                    .Replace("_albedo", "")
                    .Replace("_diffuse", "");
                var outputPath = Path.Combine(
                    Path.GetDirectoryName(materialPath)!,
                    $"{baseName}_orm.ktx2"
                );

                var result = await pipeline.ConvertPackedTextureAsync(
                    packingSettings,
                    outputPath,
                    compressionSettings
                );

                Console.WriteLine($"  {(result.Success ? "✓" : "✗")} {Path.GetFileName(outputPath)}");
            }
        }
    }
}
