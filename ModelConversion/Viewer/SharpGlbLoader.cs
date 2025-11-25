using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text;
using NLog;
using SharpGLTF.Schema2;

namespace AssetProcessor.ModelConversion.Viewer {
    /// <summary>
    /// Загрузчик GLB файлов на основе SharpGLTF
    /// Правильно обрабатывает KHR_mesh_quantization через декодирование gltfpack
    /// </summary>
    public class SharpGlbLoader : IDisposable {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly string? _gltfPackPath;
        private readonly Dictionary<string, string> _decodeCache = new();

        /// <summary>
        /// Данные меша для рендеринга в WPF
        /// </summary>
        public class MeshData {
            public List<Vector3> Positions { get; set; } = new();
            public List<Vector3> Normals { get; set; } = new();
            public List<Vector2> TextureCoordinates { get; set; } = new();
            public List<int> Indices { get; set; } = new();
            public string Name { get; set; } = string.Empty;
        }

        /// <summary>
        /// Результат загрузки GLB
        /// </summary>
        public class GlbData {
            public List<MeshData> Meshes { get; set; } = new();
            public bool Success { get; set; }
            public string? Error { get; set; }
        }

        /// <summary>
        /// Создаёт загрузчик с указанием пути к gltfpack
        /// </summary>
        /// <param name="gltfPackPath">Путь к gltfpack.exe для декодирования meshopt compression</param>
        public SharpGlbLoader(string? gltfPackPath = null) {
            _gltfPackPath = gltfPackPath;
        }

        /// <summary>
        /// Загружает GLB файл через SharpGLTF
        /// Если файл содержит EXT_meshopt_compression, сначала декодирует через gltfpack
        /// SharpGLTF автоматически декодирует KHR_mesh_quantization
        /// </summary>
        public GlbData LoadGlb(string glbPath) {
            var result = new GlbData();

            try {
                Logger.Info($"[SharpGLTF] Loading GLB: {glbPath}");

                // Проверяем, содержит ли файл EXT_meshopt_compression
                var pathToLoad = glbPath;
                if (HasMeshOptCompression(glbPath)) {
                    Logger.Info($"[SharpGLTF] GLB contains EXT_meshopt_compression, decoding via gltfpack first...");

                    var decodedPath = DecodeGlbWithGltfpack(glbPath);
                    if (decodedPath == null) {
                        result.Success = false;
                        result.Error = "Failed to decode meshopt compression via gltfpack";
                        return result;
                    }
                    pathToLoad = decodedPath;
                    Logger.Info($"[SharpGLTF] Using decoded file: {pathToLoad}");
                }

                // Загружаем модель (декодированную или оригинальную)
                var model = ModelRoot.Load(pathToLoad);

                Logger.Info($"[SharpGLTF] Loaded: {model.LogicalMeshes.Count} meshes, {model.LogicalMaterials.Count} materials");

                // Проверяем extensions
                if (model.ExtensionsUsed != null) {
                    Logger.Info($"[SharpGLTF] Extensions used: {string.Join(", ", model.ExtensionsUsed)}");
                }

                // Обрабатываем каждый mesh
                foreach (var logicalMesh in model.LogicalMeshes) {
                    Logger.Info($"[SharpGLTF] Processing mesh: {logicalMesh.Name ?? "unnamed"}, {logicalMesh.Primitives.Count} primitives");

                    foreach (var primitive in logicalMesh.Primitives) {
                        var meshData = new MeshData {
                            Name = logicalMesh.Name ?? "mesh"
                        };

                        // Получаем позиции
                        var positionAccessor = primitive.GetVertexAccessor("POSITION");
                        if (positionAccessor != null) {
                            var positions = positionAccessor.AsVector3Array();
                            meshData.Positions.AddRange(positions);
                            Logger.Info($"[SharpGLTF]   Positions: {positions.Count}");

                            // Логируем bounds
                            if (positions.Count > 0) {
                                var minX = positions.Min(p => p.X);
                                var maxX = positions.Max(p => p.X);
                                var minY = positions.Min(p => p.Y);
                                var maxY = positions.Max(p => p.Y);
                                var minZ = positions.Min(p => p.Z);
                                var maxZ = positions.Max(p => p.Z);
                                Logger.Info($"[SharpGLTF]   Position bounds: X=[{minX:F3}, {maxX:F3}], Y=[{minY:F3}, {maxY:F3}], Z=[{minZ:F3}, {maxZ:F3}]");
                                Logger.Info($"[SharpGLTF]   Model size: {maxX - minX:F3} x {maxY - minY:F3} x {maxZ - minZ:F3}");

                                // Первые 5 позиций
                                Logger.Info($"[SharpGLTF]   First 5 positions:");
                                for (int i = 0; i < Math.Min(5, positions.Count); i++) {
                                    Logger.Info($"[SharpGLTF]     [{i}]: ({positions[i].X:F4}, {positions[i].Y:F4}, {positions[i].Z:F4})");
                                }
                            }
                        }

                        // Получаем нормали
                        var normalAccessor = primitive.GetVertexAccessor("NORMAL");
                        if (normalAccessor != null) {
                            var normals = normalAccessor.AsVector3Array();
                            meshData.Normals.AddRange(normals);
                            Logger.Info($"[SharpGLTF]   Normals: {normals.Count}");
                        } else {
                            // Генерируем плоские нормали если нет
                            Logger.Warn($"[SharpGLTF]   No normals, will generate later");
                        }

                        // Получаем UV координаты
                        var texCoordAccessor = primitive.GetVertexAccessor("TEXCOORD_0");
                        if (texCoordAccessor != null) {
                            var uvs = texCoordAccessor.AsVector2Array();
                            meshData.TextureCoordinates.AddRange(uvs);
                            Logger.Info($"[SharpGLTF]   UVs: {uvs.Count}");

                            // Логируем UV range
                            if (uvs.Count > 0) {
                                var minU = uvs.Min(uv => uv.X);
                                var maxU = uvs.Max(uv => uv.X);
                                var minV = uvs.Min(uv => uv.Y);
                                var maxV = uvs.Max(uv => uv.Y);
                                Logger.Info($"[SharpGLTF]   UV range: U=[{minU:F6}, {maxU:F6}], V=[{minV:F6}, {maxV:F6}]");

                                // Проверяем квантованные UV
                                if (maxU < 0.1f && maxV < 0.1f) {
                                    Logger.Warn($"[SharpGLTF]   WARNING: UV values appear QUANTIZED or very small!");
                                }

                                // Первые 5 UV
                                Logger.Info($"[SharpGLTF]   First 5 UVs:");
                                for (int i = 0; i < Math.Min(5, uvs.Count); i++) {
                                    Logger.Info($"[SharpGLTF]     [{i}]: ({uvs[i].X:F6}, {uvs[i].Y:F6})");
                                }
                            }
                        } else {
                            Logger.Warn($"[SharpGLTF]   No TEXCOORD_0 found");
                        }

                        // Получаем индексы
                        var indexAccessor = primitive.IndexAccessor;
                        if (indexAccessor != null) {
                            var indices = indexAccessor.AsIndicesArray();
                            meshData.Indices.AddRange(indices.Select(i => (int)i));
                            Logger.Info($"[SharpGLTF]   Indices: {indices.Count} ({indices.Count / 3} triangles)");
                        } else {
                            // Non-indexed geometry - создаём sequential indices
                            for (int i = 0; i < meshData.Positions.Count; i++) {
                                meshData.Indices.Add(i);
                            }
                            Logger.Info($"[SharpGLTF]   Non-indexed, created {meshData.Indices.Count} sequential indices");
                        }

                        // Генерируем нормали если нет
                        if (meshData.Normals.Count == 0 && meshData.Positions.Count > 0) {
                            meshData.Normals = GenerateFlatNormals(meshData.Positions, meshData.Indices);
                            Logger.Info($"[SharpGLTF]   Generated {meshData.Normals.Count} flat normals");
                        }

                        result.Meshes.Add(meshData);
                    }
                }

                result.Success = true;
                Logger.Info($"[SharpGLTF] Successfully loaded {result.Meshes.Count} mesh primitives");

            } catch (Exception ex) {
                Logger.Error(ex, $"[SharpGLTF] Failed to load GLB: {glbPath}");
                result.Success = false;
                result.Error = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Генерирует плоские нормали для меша
        /// </summary>
        private List<Vector3> GenerateFlatNormals(List<Vector3> positions, List<int> indices) {
            var normals = new Vector3[positions.Count];

            for (int i = 0; i < indices.Count; i += 3) {
                if (i + 2 >= indices.Count) break;

                var i0 = indices[i];
                var i1 = indices[i + 1];
                var i2 = indices[i + 2];

                if (i0 >= positions.Count || i1 >= positions.Count || i2 >= positions.Count) continue;

                var v0 = positions[i0];
                var v1 = positions[i1];
                var v2 = positions[i2];

                var edge1 = v1 - v0;
                var edge2 = v2 - v0;
                var normal = Vector3.Normalize(Vector3.Cross(edge1, edge2));

                normals[i0] = normal;
                normals[i1] = normal;
                normals[i2] = normal;
            }

            return normals.ToList();
        }

        /// <summary>
        /// Проверяет наличие EXT_meshopt_compression в GLB
        /// </summary>
        private bool HasMeshOptCompression(string glbPath) {
            try {
                using var fs = new FileStream(glbPath, FileMode.Open, FileAccess.Read);
                using var br = new BinaryReader(fs);

                // Читаем GLB header
                var magic = br.ReadUInt32(); // 0x46546C67 ("glTF")
                if (magic != 0x46546C67) return false;

                var version = br.ReadUInt32();
                var length = br.ReadUInt32();

                // Читаем первый chunk (JSON)
                var chunkLength = br.ReadUInt32();
                var chunkType = br.ReadUInt32(); // 0x4E4F534A ("JSON")
                if (chunkType != 0x4E4F534A) return false;

                // Читаем JSON данные
                var jsonBytes = br.ReadBytes((int)chunkLength);
                var json = Encoding.UTF8.GetString(jsonBytes);

                return json.Contains("EXT_meshopt_compression");
            } catch (Exception ex) {
                Logger.Warn(ex, $"Failed to check meshopt compression in {glbPath}");
                return false;
            }
        }

        /// <summary>
        /// Декодирует GLB через gltfpack (убирает meshopt compression и quantization)
        /// </summary>
        private string? DecodeGlbWithGltfpack(string inputPath) {
            try {
                // Проверяем кэш
                if (_decodeCache.TryGetValue(inputPath, out var cachedPath)) {
                    if (File.Exists(cachedPath)) {
                        Logger.Info($"[SharpGLTF] Using cached decoded file: {cachedPath}");
                        return cachedPath;
                    }
                    _decodeCache.Remove(inputPath);
                }

                if (string.IsNullOrEmpty(_gltfPackPath) || !File.Exists(_gltfPackPath)) {
                    Logger.Error($"[SharpGLTF] gltfpack not available at: {_gltfPackPath}");
                    return null;
                }

                // Создаём временный файл
                var tempDir = Path.Combine(Path.GetTempPath(), "SharpGlbLoader_Decode");
                Directory.CreateDirectory(tempDir);

                var fileName = Path.GetFileNameWithoutExtension(inputPath);
                var outputPath = Path.Combine(tempDir, $"{fileName}_decoded_{Guid.NewGuid():N}.glb");

                Logger.Info($"[SharpGLTF] Decoding via gltfpack: {inputPath} -> {outputPath}");

                // gltfpack -noq декодирует quantization
                // -kv сохраняет все vertex attributes
                // -vtf использует float для UV
                // -vpf использует float для позиций
                var startInfo = new ProcessStartInfo {
                    FileName = _gltfPackPath,
                    Arguments = $"-i \"{inputPath}\" -o \"{outputPath}\" -noq -kv -vtf -vpf -v",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) {
                    Logger.Error("[SharpGLTF] Failed to start gltfpack process");
                    return null;
                }

                process.WaitForExit(30000); // 30 sec timeout

                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();

                if (!string.IsNullOrEmpty(stdout)) {
                    Logger.Info($"[SharpGLTF] gltfpack stdout: {stdout}");
                }
                if (!string.IsNullOrEmpty(stderr)) {
                    Logger.Info($"[SharpGLTF] gltfpack stderr: {stderr}");
                }

                if (process.ExitCode == 0 && File.Exists(outputPath)) {
                    Logger.Info($"[SharpGLTF] Successfully decoded GLB: {outputPath}");
                    _decodeCache[inputPath] = outputPath;
                    return outputPath;
                } else {
                    Logger.Error($"[SharpGLTF] gltfpack decode failed with exit code {process.ExitCode}");
                    return null;
                }
            } catch (Exception ex) {
                Logger.Error(ex, "[SharpGLTF] Failed to decode GLB via gltfpack");
                return null;
            }
        }

        /// <summary>
        /// Очищает кэш декодированных файлов
        /// </summary>
        public void ClearCache() {
            foreach (var cachedFile in _decodeCache.Values) {
                try {
                    if (File.Exists(cachedFile)) {
                        File.Delete(cachedFile);
                    }
                } catch (Exception ex) {
                    Logger.Warn(ex, $"[SharpGLTF] Failed to delete cached file: {cachedFile}");
                }
            }
            _decodeCache.Clear();
            Logger.Info("[SharpGLTF] Cleared decode cache");
        }

        public void Dispose() {
            ClearCache();
        }
    }
}
