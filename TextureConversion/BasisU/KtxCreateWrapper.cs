using System.Diagnostics;
using System.IO;
using System.Text;
using AssetProcessor.TextureConversion.Core;
using NLog;

namespace AssetProcessor.TextureConversion.BasisU {
    /// <summary>
    /// Обёртка для ktx create - современного инструмента KTX-Software для создания KTX2 файлов
    /// Заменяет устаревший toktx.exe
    /// </summary>
    public class KtxCreateWrapper {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly string _ktxExecutablePath;

        /// <summary>
        /// Директория с ktx.exe (для загрузки ktx.dll при необходимости)
        /// </summary>
        public string? KtxDirectory {
            get {
                try {
                    if (Path.IsPathRooted(_ktxExecutablePath)) {
                        return Path.GetDirectoryName(_ktxExecutablePath);
                    }
                    return null;
                } catch {
                    return null;
                }
            }
        }

        public KtxCreateWrapper(string ktxExecutablePath = "ktx") {
            _ktxExecutablePath = ktxExecutablePath;
        }

        /// <summary>
        /// Проверяет доступность ktx
        /// </summary>
        public async Task<bool> IsAvailableAsync() {
            try {
                var psi = new ProcessStartInfo {
                    FileName = _ktxExecutablePath,
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
        /// Получает версию ktx и логирует её
        /// </summary>
        public async Task<string> GetVersionAsync() {
            try {
                var psi = new ProcessStartInfo {
                    FileName = _ktxExecutablePath,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return "Unknown (process failed to start)";

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();

                var versionText = output.Trim();
                if (string.IsNullOrEmpty(versionText)) {
                    versionText = error.Trim();
                }

                return string.IsNullOrEmpty(versionText) ? "Unknown" : versionText;
            } catch (Exception ex) {
                return $"Error: {ex.Message}";
            }
        }

        /// <summary>
        /// Упаковывает набор мипмапов в KTX2 файл с Basis Universal сжатием
        /// </summary>
        public async Task<ToktxResult> PackMipmapsAsync(
            List<string> mipmapPaths,
            string outputPath,
            CompressionSettings settings,
            Dictionary<string, string>? kvdBinaryFiles = null) {

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

                Logger.Info($"=== KTX CREATE PACKING START ===");

                // Логируем версию ktx
                var version = await GetVersionAsync();
                Logger.Info($"  ktx version: {version}");

                Logger.Info($"  Mipmaps count: {mipmapPaths.Count}");
                Logger.Info($"  Output: {outputPath}");
                Logger.Info($"  Compression format: {settings.CompressionFormat}");

                // Удаляем существующий выходной файл
                if (File.Exists(outputPath)) {
                    Logger.Info($"Deleting existing output file: {outputPath}");
                    try {
                        File.Delete(outputPath);
                    } catch (Exception ex) {
                        Logger.Warn($"Failed to delete existing output: {ex.Message}");
                    }
                }

                // Генерируем аргументы для ktx create
                var args = GenerateKtxCreateArguments(mipmapPaths, outputPath, settings, kvdBinaryFiles);

                Logger.Info($"ktx create command: {_ktxExecutablePath} {args}");

                // Запускаем ktx create
                var psi = new ProcessStartInfo {
                    FileName = _ktxExecutablePath,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory
                };

                using var process = Process.Start(psi);
                if (process == null) {
                    throw new Exception("Failed to start ktx create process");
                }

                var output = new StringBuilder();
                var error = new StringBuilder();

                process.OutputDataReceived += (s, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) {
                        output.AppendLine(e.Data);
                        Logger.Info($"[ktx] {e.Data}");
                    }
                };

                process.ErrorDataReceived += (s, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) {
                        error.AppendLine(e.Data);
                        Logger.Info($"[ktx] {e.Data}");
                    }
                };

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync();

                result.Output = output.ToString();
                result.Error = error.ToString();

                if (process.ExitCode != 0) {
                    result.Success = false;
                    Logger.Error($"ktx create failed with exit code {process.ExitCode}");
                    Logger.Error($"Error output: {result.Error}");
                    return result;
                }

                // Проверяем что файл создан
                if (!File.Exists(outputPath)) {
                    result.Success = false;
                    result.Error = $"Output file not created: {outputPath}";
                    Logger.Error(result.Error);
                    return result;
                }

                result.Success = true;
                result.OutputFileSize = new FileInfo(outputPath).Length;

                Logger.Info($"=== KTX CREATE SUCCESS ===");
                Logger.Info($"  Output file: {outputPath}");
                Logger.Info($"  File size: {result.OutputFileSize} bytes");

            } catch (Exception ex) {
                Logger.Error(ex, "ktx create error");
                result.Success = false;
                result.Error = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Генерирует аргументы командной строки для ktx create
        /// </summary>
        private string GenerateKtxCreateArguments(
            List<string> mipmapPaths,
            string outputPath,
            CompressionSettings settings,
            Dictionary<string, string>? kvdBinaryFiles) {

            var args = new List<string>();

            // Команда
            args.Add("create");

            // ============================================
            // ФОРМАТ (обязательный)
            // ============================================
            // ktx create требует явного указания формата
            args.Add("--format");
            args.Add(settings.UseGammaCorrectionForSRGB ? "R8G8B8A8_SRGB" : "R8G8B8A8_UNORM");

            // ============================================
            // КОДИРОВАНИЕ (обязательный если хотим Basis)
            // ============================================
            bool isUASTC = settings.CompressionFormat == CompressionFormat.UASTC;

            args.Add("--encode");
            args.Add(isUASTC ? "uastc" : "basis-lz");

            // ============================================
            // ГЕНЕРАЦИЯ МИПМАПОВ
            // ============================================
            if (settings.GenerateMipmaps && mipmapPaths.Count == 1) {
                args.Add("--generate-mipmap");

                // Фильтр для генерации мипмапов
                args.Add("--mipmap-filter");
                args.Add("lanczos4"); // Хорошее качество по умолчанию
            }

            // ============================================
            // ПАРАМЕТРЫ СЖАТИЯ
            // ============================================
            if (isUASTC) {
                // UASTC параметры
                args.Add("--uastc-quality");
                args.Add(settings.UASTCQuality.ToString());

                if (settings.UseUASTCRDO) {
                    args.Add("--uastc-rdo");
                    args.Add("--uastc-rdo-l");
                    args.Add(settings.UASTCRDOQuality.ToString("F3"));
                }
            } else {
                // ETC1S / BasisLZ параметры
                args.Add("--clevel");
                args.Add(settings.CompressionLevel.ToString());

                args.Add("--qlevel");
                args.Add(settings.QualityLevel.ToString());
            }

            // ============================================
            // СУПЕРКОМПРЕССИЯ (Zstandard)
            // ============================================
            if (settings.UseZstd) {
                args.Add("--zstd");
                args.Add("15"); // Хороший баланс скорость/качество
            }

            // ============================================
            // ПОТОКИ
            // ============================================
            if (settings.UseMultithreading && settings.ThreadCount > 0) {
                args.Add("--threads");
                args.Add(settings.ThreadCount.ToString());
            }

            // ============================================
            // NORMAL MAP MODE
            // ============================================
            if (settings.NormalMode) {
                args.Add("--normal-mode");
            }

            // ============================================
            // NORMALIZE
            // ============================================
            if (settings.Normalize) {
                args.Add("--normalize");
            }

            // ============================================
            // KEY-VALUE DATA (KVD) - binary metadata
            // ============================================
            // ПРИМЕЧАНИЕ: ktx create НЕ поддерживает добавление KVD через CLI
            // Метаданные будут добавлены через post-processing с Ktx2MetadataInjector
            if (kvdBinaryFiles != null && kvdBinaryFiles.Count > 0) {
                Logger.Info($"KVD metadata will be injected via post-processing ({kvdBinaryFiles.Count} file(s))");
            }

            // ============================================
            // ВХОДНЫЕ ФАЙЛЫ (СНАЧАЛА!)
            // ============================================
            // ВАЖНО: ktx create принимает входные файлы ПЕРЕД выходным
            foreach (var mipPath in mipmapPaths) {
                // Экранируем пути с пробелами
                args.Add($"\"{mipPath}\"");
            }

            // ============================================
            // ВЫХОДНОЙ ФАЙЛ (ПОСЛЕДНИМ!)
            // ============================================
            args.Add($"\"{outputPath}\"");

            return string.Join(" ", args);
        }
    }
}
