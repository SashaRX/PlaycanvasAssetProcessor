using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text;
using System.Linq;
using NLog;
using SharpGLTF.Schema2;
using SharpGLTF.Validation;

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
            public List<Vector2> TextureCoordinates { get; set; } = new();  // UV0
            public List<Vector2> TextureCoordinates2 { get; set; } = new(); // UV1 (lightmap)
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
                // Используем ValidationMode.Skip чтобы игнорировать ошибки текстур
                // (нам нужна только геометрия для viewer)
                var readSettings = new ReadSettings {
                    Validation = ValidationMode.Skip
                };

                ModelRoot model;
                try {
                    model = ModelRoot.Load(pathToLoad, readSettings);
                } catch (Exception ex) when (ex.InnerException is FileNotFoundException || ex is FileNotFoundException) {
                    // Если файл текстуры не найден, пробуем загрузить с кастомным FileReader
                    Logger.Warn($"[SharpGLTF] Texture file not found, trying to load without textures: {ex.Message}");
                    model = LoadWithoutTextures(pathToLoad);
                } catch (Exception ex) when (ex.Message.Contains("PNG") || ex.Message.Contains("Invalid")) {
                    // Если текстура повреждена, пробуем загрузить без текстур
                    Logger.Warn($"[SharpGLTF] Invalid texture data, trying to load without textures: {ex.Message}");
                    model = LoadWithoutTextures(pathToLoad);
                } catch (Exception ex) {
                    // КРИТИЧНО: Обрабатываем любые другие исключения, которые не были обработаны выше
                    // Пытаемся загрузить без текстур как последний fallback
                    Logger.Warn($"[SharpGLTF] Unexpected error during model load, trying to load without textures: {ex.GetType().Name}: {ex.Message}");
                    try {
                        model = LoadWithoutTextures(pathToLoad);
                    } catch (Exception fallbackEx) {
                        // Если fallback тоже не работает, устанавливаем ошибку и возвращаем
                        Logger.Error($"[SharpGLTF] Failed to load model even without textures: {fallbackEx.GetType().Name}: {fallbackEx.Message}");
                        result.Success = false;
                        result.Error = $"Failed to load GLB: {ex.GetType().Name}: {ex.Message}. Fallback also failed: {fallbackEx.Message}";
                        return result;
                    }
                }

                Logger.Info($"[SharpGLTF] Loaded: {model.LogicalMeshes.Count} meshes, {model.LogicalMaterials.Count} materials");

                // Проверяем extensions
                if (model.ExtensionsUsed != null) {
                    Logger.Info($"[SharpGLTF] Extensions used: {string.Join(", ", model.ExtensionsUsed)}");
                }

                // Обрабатываем каждый узел сцены (важно применять его WorldMatrix, иначе оси могут быть перевёрнуты)
                foreach (var node in model.LogicalNodes.Where(n => n.Mesh != null)) {
                    var world = node.WorldMatrix;
                    Logger.Info($"[SharpGLTF] Processing node: {node.Name ?? "unnamed"}, mesh={node.Mesh?.Name ?? "mesh"}, world={world}");

                    // Матрица для преобразования нормалей (inverse transpose без трансляции)
                    if (!Matrix4x4.Invert(world, out var inverted)) {
                        Logger.Warn("[SharpGLTF]   Failed to invert world matrix, using Identity for normals");
                        inverted = Matrix4x4.Identity;
                    }
                    var normalMatrix = Matrix4x4.Transpose(inverted);

                    foreach (var primitive in node.Mesh!.Primitives) {
                        var meshData = new MeshData {
                            Name = node.Mesh.Name ?? "mesh"
                        };

                        // Получаем позиции
                        var positionAccessor = primitive.GetVertexAccessor("POSITION");
                        if (positionAccessor != null) {
                            var positions = positionAccessor.AsVector3Array();
                            foreach (var p in positions) {
                                var transformed = Vector3.Transform(p, world);
                                meshData.Positions.Add(transformed);
                            }
                            Logger.Info($"[SharpGLTF]   Positions: {positions.Count} (with node transform, glTF right-handed preserved)");

                            // Логируем bounds
                            if (meshData.Positions.Count > 0) {
                                var minX = meshData.Positions.Min(p => p.X);
                                var maxX = meshData.Positions.Max(p => p.X);
                                var minY = meshData.Positions.Min(p => p.Y);
                                var maxY = meshData.Positions.Max(p => p.Y);
                                var minZ = meshData.Positions.Min(p => p.Z);
                                var maxZ = meshData.Positions.Max(p => p.Z);
                                Logger.Info($"[SharpGLTF]   Position bounds: X=[{minX:F3}, {maxX:F3}], Y=[{minY:F3}, {maxY:F3}], Z=[{minZ:F3}, {maxZ:F3}]");
                                Logger.Info($"[SharpGLTF]   Model size: {maxX - minX:F3} x {maxY - minY:F3} x {maxZ - minZ:F3}");

                                // Первые 5 позиций
                                Logger.Info($"[SharpGLTF]   First 5 positions (world space):");
                                for (int i = 0; i < Math.Min(5, meshData.Positions.Count); i++) {
                                    var p = meshData.Positions[i];
                                    Logger.Info($"[SharpGLTF]     [{i}]: ({p.X:F4}, {p.Y:F4}, {p.Z:F4})");
                                }
                            }
                        }

                        // Получаем нормали
                        var normalAccessor = primitive.GetVertexAccessor("NORMAL");
                        if (normalAccessor != null) {
                            var normals = normalAccessor.AsVector3Array();
                            foreach (var nrm in normals) {
                                var transformedNormal = Vector3.TransformNormal(nrm, normalMatrix);
                                transformedNormal = Vector3.Normalize(transformedNormal);
                                meshData.Normals.Add(transformedNormal);
                            }
                            Logger.Info($"[SharpGLTF]   Normals: {meshData.Normals.Count} (with node transform, no axis flip)");
                        } else {
                            // Генерируем плоские нормали если нет
                            Logger.Warn($"[SharpGLTF]   No normals, will generate later");
                        }

                        // Получаем UV0 координаты
                        var texCoordAccessor = primitive.GetVertexAccessor("TEXCOORD_0");
                        if (texCoordAccessor != null) {
                            var uvs = texCoordAccessor.AsVector2Array();
                            meshData.TextureCoordinates.AddRange(uvs);
                            Logger.Info($"[SharpGLTF]   UV0: {uvs.Count}");

                            // Логируем UV range
                            if (uvs.Count > 0) {
                                var minU = uvs.Min(uv => uv.X);
                                var maxU = uvs.Max(uv => uv.X);
                                var minV = uvs.Min(uv => uv.Y);
                                var maxV = uvs.Max(uv => uv.Y);
                                Logger.Info($"[SharpGLTF]   UV0 range: U=[{minU:F6}, {maxU:F6}], V=[{minV:F6}, {maxV:F6}]");

                                // Проверяем квантованные UV
                                if (maxU < 0.1f && maxV < 0.1f) {
                                    Logger.Warn($"[SharpGLTF]   WARNING: UV0 values appear QUANTIZED or very small!");
                                }
                            }
                        } else {
                            Logger.Warn($"[SharpGLTF]   No TEXCOORD_0 found");
                        }

                        // Получаем UV1 координаты (lightmap)
                        var texCoord1Accessor = primitive.GetVertexAccessor("TEXCOORD_1");
                        if (texCoord1Accessor != null) {
                            var uvs1 = texCoord1Accessor.AsVector2Array();
                            meshData.TextureCoordinates2.AddRange(uvs1);
                            Logger.Info($"[SharpGLTF]   UV1 (lightmap): {uvs1.Count}");

                            if (uvs1.Count > 0) {
                                var minU = uvs1.Min(uv => uv.X);
                                var maxU = uvs1.Max(uv => uv.X);
                                var minV = uvs1.Min(uv => uv.Y);
                                var maxV = uvs1.Max(uv => uv.Y);
                                Logger.Info($"[SharpGLTF]   UV1 range: U=[{minU:F6}, {maxU:F6}], V=[{minV:F6}, {maxV:F6}]");
                            }
                        }

                        // Получаем индексы
                        var indexAccessor = primitive.IndexAccessor;
                        if (indexAccessor != null) {
                            var indices = indexAccessor.AsIndicesArray();

                            for (int i = 0; i < indices.Count; i++) {
                                meshData.Indices.Add((int)indices[i]);
                            }
                            Logger.Info($"[SharpGLTF]   Indices: {meshData.Indices.Count} ({meshData.Indices.Count / 3} triangles) copied as-is (glTF winding)");
                        } else {
                            // Non-indexed geometry - создаём sequential indices без изменения порядка
                            for (int i = 0; i < meshData.Positions.Count; i++) {
                                meshData.Indices.Add(i);
                            }
                            Logger.Info($"[SharpGLTF]   Non-indexed, created {meshData.Indices.Count} sequential indices (no winding changes)");
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
        /// Загружает GLB без текстур (используется когда текстуры отсутствуют или повреждены)
        /// </summary>
        private ModelRoot LoadWithoutTextures(string glbPath) {
            Logger.Info($"[SharpGLTF] Loading without textures: {glbPath}");

            // Создаём ReadContext с кастомным FileReader который возвращает пустые данные для текстур
            var directory = Path.GetDirectoryName(glbPath) ?? ".";
            var readContext = ReadContext.Create(fileName => {
                var fullPath = Path.Combine(directory, fileName);

                // Для текстур возвращаем минимальный валидный 1x1 PNG
                if (fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)) {
                    Logger.Info($"[SharpGLTF] Skipping texture: {fileName}");
                    // Минимальный 1x1 красный PNG для заглушки
                    return new ArraySegment<byte>(CreateMinimalPng());
                }

                // Для остальных файлов читаем нормально
                if (File.Exists(fullPath)) {
                    return new ArraySegment<byte>(File.ReadAllBytes(fullPath));
                }

                throw new FileNotFoundException($"File not found: {fullPath}");
            });

            readContext.Validation = ValidationMode.Skip;

            using var stream = File.OpenRead(glbPath);
            return readContext.ReadSchema2(stream);
        }

        /// <summary>
        /// Создаёт минимальный валидный 1x1 PNG (красный пиксель)
        /// </summary>
        private static byte[] CreateMinimalPng() {
            // Минимальный 1x1 красный PNG (67 bytes)
            return new byte[] {
                0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // PNG signature
                0x00, 0x00, 0x00, 0x0D, // IHDR length
                0x49, 0x48, 0x44, 0x52, // IHDR
                0x00, 0x00, 0x00, 0x01, // width = 1
                0x00, 0x00, 0x00, 0x01, // height = 1
                0x08, 0x02,             // 8-bit RGB
                0x00, 0x00, 0x00,       // compression, filter, interlace
                0x90, 0x77, 0x53, 0xDE, // IHDR CRC
                0x00, 0x00, 0x00, 0x0C, // IDAT length
                0x49, 0x44, 0x41, 0x54, // IDAT
                0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00, 0x00, // compressed data (red pixel)
                0x01, 0x01, 0x01, 0x00, // IDAT CRC
                0xF8, 0x17, 0xC5, 0xA3,
                0x00, 0x00, 0x00, 0x00, // IEND length
                0x49, 0x45, 0x4E, 0x44, // IEND
                0xAE, 0x42, 0x60, 0x82  // IEND CRC
            };
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

                // Создаём временный файл (v2 - с -tr флагом для удаления текстур)
                var tempDir = Path.Combine(Path.GetTempPath(), "SharpGlbLoader_Decode_v2");
                Directory.CreateDirectory(tempDir);

                var fileName = Path.GetFileNameWithoutExtension(inputPath);
                var outputPath = Path.Combine(tempDir, $"{fileName}_decoded_{Guid.NewGuid():N}.glb");

                Logger.Info($"[SharpGLTF] Decoding via gltfpack: {inputPath} -> {outputPath}");

                // gltfpack -noq декодирует quantization
                // -kv сохраняет все vertex attributes
                // -vtf использует float для UV
                // -vpf использует float для позиций
                // -tr убирает текстуры (они обрабатываются отдельно, для viewer не нужны)
                var startInfo = new ProcessStartInfo {
                    FileName = _gltfPackPath,
                    Arguments = $"-i \"{inputPath}\" -o \"{outputPath}\" -noq -kv -vtf -vpf -tr -v",
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

                // КРИТИЧНО: Читаем stdout/stderr ПЕРЕД WaitForExit чтобы избежать deadlock
                // Если буфер переполнится, процесс зависнет в ожидании чтения
                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();

                process.WaitForExit(30000); // 30 sec timeout

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
