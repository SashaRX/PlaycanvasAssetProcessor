using Assimp;
using AssetProcessor.ModelConversion.Wrappers;
using NLog;
using System.IO;
using System.Text;

namespace AssetProcessor.ModelConversion.Viewer {
    /// <summary>
    /// Загрузчик GLB файлов с поддержкой EXT_meshopt_compression декомпрессии
    /// </summary>
    public class GlbLoader {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly AssimpContext _assimpContext;
        private readonly GltfPackWrapper? _gltfPackWrapper;
        private readonly Dictionary<string, string> _decompressCache = new();

        public GlbLoader(string? gltfPackPath = null) {
            _assimpContext = new AssimpContext();

            if (!string.IsNullOrEmpty(gltfPackPath)) {
                _gltfPackWrapper = new GltfPackWrapper(gltfPackPath);
            }
        }

        /// <summary>
        /// Загружает GLB файл
        /// Автоматически декомпрессирует EXT_meshopt_compression если нужно
        /// </summary>
        public async Task<Scene?> LoadGlbAsync(string glbPath) {
            try {
                Logger.Info($"Loading GLB: {glbPath}");

                // Проверяем наличие EXT_meshopt_compression
                if (await HasMeshOptCompressionAsync(glbPath)) {
                    Logger.Info("GLB contains EXT_meshopt_compression, decompressing...");

                    // Декомпрессируем через gltfpack
                    var decompressedPath = await DecompressMeshOptAsync(glbPath);

                    if (decompressedPath == null) {
                        Logger.Error("Failed to decompress meshopt GLB");
                        return null;
                    }

                    // Загружаем декомпрессированный файл
                    var scene = _assimpContext.ImportFile(decompressedPath);
                    Logger.Info($"Loaded decompressed GLB: {scene.MeshCount} meshes, {scene.MaterialCount} materials");
                    return scene;
                } else {
                    // Прямая загрузка (Quantization only)
                    Logger.Info("Loading GLB directly (no meshopt compression)");
                    var scene = _assimpContext.ImportFile(glbPath);
                    Logger.Info($"Loaded GLB: {scene.MeshCount} meshes, {scene.MaterialCount} materials");
                    return scene;
                }
            } catch (Exception ex) {
                Logger.Error(ex, $"Failed to load GLB: {glbPath}");
                return null;
            }
        }

        /// <summary>
        /// Проверяет наличие EXT_meshopt_compression в GLB
        /// </summary>
        private async Task<bool> HasMeshOptCompressionAsync(string glbPath) {
            try {
                // GLB формат: 12-byte header + JSON chunk + Binary chunk
                // Читаем JSON секцию и проверяем extensionsUsed

                using var fs = new FileStream(glbPath, FileMode.Open, FileAccess.Read);
                using var br = new BinaryReader(fs);

                // Читаем GLB header
                var magic = br.ReadUInt32(); // 0x46546C67 ("glTF")
                if (magic != 0x46546C67) {
                    Logger.Warn("Invalid GLB magic number");
                    return false;
                }

                var version = br.ReadUInt32();
                var length = br.ReadUInt32();

                // Читаем первый chunk (JSON)
                var chunkLength = br.ReadUInt32();
                var chunkType = br.ReadUInt32(); // 0x4E4F534A ("JSON")

                if (chunkType != 0x4E4F534A) {
                    Logger.Warn("First chunk is not JSON");
                    return false;
                }

                // Читаем JSON данные
                var jsonBytes = br.ReadBytes((int)chunkLength);
                var json = Encoding.UTF8.GetString(jsonBytes);

                // Простая проверка на наличие "EXT_meshopt_compression"
                return json.Contains("EXT_meshopt_compression");
            } catch (Exception ex) {
                Logger.Warn(ex, "Failed to check meshopt compression");
                return false;
            }
        }

        /// <summary>
        /// Декомпрессирует GLB с EXT_meshopt_compression через gltfpack
        /// </summary>
        private async Task<string?> DecompressMeshOptAsync(string inputPath) {
            try {
                // Проверяем кэш
                if (_decompressCache.TryGetValue(inputPath, out var cachedPath)) {
                    if (File.Exists(cachedPath)) {
                        Logger.Info($"Using cached decompressed file: {cachedPath}");
                        return cachedPath;
                    } else {
                        _decompressCache.Remove(inputPath);
                    }
                }

                if (_gltfPackWrapper == null) {
                    Logger.Error("gltfpack not available for decompression");
                    return null;
                }

                // Создаём временный файл
                var tempDir = Path.Combine(Path.GetTempPath(), "TexTool_GlbDecompress");
                Directory.CreateDirectory(tempDir);

                var fileName = Path.GetFileNameWithoutExtension(inputPath);
                var outputPath = Path.Combine(tempDir, $"{fileName}_decompressed.glb");

                Logger.Info($"Decompressing {inputPath} -> {outputPath}");

                // Запускаем gltfpack без сжатия (это убирает EXT_meshopt_compression)
                // gltfpack -i input.glb -o output.glb (без -c флага)
                var startInfo = new System.Diagnostics.ProcessStartInfo {
                    FileName = "gltfpack",
                    Arguments = $"-i \"{inputPath}\" -o \"{outputPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process == null) {
                    Logger.Error("Failed to start gltfpack process");
                    return null;
                }

                await process.WaitForExitAsync();

                if (process.ExitCode == 0 && File.Exists(outputPath)) {
                    Logger.Info($"Successfully decompressed GLB: {outputPath}");

                    // Кэшируем результат
                    _decompressCache[inputPath] = outputPath;

                    return outputPath;
                } else {
                    Logger.Error($"gltfpack decompression failed with exit code {process.ExitCode}");
                    return null;
                }
            } catch (Exception ex) {
                Logger.Error(ex, "Failed to decompress meshopt GLB");
                return null;
            }
        }

        /// <summary>
        /// Очищает кэш декомпрессированных файлов
        /// </summary>
        public void ClearCache() {
            foreach (var cachedFile in _decompressCache.Values) {
                try {
                    if (File.Exists(cachedFile)) {
                        File.Delete(cachedFile);
                    }
                } catch (Exception ex) {
                    Logger.Warn(ex, $"Failed to delete cached file: {cachedFile}");
                }
            }

            _decompressCache.Clear();
            Logger.Info("Cleared decompression cache");
        }

        public void Dispose() {
            ClearCache();
            _assimpContext.Dispose();
        }
    }
}
