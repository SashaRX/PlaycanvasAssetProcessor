using System.IO;
using System.Text.Json;
using NLog;

namespace AssetProcessor.TextureConversion.Settings {
    /// <summary>
    /// Менеджер для сохранения и загрузки настроек конвертации текстур
    /// </summary>
    public class TextureConversionSettingsManager {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private const string SettingsFileName = "TextureConversionSettings.json";

        /// <summary>
        /// Сохраняет настройки в файл
        /// </summary>
        public static void SaveSettings(GlobalTextureConversionSettings settings, string? filePath = null) {
            try {
                filePath ??= GetDefaultSettingsPath();

                var options = new JsonSerializerOptions {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var json = JsonSerializer.Serialize(settings, options);
                File.WriteAllText(filePath, json);

                Logger.Info($"Texture conversion settings saved to {filePath}");
            } catch (Exception ex) {
                Logger.Error(ex, "Failed to save texture conversion settings");
                throw;
            }
        }

        /// <summary>
        /// Загружает настройки из файла
        /// </summary>
        public static GlobalTextureConversionSettings LoadSettings(string? filePath = null) {
            try {
                filePath ??= GetDefaultSettingsPath();

                if (!File.Exists(filePath)) {
                    Logger.Info("Settings file not found, creating default settings");
                    return CreateDefaultSettings();
                }

                var json = File.ReadAllText(filePath);
                var options = new JsonSerializerOptions {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var settings = JsonSerializer.Deserialize<GlobalTextureConversionSettings>(json, options);

                if (settings == null) {
                    Logger.Warn("Failed to deserialize settings, using defaults");
                    return CreateDefaultSettings();
                }

                Logger.Info($"Texture conversion settings loaded from {filePath}");
                return settings;
            } catch (Exception ex) {
                Logger.Error(ex, "Failed to load texture conversion settings, using defaults");
                return CreateDefaultSettings();
            }
        }

        /// <summary>
        /// Создает настройки по умолчанию
        /// </summary>
        public static GlobalTextureConversionSettings CreateDefaultSettings() {
            return new GlobalTextureConversionSettings {
                ToktxExecutablePath = "toktx",
                DefaultOutputDirectory = "output_textures",
                DefaultPreset = "Balanced",
                MaxParallelTasks = Math.Max(1, Environment.ProcessorCount / 2),
                TextureSettings = new List<TextureConversionSettings>()
            };
        }

        /// <summary>
        /// Получает путь к файлу настроек по умолчанию
        /// </summary>
        private static string GetDefaultSettingsPath() {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appFolder = Path.Combine(appDataPath, "PlayCanvasAssetProcessor");
            Directory.CreateDirectory(appFolder);
            return Path.Combine(appFolder, SettingsFileName);
        }

        /// <summary>
        /// Экспортирует настройки в файл
        /// </summary>
        public static void ExportSettings(GlobalTextureConversionSettings settings, string exportPath) {
            SaveSettings(settings, exportPath);
        }

        /// <summary>
        /// Импортирует настройки из файла
        /// </summary>
        public static GlobalTextureConversionSettings ImportSettings(string importPath) {
            return LoadSettings(importPath);
        }
    }
}
