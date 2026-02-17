using System.Diagnostics;
using System.Globalization;
using System.Text;
using AssetProcessor.TextureConversion.Core;
using NLog;

namespace AssetProcessor.TextureConversion.BasisU {
    /// <summary>
    /// Обёртка для basisu CLI с поддержкой XUASTC LDR кодирования.
    /// XUASTC LDR — новый формат суперкомпрессии из basis_universal (BinomialLLC),
    /// поддерживающий все 14 размеров блоков ASTC с DCT-трансформ сжатием.
    ///
    /// Требует basisu из master ветки basis_universal (после XUASTC merge).
    /// CLI флаги определяются автоматически через help-детекцию.
    /// </summary>
    public class XuastcLdrWrapper {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly string _basisuExecutablePath;
        private readonly Lazy<XuastcLdrCliCapabilities> _cliCapabilities;

        /// <summary>
        /// Конструктор
        /// </summary>
        /// <param name="basisuExecutablePath">Путь к basisu (или просто "basisu" если в PATH)</param>
        public XuastcLdrWrapper(string basisuExecutablePath = "basisu") {
            _basisuExecutablePath = basisuExecutablePath;
            _cliCapabilities = new Lazy<XuastcLdrCliCapabilities>(
                DetectCliCapabilities,
                System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <summary>
        /// Проверяет доступность basisu с XUASTC LDR поддержкой
        /// </summary>
        public async Task<bool> IsAvailableAsync() {
            try {
                using var process = new Process {
                    StartInfo = new ProcessStartInfo {
                        FileName = _basisuExecutablePath,
                        Arguments = "-version",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                await Task.WhenAll(outputTask, errorTask);

                return process.ExitCode == 0;
            } catch (Exception ex) {
                Logger.Debug(ex, "basisu availability check failed");
                return false;
            }
        }

        /// <summary>
        /// Проверяет поддержку XUASTC LDR в текущей версии basisu
        /// </summary>
        public bool IsXuastcLdrSupported() {
            return _cliCapabilities.Value.SupportsXuastcLdr;
        }

        /// <summary>
        /// Получает версию basisu
        /// </summary>
        public async Task<string> GetVersionAsync() {
            try {
                using var process = new Process {
                    StartInfo = new ProcessStartInfo {
                        FileName = _basisuExecutablePath,
                        Arguments = "-version",
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

                var version = output.Trim();
                if (string.IsNullOrEmpty(version)) version = error.Trim();
                return string.IsNullOrEmpty(version) ? "Unknown" : version;
            } catch (Exception ex) {
                return $"Error: {ex.Message}";
            }
        }

        /// <summary>
        /// Кодирует текстуру в XUASTC LDR формат.
        /// </summary>
        /// <param name="inputPaths">Путь к входным файлам (мипмапы PNG). Если один файл — basisu генерирует мипмапы.</param>
        /// <param name="outputPath">Путь к выходному файлу (.basis или .ktx2)</param>
        /// <param name="settings">Настройки сжатия (используются XUASTC LDR параметры)</param>
        /// <returns>Результат кодирования</returns>
        public async Task<XuastcLdrResult> EncodeAsync(
            List<string> inputPaths,
            string outputPath,
            CompressionSettings settings) {

            var result = new XuastcLdrResult {
                OutputPath = outputPath
            };

            try {
                if (inputPaths.Count == 0) {
                    throw new ArgumentException("No input files provided", nameof(inputPaths));
                }

                foreach (var path in inputPaths) {
                    if (!File.Exists(path)) {
                        throw new FileNotFoundException($"Input file not found: {path}");
                    }
                }

                Logger.Info("=== XUASTC LDR ENCODING START ===");

                var version = await GetVersionAsync();
                Logger.Info($"  basisu version: {version}");

                if (!IsXuastcLdrSupported()) {
                    Logger.Warn("XUASTC LDR may not be supported by this basisu version. Attempting encoding anyway...");
                }

                Logger.Info($"  Input files: {inputPaths.Count}");
                Logger.Info($"  Output: {outputPath}");
                Logger.Info($"  Block size: {settings.XuastcBlockSize.ToCliString()}");
                Logger.Info($"  DCT quality: {settings.XuastcDctQuality}");
                Logger.Info($"  Supercompression: {settings.XuastcSupercompression}");

                // Удаляем существующий выходной файл
                if (File.Exists(outputPath)) {
                    try {
                        File.Delete(outputPath);
                    } catch (Exception ex) {
                        Logger.Warn($"Failed to delete existing output: {ex.Message}");
                    }
                }

                var args = BuildArguments(inputPaths, outputPath, settings);
                Logger.Info($"basisu command: {_basisuExecutablePath} {args}");

                using var process = new Process {
                    StartInfo = new ProcessStartInfo {
                        FileName = _basisuExecutablePath,
                        Arguments = args,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory
                    }
                };

                var output = new StringBuilder();
                var error = new StringBuilder();

                process.OutputDataReceived += (s, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) {
                        output.AppendLine(e.Data);
                        Logger.Info($"[basisu] {e.Data}");
                    }
                };

                process.ErrorDataReceived += (s, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) {
                        error.AppendLine(e.Data);
                        Logger.Info($"[basisu] {e.Data}");
                    }
                };

                var startTime = DateTime.Now;
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync();
                var duration = DateTime.Now - startTime;

                result.Output = output.ToString();
                result.Error = error.ToString();
                result.ExitCode = process.ExitCode;
                result.Duration = duration;

                if (process.ExitCode != 0) {
                    result.Success = false;
                    Logger.Error($"basisu XUASTC LDR encoding failed with exit code {process.ExitCode}");
                    Logger.Error($"Error output: {result.Error}");
                    return result;
                }

                // Проверяем что файл создан
                if (!File.Exists(outputPath)) {
                    // basisu может создать файл с другим именем, проверяем альтернативы
                    var dir = Path.GetDirectoryName(outputPath) ?? ".";
                    var baseName = Path.GetFileNameWithoutExtension(inputPaths[0]);
                    var possibleOutputs = new[] {
                        Path.Combine(dir, baseName + ".basis"),
                        Path.Combine(dir, baseName + ".ktx2")
                    };

                    string? actualOutput = null;
                    foreach (var possible in possibleOutputs) {
                        if (File.Exists(possible) && possible != outputPath) {
                            actualOutput = possible;
                            break;
                        }
                    }

                    if (actualOutput != null) {
                        Logger.Info($"basisu output found at: {actualOutput}, moving to {outputPath}");
                        File.Move(actualOutput, outputPath, overwrite: true);
                    } else {
                        result.Success = false;
                        result.Error = $"Output file not created: {outputPath}";
                        Logger.Error(result.Error);
                        return result;
                    }
                }

                result.Success = true;
                result.OutputFileSize = new FileInfo(outputPath).Length;

                Logger.Info("=== XUASTC LDR ENCODING SUCCESS ===");
                Logger.Info($"  Output: {outputPath}");
                Logger.Info($"  File size: {result.OutputFileSize} bytes");
                Logger.Info($"  Duration: {duration.TotalSeconds:F1}s");
                Logger.Info($"  Block size: {settings.XuastcBlockSize.ToCliString()} ({settings.XuastcBlockSize.GetGpuBitsPerPixel():F2} bpp GPU)");

            } catch (Exception ex) {
                Logger.Error(ex, "XUASTC LDR encoding error");
                result.Success = false;
                result.Error = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Строит аргументы командной строки для basisu с XUASTC LDR
        /// </summary>
        internal string BuildArguments(
            List<string> inputPaths,
            string outputPath,
            CompressionSettings settings) {

            var args = new List<string>();
            var capabilities = _cliCapabilities.Value;

            // ============================================
            // ВХОДНЫЕ ФАЙЛЫ
            // ============================================
            // basisu принимает входной файл первым аргументом
            foreach (var inputPath in inputPaths) {
                args.Add($"\"{inputPath}\"");
            }

            // ============================================
            // XUASTC LDR MODE
            // ============================================
            args.Add(capabilities.XuastcLdrFlag ?? "-xuastc_ldr");

            // ============================================
            // BLOCK SIZE
            // ============================================
            var blockSizeStr = settings.XuastcBlockSize.ToCliString();
            args.Add(capabilities.BlockSizeFlag ?? "-block_size");
            args.Add(blockSizeStr);

            // ============================================
            // DCT QUALITY
            // ============================================
            if (capabilities.DctQualityFlag != null) {
                args.Add(capabilities.DctQualityFlag);
                args.Add(settings.XuastcDctQuality.ToString(CultureInfo.InvariantCulture));
            } else {
                // Fallback: пробуем стандартные флаги
                args.Add("-xuastc_dct_quality");
                args.Add(settings.XuastcDctQuality.ToString(CultureInfo.InvariantCulture));
            }

            // ============================================
            // SUPERCOMPRESSION PROFILE
            // ============================================
            switch (settings.XuastcSupercompression) {
                case XuastcSupercompressionProfile.Zstd:
                    if (capabilities.ZstdFlag != null) {
                        args.Add(capabilities.ZstdFlag);
                    }
                    // Zstd часто является default, может не требовать флага
                    break;
                case XuastcSupercompressionProfile.Arithmetic:
                    if (capabilities.ArithmeticFlag != null) {
                        args.Add(capabilities.ArithmeticFlag);
                    }
                    break;
                case XuastcSupercompressionProfile.Hybrid:
                    if (capabilities.HybridFlag != null) {
                        args.Add(capabilities.HybridFlag);
                    }
                    break;
            }

            // ============================================
            // ВЫХОДНОЙ ФОРМАТ
            // ============================================
            if (settings.OutputFormat == OutputFormat.KTX2) {
                args.Add("-ktx2");
            }

            // ============================================
            // ВЫХОДНОЙ ФАЙЛ
            // ============================================
            args.Add($"-output_file \"{outputPath}\"");

            // ============================================
            // sRGB / LINEAR
            // ============================================
            bool isSrgb = settings.XuastcSrgb ||
                         settings.ColorSpace == ColorSpace.SRGB;

            if (!isSrgb) {
                // basisu по умолчанию считает что текстура sRGB (perceptual mode)
                // -linear отключает perceptual mode для linear данных
                args.Add("-linear");
            }

            // ============================================
            // МИПМАПЫ
            // ============================================
            if (settings.GenerateMipmaps && inputPaths.Count == 1) {
                args.Add("-mipmap");
            }
            // Если передано несколько файлов, basisu не поддерживает предгенерированные мипмапы
            // напрямую — вместо этого pipeline должен передать мип0, а basisu сгенерирует остальные

            // ============================================
            // МНОГОПОТОЧНОСТЬ
            // ============================================
            if (!settings.UseMultithreading) {
                args.Add("-no_multithreading");
            }

            return string.Join(" ", args);
        }

        /// <summary>
        /// Определяет доступные CLI флаги через анализ help
        /// </summary>
        private XuastcLdrCliCapabilities DetectCliCapabilities() {
            var helpOutput = GetHelpOutput();

            if (string.IsNullOrWhiteSpace(helpOutput)) {
                Logger.Warn("Could not detect basisu CLI capabilities (help output empty)");
                return XuastcLdrCliCapabilities.Default();
            }

            var supportsXuastcLdr = helpOutput.Contains("xuastc_ldr", StringComparison.OrdinalIgnoreCase);

            // Определяем флаг XUASTC LDR режима
            var xuastcLdrFlag = DetectFlag(helpOutput,
                "-xuastc_ldr", "--xuastc_ldr", "--xuastc-ldr", "-xuastc-ldr");

            // Определяем флаг размера блока
            var blockSizeFlag = DetectFlag(helpOutput,
                "-block_size", "--block_size", "--block-size", "-block-size",
                "-xuastc_block_size", "--xuastc_block_size");

            // Определяем флаг DCT quality
            var dctQualityFlag = DetectFlag(helpOutput,
                "-xuastc_dct_quality", "--xuastc_dct_quality", "--xuastc-dct-quality",
                "-dct_quality", "--dct_quality",
                "-xuastc_quality", "--xuastc_quality");

            // Определяем флаги суперкомпрессии
            var zstdFlag = DetectFlag(helpOutput,
                "-xuastc_zstd", "--xuastc_zstd", "-ktx2_zstd", "--ktx2_zstd");
            var arithmeticFlag = DetectFlag(helpOutput,
                "-xuastc_arithmetic", "--xuastc_arithmetic",
                "-xuastc_arith", "--xuastc_arith");
            var hybridFlag = DetectFlag(helpOutput,
                "-xuastc_hybrid", "--xuastc_hybrid");

            var result = new XuastcLdrCliCapabilities(
                supportsXuastcLdr,
                xuastcLdrFlag,
                blockSizeFlag,
                dctQualityFlag,
                zstdFlag,
                arithmeticFlag,
                hybridFlag);

            Logger.Info($"basisu CLI capabilities: XUASTC_LDR={supportsXuastcLdr}, " +
                       $"flags=[{xuastcLdrFlag}, {blockSizeFlag}, {dctQualityFlag}]");

            return result;
        }

        private static string? DetectFlag(string helpOutput, params string[] candidates) {
            foreach (var candidate in candidates) {
                if (helpOutput.Contains(candidate, StringComparison.OrdinalIgnoreCase)) {
                    return candidate;
                }
            }
            return null;
        }

        private string GetHelpOutput() {
            var attempts = new[] { "-help", "--help", "-h", "" }; // "" = run with no args (many tools print help)

            foreach (var attempt in attempts) {
                var result = TryCaptureBasisuOutput(attempt);
                if (!string.IsNullOrWhiteSpace(result) && result.Length > 50) {
                    return result;
                }
            }

            return string.Empty;
        }

        private string TryCaptureBasisuOutput(string arguments) {
            try {
                using var process = new Process {
                    StartInfo = new ProcessStartInfo {
                        FileName = _basisuExecutablePath,
                        Arguments = arguments,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();

                string output = string.Empty;
                string error = string.Empty;
                var outputTask = Task.Run(() => output = process.StandardOutput.ReadToEnd());
                var errorTask = Task.Run(() => error = process.StandardError.ReadToEnd());

                if (!process.WaitForExit(10000)) {
                    try { process.Kill(); } catch { }
                }

                Task.WaitAll(outputTask, errorTask);
                return (output + Environment.NewLine + error).Trim();
            } catch (Exception ex) {
                Logger.Debug(ex, "Failed to capture basisu CLI output");
                return string.Empty;
            }
        }
    }

    /// <summary>
    /// Результат XUASTC LDR кодирования
    /// </summary>
    public class XuastcLdrResult {
        public bool Success { get; set; }
        public int ExitCode { get; set; }
        public string Output { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public string OutputPath { get; set; } = string.Empty;
        public long OutputFileSize { get; set; }
    }

    /// <summary>
    /// Обнаруженные возможности basisu CLI для XUASTC LDR
    /// </summary>
    internal sealed class XuastcLdrCliCapabilities {
        public bool SupportsXuastcLdr { get; }
        public string? XuastcLdrFlag { get; }
        public string? BlockSizeFlag { get; }
        public string? DctQualityFlag { get; }
        public string? ZstdFlag { get; }
        public string? ArithmeticFlag { get; }
        public string? HybridFlag { get; }

        public XuastcLdrCliCapabilities(
            bool supportsXuastcLdr,
            string? xuastcLdrFlag,
            string? blockSizeFlag,
            string? dctQualityFlag,
            string? zstdFlag,
            string? arithmeticFlag,
            string? hybridFlag) {
            SupportsXuastcLdr = supportsXuastcLdr;
            XuastcLdrFlag = xuastcLdrFlag;
            BlockSizeFlag = blockSizeFlag;
            DctQualityFlag = dctQualityFlag;
            ZstdFlag = zstdFlag;
            ArithmeticFlag = arithmeticFlag;
            HybridFlag = hybridFlag;
        }

        /// <summary>
        /// Возвращает capabilities по умолчанию (на случай если help недоступен)
        /// </summary>
        public static XuastcLdrCliCapabilities Default() {
            return new XuastcLdrCliCapabilities(
                supportsXuastcLdr: false,
                xuastcLdrFlag: "-xuastc_ldr",
                blockSizeFlag: "-block_size",
                dctQualityFlag: "-xuastc_dct_quality",
                zstdFlag: null,
                arithmeticFlag: null,
                hybridFlag: null);
        }
    }
}
