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

        public TextureConversionPipeline(string? basisuExecutablePath = null) {
            _mipGenerator = new MipGenerator();
            _basisWrapper = new BasisUWrapper(basisuExecutablePath ?? "basisu");
        }

        /// <summary>
        /// Конвертирует текстуру в Basis Universal формат с генерацией мипмапов
        /// </summary>
        /// <param name="inputPath">Путь к входной текстуре</param>
        /// <param name="outputPath">Путь к выходному файлу (.basis или .ktx2)</param>
        /// <param name="mipProfile">Профиль генерации мипмапов</param>
        /// <param name="compressionSettings">Настройки сжатия</param>
        /// <param name="saveSeparateMipmaps">Сохранить ли мипмапы отдельно</param>
        /// <param name="mipmapOutputDir">Директория для сохранения отдельных мипмапов</param>
        public async Task<ConversionResult> ConvertTextureAsync(
            string inputPath,
            string outputPath,
            MipGenerationProfile mipProfile,
            CompressionSettings compressionSettings,
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

                // Генерируем мипмапы
                Logger.Info("Generating mipmaps...");
                var mipmaps = _mipGenerator.GenerateMipmaps(sourceImage, mipProfile);
                Logger.Info($"Generated {mipmaps.Count} mipmap levels");

                List<string>? mipmapPaths = null;

                // Сохраняем мипмапы во временные файлы для basisu
                var tempDir = Path.Combine(Path.GetTempPath(), $"basisu_mips_{Guid.NewGuid()}");
                Directory.CreateDirectory(tempDir);

                try {
                    mipmapPaths = new List<string>();
                    var fileName = Path.GetFileNameWithoutExtension(inputPath);

                    for (int i = 0; i < mipmaps.Count; i++) {
                        var tempPath = Path.Combine(tempDir, $"{fileName}_mip{i}.png");
                        await mipmaps[i].SaveAsPngAsync(tempPath);
                        mipmapPaths.Add(tempPath);
                    }

                    // Опционально: сохраняем мипмапы отдельно для будущего стриминга
                    if (saveSeparateMipmaps && !string.IsNullOrEmpty(mipmapOutputDir)) {
                        Logger.Info($"Saving separate mipmaps to {mipmapOutputDir}");
                        Directory.CreateDirectory(mipmapOutputDir);

                        for (int i = 0; i < mipmaps.Count; i++) {
                            var mipPath = Path.Combine(mipmapOutputDir, $"{fileName}_mip{i}.png");
                            await mipmaps[i].SaveAsPngAsync(mipPath);
                        }
                    }

                    // Кодируем в Basis Universal
                    Logger.Info("Encoding to Basis Universal...");
                    var basisResult = await _basisWrapper.EncodeAsync(
                        mipmapPaths[0], // Базовое изображение
                        outputPath,
                        compressionSettings,
                        mipmapPaths.Count > 1 ? mipmapPaths.Skip(1).ToList() : null
                    );

                    if (!basisResult.Success) {
                        throw new Exception($"Basis encoding failed: {basisResult.Error}");
                    }

                    result.Success = true;
                    result.BasisOutput = basisResult.Output;
                    result.MipLevels = mipmaps.Count;

                    Logger.Info($"Conversion successful: {outputPath}");
                } finally {
                    // Очищаем временные файлы
                    try {
                        Directory.Delete(tempDir, true);
                    } catch (Exception ex) {
                        Logger.Warn($"Failed to delete temp directory: {ex.Message}");
                    }
                }

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
        /// Длительность конвертации
        /// </summary>
        public TimeSpan Duration { get; set; }
    }
}
