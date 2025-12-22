using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using AssetProcessor.ModelConversion.Core;
using NLog;

namespace AssetProcessor.ModelConversion.Wrappers {
    /// <summary>
    /// Wrapper для gltfpack CLI tool
    /// Оптимизирует GLB: упрощение геометрии, квантование, EXT_meshopt_compression
    /// </summary>
    public class GltfPackWrapper {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly string _executablePath;

        /// <summary>
        /// Путь к исполняемому файлу gltfpack
        /// </summary>
        public string ExecutablePath => _executablePath;

        public GltfPackWrapper(string? executablePath = null) {
            _executablePath = executablePath ?? "gltfpack.exe";
        }

        /// <summary>
        /// Проверяет доступность gltfpack
        /// </summary>
        public async Task<bool> IsAvailableAsync() {
            try {
                Logger.Info($"Checking gltfpack availability at: {_executablePath}");

                // Проверка существования файла
                if (!File.Exists(_executablePath)) {
                    Logger.Warn($"gltfpack executable not found at: {_executablePath}");
                    return false;
                }

                // Пробуем запустить с --help (gltfpack может не принимать -h)
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
                    Logger.Warn("Failed to start gltfpack process");
                    return false;
                }

                await process.WaitForExitAsync();

                // gltfpack может возвращать non-zero exit code даже для --help
                // Главное - что файл существует и процесс запускается
                Logger.Info($"gltfpack is available (exit code: {process.ExitCode})");
                return true;
            } catch (Exception ex) {
                Logger.Error(ex, $"Failed to check gltfpack availability at: {_executablePath}");
                return false;
            }
        }

        /// <summary>
        /// Оптимизирует GLB с генерацией LOD
        /// </summary>
        /// <param name="inputPath">Путь к входному GLB</param>
        /// <param name="outputPath">Путь к выходному GLB</param>
        /// <param name="lodSettings">Настройки LOD</param>
        /// <param name="compressionMode">Режим сжатия</param>
        /// <param name="quantization">Настройки квантования</param>
        /// <param name="generateReport">Генерировать ли отчет</param>
        /// <param name="excludeTextures">Не встраивать текстуры в GLB (использовать -tr флаг)</param>
        /// <returns>Результат оптимизации</returns>
        public async Task<GltfPackResult> OptimizeAsync(
            string inputPath,
            string outputPath,
            LodSettings lodSettings,
            CompressionMode compressionMode,
            QuantizationSettings? quantization = null,
            bool generateReport = false,
            bool excludeTextures = true) {

            var result = new GltfPackResult();

            try {
                Logger.Info($"gltfpack: Optimizing {inputPath} -> {outputPath}");
                Logger.Info($"  LOD: {lodSettings.Level}, Simplification: {lodSettings.SimplificationRatio}, Aggressive: {lodSettings.AggressiveSimplification}");
                Logger.Info($"  Compression: {compressionMode}");

                var arguments = BuildArguments(
                    inputPath,
                    outputPath,
                    lodSettings,
                    compressionMode,
                    quantization,
                    generateReport,
                    excludeTextures
                );

                Logger.Info($"gltfpack command: {_executablePath} {arguments}");

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
                    throw new Exception("Failed to start gltfpack process");
                }

                process.OutputDataReceived += (sender, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) {
                        outputBuilder.AppendLine(e.Data);
                        Logger.Debug($"gltfpack output: {e.Data}");
                    }
                };

                process.ErrorDataReceived += (sender, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) {
                        errorBuilder.AppendLine(e.Data);
                        // gltfpack выводит прогресс в stderr, это не ошибка
                        if (!e.Data.Contains("Processing:") && !e.Data.Contains("%")) {
                            Logger.Debug($"gltfpack stderr: {e.Data}");
                        }
                    }
                };

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync();

                result.Output = outputBuilder.ToString();
                result.StdErr = errorBuilder.ToString();
                result.ExitCode = process.ExitCode;

                // Логируем полный вывод для диагностики (ВСЕГДА, даже если пусто)
                Logger.Info($"gltfpack stdout (length: {result.Output?.Length ?? 0}):\n{result.Output ?? "(empty)"}");
                Logger.Info($"gltfpack stderr (length: {result.StdErr?.Length ?? 0}):\n{result.StdErr ?? "(empty)"}");

                if (process.ExitCode == 0) {
                    if (File.Exists(outputPath)) {
                        result.Success = true;
                        result.OutputFilePath = outputPath;
                        result.OutputFileSize = new FileInfo(outputPath).Length;

                        // Парсим метрики из вывода gltfpack (проверяем и stdout, и stderr)
                        ParseMetrics(result, result.Output ?? "", result.StdErr ?? "");

                        Logger.Info($"gltfpack: Success, output size: {result.OutputFileSize} bytes");
                        if (result.TriangleCount > 0) {
                            Logger.Info($"  Triangles: {result.TriangleCount}, Vertices: {result.VertexCount}");
                        } else {
                            Logger.Warn("  Unable to parse triangle/vertex counts from gltfpack output");
                        }
                    } else {
                        result.Success = false;
                        result.Error = $"gltfpack completed but output file not found: {outputPath}";
                        Logger.Error(result.Error);
                    }
                } else {
                    result.Success = false;
                    result.Error = $"gltfpack failed with exit code {process.ExitCode}";
                    Logger.Error(result.Error);
                }

            } catch (Exception ex) {
                Logger.Error(ex, "gltfpack optimization error");
                result.Success = false;
                result.Error = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Строит аргументы командной строки для gltfpack
        /// </summary>
        private string BuildArguments(
            string inputPath,
            string outputPath,
            LodSettings lodSettings,
            CompressionMode compressionMode,
            QuantizationSettings? quantization,
            bool generateReport,
            bool excludeTextures) {

            var args = new List<string>();

            // Входной и выходной файлы
            args.Add($"-i \"{inputPath}\"");
            args.Add($"-o \"{outputPath}\"");

            // Исключение текстур: -tr предотвращает копирование/встраивание текстур
            if (excludeTextures) {
                args.Add("-tr");
                Logger.Info("gltfpack: Using -tr flag (textures will NOT be embedded)");
            }

            // КРИТИЧНО: -kv сохраняет ВСЕ vertex attributes (включая UV), даже если они не используются
            // Без этого флага gltfpack удаляет TEXCOORD_0 из моделей без текстур
            args.Add("-kv");
            Logger.Info("gltfpack: Using -kv flag (keeping all vertex attributes including unused UV)");

            // Упрощение геометрии
            if (lodSettings.SimplificationRatio < 1.0f) {
                // КРИТИЧНО: Используем InvariantCulture чтобы получить "0.60" вместо "0,60"
                // gltfpack не понимает запятую как десятичный разделитель
                var simplificationStr = lodSettings.SimplificationRatio.ToString("F2", CultureInfo.InvariantCulture);
                args.Add($"-si {simplificationStr}");
                Logger.Debug($"Simplification argument: -si {simplificationStr}");

                // Агрессивное упрощение
                if (lodSettings.AggressiveSimplification) {
                    args.Add("-sa");
                }
            }

            // Режим сжатия
            switch (compressionMode) {
                case CompressionMode.Quantization:
                    // KHR_mesh_quantization (совместимо с редакторами)
                    args.Add("-kn"); // quantize normal
                    args.Add("-km"); // quantize mesh
                    break;

                case CompressionMode.MeshOpt:
                    // EXT_meshopt_compression
                    args.Add("-c");
                    args.Add("-kn");
                    args.Add("-km");
                    break;

                case CompressionMode.MeshOptAggressive:
                    // EXT_meshopt_compression с дополнительным сжатием
                    args.Add("-cc");
                    break;

                case CompressionMode.MeshOptKHR:
                    // KHR_meshopt_compression (meshoptimizer 1.0+)
                    // -cz эквивалентно -ce khr -cc (лучшее сжатие)
                    // ВАЖНО: Требует кастомный WASM декодер на клиенте!
                    args.Add("-cz");
                    Logger.Info("gltfpack: Using KHR_meshopt_compression (-cz) - requires custom WASM decoder");
                    break;

                case CompressionMode.MeshOptKHRWithFallback:
                    // KHR_meshopt_compression + fallback буфер для совместимости
                    // -cf создаёт несжатый fallback для старых браузеров/движков
                    args.Add("-cz");
                    args.Add("-cf");
                    Logger.Info("gltfpack: Using KHR_meshopt_compression with fallback (-cz -cf)");
                    break;

                case CompressionMode.None:
                    // Без сжатия
                    break;
            }

            // Квантование (если указано и режим поддерживает)
            if (quantization != null && compressionMode != CompressionMode.None) {
                args.Add($"-vp {quantization.PositionBits}");
                args.Add($"-vt {quantization.TexCoordBits}");
                args.Add($"-vn {quantization.NormalBits}");
                args.Add($"-vc {quantization.ColorBits}");
            }

            // Отчет
            if (generateReport) {
                var reportPath = Path.ChangeExtension(outputPath, ".report.json");
                args.Add($"-r \"{reportPath}\"");
            }

            // Verbose вывод для диагностики
            args.Add("-v");

            return string.Join(" ", args);
        }

        /// <summary>
        /// Парсит метрики из вывода gltfpack (stdout и stderr)
        /// </summary>
        private void ParseMetrics(GltfPackResult result, string stdout, string stderr) {
            try {
                // gltfpack может выводить статистику в stdout или stderr
                var allOutput = stdout + "\n" + stderr;

                // Возможные форматы:
                // "Simplified to 1234 triangles, 567 vertices"
                // "1234 triangles, 567 vertices"
                // "triangles: 1234, vertices: 567"
                // "Output: 1234 triangles 567 vertices"

                var lines = allOutput.Split('\n');
                foreach (var line in lines) {
                    Logger.Debug($"Parsing line: {line}");

                    if (line.Contains("triangle", StringComparison.OrdinalIgnoreCase) &&
                        line.Contains("vert", StringComparison.OrdinalIgnoreCase)) {
                        // Парсим количество треугольников и вершин
                        var parts = line.Split(new[] { ' ', ',', ':', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        for (int i = 0; i < parts.Length; i++) {
                            // Ищем числа перед словами "triangle" или "vert"
                            if (int.TryParse(parts[i], out int value)) {
                                if (i + 1 < parts.Length) {
                                    var nextWord = parts[i + 1].ToLower();
                                    if (nextWord.Contains("triangle")) {
                                        result.TriangleCount = value;
                                        Logger.Debug($"Found triangles: {value}");
                                    } else if (nextWord.Contains("vert")) {
                                        result.VertexCount = value;
                                        Logger.Debug($"Found vertices: {value}");
                                    }
                                }
                            }
                        }
                    }
                }

                if (result.TriangleCount == 0 && result.VertexCount == 0) {
                    Logger.Warn("ParseMetrics: No triangle/vertex counts found in gltfpack output");
                }
            } catch (Exception ex) {
                Logger.Warn(ex, "Failed to parse gltfpack metrics");
            }
        }
    }

    /// <summary>
    /// Результат оптимизации gltfpack
    /// </summary>
    public class GltfPackResult {
        public bool Success { get; set; }
        public string? Output { get; set; }
        public string? StdErr { get; set; }
        public string? Error { get; set; }
        public int ExitCode { get; set; }
        public string? OutputFilePath { get; set; }
        public long OutputFileSize { get; set; }

        // Метрики геометрии
        public int TriangleCount { get; set; }
        public int VertexCount { get; set; }
    }
}
