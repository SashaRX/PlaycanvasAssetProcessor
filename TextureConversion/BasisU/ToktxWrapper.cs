using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AssetProcessor.TextureConversion.Core;
using NLog;

namespace AssetProcessor.TextureConversion.BasisU {
    /// <summary>
    /// Обертка для toktx CLI tool из KTX-Software
    /// Используется для упаковки предгенерированных мипмапов в KTX2
    /// </summary>
    public class ToktxWrapper {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly string _toktxExecutablePath;

        /// <summary>
        /// Конструктор
        /// </summary>
        /// <param name="toktxExecutablePath">Путь к исполняемому файлу toktx (или просто "toktx" если в PATH)</param>
        public ToktxWrapper(string toktxExecutablePath = "toktx") {
            _toktxExecutablePath = toktxExecutablePath;
        }

        /// <summary>
        /// Проверяет доступность toktx
        /// </summary>
        public async Task<bool> IsAvailableAsync() {
            try {
                var process = new Process {
                    StartInfo = new ProcessStartInfo {
                        FileName = _toktxExecutablePath,
                        Arguments = "--version",
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
        /// Упаковывает предгенерированные мипмапы в KTX2 файл
        /// </summary>
        /// <param name="mipmapPaths">Список путей к мипмапам (от mip0 до mipN)</param>
        /// <param name="outputPath">Путь к выходному .ktx2 файлу</param>
        /// <param name="settings">Настройки сжатия</param>
        /// <returns>Результат упаковки</returns>
        public async Task<ToktxResult> PackMipmapsAsync(
            List<string> mipmapPaths,
            string outputPath,
            CompressionSettings settings) {

            if (mipmapPaths == null || mipmapPaths.Count == 0) {
                return new ToktxResult {
                    Success = false,
                    Error = "No mipmap paths provided"
                };
            }

            // Проверяем существование файлов
            foreach (var path in mipmapPaths) {
                if (!File.Exists(path)) {
                    return new ToktxResult {
                        Success = false,
                        Error = $"Mipmap file not found: {path}"
                    };
                }
            }

            var args = BuildArguments(mipmapPaths, outputPath, settings);

            // Логируем полную команду
            Logger.Info($"=== TOKTX COMMAND ===");
            Logger.Info($"  Executable: {_toktxExecutablePath}");
            Logger.Info($"  Arguments: {args}");
            Logger.Info($"  Input mipmaps: {mipmapPaths.Count} levels");

            var process = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName = _toktxExecutablePath,
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

            var result = new ToktxResult {
                Success = process.ExitCode == 0,
                ExitCode = process.ExitCode,
                Output = outputBuilder.ToString(),
                Error = errorBuilder.ToString(),
                Duration = duration,
                OutputPath = outputPath,
                MipLevels = mipmapPaths.Count
            };

            if (result.Success) {
                Logger.Info($"toktx успешно упаковал {mipmapPaths.Count} мипмапов в {outputPath}");
            } else {
                Logger.Error($"toktx завершился с ошибкой (код {result.ExitCode}): {result.Error}");
            }

            return result;
        }

        /// <summary>
        /// Строит аргументы командной строки для toktx
        /// </summary>
        private string BuildArguments(
            List<string> mipmapPaths,
            string outputPath,
            CompressionSettings settings) {

            var args = new List<string>();

            // ВАЖНО: Все флаги должны идти ДО имени выходного файла!
            // Синтаксис: toktx [options] <outfile> <infile1> <infile2> ...

            // Формат сжатия
            // toktx использует --bcmp для ETC1S/BasisLZ и --uastc для UASTC
            if (settings.CompressionFormat == CompressionFormat.UASTC) {
                args.Add("--uastc");
                args.Add($"{settings.UASTCQuality}");

                // UASTC RDO (Rate-Distortion Optimization)
                if (settings.UseUASTCRDO) {
                    args.Add(FormattableString.Invariant($"--uastc_rdo_l {settings.UASTCRDOQuality}"));
                }
            } else {
                // ETC1S (BasisLZ)
                args.Add("--bcmp");
                args.Add(settings.QualityLevel.ToString());
            }

            // KTX2 формат включается автоматически при использовании --bcmp или --uastc
            // Не нужен флаг --t2

            // Supercompression для KTX2
            // По умолчанию toktx применяет zstd для Basis, отключаем только если нужно
            if (settings.KTX2Supercompression == KTX2SupercompressionType.None) {
                Logger.Info("Supercompression отключен (None)");
                // В новых версиях можно попробовать --no_zcmp, но это не обязательно
            }

            // Color space
            if (settings.ForceLinearColorSpace) {
                args.Add("--linear");
            } else if (settings.PerceptualMode && settings.CompressionFormat == CompressionFormat.ETC1S) {
                args.Add("--srgb");
            }

            // Многопоточность
            if (settings.UseMultithreading && settings.ThreadCount > 0) {
                args.Add($"--threads {settings.ThreadCount}");
            }

            // Выходной файл (после всех флагов!)
            args.Add($"\"{outputPath}\"");

            // Входные файлы (мипмапы в порядке от mip0 до mipN)
            foreach (var mipmapPath in mipmapPaths) {
                args.Add($"\"{mipmapPath}\"");
            }

            return string.Join(" ", args);
        }

        /// <summary>
        /// Получает версию toktx для диагностики
        /// </summary>
        public async Task<string> GetVersionAsync() {
            try {
                var process = new Process {
                    StartInfo = new ProcessStartInfo {
                        FileName = _toktxExecutablePath,
                        Arguments = "--version",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                return !string.IsNullOrEmpty(output) ? output.Trim() : error.Trim();
            } catch (Exception ex) {
                return $"Error: {ex.Message}";
            }
        }
    }

    /// <summary>
    /// Результат упаковки в KTX2 с помощью toktx
    /// </summary>
    public class ToktxResult {
        /// <summary>
        /// Успешно ли выполнена упаковка
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

        /// <summary>
        /// Количество упакованных уровней мипмапов
        /// </summary>
        public int MipLevels { get; set; }
    }
}
