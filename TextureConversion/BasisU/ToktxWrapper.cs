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

            // ============================================
            // OUTPUT FORMAT
            // ============================================
            // КРИТИЧНО: --t2 должен быть первым для KTX2 формата
            if (settings.OutputFormat == OutputFormat.KTX2) {
                args.Add("--t2");
            }

            // ============================================
            // COLOR SPACE
            // ============================================
            // КРИТИЧНО: --assign_oetf ДОЛЖЕН быть ПЕРЕД флагами сжатия (--clevel, --bcmp)!
            if (settings.TreatAsLinear) {
                args.Add("--assign_oetf");
                args.Add("linear");
            } else if (settings.TreatAsSRGB) {
                args.Add("--assign_oetf");
                args.Add("srgb");
            }

            // ============================================
            // COMPRESSION FORMAT & QUALITY
            // ============================================
            // ВАЖНО: toktx не позволяет использовать --encode вместе с --zcmp
            // Поэтому используем старый синтаксис --bcmp/--uastc для совместимости
            if (settings.CompressionFormat == CompressionFormat.ETC1S) {
                // ETC1S mode - используем --bcmp для совместимости с --zcmp
                // КРИТИЧНО: --clevel ДОЛЖЕН быть ПЕРЕД --bcmp!
                args.Add("--clevel");
                args.Add(settings.CompressionLevel.ToString());

                args.Add("--bcmp");
                // Quality передается как аргумент к --bcmp
                args.Add(settings.QualityLevel.ToString());

            } else if (settings.CompressionFormat == CompressionFormat.UASTC) {
                // UASTC mode
                args.Add("--uastc");
                args.Add(settings.UASTCQuality.ToString());

                // UASTC RDO
                if (settings.UseUASTCRDO) {
                    args.Add("--uastc_rdo_l");
                    args.Add(FormattableString.Invariant($"{settings.UASTCRDOQuality}"));
                }
            }

            // ============================================
            // SUPERCOMPRESSION (KTX2 only)
            // ============================================
            // ВАЖНО: --zcmp нельзя использовать с ETC1S/BasisLZ!
            // Только для UASTC и несжатых форматов
            if (settings.OutputFormat == OutputFormat.KTX2 && settings.CompressionFormat != CompressionFormat.ETC1S) {
                if (settings.KTX2Supercompression == KTX2SupercompressionType.Zstandard) {
                    args.Add("--zcmp");
                    args.Add(settings.KTX2ZstdLevel.ToString());
                } else if (settings.KTX2Supercompression == KTX2SupercompressionType.ZLIB) {
                    args.Add("--zcmp");
                    args.Add("0"); // ZLIB mode
                }
                // None = no --zcmp flag
            }

            // ============================================
            // ALPHA CHANNEL OPTIONS
            // ============================================
            if (settings.ForceAlphaChannel) {
                args.Add("--target_type");
                args.Add("RGBA");
            } else if (settings.RemoveAlphaChannel) {
                args.Add("--target_type");
                args.Add("RGB");
            }

            // ============================================
            // NORMAL MAPS
            // ============================================
            if (settings.ConvertToNormalMap) {
                args.Add("--normal_mode");
            }

            if (settings.NormalizeVectors) {
                args.Add("--normalize");
            }

            if (settings.KeepRGBLayout) {
                args.Add("--input_swizzle");
                args.Add("rgb1");
            }

            // ============================================
            // MIPMAPS
            // ============================================
            if (settings.GenerateMipmaps && mipmapPaths.Count == 1) {
                // Если toktx должен сгенерировать мипмапы сам
                args.Add("--genmipmap");
            }

            if (settings.ClampMipmaps) {
                args.Add("--wmode");
                args.Add("clamp");
            }

            // ============================================
            // MULTITHREADING
            // ============================================
            if (settings.UseMultithreading && settings.ThreadCount > 0) {
                args.Add("--threads");
                args.Add(settings.ThreadCount.ToString());
            }

            // ============================================
            // OUTPUT FILE - ПОСЛЕ всех флагов!
            // ============================================
            args.Add(outputPath);

            // ============================================
            // INPUT FILES (mipmaps)
            // ============================================
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
