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
            // basisu использует многопоточность по умолчанию
            // Опция -no_multithreading отключает её
            if (!settings.UseMultithreading) {
                args.Add("-no_multithreading");
            }
            // Примечание: basisu не поддерживает явное указание количества потоков через -max_threads
            // Он автоматически использует все доступные процессоры

            // Перцептивный режим
            // basisu по умолчанию использует perceptual режим для фотографического контента
            // Опция -linear отключает perceptual режим для не-фотографического контента
            if (!settings.PerceptualMode && settings.CompressionFormat == CompressionFormat.ETC1S) {
                args.Add("-linear");
            }

            // Раздельное сжатие альфа-канала
            // Примечание: опция -separate_rg_to_color_alpha не найдена в текущей документации basisu
            // Возможно была удалена или переименована. Закомментировано для совместимости.
            // if (settings.SeparateAlpha) {
            //     args.Add("-separate_rg_to_color_alpha");
            // }

            // Масштаб мипмапов
            if (settings.MipScale != 1.0f) {
                args.Add($"-mip_scale {settings.MipScale}");
            }

            // Минимальный размер мипмапа
            // Примечание: опция -mip_smallest не подтверждена в официальной документации
            // Оставлена для возможной совместимости со старыми версиями basisu
            if (settings.MipSmallestDimension > 1) {
                args.Add($"-mip_smallest {settings.MipSmallestDimension}");
            }

            // SSE4.1 - это опция компиляции, а не runtime флаг
            // basisu автоматически использует SSE4.1 если был скомпилирован с поддержкой
            // Нет runtime флага для отключения SSE

            // OpenCL
            if (settings.UseOpenCL) {
                args.Add("-opencl");
            }

            // KTX2 Supercompression
            // Примечание: В basisu нет явных флагов -ktx2_zstd/-ktx2_zlib
            // Zstandard supercompression включается автоматически для KTX2 UASTC
            // Для ETC1S можно использовать только базовое сжатие
            // Флаг для отключения zstd: используем старый формат или явно не включаем
            if (settings.OutputFormat == OutputFormat.KTX2) {
                // Zstandard включен по умолчанию в современных версиях basisu для UASTC KTX2
                // Явного флага для включения нет, он активируется автоматически
                // Для совместимости просто не добавляем никаких специальных флагов
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
