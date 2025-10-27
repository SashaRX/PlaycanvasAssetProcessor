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
        private readonly BasisUWrapper _basisWrapper;
        private readonly ToksvigProcessor _toksvigProcessor;
        private readonly NormalMapMatcher _normalMapMatcher;

        public TextureConversionPipeline(string? basisuExecutablePath = null) {
            _mipGenerator = new MipGenerator();
            _basisWrapper = new BasisUWrapper(basisuExecutablePath ?? "basisu");
            _toksvigProcessor = new ToksvigProcessor();
            _normalMapMatcher = new NormalMapMatcher();
        }

        /// <summary>
        /// Конвертирует текстуру в Basis Universal формат с генерацией мипмапов
        /// </summary>
        /// <param name="inputPath">Путь к входной текстуре</param>
        /// <param name="outputPath">Путь к выходному файлу (.basis или .ktx2)</param>
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

                // Проверяем доступность basisu
                if (!await _basisWrapper.IsAvailableAsync()) {
                    throw new Exception("basisu executable not found. Please install Basis Universal and add it to PATH.");
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
                        }
                    } catch (Exception ex) {
                        Logger.Error(ex, "Ошибка при применении Toksvig коррекции");
                        result.ToksvigApplied = false;
                        // Продолжаем с оригинальными мипмапами
                    }
                }

                var fileName = Path.GetFileNameWithoutExtension(inputPath);

                // Опционально: сохраняем мипмапы отдельно для будущего стриминга
                if (saveSeparateMipmaps && !string.IsNullOrEmpty(mipmapOutputDir)) {
                    Logger.Info($"Saving {mipmaps.Count} separate mipmaps to {mipmapOutputDir}");
                    Directory.CreateDirectory(mipmapOutputDir);

                    for (int i = 0; i < mipmaps.Count; i++) {
                        var mipPath = Path.Combine(mipmapOutputDir, $"{fileName}_mip{i}.png");
                        Logger.Info($"Saving mipmap level {i} ({mipmaps[i].Width}x{mipmaps[i].Height}) to: {mipPath}");

                        await mipmaps[i].SaveAsPngAsync(mipPath);

                        // Проверяем, что файл действительно создан
                        if (File.Exists(mipPath)) {
                            var fileInfo = new FileInfo(mipPath);
                            Logger.Info($"✓ Mipmap {i} saved successfully ({fileInfo.Length} bytes)");
                        } else {
                            Logger.Error($"✗ Failed to save mipmap {i} - file not found after save!");
                        }
                    }

                    result.MipmapsSavedPath = mipmapOutputDir;
                    Logger.Info($"Successfully saved {mipmaps.Count} mipmap levels to {mipmapOutputDir}");
                }

                // Определяем входной файл для basisu
                string basisInputPath = inputPath;
                bool useTempFile = false;

                // Если применялась Toksvig коррекция, сохраняем базовый уровень (mip0) во временный файл
                // basisu сгенерирует остальные мипмапы из него
                if (toksvigSettings != null && toksvigSettings.Enabled && result.ToksvigApplied) {
                    Logger.Info("Toksvig коррекция применена - сохраняем скорректированный базовый уровень");
                    var tempDir = Path.Combine(Path.GetTempPath(), "TexTool_Toksvig");
                    Directory.CreateDirectory(tempDir);
                    basisInputPath = Path.Combine(tempDir, $"{fileName}_toksvig_temp.png");
                    await mipmaps[0].SaveAsPngAsync(basisInputPath);
                    useTempFile = true;
                    Logger.Info($"Временный файл создан: {basisInputPath}");
                }

                // Кодируем в Basis Universal
                Logger.Info("=== ENCODING TO BASIS UNIVERSAL ===");
                Logger.Info($"  Input path: {basisInputPath}");
                Logger.Info($"  Output path: {outputPath}");
                Logger.Info($"  GenerateMipmaps: {compressionSettings.GenerateMipmaps}");
                Logger.Info($"  CompressionFormat: {compressionSettings.CompressionFormat}");
                Logger.Info($"  OutputFormat: {compressionSettings.OutputFormat}");

                var basisResult = await _basisWrapper.EncodeAsync(
                    basisInputPath,
                    outputPath,
                    compressionSettings,
                    null
                );

                // Удаляем временный файл если использовали
                if (useTempFile && File.Exists(basisInputPath)) {
                    try {
                        File.Delete(basisInputPath);
                        Logger.Info("Временный файл удалён");
                    } catch (Exception ex) {
                        Logger.Warn($"Не удалось удалить временный файл: {ex.Message}");
                    }
                }

                if (!basisResult.Success) {
                    throw new Exception($"Basis encoding failed: {basisResult.Error}");
                }

                result.Success = true;
                result.BasisOutput = basisResult.Output;
                result.MipLevels = mipmaps.Count;

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
