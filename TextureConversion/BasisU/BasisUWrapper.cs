using System.Diagnostics;
using System.IO;
using System.Text;
using AssetProcessor.TextureConversion.Core;

namespace AssetProcessor.TextureConversion.BasisU {
    /// <summary>
    /// Обертка для basisu CLI encoder
    /// Использует командную строку для вызова basisu
    /// </summary>
    public class BasisUWrapper {
        private readonly string _basisuExecutablePath;

        /// <summary>
        /// Конструктор
        /// </summary>
        /// <param name="basisuExecutablePath">Путь к исполняемому файлу basisu (или просто "basisu" если в PATH)</param>
        public BasisUWrapper(string basisuExecutablePath = "basisu") {
            _basisuExecutablePath = basisuExecutablePath;
        }

        /// <summary>
        /// Проверяет доступность basisu
        /// </summary>
        public async Task<bool> IsAvailableAsync() {
            try {
                var process = new Process {
                    StartInfo = new ProcessStartInfo {
                        FileName = _basisuExecutablePath,
                        Arguments = "-version",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            } catch {
                return false;
            }
        }

        /// <summary>
        /// Кодирует изображение в Basis Universal формат
        /// </summary>
        /// <param name="inputPath">Путь к входному файлу (PNG, JPG, TGA, BMP)</param>
        /// <param name="outputPath">Путь к выходному файлу (.basis или .ktx2)</param>
        /// <param name="settings">Настройки сжатия</param>
        /// <param name="mipmapPaths">Пути к предгенерированным мипмапам (опционально)</param>
        public async Task<BasisUResult> EncodeAsync(
            string inputPath,
            string outputPath,
            CompressionSettings settings,
            List<string>? mipmapPaths = null) {
            var args = BuildArguments(inputPath, outputPath, settings, mipmapPaths);

            var process = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName = _basisuExecutablePath,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (sender, e) => {
                if (e.Data != null) outputBuilder.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (sender, e) => {
                if (e.Data != null) errorBuilder.AppendLine(e.Data);
            };

            var startTime = DateTime.Now;
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();
            var duration = DateTime.Now - startTime;

            return new BasisUResult {
                Success = process.ExitCode == 0,
                ExitCode = process.ExitCode,
                Output = outputBuilder.ToString(),
                Error = errorBuilder.ToString(),
                Duration = duration,
                OutputPath = outputPath
            };
        }

        /// <summary>
        /// Строит аргументы командной строки для basisu
        /// </summary>
        private string BuildArguments(
            string inputPath,
            string outputPath,
            CompressionSettings settings,
            List<string>? mipmapPaths) {
            var args = new List<string>();

            // Входной файл
            args.Add($"\"{inputPath}\"");

            // Выходной файл
            args.Add($"-output_file \"{outputPath}\"");

            // Формат выходного файла
            if (settings.OutputFormat == OutputFormat.KTX2) {
                args.Add("-ktx2");
            }

            // Формат сжатия
            if (settings.CompressionFormat == CompressionFormat.UASTC) {
                args.Add("-uastc");
                args.Add($"-uastc_level {settings.UASTCQuality}");

                if (settings.UseUASTCRDO) {
                    args.Add($"-uastc_rdo_l {settings.UASTCRDOQuality}");
                }
            } else {
                // ETC1S по умолчанию
                args.Add($"-q {settings.QualityLevel}");
            }

            // Мипмапы
            // Примечание: basisu не поддерживает передачу предгенерированных мипмапов как отдельных файлов
            // Вместо этого он должен генерировать их сам при кодировании
            if (settings.GenerateMipmaps) {
                args.Add("-mipmap");
            }

            // Если были предгенерированные мипмапы, они используются только для отдельного сохранения
            // basisu всё равно генерирует свои мипмапы при сжатии

            // Многопоточность
            if (settings.UseMultithreading) {
                if (settings.ThreadCount > 0) {
                    args.Add($"-max_threads {settings.ThreadCount}");
                }
            } else {
                args.Add("-max_threads 1");
            }

            // Перцептивный режим
            if (settings.PerceptualMode && settings.CompressionFormat == CompressionFormat.ETC1S) {
                args.Add("-perceptual");
            }

            // Раздельное сжатие альфа-канала
            if (settings.SeparateAlpha) {
                args.Add("-separate_rg_to_color_alpha");
            }

            // Масштаб мипмапов
            if (settings.MipScale != 1.0f) {
                args.Add($"-mip_scale {settings.MipScale}");
            }

            // Минимальный размер мипмапа
            if (settings.MipSmallestDimension > 1) {
                args.Add($"-mip_smallest {settings.MipSmallestDimension}");
            }

            return string.Join(" ", args);
        }

        /// <summary>
        /// Пакетное кодирование множества файлов
        /// </summary>
        public async Task<List<BasisUResult>> EncodeBatchAsync(
            List<string> inputPaths,
            string outputDirectory,
            CompressionSettings settings,
            IProgress<(int current, int total, string fileName)>? progress = null,
            CancellationToken cancellationToken = default) {
            var results = new List<BasisUResult>();
            var semaphore = new SemaphoreSlim(
                settings.UseMultithreading ? (settings.ThreadCount > 0 ? settings.ThreadCount : Environment.ProcessorCount) : 1
            );

            var tasks = inputPaths.Select(async (inputPath, index) => {
                await semaphore.WaitAsync(cancellationToken);
                try {
                    var fileName = Path.GetFileNameWithoutExtension(inputPath);
                    var extension = settings.OutputFormat == OutputFormat.KTX2 ? ".ktx2" : ".basis";
                    var outputPath = Path.Combine(outputDirectory, fileName + extension);

                    progress?.Report((index + 1, inputPaths.Count, Path.GetFileName(inputPath)));

                    var result = await EncodeAsync(inputPath, outputPath, settings);
                    return result;
                } finally {
                    semaphore.Release();
                }
            });

            results = (await Task.WhenAll(tasks)).ToList();
            return results;
        }
    }

    /// <summary>
    /// Результат кодирования Basis Universal
    /// </summary>
    public class BasisUResult {
        /// <summary>
        /// Успешно ли выполнено кодирование
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Код выхода процесса
        /// </summary>
        public int ExitCode { get; set; }

        /// <summary>
        /// Вывод процесса
        /// </summary>
        public string Output { get; set; } = string.Empty;

        /// <summary>
        /// Ошибки процесса
        /// </summary>
        public string Error { get; set; } = string.Empty;

        /// <summary>
        /// Длительность операции
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// Путь к выходному файлу
        /// </summary>
        public string OutputPath { get; set; } = string.Empty;
    }
}
