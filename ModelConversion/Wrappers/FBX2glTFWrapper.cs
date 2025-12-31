using System.Diagnostics;
using System.IO;
using System.Text;
using Assimp;
using Assimp.Configs;
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
                Logger.Info($"FBX2glTF: Converting {inputPath} (exclude textures: {excludeTextures})");

                string arguments;
                string expectedExtension;

                if (excludeTextures) {
                    // ВАЖНО: --separate-textures БЕЗ --binary создает .gltf (JSON) + .bin + внешние текстуры
                    // Текстуры НЕ встраиваются в файл, остаются внешними
                    // gltfpack затем конвертирует .gltf -> .glb с сохранением внешних текстур через -tr
                    // БЕЗ --keep-attribute - экспортируем ВСЕ атрибуты из FBX (включая UV)
                    arguments = $"--separate-textures --input \"{inputPath}\" --output \"{outputPath}\"";
                    expectedExtension = ".gltf";
                    Logger.Info("FBX2glTF: Using .gltf format with --separate-textures (textures will be external), exporting all attributes");
                } else {
                    // --binary создает .glb (binary) с встроенными текстурами
                    // БЕЗ --keep-attribute - экспортируем ВСЕ атрибуты из FBX (включая UV)
                    arguments = $"--binary --input \"{inputPath}\" --output \"{outputPath}\"";
                    expectedExtension = ".glb";
                    Logger.Info("FBX2glTF: Using .glb format with embedded textures, exporting all attributes");
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

                // Таймаут 10 минут (FBX конвертация может быть долгой для больших файлов)
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                try {
                    await process.WaitForExitAsync(cts.Token);
                } catch (OperationCanceledException) {
                    Logger.Error("FBX2glTF process timed out after 10 minutes, killing...");
                    try { process.Kill(entireProcessTree: true); } catch { }
                    result.Success = false;
                    result.Error = "FBX2glTF process timed out after 10 minutes";
                    return result;
                }

                result.Output = outputBuilder.ToString();
                result.Error = errorBuilder.ToString();
                result.ExitCode = process.ExitCode;

                // Логируем вывод FBX2glTF
                Logger.Info($"FBX2glTF stdout (length: {result.Output?.Length ?? 0}):\n{result.Output ?? "(empty)"}");
                Logger.Info($"FBX2glTF stderr (length: {result.Error?.Length ?? 0}):\n{result.Error ?? "(empty)"}");

                if (process.ExitCode == 0) {
                    // Логируем файлы в выходной директории для диагностики
                    var outputDir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(outputDir) && Directory.Exists(outputDir)) {
                        var files = Directory.GetFiles(outputDir);
                        Logger.Info($"Files in output directory {outputDir}: {string.Join(", ", files.Select(Path.GetFileName))}");
                    }

                    // ВАЖНО: Godot FBX2glTF fork создаёт поддиректорию {basename}_out/
                    // Пример: --output "path/to/model" создаёт "path/to/model_out/model.gltf"
                    var outputBaseName = Path.GetFileName(outputPath);
                    var outputDirPath = Path.GetDirectoryName(outputPath);
                    var outSubdirectory = Path.Combine(outputDirPath!, outputBaseName + "_out");

                    string? foundPath = null;

                    // Сначала проверяем поддиректорию _out (поведение Godot fork)
                    if (Directory.Exists(outSubdirectory)) {
                        Logger.Info($"FBX2glTF: Found _out subdirectory: {outSubdirectory}");
                        var outFiles = Directory.GetFiles(outSubdirectory);
                        Logger.Info($"Files in _out subdirectory: {string.Join(", ", outFiles.Select(Path.GetFileName))}");

                        var gltfInOut = Path.Combine(outSubdirectory, outputBaseName + ".gltf");
                        var glbInOut = Path.Combine(outSubdirectory, outputBaseName + ".glb");

                        if (File.Exists(gltfInOut)) {
                            foundPath = gltfInOut;
                            Logger.Info($"FBX2glTF: Found .gltf in _out subdirectory");
                        } else if (File.Exists(glbInOut)) {
                            foundPath = glbInOut;
                            Logger.Info($"FBX2glTF: Found .glb in _out subdirectory");
                        }
                    }

                    // Если не нашли в _out, проверяем основную директорию
                    if (foundPath == null) {
                        var gltfPath = outputPath + ".gltf";
                        var glbPath = outputPath + ".glb";

                        if (File.Exists(gltfPath)) {
                            foundPath = gltfPath;
                            Logger.Info($"FBX2glTF: Found .gltf file (expected: {expectedExtension})");
                        } else if (File.Exists(glbPath)) {
                            foundPath = glbPath;
                            Logger.Info($"FBX2glTF: Found .glb file (expected: {expectedExtension})");
                        }
                    }

                    if (foundPath != null) {
                        result.Success = true;
                        result.OutputFilePath = foundPath;
                        result.OutputFileSize = new FileInfo(foundPath).Length;
                        Logger.Info($"FBX2glTF: Success, created {foundPath}, size: {result.OutputFileSize} bytes");
                    } else {
                        result.Success = false;
                        result.Error = $"FBX2glTF completed but output file not found. Expected: {outputPath + expectedExtension} or {outSubdirectory}";
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
