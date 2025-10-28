using System.IO;
using AssetProcessor.TextureConversion.BasisU;
using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureConversion.MipGeneration;
using NLog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AssetProcessor.TextureConversion.Pipeline {
    /// <summary>
    /// Главный пайплайн конвертации текстур
    /// Объединяет генерацию мипмапов и сжатие в Basis Universal
    /// </summary>
    public class TextureConversionPipeline {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly MipGenerator _mipGenerator;
        private readonly ToktxWrapper _toktxWrapper;
        private readonly ToksvigProcessor _toksvigProcessor;
        private readonly NormalMapMatcher _normalMapMatcher;

        public TextureConversionPipeline(string? toktxExecutablePath = null) {
            _mipGenerator = new MipGenerator();
            _toktxWrapper = new ToktxWrapper(toktxExecutablePath ?? "toktx");
            _toksvigProcessor = new ToksvigProcessor();
            _normalMapMatcher = new NormalMapMatcher();
        }

        /// <summary>
        /// Конвертирует текстуру в KTX2 формат с генерацией мипмапов используя toktx
        /// </summary>
        /// <param name="inputPath">Путь к входной текстуре</param>
        /// <param name="outputPath">Путь к выходному файлу (.ktx2)</param>
        /// <param name="mipProfile">Профиль генерации мипмапов</param>
        /// <param name="compressionSettings">Настройки сжатия</param>
        /// <param name="toksvigSettings">Настройки Toksvig (optional, для gloss/roughness текстур)</param>
        /// <param name="saveSeparateMipmaps">Сохранить ли мипмапы отдельно</param>
        /// <param name="mipmapOutputDir">Директория для сохранения отдельных мипмапов</param>
        public async Task<ConversionResult> ConvertTextureAsync(
            string inputPath,
            string outputPath,
            MipGenerationProfile mipProfile,
            CompressionSettings compressionSettings,
            ToksvigSettings? toksvigSettings = null,
            bool saveSeparateMipmaps = false,
            string? mipmapOutputDir = null) {
            var result = new ConversionResult {
                InputPath = inputPath,
                OutputPath = outputPath
            };

            var startTime = DateTime.Now;

            try {
                Logger.Info($"Starting conversion: {inputPath}");

                // Проверяем доступность toktx
                if (!await _toktxWrapper.IsAvailableAsync()) {
                    throw new Exception("toktx executable not found. Please install KTX-Software: winget install KhronosGroup.KTX-Software");
                }

                // Загружаем изображение
                using var sourceImage = await Image.LoadAsync<Rgba32>(inputPath);
                Logger.Info($"Loaded image: {sourceImage.Width}x{sourceImage.Height}");

                // Генерируем мипмапы (для подсчета уровней и опционального сохранения)
                Logger.Info("Generating mipmaps...");
                var mipmaps = _mipGenerator.GenerateMipmaps(sourceImage, mipProfile);
                Logger.Info($"Generated {mipmaps.Count} mipmap levels");

                // Выводим информацию о каждом мипмапе
                for (int i = 0; i < mipmaps.Count; i++) {
                    Logger.Info($"Mipmap level {i}: {mipmaps[i].Width}x{mipmaps[i].Height}");
                }

                // Применяем Toksvig коррекцию если включена (для gloss/roughness текстур)
                Logger.Info($"=== TOKSVIG CHECK ===");
                Logger.Info($"  toksvigSettings != null: {toksvigSettings != null}");
                if (toksvigSettings != null) {
                    Logger.Info($"  toksvigSettings.Enabled: {toksvigSettings.Enabled}");
                    Logger.Info($"  toksvigSettings.CompositePower: {toksvigSettings.CompositePower}");
                    Logger.Info($"  toksvigSettings.NormalMapPath: {toksvigSettings.NormalMapPath ?? "(null - auto)"}");
                }
                Logger.Info($"  mipProfile.TextureType: {mipProfile.TextureType}");
                Logger.Info($"  Is Gloss or Roughness: {mipProfile.TextureType == TextureType.Gloss || mipProfile.TextureType == TextureType.Roughness}");

                if (toksvigSettings != null && toksvigSettings.Enabled &&
                    (mipProfile.TextureType == TextureType.Gloss || mipProfile.TextureType == TextureType.Roughness)) {

                    Logger.Info("=== ПРИМЕНЯЕМ TOKSVIG КОРРЕКЦИЮ ===");

                    try {
                        // Ищем normal map если путь не указан
                        string? normalMapPath = toksvigSettings.NormalMapPath;
                        if (string.IsNullOrEmpty(normalMapPath)) {
                            Logger.Info("Путь к normal map не указан, выполняем автоматический поиск...");
                            normalMapPath = _normalMapMatcher.FindNormalMapAuto(inputPath, validateDimensions: true);
                        }

                        if (string.IsNullOrEmpty(normalMapPath)) {
                            Logger.Warn("Normal map не найдена, пропускаем Toksvig коррекцию");
                        } else {
                            // Загружаем normal map
                            using var normalMapImage = await Image.LoadAsync<Rgba32>(normalMapPath);
                            Logger.Info($"Загружена normal map: {normalMapPath} ({normalMapImage.Width}x{normalMapImage.Height})");

                            // Определяем, gloss или roughness
                            bool isGloss = mipProfile.TextureType == TextureType.Gloss;
                            if (isGloss) {
                                var detectedType = _normalMapMatcher.IsGlossByName(inputPath);
                                if (detectedType.HasValue) {
                                    isGloss = detectedType.Value;
                                    Logger.Info($"Определён тип текстуры по имени: {(isGloss ? "Gloss" : "Roughness")}");
                                }
                            }

                            // ЕСЛИ Keep Temporal Mipmaps включен - сохраняем ОРИГИНАЛЬНЫЕ мипмапы БЕЗ Toksvig
                            if (!compressionSettings.RemoveTemporaryMipmaps) {
                                await SaveDebugMipmapsAsync(inputPath, mipmaps, "_original");
                            }

                            // Применяем Toksvig
                            var correctedMipmaps = _toksvigProcessor.ApplyToksvigCorrection(
                                mipmaps,
                                normalMapImage,
                                toksvigSettings,
                                isGloss
                            );

                            // Освобождаем старые мипмапы
                            foreach (var mip in mipmaps) {
                                mip.Dispose();
                            }

                            mipmaps = correctedMipmaps;
                            result.ToksvigApplied = true;
                            result.NormalMapUsed = normalMapPath;

                            Logger.Info("Toksvig коррекция успешно применена");

                            // ЕСЛИ Keep Temporal Mipmaps включен - сохраняем СКОРРЕКТИРОВАННЫЕ мипмапы ПОСЛЕ Toksvig
                            if (!compressionSettings.RemoveTemporaryMipmaps) {
                                await SaveDebugMipmapsAsync(inputPath, mipmaps, "_toksvig");
                            }
                        }
                    } catch (Exception ex) {
                        Logger.Error(ex, "Ошибка при применении Toksvig коррекции");
                        result.ToksvigApplied = false;
                        // Продолжаем с оригинальными мипмапами
                    }
                }

                var fileName = Path.GetFileNameWithoutExtension(inputPath);

                // ВСЕГДА сохраняем все мипмапы во временную директорию для toktx
                var tempMipmapDir = Path.Combine(Path.GetTempPath(), "TexTool_Mipmaps", Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempMipmapDir);
                Logger.Info($"Создана временная директория для мипмапов: {tempMipmapDir}");

                var tempMipmapPaths = new List<string>();

                try {
                    // Сохраняем все мипмапы как временные PNG для toktx
                    Logger.Info($"Сохраняем {mipmaps.Count} мипмапов для toktx...");
                    for (int i = 0; i < mipmaps.Count; i++) {
                        var mipPath = Path.Combine(tempMipmapDir, $"{fileName}_mip{i}.png");
                        Logger.Info($"Сохраняем mipmap level {i} ({mipmaps[i].Width}x{mipmaps[i].Height}) в: {mipPath}");

                        await mipmaps[i].SaveAsPngAsync(mipPath);

                        // Проверяем создание файла
                        if (File.Exists(mipPath)) {
                            var fileInfo = new FileInfo(mipPath);
                            Logger.Info($"✓ Mipmap {i} сохранён ({fileInfo.Length} bytes)");
                            tempMipmapPaths.Add(mipPath);
                        } else {
                            Logger.Error($"✗ Не удалось сохранить mipmap {i}!");
                            throw new Exception($"Failed to save mipmap level {i}");
                        }
                    }

                    // Опционально: копируем мипмапы в директорию пользователя
                    if (saveSeparateMipmaps && !string.IsNullOrEmpty(mipmapOutputDir)) {
                        Logger.Info($"Копируем {mipmaps.Count} мипмапов в {mipmapOutputDir}");
                        Directory.CreateDirectory(mipmapOutputDir);

                        for (int i = 0; i < tempMipmapPaths.Count; i++) {
                            var destPath = Path.Combine(mipmapOutputDir, $"{fileName}_mip{i}.png");
                            File.Copy(tempMipmapPaths[i], destPath, overwrite: true);
                            Logger.Info($"✓ Mipmap {i} скопирован в {destPath}");
                        }

                        result.MipmapsSavedPath = mipmapOutputDir;
                        Logger.Info($"Мипмапы сохранены в {mipmapOutputDir}");
                    }

                    // Упаковываем мипмапы в KTX2 используя toktx
                    Logger.Info("=== PACKING TO KTX2 WITH TOKTX ===");
                    Logger.Info($"  Mipmaps: {tempMipmapPaths.Count}");
                    Logger.Info($"  Output: {outputPath}");
                    Logger.Info($"  Compression: {compressionSettings.CompressionFormat}");

                    var toktxResult = await _toktxWrapper.PackMipmapsAsync(
                        tempMipmapPaths,
                        outputPath,
                        compressionSettings
                    );

                    if (!toktxResult.Success) {
                        throw new Exception($"toktx packing failed: {toktxResult.Error}");
                    }

                    result.Success = true;
                    result.BasisOutput = toktxResult.Output;
                    result.MipLevels = mipmaps.Count;

                    Logger.Info($"=== KTX2 PACKING SUCCESS ===");
                    Logger.Info($"  Output: {outputPath}");
                    Logger.Info($"  File size: {toktxResult.OutputFileSize} bytes");
                    Logger.Info($"  Mip levels: {mipmaps.Count}");

                } finally {
                    // Обрабатываем временные мипмапы
                    try {
                        if (Directory.Exists(tempMipmapDir)) {
                            // Если Keep Temporal Mipmaps включен - копируем в debug папку
                            if (!compressionSettings.RemoveTemporaryMipmaps) {
                                // Создаём папку "mipmaps" рядом с текстурой
                                var textureDir = Path.GetDirectoryName(inputPath);
                                if (!string.IsNullOrEmpty(textureDir)) {
                                    var debugMipmapDir = Path.Combine(textureDir, "mipmaps");
                                    Directory.CreateDirectory(debugMipmapDir);

                                    // Копируем все мипмапы из temp в debug папку
                                    var tempFiles = Directory.GetFiles(tempMipmapDir, "*.png");
                                    foreach (var tempFile in tempFiles) {
                                        var tempFileName = Path.GetFileName(tempFile);
                                        var debugPath = Path.Combine(debugMipmapDir, tempFileName);
                                        File.Copy(tempFile, debugPath, overwrite: true);
                                    }

                                    Logger.Info($"✓ Debug mipmaps сохранены в: {debugMipmapDir} ({tempFiles.Length} файлов)");
                                }
                            }

                            // Всегда удаляем временную директорию (даже если копировали)
                            Directory.Delete(tempMipmapDir, recursive: true);
                            Logger.Info($"Временная директория удалена: {tempMipmapDir}");
                        }
                    } catch (Exception ex) {
                        Logger.Warn($"Не удалось обработать временную директорию: {ex.Message}");
                    }
                }

                Logger.Info($"Conversion successful: {outputPath}");

                // Освобождаем память
                foreach (var mip in mipmaps) {
                    mip.Dispose();
                }
            } catch (Exception ex) {
                Logger.Error(ex, $"Conversion failed: {inputPath}");
                result.Success = false;
                result.Error = ex.Message;
            }

            result.Duration = DateTime.Now - startTime;
            return result;
        }

        /// <summary>
        /// Сохраняет мипмапы в debug папку для инспекции
        /// </summary>
        /// <param name="inputPath">Путь к исходной текстуре</param>
        /// <param name="mipmaps">Список мипмапов для сохранения</param>
        /// <param name="suffix">Суффикс для имени файла (например "_original" или "_toksvig")</param>
        private async Task SaveDebugMipmapsAsync(string inputPath, List<Image<Rgba32>> mipmaps, string suffix) {
            try {
                var textureDir = Path.GetDirectoryName(inputPath);
                if (string.IsNullOrEmpty(textureDir)) return;

                var fileName = Path.GetFileNameWithoutExtension(inputPath);
                var debugMipmapDir = Path.Combine(textureDir, "mipmaps");
                Directory.CreateDirectory(debugMipmapDir);

                Logger.Info($"Сохраняем {mipmaps.Count} debug mipmaps{suffix}...");

                for (int i = 0; i < mipmaps.Count; i++) {
                    var debugPath = Path.Combine(debugMipmapDir, $"{fileName}{suffix}_mip{i}.png");
                    await mipmaps[i].SaveAsPngAsync(debugPath);
                }

                Logger.Info($"✓ Debug mipmaps{suffix} сохранены в: {debugMipmapDir} ({mipmaps.Count} файлов)");
            } catch (Exception ex) {
                Logger.Warn($"Не удалось сохранить debug mipmaps{suffix}: {ex.Message}");
            }
        }

        /// <summary>
        /// Конвертирует только мипмапы без сжатия (для тестирования)
        /// </summary>
        public async Task<List<string>> GenerateMipmapsOnlyAsync(
            string inputPath,
            string outputDirectory,
            MipGenerationProfile profile) {
            using var sourceImage = await Image.LoadAsync<Rgba32>(inputPath);
            var mipmaps = _mipGenerator.GenerateMipmaps(sourceImage, profile);

            var fileName = Path.GetFileNameWithoutExtension(inputPath);
            Directory.CreateDirectory(outputDirectory);

            var outputPaths = new List<string>();

            for (int i = 0; i < mipmaps.Count; i++) {
                var outputPath = Path.Combine(outputDirectory, $"{fileName}_mip{i}.png");
                await mipmaps[i].SaveAsPngAsync(outputPath);
                outputPaths.Add(outputPath);
            }

            foreach (var mip in mipmaps) {
                mip.Dispose();
            }

            return outputPaths;
        }
    }

    /// <summary>
    /// Результат конвертации текстуры
    /// </summary>
    public class ConversionResult {
        /// <summary>
        /// Успешно ли выполнена конвертация
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Путь к входному файлу
        /// </summary>
        public string InputPath { get; set; } = string.Empty;

        /// <summary>
        /// Путь к выходному файлу
        /// </summary>
        public string OutputPath { get; set; } = string.Empty;

        /// <summary>
        /// Сообщение об ошибке (если есть)
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// Вывод basisu encoder
        /// </summary>
        public string? BasisOutput { get; set; }

        /// <summary>
        /// Количество уровней мипмапов
        /// </summary>
        public int MipLevels { get; set; }

        /// <summary>
        /// Путь к директории с сохраненными отдельными мипмапами (если применимо)
        /// </summary>
        public string? MipmapsSavedPath { get; set; }

        /// <summary>
        /// Длительность конвертации
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// Была ли применена Toksvig коррекция
        /// </summary>
        public bool ToksvigApplied { get; set; }

        /// <summary>
        /// Путь к использованной normal map (если Toksvig применён)
        /// </summary>
        public string? NormalMapUsed { get; set; }
    }
}
