using System.Numerics;
using NLog;
using SharpGLTF.Schema2;

namespace AssetProcessor.ModelConversion.Viewer {
    /// <summary>
    /// Загрузчик GLB файлов на основе SharpGLTF
    /// Правильно обрабатывает KHR_mesh_quantization и EXT_meshopt_compression
    /// </summary>
    public class SharpGlbLoader : IDisposable {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

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
        /// Загружает GLB файл напрямую через SharpGLTF
        /// SharpGLTF автоматически декодирует KHR_mesh_quantization
        /// </summary>
        public GlbData LoadGlb(string glbPath) {
            var result = new GlbData();

            try {
                Logger.Info($"[SharpGLTF] Loading GLB: {glbPath}");

                // Загружаем модель
                var model = ModelRoot.Load(glbPath);

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

        public void Dispose() {
            // SharpGLTF не требует явного освобождения ресурсов
        }
    }
}
