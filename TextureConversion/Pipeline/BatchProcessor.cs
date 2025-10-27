using System.IO;
using AssetProcessor.TextureConversion.Core;
using NLog;

namespace AssetProcessor.TextureConversion.Pipeline {
    /// <summary>
    /// Пакетная обработка текстур в директории
    /// </summary>
    public class BatchProcessor {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly TextureConversionPipeline _pipeline;

        public BatchProcessor(TextureConversionPipeline pipeline) {
            _pipeline = pipeline;
        }

        /// <summary>
        /// Обрабатывает все текстуры в директории
        /// </summary>
        /// <param name="inputDirectory">Входная директория</param>
        /// <param name="outputDirectory">Выходная директория</param>
        /// <param name="profileSelector">Функция для выбора профиля на основе имени файла</param>
        /// <param name="compressionSettings">Настройки сжатия</param>
        /// <param name="saveSeparateMipmaps">Сохранять ли отдельные мипмапы</param>
        /// <param name="progress">Прогресс обработки</param>
        /// <param name="maxParallelism">Максимальное количество параллельных задач</param>
        public async Task<BatchResult> ProcessDirectoryAsync(
            string inputDirectory,
            string outputDirectory,
            Func<string, MipGenerationProfile> profileSelector,
            CompressionSettings compressionSettings,
            bool saveSeparateMipmaps = false,
            IProgress<BatchProgress>? progress = null,
            int maxParallelism = 4,
            CancellationToken cancellationToken = default) {
            var batchResult = new BatchResult {
                StartTime = DateTime.Now
            };

            try {
                // Поддерживаемые форматы
                var supportedExtensions = new[] { ".png", ".jpg", ".jpeg", ".tga", ".bmp" };

                // Находим все файлы
                var files = Directory.GetFiles(inputDirectory, "*.*", SearchOption.AllDirectories)
                    .Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLower()))
                    .ToList();

                if (files.Count == 0) {
                    Logger.Warn($"No supported image files found in {inputDirectory}");
                    return batchResult;
                }

                Logger.Info($"Found {files.Count} files to process");
                batchResult.TotalFiles = files.Count;

                // Создаем выходную директорию
                Directory.CreateDirectory(outputDirectory);

                // Создаем семафор для ограничения параллелизма
                var semaphore = new SemaphoreSlim(maxParallelism);

                // Обрабатываем файлы
                var tasks = files.Select(async (filePath, index) => {
                    await semaphore.WaitAsync(cancellationToken);
                    try {
                        var fileName = Path.GetFileName(filePath);
                        var relativePath = Path.GetRelativePath(inputDirectory, filePath);
                        var outputPath = Path.Combine(
                            outputDirectory,
                            Path.ChangeExtension(relativePath, compressionSettings.OutputFormat == OutputFormat.KTX2 ? ".ktx2" : ".basis")
                        );

                        // Создаем поддиректории если нужно
                        var outputDir = Path.GetDirectoryName(outputPath);
                        if (!string.IsNullOrEmpty(outputDir)) {
                            Directory.CreateDirectory(outputDir);
                        }

                        // Выбираем профиль
                        var profile = profileSelector(fileName);

                        // Директория для отдельных мипмапов
                        string? mipmapDir = null;
                        if (saveSeparateMipmaps) {
                            mipmapDir = Path.Combine(outputDirectory, "mipmaps", Path.GetFileNameWithoutExtension(fileName));
                        }

                        // Конвертируем
                        Logger.Info($"[{index + 1}/{files.Count}] Processing: {fileName}");
                        var result = await _pipeline.ConvertTextureAsync(
                            filePath,
                            outputPath,
                            profile,
                            compressionSettings,
                            toksvigSettings: null, // TODO: Add toksvigSettings parameter to batch processor
                            saveSeparateMipmaps,
                            mipmapDir
                        );

                        lock (batchResult) {
                            batchResult.Results.Add(result);
                            if (result.Success) {
                                batchResult.SuccessCount++;
                            } else {
                                batchResult.FailureCount++;
                            }
                        }

                        // Отчет о прогрессе
                        progress?.Report(new BatchProgress {
                            CurrentFile = index + 1,
                            TotalFiles = files.Count,
                            CurrentFileName = fileName,
                            SuccessCount = batchResult.SuccessCount,
                            FailureCount = batchResult.FailureCount
                        });
                    } catch (Exception ex) {
                        Logger.Error(ex, $"Failed to process {filePath}");
                        lock (batchResult) {
                            batchResult.FailureCount++;
                            batchResult.Results.Add(new ConversionResult {
                                InputPath = filePath,
                                Success = false,
                                Error = ex.Message
                            });
                        }
                    } finally {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);

                batchResult.EndTime = DateTime.Now;
                Logger.Info($"Batch processing complete: {batchResult.SuccessCount} succeeded, {batchResult.FailureCount} failed");
            } catch (Exception ex) {
                Logger.Error(ex, "Batch processing failed");
                batchResult.Error = ex.Message;
            }

            return batchResult;
        }

        /// <summary>
        /// Создает селектор профиля на основе имени файла
        /// </summary>
        public static Func<string, MipGenerationProfile> CreateNameBasedProfileSelector() {
            return fileName => {
                var lowerName = fileName.ToLower();

                if (lowerName.Contains("albedo") || lowerName.Contains("diffuse") || lowerName.Contains("basecolor")) {
                    return MipGenerationProfile.CreateDefault(TextureType.Albedo);
                } else if (lowerName.Contains("normal") || lowerName.Contains("norm")) {
                    return MipGenerationProfile.CreateDefault(TextureType.Normal);
                } else if (lowerName.Contains("rough")) {
                    return MipGenerationProfile.CreateDefault(TextureType.Roughness);
                } else if (lowerName.Contains("metal")) {
                    return MipGenerationProfile.CreateDefault(TextureType.Metallic);
                } else if (lowerName.Contains("ao") || lowerName.Contains("occlusion")) {
                    return MipGenerationProfile.CreateDefault(TextureType.AmbientOcclusion);
                } else if (lowerName.Contains("emissive") || lowerName.Contains("emit")) {
                    return MipGenerationProfile.CreateDefault(TextureType.Emissive);
                } else if (lowerName.Contains("gloss")) {
                    return MipGenerationProfile.CreateDefault(TextureType.Gloss);
                } else if (lowerName.Contains("height") || lowerName.Contains("disp")) {
                    return MipGenerationProfile.CreateDefault(TextureType.Height);
                } else {
                    return MipGenerationProfile.CreateDefault(TextureType.Generic);
                }
            };
        }
    }

    /// <summary>
    /// Результат пакетной обработки
    /// </summary>
    public class BatchResult {
        /// <summary>
        /// Время начала
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// Время окончания
        /// </summary>
        public DateTime? EndTime { get; set; }

        /// <summary>
        /// Общее количество файлов
        /// </summary>
        public int TotalFiles { get; set; }

        /// <summary>
        /// Количество успешных конвертаций
        /// </summary>
        public int SuccessCount { get; set; }

        /// <summary>
        /// Количество неудачных конвертаций
        /// </summary>
        public int FailureCount { get; set; }

        /// <summary>
        /// Результаты конвертации
        /// </summary>
        public List<ConversionResult> Results { get; set; } = new();

        /// <summary>
        /// Общая ошибка (если есть)
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// Общая длительность
        /// </summary>
        public TimeSpan Duration => EndTime.HasValue ? EndTime.Value - StartTime : TimeSpan.Zero;
    }

    /// <summary>
    /// Прогресс пакетной обработки
    /// </summary>
    public class BatchProgress {
        /// <summary>
        /// Текущий файл
        /// </summary>
        public int CurrentFile { get; set; }

        /// <summary>
        /// Общее количество файлов
        /// </summary>
        public int TotalFiles { get; set; }

        /// <summary>
        /// Имя текущего файла
        /// </summary>
        public string CurrentFileName { get; set; } = string.Empty;

        /// <summary>
        /// Количество успешных конвертаций
        /// </summary>
        public int SuccessCount { get; set; }

        /// <summary>
        /// Количество неудачных конвертаций
        /// </summary>
        public int FailureCount { get; set; }

        /// <summary>
        /// Процент выполнения (0-100)
        /// </summary>
        public double PercentComplete => TotalFiles > 0 ? (CurrentFile * 100.0 / TotalFiles) : 0;
    }
}
