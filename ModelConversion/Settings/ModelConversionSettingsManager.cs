using System.IO;
using System.Text.Json;

namespace AssetProcessor.ModelConversion.Settings {
    /// <summary>
    /// Менеджер для сохранения/загрузки настроек конвертации моделей
    /// </summary>
    public static class ModelConversionSettingsManager {
        private static readonly string SettingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TexTool",
            "ModelConversionSettings.json"
        );

        /// <summary>
        /// Сохраняет настройки в JSON файл
        /// </summary>
        public static void SaveSettings(GlobalModelConversionSettings settings) {
            try {
                var directory = Path.GetDirectoryName(SettingsFilePath);
                if (!string.IsNullOrEmpty(directory)) {
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var json = JsonSerializer.Serialize(settings, options);
                File.WriteAllText(SettingsFilePath, json);
            } catch {
                // Ignore save errors
            }
        }

        /// <summary>
        /// Загружает настройки из JSON файла
        /// </summary>
        public static GlobalModelConversionSettings LoadSettings() {
            try {
                if (File.Exists(SettingsFilePath)) {
                    var json = File.ReadAllText(SettingsFilePath);
                    var options = new JsonSerializerOptions {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    };

                    var settings = JsonSerializer.Deserialize<GlobalModelConversionSettings>(json, options);
                    if (settings != null) {
                        return settings;
                    }
                }
            } catch {
                // Ignore load errors - return defaults
            }

            return new GlobalModelConversionSettings();
        }
    }
}
