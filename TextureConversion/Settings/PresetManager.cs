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
        private HashSet<string> _hiddenBuiltInPresets; // Список скрытых встроенных пресетов

        public PresetManager() {
            _presets = new List<TextureConversionPreset>();
            _hiddenBuiltInPresets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
        /// Находит подходящий пресет по имени файла (проверяет постфиксы)
        /// </summary>
        public TextureConversionPreset? FindPresetByFileName(string fileName) {
            // Сначала проверяем пресеты с постфиксами (кроме Default пресетов)
            return _presets.FirstOrDefault(p => p.MatchesFileName(fileName));
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
        /// Обновляет существующий пресет
        /// Если пресет был встроенным, создается его пользовательская версия
        /// </summary>
        public bool UpdatePreset(string oldName, TextureConversionPreset updatedPreset) {
            var existingPreset = _presets.FirstOrDefault(p => p.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase));

            if (existingPreset == null) {
                throw new InvalidOperationException($"Preset '{oldName}' not found");
            }

            bool wasBuiltIn = existingPreset.IsBuiltIn;

            // Check if new name conflicts with another preset
            if (!oldName.Equals(updatedPreset.Name, StringComparison.OrdinalIgnoreCase)) {
                if (_presets.Any(p => p.Name.Equals(updatedPreset.Name, StringComparison.OrdinalIgnoreCase))) {
                    throw new InvalidOperationException($"Preset with name '{updatedPreset.Name}' already exists");
                }
            }

            _presets.Remove(existingPreset);

            // Если был встроенный, сохраняем как пользовательский (переопределяем встроенный)
            updatedPreset.IsBuiltIn = false;
            _presets.Add(updatedPreset);
            SavePresets();

            // Перезагружаем, чтобы встроенные пресеты были в начале списка
            if (wasBuiltIn) {
                LoadPresets();
            }

            return true;
        }

        /// <summary>
        /// Удаляет пресет
        /// Для встроенных пресетов: добавляет в список скрытых
        /// Для пользовательских пресетов с именем встроенного: удаляет кастомизацию, восстанавливает встроенный
        /// Для обычных пользовательских пресетов: просто удаляет
        /// </summary>
        public bool DeletePreset(string name) {
            var preset = _presets.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (preset == null) {
                return false;
            }

            if (preset.IsBuiltIn) {
                // Это встроенный пресет - добавляем в список скрытых
                _hiddenBuiltInPresets.Add(name);
                _presets.Remove(preset);
                SavePresets();
                return true;
            } else {
                // Это пользовательский пресет
                // Проверяем, переопределяет ли он встроенный
                var builtInPresets = TextureConversionPreset.GetBuiltInPresets();
                bool isOverridingBuiltIn = builtInPresets.Any(p =>
                    p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

                _presets.Remove(preset);
                SavePresets();

                if (isOverridingBuiltIn) {
                    // Перезагружаем, чтобы восстановить встроенный пресет
                    LoadPresets();
                }

                return true;
            }
        }

        /// <summary>
        /// Загружает пресеты из JSON файла
        /// </summary>
        private void LoadPresets() {
            _presets.Clear();

            // Загружаем сохраненные данные (пользовательские пресеты + скрытые встроенные)
            PresetData? savedData = null;
            if (File.Exists(PresetsFilePath)) {
                try {
                    string json = File.ReadAllText(PresetsFilePath);

                    // Пробуем загрузить как новый формат (PresetData)
                    try {
                        savedData = JsonConvert.DeserializeObject<PresetData>(json);
                    } catch {
                        // Не получилось как PresetData, пробуем старый формат
                        savedData = null;
                    }

                    // Если получилось null или UserPresets == null, пробуем старый формат (просто массив)
                    if (savedData == null || savedData.UserPresets == null) {
                        try {
                            var oldFormatPresets = JsonConvert.DeserializeObject<List<TextureConversionPreset>>(json);
                            if (oldFormatPresets != null && oldFormatPresets.Count > 0) {
                                // Мигрируем старый формат в новый
                                savedData = new PresetData {
                                    UserPresets = oldFormatPresets,
                                    HiddenBuiltInPresets = new List<string>()
                                };
                                Console.WriteLine($"Migrated {oldFormatPresets.Count} presets from old format to new format");
                            }
                        } catch (Exception ex) {
                            Console.WriteLine($"Error loading old format presets: {ex.Message}");
                        }
                    }
                } catch (Exception ex) {
                    // Логируем ошибку, но продолжаем работу
                    Console.WriteLine($"Error loading presets file: {ex.Message}");
                }
            }

            // Восстанавливаем список скрытых встроенных пресетов
            _hiddenBuiltInPresets.Clear();
            if (savedData?.HiddenBuiltInPresets != null) {
                foreach (var name in savedData.HiddenBuiltInPresets) {
                    _hiddenBuiltInPresets.Add(name);
                }
            }

            // Добавляем встроенные пресеты (кроме скрытых)
            var builtInPresets = TextureConversionPreset.GetBuiltInPresets();
            foreach (var preset in builtInPresets) {
                if (!_hiddenBuiltInPresets.Contains(preset.Name)) {
                    _presets.Add(preset);
                }
            }

            // Загружаем пользовательские пресеты
            if (savedData?.UserPresets != null) {
                foreach (var preset in savedData.UserPresets) {
                    // Убеждаемся что пользовательские пресеты не помечены как встроенные
                    preset.IsBuiltIn = false;

                    // Если пользовательский пресет имеет то же имя что и встроенный,
                    // заменяем встроенный (это позволяет переопределять встроенные пресеты)
                    var existingBuiltIn = _presets.FirstOrDefault(p =>
                        p.Name.Equals(preset.Name, StringComparison.OrdinalIgnoreCase) && p.IsBuiltIn);

                    if (existingBuiltIn != null) {
                        // Заменяем встроенный пресет пользовательским
                        int index = _presets.IndexOf(existingBuiltIn);
                        _presets[index] = preset;
                    } else {
                        // Добавляем новый пользовательский пресет
                        _presets.Add(preset);
                    }
                }
            }
        }

        /// <summary>
        /// Сохраняет пользовательские пресеты и список скрытых встроенных в JSON файл
        /// </summary>
        private void SavePresets() {
            try {
                // Создаем директорию если её нет
                var directory = Path.GetDirectoryName(PresetsFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) {
                    Directory.CreateDirectory(directory);
                }

                // Создаем объект с данными для сохранения
                var data = new PresetData {
                    UserPresets = _presets.Where(p => !p.IsBuiltIn).ToList(),
                    HiddenBuiltInPresets = _hiddenBuiltInPresets.ToList()
                };

                string json = JsonConvert.SerializeObject(data, Formatting.Indented);
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
            _hiddenBuiltInPresets.Clear();
            SavePresets();
            LoadPresets(); // Перезагружаем чтобы показать все встроенные пресеты
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

    /// <summary>
    /// Данные для сериализации пресетов (пользовательские + скрытые встроенные)
    /// </summary>
    internal class PresetData {
        public List<TextureConversionPreset> UserPresets { get; set; } = new();
        public List<string> HiddenBuiltInPresets { get; set; } = new();
    }
}
