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
        /// Автоматически декодирует quantization и декомпрессирует EXT_meshopt_compression
        /// </summary>
        public async Task<Scene?> LoadGlbAsync(string glbPath) {
            try {
                Logger.Info($"Loading GLB: {glbPath}");

                // CRITICAL FIX: Assimp не поддерживает KHR_mesh_quantization!
                // Всегда декодируем через gltfpack, который правильно декодирует quantization
                Logger.Info("Decoding GLB through gltfpack (removes quantization + meshopt compression)");

                var decodedPath = await DecodeGlbAsync(glbPath);

                if (decodedPath == null) {
                    Logger.Error("Failed to decode GLB");
                    return null;
                }

                // Загружаем декодированный файл
                // НЕ используем FlipUVs - GLB/glTF уже имеет корректную ориентацию UV
                // FlipUVs нужен только для FBX при загрузке
                var postProcess = PostProcessSteps.Triangulate | PostProcessSteps.GenerateSmoothNormals;
                var scene = _assimpContext.ImportFile(decodedPath, postProcess);
                Logger.Info($"Loaded decoded GLB: {scene.MeshCount} meshes, {scene.MaterialCount} materials");

                // DEBUG: Детальная проверка UV координат
                for (int i = 0; i < scene.MeshCount; i++) {
                    var mesh = scene.Meshes[i];
                    Logger.Info($"  Mesh {i}: {mesh.VertexCount} verts, TextureCoordinateChannelCount={mesh.TextureCoordinateChannelCount}, HasTextureCoords(0)={mesh.HasTextureCoords(0)}");

                    // Вычисляем bounding box для диагностики размера модели
                    if (mesh.VertexCount > 0) {
                        float minX = float.MaxValue, maxX = float.MinValue;
                        float minY = float.MaxValue, maxY = float.MinValue;
                        float minZ = float.MaxValue, maxZ = float.MinValue;
                        foreach (var v in mesh.Vertices) {
                            if (v.X < minX) minX = v.X;
                            if (v.X > maxX) maxX = v.X;
                            if (v.Y < minY) minY = v.Y;
                            if (v.Y > maxY) maxY = v.Y;
                            if (v.Z < minZ) minZ = v.Z;
                            if (v.Z > maxZ) maxZ = v.Z;
                        }
                        float sizeX = maxX - minX;
                        float sizeY = maxY - minY;
                        float sizeZ = maxZ - minZ;
                        Logger.Info($"    Position Bounds: X=[{minX:F3}, {maxX:F3}], Y=[{minY:F3}, {maxY:F3}], Z=[{minZ:F3}, {maxZ:F3}]");
                        Logger.Info($"    Model Size: {sizeX:F3} x {sizeY:F3} x {sizeZ:F3}");
                    }

                    if (mesh.HasTextureCoords(0) && mesh.TextureCoordinateChannels[0].Count > 0) {
                        // Вычисляем min/max UV для диагностики
                        float minU = float.MaxValue, maxU = float.MinValue;
                        float minV = float.MaxValue, maxV = float.MinValue;
                        foreach (var uv in mesh.TextureCoordinateChannels[0]) {
                            if (uv.X < minU) minU = uv.X;
                            if (uv.X > maxU) maxU = uv.X;
                            if (uv.Y < minV) minV = uv.Y;
                            if (uv.Y > maxV) maxV = uv.Y;
                        }
                        Logger.Info($"    UV Range: U=[{minU:F6}, {maxU:F6}], V=[{minV:F6}, {maxV:F6}]");

                        // Проверяем если UV квантованы (очень маленький диапазон)
                        float uvMaxRange = Math.Max(maxU, maxV);
                        if (uvMaxRange < 0.1f) {
                            Logger.Warn($"    WARNING: UV values appear QUANTIZED! Max={uvMaxRange:F6}");
                            Logger.Warn($"    gltfpack -noq НЕ декодировал квантование UV!");
                        }

                        // Первые 5 UV для примера
                        Logger.Info($"    First 5 UVs:");
                        for (int j = 0; j < Math.Min(5, mesh.TextureCoordinateChannels[0].Count); j++) {
                            var uv = mesh.TextureCoordinateChannels[0][j];
                            Logger.Info($"      UV[{j}]: ({uv.X:F6}, {uv.Y:F6})");
                        }
                    }
                }

                return scene;
            } catch (Exception ex) {
                Logger.Error(ex, $"Failed to load GLB: {glbPath}");
                return null;
            }
        }

        /// <summary>
        /// Декодирует GLB файл через gltfpack (убирает quantization и meshopt compression)
        /// </summary>
        private async Task<string?> DecodeGlbAsync(string inputPath) {
            try {
                // Проверяем кэш
                if (_decompressCache.TryGetValue(inputPath, out var cachedPath)) {
                    if (File.Exists(cachedPath)) {
                        Logger.Info($"Using cached decoded file: {cachedPath}");
                        return cachedPath;
                    } else {
                        _decompressCache.Remove(inputPath);
                    }
                }

                if (_gltfPackWrapper == null) {
                    Logger.Error("gltfpack not available for decoding");
                    return null;
                }

                // Создаём временный файл
                var tempDir = Path.Combine(Path.GetTempPath(), "TexTool_GlbDecode");
                Directory.CreateDirectory(tempDir);

                var fileName = Path.GetFileNameWithoutExtension(inputPath);
                var outputPath = Path.Combine(tempDir, $"{fileName}_decoded.glb");

                Logger.Info($"Decoding GLB: {inputPath} -> {outputPath}");

                // DEBUG: Проверяем ОРИГИНАЛЬНЫЙ GLB перед декодированием
                Logger.Info("=== Inspecting ORIGINAL GLB (before decode) ===");
                await InspectGlbAttributesAsync(inputPath);

                // gltfpack с -noq декодирует quantization (KHR_mesh_quantization)
                // Флаг -noq означает "не применять квантование", что деквантует данные в float
                // Флаг -kv сохраняет ВСЕ vertex attributes (включая UV), даже если они не используются материалом
                var startInfo = new System.Diagnostics.ProcessStartInfo {
                    FileName = _gltfPackWrapper.ExecutablePath,
                    Arguments = $"-i \"{inputPath}\" -o \"{outputPath}\" -noq -kv -v",
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

                // Читаем вывод gltfpack для диагностики
                var stdout = await process.StandardOutput.ReadToEndAsync();
                var stderr = await process.StandardError.ReadToEndAsync();

                if (!string.IsNullOrEmpty(stdout)) {
                    Logger.Info($"gltfpack stdout: {stdout}");
                }
                if (!string.IsNullOrEmpty(stderr)) {
                    Logger.Info($"gltfpack stderr: {stderr}");
                }

                if (process.ExitCode == 0 && File.Exists(outputPath)) {
                    Logger.Info($"Successfully decoded GLB: {outputPath}");

                    // DEBUG: Проверяем, есть ли TEXCOORD в декодированном GLB
                    Logger.Info("=== Inspecting DECODED GLB (after gltfpack -noq) ===");
                    await InspectGlbAttributesAsync(outputPath);

                    // Кэшируем результат
                    _decompressCache[inputPath] = outputPath;

                    return outputPath;
                } else {
                    Logger.Error($"gltfpack decode failed with exit code {process.ExitCode}");
                    return null;
                }
            } catch (Exception ex) {
                Logger.Error(ex, "Failed to decode GLB");
                return null;
            }
        }

        /// <summary>
        /// Инспектирует атрибуты в GLB файле (проверяет наличие TEXCOORD)
        /// </summary>
        private async Task InspectGlbAttributesAsync(string glbPath) {
            try {
                using var fs = new FileStream(glbPath, FileMode.Open, FileAccess.Read);
                using var br = new BinaryReader(fs);

                // Читаем GLB header
                var magic = br.ReadUInt32();
                if (magic != 0x46546C67) return;

                var version = br.ReadUInt32();
                var length = br.ReadUInt32();

                // Читаем первый chunk (JSON)
                var chunkLength = br.ReadUInt32();
                var chunkType = br.ReadUInt32();

                if (chunkType != 0x4E4F534A) return;

                // Читаем JSON данные
                var jsonBytes = br.ReadBytes((int)chunkLength);
                var json = Encoding.UTF8.GetString(jsonBytes);

                Logger.Info("=== GLB JSON Content (first 2000 chars) ===");
                Logger.Info(json.Length > 2000 ? json.Substring(0, 2000) + "..." : json);

                // Проверяем наличие TEXCOORD
                var hasTexCoord = json.Contains("TEXCOORD");
                Logger.Info($"GLB contains TEXCOORD attributes: {hasTexCoord}");

                if (hasTexCoord) {
                    // Подсчитываем количество TEXCOORD упоминаний
                    var count = System.Text.RegularExpressions.Regex.Matches(json, "TEXCOORD").Count;
                    Logger.Info($"Found {count} TEXCOORD references in GLB JSON");
                }
            } catch (Exception ex) {
                Logger.Warn(ex, "Failed to inspect GLB attributes");
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
