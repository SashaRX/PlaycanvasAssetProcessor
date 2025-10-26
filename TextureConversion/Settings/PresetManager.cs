using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace AssetProcessor.TextureConversion.Settings {
    /// <summary>
    /// Менеджер для управления пресетами конвертации текстур
    /// </summary>
    public class PresetManager {
        private const string PRESETS_FILENAME = "texture_presets.json";
        private static readonly string PresetsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TexTool",
            PRESETS_FILENAME
        );

        private List<TextureConversionPreset> _presets;

        public PresetManager() {
            _presets = new List<TextureConversionPreset>();
            LoadPresets();
        }

        /// <summary>
        /// Возвращает все доступные пресеты (встроенные + пользовательские)
        /// </summary>
        public List<TextureConversionPreset> GetAllPresets() {
            return _presets;
        }

        /// <summary>
        /// Возвращает пресет по имени
        /// </summary>
        public TextureConversionPreset? GetPreset(string name) {
            return _presets.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Добавляет новый пользовательский пресет
        /// </summary>
        public bool AddPreset(TextureConversionPreset preset) {
            if (string.IsNullOrWhiteSpace(preset.Name)) {
                throw new ArgumentException("Preset name cannot be empty");
            }

            if (_presets.Any(p => p.Name.Equals(preset.Name, StringComparison.OrdinalIgnoreCase))) {
                throw new InvalidOperationException($"Preset with name '{preset.Name}' already exists");
            }

            preset.IsBuiltIn = false;
            _presets.Add(preset);
            SavePresets();
            return true;
        }

        /// <summary>
        /// Обновляет существующий пользовательский пресет
        /// </summary>
        public bool UpdatePreset(string oldName, TextureConversionPreset updatedPreset) {
            var existingPreset = _presets.FirstOrDefault(p => p.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase));

            if (existingPreset == null) {
                throw new InvalidOperationException($"Preset '{oldName}' not found");
            }

            if (existingPreset.IsBuiltIn) {
                throw new InvalidOperationException("Cannot modify built-in presets");
            }

            // Check if new name conflicts with another preset
            if (!oldName.Equals(updatedPreset.Name, StringComparison.OrdinalIgnoreCase)) {
                if (_presets.Any(p => p.Name.Equals(updatedPreset.Name, StringComparison.OrdinalIgnoreCase))) {
                    throw new InvalidOperationException($"Preset with name '{updatedPreset.Name}' already exists");
                }
            }

            _presets.Remove(existingPreset);
            updatedPreset.IsBuiltIn = false;
            _presets.Add(updatedPreset);
            SavePresets();
            return true;
        }

        /// <summary>
        /// Удаляет пользовательский пресет
        /// </summary>
        public bool DeletePreset(string name) {
            var preset = _presets.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (preset == null) {
                return false;
            }

            if (preset.IsBuiltIn) {
                throw new InvalidOperationException("Cannot delete built-in presets");
            }

            _presets.Remove(preset);
            SavePresets();
            return true;
        }

        /// <summary>
        /// Загружает пресеты из JSON файла
        /// </summary>
        private void LoadPresets() {
            _presets.Clear();

            // Всегда добавляем встроенные пресеты
            _presets.AddRange(TextureConversionPreset.GetBuiltInPresets());

            // Загружаем пользовательские пресеты из JSON
            if (File.Exists(PresetsFilePath)) {
                try {
                    string json = File.ReadAllText(PresetsFilePath);
                    var userPresets = JsonConvert.DeserializeObject<List<TextureConversionPreset>>(json);

                    if (userPresets != null) {
                        foreach (var preset in userPresets) {
                            // Убеждаемся что пользовательские пресеты не помечены как встроенные
                            preset.IsBuiltIn = false;

                            // Проверяем что имя не конфликтует со встроенными
                            if (!_presets.Any(p => p.Name.Equals(preset.Name, StringComparison.OrdinalIgnoreCase))) {
                                _presets.Add(preset);
                            }
                        }
                    }
                } catch (Exception ex) {
                    // Логируем ошибку, но продолжаем работу с встроенными пресетами
                    Console.WriteLine($"Error loading presets: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Сохраняет пользовательские пресеты в JSON файл
        /// </summary>
        private void SavePresets() {
            try {
                // Создаем директорию если её нет
                var directory = Path.GetDirectoryName(PresetsFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) {
                    Directory.CreateDirectory(directory);
                }

                // Сохраняем только пользовательские пресеты
                var userPresets = _presets.Where(p => !p.IsBuiltIn).ToList();
                string json = JsonConvert.SerializeObject(userPresets, Formatting.Indented);
                File.WriteAllText(PresetsFilePath, json);
            } catch (Exception ex) {
                throw new InvalidOperationException($"Failed to save presets: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Сбрасывает все пользовательские пресеты (оставляет только встроенные)
        /// </summary>
        public void ResetToDefaults() {
            _presets.RemoveAll(p => !p.IsBuiltIn);
            SavePresets();
        }

        /// <summary>
        /// Экспортирует пресеты в файл
        /// </summary>
        public void ExportPresets(string filePath, bool includeBuiltIn = false) {
            var presetsToExport = includeBuiltIn
                ? _presets
                : _presets.Where(p => !p.IsBuiltIn).ToList();

            string json = JsonConvert.SerializeObject(presetsToExport, Formatting.Indented);
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// Импортирует пресеты из файла
        /// </summary>
        public int ImportPresets(string filePath, bool overwriteExisting = false) {
            if (!File.Exists(filePath)) {
                throw new FileNotFoundException($"File not found: {filePath}");
            }

            string json = File.ReadAllText(filePath);
            var importedPresets = JsonConvert.DeserializeObject<List<TextureConversionPreset>>(json);

            if (importedPresets == null || importedPresets.Count == 0) {
                return 0;
            }

            int importedCount = 0;

            foreach (var preset in importedPresets) {
                preset.IsBuiltIn = false; // Импортированные пресеты всегда пользовательские

                var existing = _presets.FirstOrDefault(p => p.Name.Equals(preset.Name, StringComparison.OrdinalIgnoreCase));

                if (existing != null) {
                    if (existing.IsBuiltIn) {
                        // Нельзя перезаписать встроенный пресет
                        continue;
                    }

                    if (overwriteExisting) {
                        _presets.Remove(existing);
                        _presets.Add(preset);
                        importedCount++;
                    }
                } else {
                    _presets.Add(preset);
                    importedCount++;
                }
            }

            if (importedCount > 0) {
                SavePresets();
            }

            return importedCount;
        }
    }
}
