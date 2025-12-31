using System.Text.Json;
using NLog;

namespace AssetProcessor.ModelConversion.Native {
    /// <summary>
    /// Нативный симплификатор мешей с поддержкой UV атрибутов
    /// Использует meshoptimizer через P/Invoke для лучшего сохранения UV seams
    /// </summary>
    public class NativeMeshSimplifier {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Проверяет доступность нативной библиотеки
        /// </summary>
        public static bool IsAvailable() {
            try {
                var available = MeshOptimizer.IsAvailable();
                if (available) {
                    Logger.Info($"Native meshoptimizer available: {MeshOptimizer.GetVersion()}");
                }
                return available;
            } catch (Exception ex) {
                Logger.Debug($"Native meshoptimizer not available: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Упрощает GLB файл с сохранением UV атрибутов
        /// </summary>
        /// <param name="inputPath">Путь к входному GLB</param>
        /// <param name="outputPath">Путь к выходному GLB</param>
        /// <param name="targetRatio">Целевое соотношение треугольников (0.0-1.0)</param>
        /// <param name="uvWeight">Вес UV атрибутов (1.0 стандартный, 2.0+ сильнее сохраняет UV)</param>
        /// <returns>Результат симплификации</returns>
        public static SimplifyResult Simplify(
            string inputPath,
            string outputPath,
            float targetRatio,
            float uvWeight = 1.5f
        ) {
            var result = new SimplifyResult();

            try {
                Logger.Info($"Native simplification: {inputPath} -> {outputPath}");
                Logger.Info($"  Target ratio: {targetRatio:P0}, UV weight: {uvWeight}");

                // Читаем GLB
                var glbData = ReadGlb(inputPath);
                if (glbData == null) {
                    result.Error = "Failed to read GLB file";
                    return result;
                }

                // Извлекаем геометрию
                var meshData = ExtractMeshData(glbData);
                if (meshData == null) {
                    result.Error = "Failed to extract mesh data from GLB";
                    return result;
                }

                Logger.Info($"  Original: {meshData.Indices.Length / 3} triangles, {meshData.Positions.Length / 3} vertices");

                // Симплифицируем
                var options = MeshOptimizer.SimplifyOptions.FromRatio(targetRatio, uvWeight);
                var newIndices = MeshOptimizer.SimplifyWithUvs(
                    meshData.Indices,
                    meshData.Positions,
                    meshData.Uvs,
                    options
                );

                // Оптимизируем vertex cache
                newIndices = MeshOptimizer.OptimizeVertexCache(newIndices, meshData.Positions.Length / 3);

                Logger.Info($"  Simplified: {newIndices.Length / 3} triangles ({(float)newIndices.Length / meshData.Indices.Length:P0})");

                // Записываем новый GLB
                WriteGlb(outputPath, glbData, meshData, newIndices);

                result.Success = true;
                result.OriginalTriangles = meshData.Indices.Length / 3;
                result.SimplifiedTriangles = newIndices.Length / 3;
                result.OutputPath = outputPath;

            } catch (Exception ex) {
                Logger.Error(ex, "Native simplification failed");
                result.Error = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Данные меша для симплификации
        /// </summary>
        private class MeshData {
            public uint[] Indices { get; set; } = Array.Empty<uint>();
            public float[] Positions { get; set; } = Array.Empty<float>();
            public float[]? Uvs { get; set; }
            public float[]? Normals { get; set; }

            // Оригинальные accessor индексы для обновления
            public int PositionAccessor { get; set; }
            public int UvAccessor { get; set; }
            public int NormalAccessor { get; set; }
            public int IndicesAccessor { get; set; }
        }

        /// <summary>
        /// Читает GLB файл
        /// </summary>
        private static GlbData? ReadGlb(string path) {
            try {
                using var stream = File.OpenRead(path);
                using var reader = new BinaryReader(stream);

                // GLB header
                var magic = reader.ReadUInt32();
                if (magic != 0x46546C67) { // 'glTF'
                    Logger.Error($"Invalid GLB magic: 0x{magic:X8}");
                    return null;
                }

                var version = reader.ReadUInt32();
                var length = reader.ReadUInt32();

                // JSON chunk
                var jsonLength = reader.ReadUInt32();
                var jsonType = reader.ReadUInt32();
                var jsonBytes = reader.ReadBytes((int)jsonLength);
                var json = System.Text.Encoding.UTF8.GetString(jsonBytes);

                // BIN chunk
                byte[]? binData = null;
                if (stream.Position < stream.Length) {
                    var binLength = reader.ReadUInt32();
                    var binType = reader.ReadUInt32();
                    binData = reader.ReadBytes((int)binLength);
                }

                return new GlbData {
                    Json = json,
                    BinData = binData
                };
            } catch (Exception ex) {
                Logger.Error(ex, $"Failed to read GLB: {path}");
                return null;
            }
        }

        /// <summary>
        /// Извлекает данные меша из GLB
        /// </summary>
        private static MeshData? ExtractMeshData(GlbData glb) {
            try {
                using var doc = JsonDocument.Parse(glb.Json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("meshes", out var meshes) || meshes.GetArrayLength() == 0) {
                    return null;
                }

                var mesh = meshes[0];
                if (!mesh.TryGetProperty("primitives", out var primitives) || primitives.GetArrayLength() == 0) {
                    return null;
                }

                var primitive = primitives[0];
                if (!primitive.TryGetProperty("attributes", out var attributes)) {
                    return null;
                }

                var result = new MeshData();

                // Индексы
                if (primitive.TryGetProperty("indices", out var indicesAccessor)) {
                    result.IndicesAccessor = indicesAccessor.GetInt32();
                    result.Indices = ReadAccessorAsUInt32(root, glb.BinData!, result.IndicesAccessor);
                }

                // Позиции
                if (attributes.TryGetProperty("POSITION", out var posAccessor)) {
                    result.PositionAccessor = posAccessor.GetInt32();
                    result.Positions = ReadAccessorAsFloat(root, glb.BinData!, result.PositionAccessor);
                }

                // UV
                if (attributes.TryGetProperty("TEXCOORD_0", out var uvAccessor)) {
                    result.UvAccessor = uvAccessor.GetInt32();
                    result.Uvs = ReadAccessorAsFloat(root, glb.BinData!, result.UvAccessor);
                }

                // Нормали
                if (attributes.TryGetProperty("NORMAL", out var normalAccessor)) {
                    result.NormalAccessor = normalAccessor.GetInt32();
                    result.Normals = ReadAccessorAsFloat(root, glb.BinData!, result.NormalAccessor);
                }

                return result;
            } catch (Exception ex) {
                Logger.Error(ex, "Failed to extract mesh data");
                return null;
            }
        }

        /// <summary>
        /// Читает accessor как массив uint
        /// </summary>
        private static uint[] ReadAccessorAsUInt32(JsonElement root, byte[] binData, int accessorIndex) {
            var accessor = root.GetProperty("accessors")[accessorIndex];
            var bufferView = root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];

            var byteOffset = bufferView.TryGetProperty("byteOffset", out var bo) ? bo.GetInt32() : 0;
            var accessorOffset = accessor.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0;
            var count = accessor.GetProperty("count").GetInt32();
            var componentType = accessor.GetProperty("componentType").GetInt32();

            var offset = byteOffset + accessorOffset;
            var result = new uint[count];

            for (int i = 0; i < count; i++) {
                result[i] = componentType switch {
                    5123 => BitConverter.ToUInt16(binData, offset + i * 2), // UNSIGNED_SHORT
                    5125 => BitConverter.ToUInt32(binData, offset + i * 4), // UNSIGNED_INT
                    _ => throw new NotSupportedException($"Unsupported component type: {componentType}")
                };
            }

            return result;
        }

        /// <summary>
        /// Читает accessor как массив float
        /// </summary>
        private static float[] ReadAccessorAsFloat(JsonElement root, byte[] binData, int accessorIndex) {
            var accessor = root.GetProperty("accessors")[accessorIndex];
            var bufferView = root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];

            var byteOffset = bufferView.TryGetProperty("byteOffset", out var bo) ? bo.GetInt32() : 0;
            var accessorOffset = accessor.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0;
            var count = accessor.GetProperty("count").GetInt32();
            var type = accessor.GetProperty("type").GetString();

            var components = type switch {
                "SCALAR" => 1,
                "VEC2" => 2,
                "VEC3" => 3,
                "VEC4" => 4,
                _ => throw new NotSupportedException($"Unsupported type: {type}")
            };

            var offset = byteOffset + accessorOffset;
            var result = new float[count * components];

            for (int i = 0; i < result.Length; i++) {
                result[i] = BitConverter.ToSingle(binData, offset + i * 4);
            }

            return result;
        }

        /// <summary>
        /// Записывает GLB с новыми индексами
        /// </summary>
        private static void WriteGlb(string outputPath, GlbData originalGlb, MeshData meshData, uint[] newIndices) {
            // TODO: Реализовать полную запись GLB с обновлёнными индексами
            // Пока копируем оригинал (для тестирования интеграции)
            // В полной реализации нужно:
            // 1. Обновить indices accessor
            // 2. Пересчитать bufferView
            // 3. Обновить BIN chunk

            Logger.Warn("WriteGlb: Full implementation pending, copying original file");
            File.Copy(originalGlb.SourcePath ?? "", outputPath, overwrite: true);
        }

        private class GlbData {
            public string Json { get; set; } = "";
            public byte[]? BinData { get; set; }
            public string? SourcePath { get; set; }
        }
    }

    /// <summary>
    /// Результат симплификации
    /// </summary>
    public class SimplifyResult {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public int OriginalTriangles { get; set; }
        public int SimplifiedTriangles { get; set; }
        public string? OutputPath { get; set; }

        public float Ratio => OriginalTriangles > 0
            ? (float)SimplifiedTriangles / OriginalTriangles
            : 0;
    }
}
