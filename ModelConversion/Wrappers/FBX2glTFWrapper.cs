using System.Diagnostics;
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
                var startInfo = new ProcessStartInfo {
                    FileName = _executablePath,
                    Arguments = "--help",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return false;

                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            } catch (Exception ex) {
                Logger.Error(ex, "Failed to check FBX2glTF availability");
                return false;
            }
        }

        /// <summary>
        /// Конвертирует FBX в GLB
        /// </summary>
        /// <param name="inputPath">Путь к FBX файлу</param>
        /// <param name="outputPath">Путь к выходному GLB (без расширения)</param>
        /// <returns>Результат конвертации</returns>
        public async Task<ConversionResult> ConvertToGlbAsync(string inputPath, string outputPath) {
            var result = new ConversionResult();

            try {
                Logger.Info($"FBX2glTF: Converting {inputPath} to {outputPath}.glb");

                // Аргументы: --binary --input "file.fbx" --output "output_path"
                var arguments = $"--binary --input \"{inputPath}\" --output \"{outputPath}\"";

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

                if (process.ExitCode == 0) {
                    // Проверяем что файл создан
                    var glbPath = outputPath + ".glb";
                    if (File.Exists(glbPath)) {
                        result.Success = true;
                        result.OutputFilePath = glbPath;
                        result.OutputFileSize = new FileInfo(glbPath).Length;
                        Logger.Info($"FBX2glTF: Success, output size: {result.OutputFileSize} bytes");
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
