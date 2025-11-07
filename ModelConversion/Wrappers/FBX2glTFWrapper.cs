using System.Diagnostics;
using System.IO;
using System.Text;
using NLog;

namespace AssetProcessor.ModelConversion.Wrappers {
    /// <summary>
    /// Wrapper для FBX2glTF CLI tool
    /// Конвертирует FBX файлы в glTF/GLB формат
    /// </summary>
    public class FBX2glTFWrapper {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly string _executablePath;

        public FBX2glTFWrapper(string? executablePath = null) {
            _executablePath = executablePath ?? "FBX2glTF-windows-x64.exe";
        }

        /// <summary>
        /// Проверяет доступность FBX2glTF
        /// </summary>
        public async Task<bool> IsAvailableAsync() {
            try {
                Logger.Info($"Checking FBX2glTF availability at: {_executablePath}");

                // Проверка существования файла
                if (!File.Exists(_executablePath)) {
                    Logger.Warn($"FBX2glTF executable not found at: {_executablePath}");
                    return false;
                }

                var startInfo = new ProcessStartInfo {
                    FileName = _executablePath,
                    Arguments = "--help",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) {
                    Logger.Warn("Failed to start FBX2glTF process");
                    return false;
                }

                await process.WaitForExitAsync();

                if (process.ExitCode == 0) {
                    Logger.Info("FBX2glTF is available and working");
                    return true;
                } else {
                    Logger.Warn($"FBX2glTF returned non-zero exit code: {process.ExitCode}");
                    return false;
                }
            } catch (Exception ex) {
                Logger.Error(ex, $"Failed to check FBX2glTF availability at: {_executablePath}");
                return false;
            }
        }

        /// <summary>
        /// Конвертирует FBX в GLB
        /// </summary>
        /// <param name="inputPath">Путь к FBX файлу</param>
        /// <param name="outputPath">Путь к выходному GLB (без расширения)</param>
        /// <param name="excludeTextures">Исключить текстуры (экспортировать только геометрию)</param>
        /// <returns>Результат конвертации</returns>
        public async Task<ConversionResult> ConvertToGlbAsync(string inputPath, string outputPath, bool excludeTextures = false) {
            var result = new ConversionResult();

            try {
                Logger.Info($"FBX2glTF: Converting {inputPath} to GLB (exclude textures: {excludeTextures})");

                // Аргументы: --binary всегда (GLB формат), --separate-textures для исключения текстур
                var arguments = $"--binary --input \"{inputPath}\" --output \"{outputPath}\"";

                if (excludeTextures) {
                    // --separate-textures: текстуры НЕ встраиваются в GLB, остаются внешними файлами
                    arguments += " --separate-textures";
                    Logger.Info("FBX2glTF: Using --separate-textures flag (textures NOT embedded in GLB)");
                } else {
                    Logger.Info("FBX2glTF: Textures will be embedded in GLB");
                }

                Logger.Debug($"FBX2glTF command: {_executablePath} {arguments}");

                var startInfo = new ProcessStartInfo {
                    FileName = _executablePath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                using var process = Process.Start(startInfo);
                if (process == null) {
                    throw new Exception("Failed to start FBX2glTF process");
                }

                process.OutputDataReceived += (sender, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) {
                        outputBuilder.AppendLine(e.Data);
                        Logger.Debug($"FBX2glTF output: {e.Data}");
                    }
                };

                process.ErrorDataReceived += (sender, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) {
                        errorBuilder.AppendLine(e.Data);
                        Logger.Debug($"FBX2glTF error: {e.Data}");
                    }
                };

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync();

                result.Output = outputBuilder.ToString();
                result.Error = errorBuilder.ToString();
                result.ExitCode = process.ExitCode;

                // Логируем вывод FBX2glTF
                Logger.Info($"FBX2glTF stdout (length: {result.Output?.Length ?? 0}):\n{result.Output ?? "(empty)"}");
                Logger.Info($"FBX2glTF stderr (length: {result.Error?.Length ?? 0}):\n{result.Error ?? "(empty)"}");

                if (process.ExitCode == 0) {
                    // Проверяем что GLB файл создан (всегда используем --binary)
                    var glbPath = outputPath + ".glb";

                    // Логируем файлы в выходной директории для диагностики
                    var outputDir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(outputDir) && Directory.Exists(outputDir)) {
                        var files = Directory.GetFiles(outputDir);
                        Logger.Info($"Files in output directory {outputDir}: {string.Join(", ", files.Select(Path.GetFileName))}");
                    }

                    if (File.Exists(glbPath)) {
                        result.Success = true;
                        result.OutputFilePath = glbPath;
                        result.OutputFileSize = new FileInfo(glbPath).Length;
                        Logger.Info($"FBX2glTF: Success, created {glbPath}, size: {result.OutputFileSize} bytes");
                    } else {
                        result.Success = false;
                        result.Error = $"FBX2glTF completed but output file not found: {glbPath}";
                        Logger.Error(result.Error);
                    }
                } else {
                    result.Success = false;
                    Logger.Error($"FBX2glTF failed with exit code {process.ExitCode}");
                }

            } catch (Exception ex) {
                Logger.Error(ex, "FBX2glTF conversion error");
                result.Success = false;
                result.Error = ex.Message;
            }

            return result;
        }
    }

    /// <summary>
    /// Результат конвертации FBX2glTF
    /// </summary>
    public class ConversionResult {
        public bool Success { get; set; }
        public string? Output { get; set; }
        public string? Error { get; set; }
        public int ExitCode { get; set; }
        public string? OutputFilePath { get; set; }
        public long OutputFileSize { get; set; }
    }
}
