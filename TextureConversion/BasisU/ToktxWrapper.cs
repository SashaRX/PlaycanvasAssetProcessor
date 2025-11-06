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
        /// Получает версию toktx и логирует её
        /// </summary>
        public async Task<string> GetVersionAsync() {
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
                if (process == null) return "Unknown (process failed to start)";

                var output = new StringBuilder();
                var error = new StringBuilder();

                process.OutputDataReceived += (s, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) output.AppendLine(e.Data);
                };

                process.ErrorDataReceived += (s, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) error.AppendLine(e.Data);
                };

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync();

                var versionText = output.ToString().Trim();
                if (string.IsNullOrEmpty(versionText)) {
                    versionText = error.ToString().Trim();
                }

                return string.IsNullOrEmpty(versionText) ? "Unknown" : versionText;
            } catch (Exception ex) {
                return $"Error: {ex.Message}";
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

                // Логируем версию toktx
                var version = await GetVersionAsync();
                Logger.Info($"  toktx version: {version}");

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
                Logger.Info($"  Full command line:");
                var commandLine = new StringBuilder();
                commandLine.Append(_toktxExecutablePath);
                foreach (var arg in args) {
                    // Экранируем аргументы с пробелами
                    if (arg.Contains(' ')) {
                        commandLine.Append($" \"{arg}\"");
                    } else {
                        commandLine.Append($" {arg}");
                    }
                }
                Logger.Info($"  {commandLine}");
                Logger.Info($"  Individual arguments:");
                for (int i = 0; i < args.Count; i++) {
                    Logger.Info($"    [{i}] = {args[i]}");
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
        /// Конвертирует ToktxFilterType в строку для toktx --filter
        /// </summary>
        private static string ToktxFilterTypeToString(ToktxFilterType filter) {
            return filter switch {
                ToktxFilterType.Box => "box",
                ToktxFilterType.Tent => "tent",
                ToktxFilterType.Bell => "bell",
                ToktxFilterType.BSpline => "b-spline",
                ToktxFilterType.Mitchell => "mitchell",
                ToktxFilterType.Lanczos3 => "lanczos3",
                ToktxFilterType.Lanczos4 => "lanczos4",
                ToktxFilterType.Lanczos6 => "lanczos6",
                ToktxFilterType.Lanczos12 => "lanczos12",
                ToktxFilterType.Blackman => "blackman",
                ToktxFilterType.Kaiser => "kaiser",
                ToktxFilterType.Gaussian => "gaussian",
                ToktxFilterType.CatmullRom => "catmullrom",
                ToktxFilterType.QuadraticInterp => "quadratic_interp",
                ToktxFilterType.QuadraticApprox => "quadratic_approx",
                ToktxFilterType.QuadraticMix => "quadratic_mix",
                _ => "kaiser" // default
            };
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
            switch (settings.ColorSpace) {
                case ColorSpace.Linear:
                    args.Add("--assign_oetf");
                    args.Add("linear");
                    break;
                case ColorSpace.SRGB:
                    args.Add("--assign_oetf");
                    args.Add("srgb");
                    break;
                // ColorSpace.Auto - не добавляем флаг
            }

            // ============================================
            // COMPRESSION FORMAT & QUALITY
            // ============================================
            // Используем новый синтаксис --encode (toktx v4.4+)
            if (settings.CompressionFormat == CompressionFormat.ETC1S) {
                // ETC1S mode - современный синтаксис
                args.Add("--encode");
                args.Add("etc1s");

                // Compression level для ETC1S
                args.Add("--clevel");
                args.Add(settings.CompressionLevel.ToString());

                // Quality level для ETC1S
                args.Add("--qlevel");
                args.Add(settings.QualityLevel.ToString());

            } else if (settings.CompressionFormat == CompressionFormat.UASTC) {
                // UASTC mode - современный синтаксис
                args.Add("--encode");
                args.Add("uastc");

                // UASTC quality level
                args.Add("--uastc_quality");
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
            // --zcmp можно использовать с UASTC и несжатыми форматами
            // ETC1S уже имеет встроенное BasisLZ суперсжатие, поэтому --zcmp не нужен
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
            // Определяем: используются ли pre-generated mipmaps или toktx будет генерировать их сам
            bool usePreGeneratedMipmaps = mipmapPaths.Count > 1;

            // --normal_mode: Конвертирует 3-4 компонентные XYZ нормали в 2-компонентный X+Y формат (RGB=X, A=Y)
            // Документация: "only valid for linear textures with two or more components"
            // ВАЖНО: Имеет смысл ТОЛЬКО когда toktx обрабатывает оригинальное изображение (single input)
            // С pre-generated mipmaps эта конвертация должна быть сделана на этапе генерации мипмапов
            if (settings.ConvertToNormalMap && !usePreGeneratedMipmaps) {
                args.Add("--normal_mode");
            }

            // --normalize: Нормализует входные нормали к единичной длине
            // Документация: "normalizes input normals to unit length" для linear текстур с 2+ компонентами
            // ВАЖНО: НЕ совместим с --mipmap (pre-generated mipmaps)!
            // --normalize работает с ВХОДНЫМ изображением перед генерацией мипмапов
            // Поэтому добавляем его ТОЛЬКО если передаем одно изображение
            if (settings.NormalizeVectors && !usePreGeneratedMipmaps) {
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

                // Добавляем фильтр для генерации мипмапов
                args.Add("--filter");
                args.Add(ToktxFilterTypeToString(settings.ToktxMipFilter));
            } else if (usePreGeneratedMipmaps) {
                // КРИТИЧНО: Флаг --mipmap ОБЯЗАТЕЛЕН когда передаём готовые мипмапы!
                // Без него toktx игнорирует все файлы кроме первого ("Ignoring excess input images")
                args.Add("--mipmap");

                // --levels ограничивает количество уровней (опционально)
                // НО в комбинации с --mipmap он ОБЯЗАТЕЛЕН!
                args.Add("--levels");
                args.Add(mipmapPaths.Count.ToString());
            }

            // WrapMode - режим сэмплирования на границах изображения
            if (settings.WrapMode == WrapMode.Wrap) {
                args.Add("--wmode");
                args.Add("wrap");
            } else if (settings.WrapMode == WrapMode.Clamp || settings.ClampMipmaps) {
                // По умолчанию toktx использует clamp, но можем явно указать
                args.Add("--wmode");
                args.Add("clamp");
            }

            // ============================================
            // MULTITHREADING & PERFORMANCE
            // ============================================
            if (settings.UseMultithreading && settings.ThreadCount > 0) {
                args.Add("--threads");
                args.Add(settings.ThreadCount.ToString());
            }

            // --no_sse - отключить SSE оптимизации (если UseSSE41 = false)
            if (!settings.UseSSE41) {
                args.Add("--no_sse");
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
