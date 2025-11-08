using System.IO;
using System.Text;
using System.Text.Json;
using AssetProcessor.ModelConversion.Core;
using NLog;

namespace AssetProcessor.ModelConversion.Pipeline {
    /// <summary>
    /// Генератор JSON манифестов для LOD цепочки
    /// Используется рантайм-лоадером для загрузки правильных LOD уровней
    /// </summary>
    public class LodManifestGenerator {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        /// <summary>
        /// Генерирует манифест для модели с LOD цепочкой
        /// </summary>
        /// <param name="modelName">Имя модели</param>
        /// <param name="lodFiles">Словарь: LOD уровень → путь к GLB файлу</param>
        /// <param name="settings">Настройки конвертации (для гистерезиса)</param>
        /// <param name="outputDirectory">Директория для выходного файла манифеста</param>
        /// <returns>Путь к сгенерированному манифесту</returns>
        public string GenerateManifest(
            string modelName,
            Dictionary<LodLevel, string> lodFiles,
            ModelConversionSettings settings,
            string outputDirectory) {

            var lodEntries = new List<LodEntry>();

            foreach (var lodLevel in Enum.GetValues<LodLevel>().OrderBy(l => l)) {
                if (lodFiles.ContainsKey(lodLevel)) {
                    var filePath = lodFiles[lodLevel];
                    var lodSettings = settings.LodChain.FirstOrDefault(l => l.Level == lodLevel);

                    if (lodSettings != null) {
                        // Конвертируем абсолютный путь в относительный (от манифеста)
                        var relativePath = GetRelativePath(outputDirectory, filePath);

                        // Конвертируем switchThreshold в distance
                        // LOD0 всегда distance=0, остальные пропорционально
                        float distance = lodLevel == LodLevel.LOD0 ? 0f : (1f - lodSettings.SwitchThreshold) * 100f;

                        // Извлекаем материалы из GLB (заглушка, будет реализовано позже)
                        var materials = ExtractMaterials(filePath);

                        lodEntries.Add(new LodEntry {
                            Model = relativePath,
                            Distance = distance,
                            Materials = materials
                        });
                    }
                }
            }

            // Создаём структуру манифеста (новый формат)
            var manifest = new ModelLodManifest {
                Id = $"id_{modelName}",
                Name = modelName,
                Lods = lodEntries
            };

            // Сохраняем манифест
            var manifestPath = Path.Combine(outputDirectory, $"{modelName}_manifest.json");
            SaveManifest(manifest, manifestPath);

            return manifestPath;
        }

        /// <summary>
        /// Извлекает список материалов из GLB файла
        /// </summary>
        private List<string> ExtractMaterials(string glbPath) {
            try {
                if (!File.Exists(glbPath)) {
                    Logger.Warn($"GLB file not found: {glbPath}");
                    return new List<string> { "default_material" };
                }

                using var stream = File.OpenRead(glbPath);
                using var reader = new BinaryReader(stream);

                // Read GLB header (12 bytes)
                uint magic = reader.ReadUInt32();
                uint version = reader.ReadUInt32();
                uint length = reader.ReadUInt32();

                // Verify GLB magic number ("glTF")
                if (magic != 0x46546C67) {
                    Logger.Warn($"Invalid GLB file (bad magic): {glbPath}");
                    return new List<string> { "default_material" };
                }

                // Read JSON chunk
                uint chunkLength = reader.ReadUInt32();
                uint chunkType = reader.ReadUInt32();

                // Verify JSON chunk type ("JSON")
                if (chunkType != 0x4E4F534A) {
                    Logger.Warn($"Invalid GLB file (no JSON chunk): {glbPath}");
                    return new List<string> { "default_material" };
                }

                // Read JSON data
                byte[] jsonBytes = reader.ReadBytes((int)chunkLength);
                string jsonString = Encoding.UTF8.GetString(jsonBytes);

                // Parse JSON to extract materials
                using var jsonDoc = JsonDocument.Parse(jsonString);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("materials", out var materialsArray)) {
                    var materialNames = new List<string>();

                    foreach (var material in materialsArray.EnumerateArray()) {
                        if (material.TryGetProperty("name", out var nameElement)) {
                            var name = nameElement.GetString();
                            if (!string.IsNullOrEmpty(name)) {
                                materialNames.Add(name);
                            }
                        } else {
                            // Material без имени - генерируем
                            materialNames.Add($"material_{materialNames.Count}");
                        }
                    }

                    if (materialNames.Count > 0) {
                        Logger.Info($"Extracted {materialNames.Count} materials from {Path.GetFileName(glbPath)}");
                        return materialNames;
                    }
                }

                // Если материалов нет, возвращаем дефолтный
                Logger.Info($"No materials found in {Path.GetFileName(glbPath)}, using default");
                return new List<string> { "default_material" };

            } catch (Exception ex) {
                Logger.Error(ex, $"Failed to extract materials from {glbPath}");
                return new List<string> { "default_material" };
            }
        }

        /// <summary>
        /// Сохраняет манифест в JSON файл
        /// </summary>
        private void SaveManifest(ModelLodManifest manifest, string filePath) {
            var options = new JsonSerializerOptions {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(manifest, options);

            // Создаём директорию если не существует
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory)) {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// Получает относительный путь между двумя путями
        /// </summary>
        private string GetRelativePath(string fromPath, string toPath) {
            if (string.IsNullOrEmpty(fromPath)) return toPath;
            if (string.IsNullOrEmpty(toPath)) return string.Empty;

            // Нормализуем пути
            var fromUri = new Uri(Path.GetFullPath(fromPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
            var toUri = new Uri(Path.GetFullPath(toPath));

            var relativeUri = fromUri.MakeRelativeUri(toUri);
            var relativePath = Uri.UnescapeDataString(relativeUri.ToString());

            // Конвертируем обратные слеши в прямые (для web)
            return relativePath.Replace('\\', '/');
        }

        /// <summary>
        /// Загружает манифест из JSON файла
        /// </summary>
        public static ModelLodManifest? LoadManifest(string filePath) {
            if (!File.Exists(filePath)) return null;

            var json = File.ReadAllText(filePath);
            var options = new JsonSerializerOptions {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            return JsonSerializer.Deserialize<ModelLodManifest>(json, options);
        }
    }

    /// <summary>
    /// Манифест LOD для одной модели
    /// </summary>
    public class ModelLodManifest {
        /// <summary>
        /// Идентификатор модели
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Имя модели
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Список LOD уровней
        /// </summary>
        public List<LodEntry> Lods { get; set; } = new();
    }

    /// <summary>
    /// Запись для одного LOD уровня
    /// </summary>
    public class LodEntry {
        /// <summary>
        /// Расстояние переключения LOD (в метрах)
        /// </summary>
        public float Distance { get; set; }

        /// <summary>
        /// Путь к GLB файлу модели
        /// </summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>
        /// Список используемых материалов
        /// </summary>
        public List<string> Materials { get; set; } = new();
    }
}
