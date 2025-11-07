using System.IO;
using System.Text.Json;
using AssetProcessor.ModelConversion.Core;

namespace AssetProcessor.ModelConversion.Pipeline {
    /// <summary>
    /// Генератор JSON манифестов для LOD цепочки
    /// Используется рантайм-лоадером для загрузки правильных LOD уровней
    /// </summary>
    public class LodManifestGenerator {
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

            // Создаём структуру манифеста
            var manifest = new Dictionary<string, ModelLodManifest>();

            var lodEntries = new List<LodEntry>();

            foreach (var lodLevel in Enum.GetValues<LodLevel>().OrderBy(l => l)) {
                if (lodFiles.ContainsKey(lodLevel)) {
                    var filePath = lodFiles[lodLevel];
                    var lodSettings = settings.LodChain.FirstOrDefault(l => l.Level == lodLevel);

                    if (lodSettings != null) {
                        // Конвертируем абсолютный путь в относительный (от манифеста)
                        var relativePath = GetRelativePath(outputDirectory, filePath);

                        lodEntries.Add(new LodEntry {
                            Url = relativePath,
                            SwitchThreshold = lodSettings.SwitchThreshold
                        });
                    }
                }
            }

            manifest[modelName] = new ModelLodManifest {
                Lods = lodEntries,
                Hysteresis = settings.LodHysteresis
            };

            // Сохраняем манифест
            var manifestPath = Path.Combine(outputDirectory, "lod-index.json");
            SaveManifest(manifest, manifestPath);

            return manifestPath;
        }

        /// <summary>
        /// Сохраняет манифест в JSON файл
        /// </summary>
        private void SaveManifest(Dictionary<string, ModelLodManifest> manifest, string filePath) {
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
        public static Dictionary<string, ModelLodManifest>? LoadManifest(string filePath) {
            if (!File.Exists(filePath)) return null;

            var json = File.ReadAllText(filePath);
            var options = new JsonSerializerOptions {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            return JsonSerializer.Deserialize<Dictionary<string, ModelLodManifest>>(json, options);
        }
    }

    /// <summary>
    /// Манифест LOD для одной модели
    /// </summary>
    public class ModelLodManifest {
        /// <summary>
        /// Список LOD уровней
        /// </summary>
        public List<LodEntry> Lods { get; set; } = new();

        /// <summary>
        /// Гистерезис для переключения LOD (0.0-1.0)
        /// </summary>
        public float Hysteresis { get; set; }
    }

    /// <summary>
    /// Запись для одного LOD уровня
    /// </summary>
    public class LodEntry {
        /// <summary>
        /// URL/путь к GLB файлу
        /// </summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// Порог переключения (screen coverage или расстояние)
        /// </summary>
        public float SwitchThreshold { get; set; }
    }
}
