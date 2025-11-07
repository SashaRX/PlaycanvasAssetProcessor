using System.Diagnostics;
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

        public GltfPackWrapper(string? executablePath = null) {
            _executablePath = executablePath ?? "gltfpack.exe";
        }

        /// <summary>
        /// Проверяет доступность gltfpack
        /// </summary>
        public async Task<bool> IsAvailableAsync() {
            try {
                var startInfo = new ProcessStartInfo {
                    FileName = _executablePath,
                    Arguments = "-h",
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
                Logger.Error(ex, "Failed to check gltfpack availability");
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
        /// <returns>Результат оптимизации</returns>
        public async Task<GltfPackResult> OptimizeAsync(
            string inputPath,
            string outputPath,
            LodSettings lodSettings,
            CompressionMode compressionMode,
            QuantizationSettings? quantization = null,
            bool generateReport = false) {

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
                    generateReport
                );

                Logger.Debug($"gltfpack command: {_executablePath} {arguments}");

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

                if (process.ExitCode == 0) {
                    if (File.Exists(outputPath)) {
                        result.Success = true;
                        result.OutputFilePath = outputPath;
                        result.OutputFileSize = new FileInfo(outputPath).Length;

                        // Парсим метрики из вывода gltfpack
                        ParseMetrics(result, outputBuilder.ToString());

                        Logger.Info($"gltfpack: Success, output size: {result.OutputFileSize} bytes");
                        if (result.TriangleCount > 0) {
                            Logger.Info($"  Triangles: {result.TriangleCount}, Vertices: {result.VertexCount}");
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
            bool generateReport) {

            var args = new List<string>();

            // Входной и выходной файлы
            args.Add($"-i \"{inputPath}\"");
            args.Add($"-o \"{outputPath}\"");

            // Упрощение геометрии
            if (lodSettings.SimplificationRatio < 1.0f) {
                args.Add($"-si {lodSettings.SimplificationRatio:F2}");

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

            return string.Join(" ", args);
        }

        /// <summary>
        /// Парсит метрики из вывода gltfpack
        /// </summary>
        private void ParseMetrics(GltfPackResult result, string output) {
            try {
                // Пример вывода gltfpack:
                // "Simplified to 1234 triangles, 567 vertices"
                // "Vertex cache: 89.5%"
                // "Overdraw: 1.23x"

                var lines = output.Split('\n');
                foreach (var line in lines) {
                    if (line.Contains("triangles") && line.Contains("vertices")) {
                        // Парсим количество треугольников и вершин
                        var parts = line.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                        for (int i = 0; i < parts.Length - 1; i++) {
                            if (parts[i + 1] == "triangles" && int.TryParse(parts[i], out int triangles)) {
                                result.TriangleCount = triangles;
                            }
                            if (parts[i + 1] == "vertices" && int.TryParse(parts[i], out int vertices)) {
                                result.VertexCount = vertices;
                            }
                        }
                    }
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
