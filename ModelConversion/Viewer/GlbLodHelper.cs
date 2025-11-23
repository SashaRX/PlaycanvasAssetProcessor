using System.IO;
using System.Text;
using System.Text.Json;
using AssetProcessor.ModelConversion.Core;
using AssetProcessor.ModelConversion.Pipeline;
using Assimp;
using NLog;

namespace AssetProcessor.ModelConversion.Viewer {
    /// <summary>
    /// Helper для обнаружения и загрузки GLB LOD файлов рядом с FBX моделями
    /// </summary>
    public class GlbLodHelper {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Информация о LOD уровне
        /// </summary>
        public class LodInfo {
            public LodLevel Level { get; set; }
            public string FilePath { get; set; } = string.Empty;
            public int TriangleCount { get; set; }
            public int VertexCount { get; set; }
            public long FileSize { get; set; }
            public string FileSizeFormatted => FormatFileSize(FileSize);

            private static string FormatFileSize(long bytes) {
                if (bytes >= 1024 * 1024) {
                    return $"{bytes / (1024.0 * 1024.0):F1} MB";
                } else if (bytes >= 1024) {
                    return $"{bytes / 1024.0:F0} KB";
                } else {
                    return $"{bytes} B";
                }
            }
        }

        /// <summary>
        /// Ищет GLB LOD файлы рядом с FBX моделью
        /// </summary>
        /// <param name="fbxPath">Путь к FBX файлу</param>
        /// <returns>Словарь: LOD уровень → информация о LOD</returns>
        public static Dictionary<LodLevel, LodInfo> FindGlbLodFiles(string fbxPath) {
            var result = new Dictionary<LodLevel, LodInfo>();

            try {
                var directory = Path.GetDirectoryName(fbxPath);
                var modelName = Path.GetFileNameWithoutExtension(fbxPath);

                if (string.IsNullOrEmpty(directory)) {
                    Logger.Warn($"Cannot determine directory for FBX: {fbxPath}");
                    return result;
                }

                Logger.Info($"Searching for GLB LOD files for: {modelName}");

                // Список директорий для поиска:
                // 1. Поддиректория glb/ (основной путь для конвертированных моделей)
                // 2. Та же директория, что и FBX
                var searchDirectories = new List<string> {
                    Path.Combine(directory, "glb"),
                    directory
                };

                string? searchDir = null;

                // Сначала проверяем наличие манифеста в каждой из директорий
                foreach (var dir in searchDirectories) {
                    var manifestPath = Path.Combine(dir, $"{modelName}_manifest.json");
                    if (File.Exists(manifestPath)) {
                        Logger.Info($"Found manifest: {manifestPath}");
                        var manifestResult = LoadFromManifest(manifestPath, dir);
                        if (manifestResult.Count > 0) {
                            return manifestResult;
                        }
                    }
                }

                // Если манифеста нет, ищем файлы по паттерну
                foreach (var dir in searchDirectories) {
                    if (!Directory.Exists(dir)) {
                        continue;
                    }

                    bool foundInThisDir = false;

                    foreach (var lodLevel in Enum.GetValues<LodLevel>()) {
                        var lodFileName = $"{modelName}_lod{(int)lodLevel}.glb";
                        var lodFilePath = Path.Combine(dir, lodFileName);

                        if (File.Exists(lodFilePath)) {
                            if (!foundInThisDir) {
                                Logger.Info($"Searching in directory: {dir}");
                                foundInThisDir = true;
                            }

                            Logger.Info($"  Found LOD{(int)lodLevel}: {lodFileName}");

                            var lodInfo = new LodInfo {
                                Level = lodLevel,
                                FilePath = lodFilePath,
                                FileSize = new FileInfo(lodFilePath).Length
                            };

                            // Извлекаем информацию о геометрии
                            var metrics = ExtractMeshMetrics(lodFilePath);
                            lodInfo.TriangleCount = metrics.TriangleCount;
                            lodInfo.VertexCount = metrics.VertexCount;

                            result[lodLevel] = lodInfo;
                        }
                    }

                    // Если нашли файлы в этой директории, прекращаем поиск
                    if (result.Count > 0) {
                        break;
                    }
                }

                Logger.Info($"Found {result.Count} GLB LOD files for {modelName}");
            } catch (Exception ex) {
                Logger.Error(ex, $"Failed to find GLB LOD files for: {fbxPath}");
            }

            return result;
        }

        /// <summary>
        /// Загружает LOD информацию из манифеста
        /// </summary>
        private static Dictionary<LodLevel, LodInfo> LoadFromManifest(string manifestPath, string baseDirectory) {
            var result = new Dictionary<LodLevel, LodInfo>();

            try {
                var manifest = LodManifestGenerator.LoadManifest(manifestPath);
                if (manifest == null) {
                    Logger.Warn($"Failed to parse manifest: {manifestPath}");
                    return result;
                }

                Logger.Info($"Loaded manifest for model: {manifest.Name}");

                for (int i = 0; i < manifest.Lods.Count; i++) {
                    var lodEntry = manifest.Lods[i];
                    var lodLevel = (LodLevel)i;

                    // Конвертируем относительный путь в абсолютный
                    var lodFilePath = Path.Combine(baseDirectory, lodEntry.Model);

                    if (File.Exists(lodFilePath)) {
                        var lodInfo = new LodInfo {
                            Level = lodLevel,
                            FilePath = lodFilePath,
                            FileSize = new FileInfo(lodFilePath).Length
                        };

                        // Извлекаем информацию о геометрии
                        var metrics = ExtractMeshMetrics(lodFilePath);
                        lodInfo.TriangleCount = metrics.TriangleCount;
                        lodInfo.VertexCount = metrics.VertexCount;

                        result[lodLevel] = lodInfo;
                        Logger.Info($"  LOD{(int)lodLevel}: {metrics.TriangleCount} tris, {metrics.VertexCount} verts, {lodInfo.FileSizeFormatted}");
                    } else {
                        Logger.Warn($"GLB file not found: {lodFilePath}");
                    }
                }
            } catch (Exception ex) {
                Logger.Error(ex, $"Failed to load LOD info from manifest: {manifestPath}");
            }

            return result;
        }

        /// <summary>
        /// Извлекает информацию о геометрии из GLB файла
        /// </summary>
        private static (int TriangleCount, int VertexCount) ExtractMeshMetrics(string glbPath) {
            try {
                // Читаем GLB header для быстрой проверки
                using var stream = File.OpenRead(glbPath);
                using var reader = new BinaryReader(stream);

                // Read GLB header (12 bytes)
                uint magic = reader.ReadUInt32();
                uint version = reader.ReadUInt32();
                uint length = reader.ReadUInt32();

                // Verify GLB magic number ("glTF")
                if (magic != 0x46546C67) {
                    Logger.Warn($"Invalid GLB file (bad magic): {glbPath}");
                    return (0, 0);
                }

                // Read JSON chunk
                uint chunkLength = reader.ReadUInt32();
                uint chunkType = reader.ReadUInt32();

                // Verify JSON chunk type ("JSON")
                if (chunkType != 0x4E4F534A) {
                    Logger.Warn($"Invalid GLB file (no JSON chunk): {glbPath}");
                    return (0, 0);
                }

                // Read JSON data
                byte[] jsonBytes = reader.ReadBytes((int)chunkLength);
                string jsonString = Encoding.UTF8.GetString(jsonBytes);

                // Parse JSON для извлечения meshes информации
                using var jsonDoc = JsonDocument.Parse(jsonString);
                var root = jsonDoc.RootElement;

                int totalTriangles = 0;
                int totalVertices = 0;

                if (root.TryGetProperty("meshes", out var meshesArray)) {
                    foreach (var mesh in meshesArray.EnumerateArray()) {
                        if (mesh.TryGetProperty("primitives", out var primitivesArray)) {
                            foreach (var primitive in primitivesArray.EnumerateArray()) {
                                // Извлекаем indices accessor
                                if (primitive.TryGetProperty("indices", out var indicesElement)) {
                                    int indicesAccessor = indicesElement.GetInt32();

                                    // Получаем count из accessors
                                    if (root.TryGetProperty("accessors", out var accessorsArray)) {
                                        var accessorsList = accessorsArray.EnumerateArray().ToList();
                                        if (indicesAccessor < accessorsList.Count) {
                                            var accessor = accessorsList[indicesAccessor];
                                            if (accessor.TryGetProperty("count", out var countElement)) {
                                                int indicesCount = countElement.GetInt32();
                                                totalTriangles += indicesCount / 3;
                                            }
                                        }
                                    }
                                }

                                // Извлекаем POSITION accessor для вершин
                                if (primitive.TryGetProperty("attributes", out var attributesElement)) {
                                    if (attributesElement.TryGetProperty("POSITION", out var positionElement)) {
                                        int positionAccessor = positionElement.GetInt32();

                                        // Получаем count из accessors
                                        if (root.TryGetProperty("accessors", out var accessorsArray)) {
                                            var accessorsList = accessorsArray.EnumerateArray().ToList();
                                            if (positionAccessor < accessorsList.Count) {
                                                var accessor = accessorsList[positionAccessor];
                                                if (accessor.TryGetProperty("count", out var countElement)) {
                                                    totalVertices += countElement.GetInt32();
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                return (totalTriangles, totalVertices);

            } catch (Exception ex) {
                Logger.Error(ex, $"Failed to extract mesh metrics from GLB: {glbPath}");
                return (0, 0);
            }
        }

        /// <summary>
        /// Проверяет наличие GLB LOD файлов для FBX модели
        /// </summary>
        public static bool HasGlbLodFiles(string fbxPath) {
            var lodFiles = FindGlbLodFiles(fbxPath);
            return lodFiles.Count > 0;
        }

        /// <summary>
        /// Получает словарь путей к GLB файлам для GlbViewerControl
        /// </summary>
        public static Dictionary<LodLevel, string> GetLodFilePaths(string fbxPath) {
            var lodInfos = FindGlbLodFiles(fbxPath);
            return lodInfos.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.FilePath);
        }
    }
}
