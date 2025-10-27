using System.Diagnostics;
using System.IO;
using System.Text;
using AssetProcessor.TextureConversion.Core;
using NLog;

namespace AssetProcessor.TextureConversion.BasisU {
    /// <summary>
    /// Обёртка для toktx - инструмента KTX-Software для упаковки текстур в KTX2 формат
    /// </summary>
    public class ToktxWrapper {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly string _toktxExecutablePath;

        public ToktxWrapper(string toktxExecutablePath = "toktx") {
            _toktxExecutablePath = toktxExecutablePath;
        }

        /// <summary>
        /// Проверяет доступность toktx
        /// </summary>
        public async Task<bool> IsAvailableAsync() {
            try {
                var psi = new ProcessStartInfo {
                    FileName = _toktxExecutablePath,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return false;

                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            } catch {
                return false;
            }
        }

        /// <summary>
        /// Упаковывает набор мипмапов в KTX2 файл с Basis Universal сжатием
        /// </summary>
        /// <param name="mipmapPaths">Пути к мипмапам (от mip0 до mipN)</param>
        /// <param name="outputPath">Путь к выходному .ktx2 файлу</param>
        /// <param name="settings">Настройки сжатия</param>
        public async Task<ToktxResult> PackMipmapsAsync(
            List<string> mipmapPaths,
            string outputPath,
            CompressionSettings settings) {

            var result = new ToktxResult {
                OutputPath = outputPath
            };

            try {
                if (mipmapPaths.Count == 0) {
                    throw new ArgumentException("No mipmaps provided", nameof(mipmapPaths));
                }

                // Проверяем существование всех мипмапов
                foreach (var path in mipmapPaths) {
                    if (!File.Exists(path)) {
                        throw new FileNotFoundException($"Mipmap not found: {path}");
                    }
                }

                Logger.Info($"=== TOKTX PACKING START ===");
                Logger.Info($"  Mipmaps count: {mipmapPaths.Count}");
                Logger.Info($"  Output: {outputPath}");
                Logger.Info($"  Compression format: {settings.CompressionFormat}");

                // КРИТИЧЕСКИ ВАЖНО: Удаляем существующий выходной файл!
                // toktx пытается открыть его как входное изображение если он существует
                if (File.Exists(outputPath)) {
                    Logger.Info($"Удаляем существующий выходной файл: {outputPath}");
                    try {
                        File.Delete(outputPath);
                    } catch (Exception ex) {
                        Logger.Error($"Не удалось удалить файл {outputPath}: {ex.Message}");
                        return new ToktxResult {
                            Success = false,
                            Error = $"Cannot delete existing output file: {outputPath}. {ex.Message}"
                        };
                    }
                }

                // Создаём выходную директорию
                var outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir)) {
                    Directory.CreateDirectory(outputDir);
                }

                // Собираем аргументы командной строки
                var args = BuildArguments(mipmapPaths, outputPath, settings);

                Logger.Info($"=== TOKTX COMMAND ===");
                Logger.Info($"  Executable: {_toktxExecutablePath}");
                Logger.Info($"  Arguments count: {args.Count}");
                foreach (var arg in args) {
                    Logger.Info($"    {arg}");
                }

                // Запускаем toktx с ArgumentList для правильной обработки путей с пробелами
                var psi = new ProcessStartInfo {
                    FileName = _toktxExecutablePath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Directory.GetCurrentDirectory()
                };

                // Используем ArgumentList вместо Arguments для правильной обработки кавычек
                foreach (var arg in args) {
                    psi.ArgumentList.Add(arg);
                }

                using var process = Process.Start(psi);
                if (process == null) {
                    throw new Exception("Failed to start toktx process");
                }

                var output = new StringBuilder();
                var error = new StringBuilder();

                process.OutputDataReceived += (s, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) {
                        output.AppendLine(e.Data);
                        Logger.Info($"[toktx] {e.Data}");
                    }
                };

                process.ErrorDataReceived += (s, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) {
                        error.AppendLine(e.Data);
                        Logger.Error($"[toktx ERROR] {e.Data}");
                    }
                };

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync();

                result.Output = output.ToString();
                result.ErrorOutput = error.ToString();

                if (process.ExitCode != 0) {
                    result.Success = false;
                    result.Error = $"toktx exited with code {process.ExitCode}. Error: {error}";
                    Logger.Error($"toktx failed with exit code {process.ExitCode}");
                    Logger.Error($"Error output: {error}");
                    return result;
                }

                // Проверяем создание выходного файла
                if (!File.Exists(outputPath)) {
                    result.Success = false;
                    result.Error = "toktx completed but output file was not created";
                    Logger.Error("Output file not found after toktx completion");
                    return result;
                }

                var fileInfo = new FileInfo(outputPath);
                result.Success = true;
                result.OutputFileSize = fileInfo.Length;

                Logger.Info($"=== TOKTX PACKING SUCCESS ===");
                Logger.Info($"  Output file: {outputPath}");
                Logger.Info($"  File size: {fileInfo.Length} bytes");

                return result;

            } catch (Exception ex) {
                Logger.Error(ex, "toktx packing failed");
                result.Success = false;
                result.Error = ex.Message;
                return result;
            }
        }

        /// <summary>
        /// Собирает аргументы командной строки для toktx
        /// </summary>
        private List<string> BuildArguments(
            List<string> mipmapPaths,
            string outputPath,
            CompressionSettings settings) {

            var args = new List<string>();

            // Compression format
            if (settings.CompressionFormat == CompressionFormat.ETC1S) {
                args.Add("--bcmp");
                args.Add(settings.QualityLevel.ToString());
            } else if (settings.CompressionFormat == CompressionFormat.UASTC) {
                args.Add("--uastc");
                args.Add(settings.UASTCQuality.ToString());

                if (settings.UseUASTCRDO) {
                    args.Add("--uastc_rdo_l");
                    args.Add(FormattableString.Invariant($"{settings.UASTCRDOQuality}"));
                }
            }

            // Color space
            if (settings.ForceLinearColorSpace) {
                args.Add("--linear");
            } else if (settings.PerceptualMode && settings.CompressionFormat == CompressionFormat.ETC1S) {
                args.Add("--srgb");
            }

            // Multithreading
            if (settings.UseMultithreading && settings.ThreadCount > 0) {
                args.Add("--threads");
                args.Add(settings.ThreadCount.ToString());
            }

            // Output file - ПОСЛЕ всех флагов!
            // ArgumentList сам добавит кавычки при необходимости
            args.Add(outputPath);

            // Input files (mipmaps)
            foreach (var mipmapPath in mipmapPaths) {
                args.Add(mipmapPath);
            }

            return args;
        }
    }

    /// <summary>
    /// Результат работы toktx
    /// </summary>
    public class ToktxResult {
        public bool Success { get; set; }
        public string? OutputPath { get; set; }
        public string? Error { get; set; }
        public string? Output { get; set; }
        public string? ErrorOutput { get; set; }
        public long OutputFileSize { get; set; }
    }
}
