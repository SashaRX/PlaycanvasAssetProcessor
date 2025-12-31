using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using NLog;

namespace AssetProcessor.ModelConversion.Wrappers {
    /// <summary>
    /// Wrapper для glTF-Transform CLI tool
    /// Симплификация mesh с сохранением UV seams (split vertices)
    /// Требует Node.js и npx
    /// </summary>
    public class GltfTransformWrapper {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly string _npxPath;

        /// <summary>
        /// Путь к npx (Node Package Execute)
        /// </summary>
        public string NpxPath => _npxPath;

        public GltfTransformWrapper(string? npxPath = null) {
            _npxPath = npxPath ?? "npx";
        }

        /// <summary>
        /// Проверяет доступность glTF-Transform через npx
        /// </summary>
        public async Task<bool> IsAvailableAsync() {
            try {
                Logger.Info("Checking glTF-Transform availability via npx");

                var startInfo = new ProcessStartInfo {
                    FileName = _npxPath,
                    Arguments = "@gltf-transform/cli --version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) {
                    Logger.Warn("Failed to start npx process");
                    return false;
                }

                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0) {
                    Logger.Info($"glTF-Transform is available, version: {output.Trim()}");
                    return true;
                } else {
                    Logger.Warn($"glTF-Transform check failed with exit code: {process.ExitCode}");
                    return false;
                }
            } catch (Exception ex) {
                Logger.Error(ex, "Failed to check glTF-Transform availability");
                return false;
            }
        }

        /// <summary>
        /// Упрощает mesh с сохранением UV seams
        /// </summary>
        /// <param name="inputPath">Путь к входному GLB/glTF</param>
        /// <param name="outputPath">Путь к выходному GLB</param>
        /// <param name="settings">Настройки симплификации</param>
        /// <returns>Результат симплификации</returns>
        public async Task<GltfTransformResult> SimplifyAsync(
            string inputPath,
            string outputPath,
            GltfTransformSimplifySettings settings) {

            var result = new GltfTransformResult();

            try {
                Logger.Info($"glTF-Transform: Simplifying {inputPath} -> {outputPath}");
                Logger.Info($"  Ratio: {settings.Ratio}, Error: {settings.Error}, LockBorder: {settings.LockBorder}");

                var arguments = BuildSimplifyArguments(inputPath, outputPath, settings);

                Logger.Info($"glTF-Transform command: {_npxPath} {arguments}");

                var startInfo = new ProcessStartInfo {
                    FileName = _npxPath,
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
                    throw new Exception("Failed to start npx process");
                }

                process.OutputDataReceived += (sender, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) {
                        outputBuilder.AppendLine(e.Data);
                        Logger.Debug($"glTF-Transform output: {e.Data}");
                    }
                };

                process.ErrorDataReceived += (sender, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) {
                        errorBuilder.AppendLine(e.Data);
                        Logger.Debug($"glTF-Transform stderr: {e.Data}");
                    }
                };

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync();

                result.Output = outputBuilder.ToString();
                result.StdErr = errorBuilder.ToString();
                result.ExitCode = process.ExitCode;

                Logger.Info($"glTF-Transform stdout:\n{result.Output}");
                if (!string.IsNullOrEmpty(result.StdErr)) {
                    Logger.Info($"glTF-Transform stderr:\n{result.StdErr}");
                }

                if (process.ExitCode == 0 && File.Exists(outputPath)) {
                    result.Success = true;
                    result.OutputFilePath = outputPath;
                    result.OutputFileSize = new FileInfo(outputPath).Length;
                    Logger.Info($"glTF-Transform: Success, output size: {result.OutputFileSize} bytes");
                } else {
                    result.Success = false;
                    result.Error = process.ExitCode != 0
                        ? $"glTF-Transform failed with exit code {process.ExitCode}"
                        : $"Output file not found: {outputPath}";
                    Logger.Error(result.Error);
                }

            } catch (Exception ex) {
                Logger.Error(ex, "glTF-Transform simplification error");
                result.Success = false;
                result.Error = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Оптимизирует GLB (draco compression, quantization, etc.)
        /// </summary>
        public async Task<GltfTransformResult> OptimizeAsync(
            string inputPath,
            string outputPath,
            GltfTransformOptimizeSettings? settings = null) {

            var result = new GltfTransformResult();
            settings ??= new GltfTransformOptimizeSettings();

            try {
                Logger.Info($"glTF-Transform: Optimizing {inputPath} -> {outputPath}");

                var arguments = BuildOptimizeArguments(inputPath, outputPath, settings);

                Logger.Info($"glTF-Transform command: {_npxPath} {arguments}");

                var startInfo = new ProcessStartInfo {
                    FileName = _npxPath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) {
                    throw new Exception("Failed to start npx process");
                }

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                result.Output = output;
                result.StdErr = error;
                result.ExitCode = process.ExitCode;

                if (process.ExitCode == 0 && File.Exists(outputPath)) {
                    result.Success = true;
                    result.OutputFilePath = outputPath;
                    result.OutputFileSize = new FileInfo(outputPath).Length;
                    Logger.Info($"glTF-Transform: Optimize success, output size: {result.OutputFileSize} bytes");
                } else {
                    result.Success = false;
                    result.Error = $"glTF-Transform optimize failed with exit code {process.ExitCode}";
                    Logger.Error(result.Error);
                }

            } catch (Exception ex) {
                Logger.Error(ex, "glTF-Transform optimize error");
                result.Success = false;
                result.Error = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Строит аргументы для команды simplify
        /// </summary>
        private string BuildSimplifyArguments(
            string inputPath,
            string outputPath,
            GltfTransformSimplifySettings settings) {

            var args = new List<string> {
                "@gltf-transform/cli",
                "simplify",
                $"\"{inputPath}\"",
                $"\"{outputPath}\""
            };

            // Ratio (0-1)
            var ratioStr = settings.Ratio.ToString("F2", CultureInfo.InvariantCulture);
            args.Add($"--ratio {ratioStr}");

            // Error threshold
            if (settings.Error.HasValue) {
                var errorStr = settings.Error.Value.ToString("F6", CultureInfo.InvariantCulture);
                args.Add($"--error {errorStr}");
            }

            // Lock border vertices (preserve seams between mesh chunks)
            if (settings.LockBorder) {
                args.Add("--lock-border");
            }

            return string.Join(" ", args);
        }

        /// <summary>
        /// Строит аргументы для команды optimize
        /// </summary>
        private string BuildOptimizeArguments(
            string inputPath,
            string outputPath,
            GltfTransformOptimizeSettings settings) {

            var args = new List<string> {
                "@gltf-transform/cli",
                "optimize",
                $"\"{inputPath}\"",
                $"\"{outputPath}\""
            };

            if (settings.Compress) {
                args.Add("--compress draco");
            }

            if (settings.TextureCompress) {
                args.Add("--texture-compress webp");
            }

            return string.Join(" ", args);
        }
    }

    /// <summary>
    /// Настройки симплификации glTF-Transform
    /// </summary>
    public class GltfTransformSimplifySettings {
        /// <summary>
        /// Целевое соотношение вершин (0-1)
        /// 0.5 = сохранить 50% вершин
        /// </summary>
        public float Ratio { get; set; } = 0.5f;

        /// <summary>
        /// Максимальная ошибка как доля радиуса mesh
        /// По умолчанию 0.0001 (0.01%)
        /// </summary>
        public float? Error { get; set; } = 0.0001f;

        /// <summary>
        /// Блокировать граничные вершины
        /// Важно для terrain chunks и предотвращения швов
        /// </summary>
        public bool LockBorder { get; set; } = true;

        /// <summary>
        /// Создает настройки из LodSettings
        /// </summary>
        public static GltfTransformSimplifySettings FromLodRatio(float simplificationRatio) {
            return new GltfTransformSimplifySettings {
                Ratio = simplificationRatio,
                Error = null, // Без лимита ошибки - полагаемся только на ratio
                LockBorder = false // Отключаем для лучшей симплификации
            };
        }
    }

    /// <summary>
    /// Настройки оптимизации glTF-Transform
    /// </summary>
    public class GltfTransformOptimizeSettings {
        /// <summary>
        /// Применить Draco сжатие
        /// </summary>
        public bool Compress { get; set; } = false;

        /// <summary>
        /// Сжать текстуры в WebP
        /// </summary>
        public bool TextureCompress { get; set; } = false;
    }

    /// <summary>
    /// Результат операции glTF-Transform
    /// </summary>
    public class GltfTransformResult {
        public bool Success { get; set; }
        public string? Output { get; set; }
        public string? StdErr { get; set; }
        public string? Error { get; set; }
        public int ExitCode { get; set; }
        public string? OutputFilePath { get; set; }
        public long OutputFileSize { get; set; }
    }
}
