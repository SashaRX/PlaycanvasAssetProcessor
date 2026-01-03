using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using AssetProcessor.TextureConversion.Core;

namespace AssetProcessor.TextureConversion.Settings;

/// <summary>
/// Менеджер для управления ORM пресетами
/// </summary>
public class ORMPresetManager {
    private const string PRESETS_FILENAME = "orm_presets.json";
    private static readonly string PresetsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TexTool",
        PRESETS_FILENAME
    );

    private List<ORMSettings> _presets;
    private HashSet<string> _hiddenBuiltInPresets;

    private static ORMPresetManager? _instance;
    public static ORMPresetManager Instance => _instance ??= new ORMPresetManager();

    public ORMPresetManager() {
        _presets = new List<ORMSettings>();
        _hiddenBuiltInPresets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        LoadPresets();
    }

    /// <summary>
    /// Возвращает все доступные пресеты
    /// </summary>
    public List<ORMSettings> GetAllPresets() {
        return _presets.ToList();
    }

    /// <summary>
    /// Возвращает пресет по имени
    /// </summary>
    public ORMSettings? GetPreset(string name) {
        return _presets.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Возвращает пресет по умолчанию (Standard)
    /// </summary>
    public ORMSettings GetDefaultPreset() {
        return GetPreset("Standard") ?? ORMSettings.CreateStandard();
    }

    /// <summary>
    /// Добавляет новый пользовательский пресет
    /// </summary>
    public bool AddPreset(ORMSettings preset) {
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
    /// </summary>
    public bool UpdatePreset(string oldName, ORMSettings updatedPreset) {
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
        updatedPreset.IsBuiltIn = false;
        _presets.Add(updatedPreset);
        SavePresets();

        if (wasBuiltIn) {
            LoadPresets();
        }

        return true;
    }

    /// <summary>
    /// Удаляет пресет
    /// </summary>
    public bool DeletePreset(string name) {
        var preset = _presets.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (preset == null) {
            return false;
        }

        if (preset.IsBuiltIn) {
            _hiddenBuiltInPresets.Add(name);
            _presets.Remove(preset);
            SavePresets();
            return true;
        } else {
            var builtInPresets = ORMSettings.GetBuiltInPresets();
            bool isOverridingBuiltIn = builtInPresets.Any(p =>
                p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            _presets.Remove(preset);
            SavePresets();

            if (isOverridingBuiltIn) {
                LoadPresets();
            }

            return true;
        }
    }

    /// <summary>
    /// Дублирует пресет с новым именем
    /// </summary>
    public ORMSettings DuplicatePreset(string sourceName, string newName) {
        var source = GetPreset(sourceName);
        if (source == null) {
            throw new InvalidOperationException($"Preset '{sourceName}' not found");
        }

        var newPreset = source.Clone();
        newPreset.Name = newName;
        newPreset.IsBuiltIn = false;

        AddPreset(newPreset);
        return newPreset;
    }

    /// <summary>
    /// Сбрасывает к настройкам по умолчанию
    /// </summary>
    public void ResetToDefaults() {
        _presets.RemoveAll(p => !p.IsBuiltIn);
        _hiddenBuiltInPresets.Clear();
        SavePresets();
        LoadPresets();
    }

    private void LoadPresets() {
        _presets.Clear();

        ORMPresetData? savedData = null;
        if (File.Exists(PresetsFilePath)) {
            try {
                string json = File.ReadAllText(PresetsFilePath);
                savedData = JsonConvert.DeserializeObject<ORMPresetData>(json);
            } catch (Exception ex) {
                Console.WriteLine($"Error loading ORM presets: {ex.Message}");
            }
        }

        _hiddenBuiltInPresets.Clear();
        if (savedData?.HiddenBuiltInPresets != null) {
            foreach (var name in savedData.HiddenBuiltInPresets) {
                _hiddenBuiltInPresets.Add(name);
            }
        }

        // Add built-in presets (except hidden)
        var builtInPresets = ORMSettings.GetBuiltInPresets();
        foreach (var preset in builtInPresets) {
            if (!_hiddenBuiltInPresets.Contains(preset.Name)) {
                _presets.Add(preset);
            }
        }

        // Load user presets
        if (savedData?.UserPresets != null) {
            foreach (var preset in savedData.UserPresets) {
                preset.IsBuiltIn = false;

                var existingBuiltIn = _presets.FirstOrDefault(p =>
                    p.Name.Equals(preset.Name, StringComparison.OrdinalIgnoreCase) && p.IsBuiltIn);

                if (existingBuiltIn != null) {
                    int index = _presets.IndexOf(existingBuiltIn);
                    _presets[index] = preset;
                } else {
                    _presets.Add(preset);
                }
            }
        }
    }

    private void SavePresets() {
        try {
            var directory = Path.GetDirectoryName(PresetsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) {
                Directory.CreateDirectory(directory);
            }

            var data = new ORMPresetData {
                UserPresets = _presets.Where(p => !p.IsBuiltIn).ToList(),
                HiddenBuiltInPresets = _hiddenBuiltInPresets.ToList()
            };

            string json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(PresetsFilePath, json);
        } catch (Exception ex) {
            throw new InvalidOperationException($"Failed to save ORM presets: {ex.Message}", ex);
        }
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
        var importedPresets = JsonConvert.DeserializeObject<List<ORMSettings>>(json);

        if (importedPresets == null || importedPresets.Count == 0) {
            return 0;
        }

        int importedCount = 0;

        foreach (var preset in importedPresets) {
            preset.IsBuiltIn = false;

            var existing = _presets.FirstOrDefault(p => p.Name.Equals(preset.Name, StringComparison.OrdinalIgnoreCase));

            if (existing != null) {
                if (existing.IsBuiltIn) {
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
/// Данные для сериализации ORM пресетов
/// </summary>
internal class ORMPresetData {
    public List<ORMSettings> UserPresets { get; set; } = new();
    public List<string> HiddenBuiltInPresets { get; set; } = new();
}
