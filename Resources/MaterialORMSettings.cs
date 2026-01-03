using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureConversion.Settings;
using System.Text.Json.Serialization;

namespace AssetProcessor.Resources;

/// <summary>
/// Настройки ORM упаковки для материала
/// Обёртка над ORMSettings с привязкой к конкретному материалу
/// </summary>
public class MaterialORMSettings {
    /// <summary>
    /// Полные настройки ORM
    /// </summary>
    [JsonPropertyName("settings")]
    public ORMSettings Settings { get; set; } = ORMSettings.CreateStandard();

    /// <summary>
    /// Имя используемого пресета (или null для кастомных настроек)
    /// </summary>
    [JsonPropertyName("presetName")]
    public string? PresetName { get; set; } = "Standard";

    /// <summary>
    /// Использовать глобальные настройки (игнорировать локальные Settings)
    /// </summary>
    [JsonPropertyName("useGlobal")]
    public bool UseGlobalSettings { get; set; } = true;

    // === Прокси свойства для совместимости ===

    [JsonIgnore]
    public bool Enabled {
        get => Settings.Enabled;
        set => Settings.Enabled = value;
    }

    [JsonIgnore]
    public ChannelPackingMode PackingMode {
        get => Settings.PackingMode;
        set => Settings.PackingMode = value;
    }

    [JsonIgnore]
    public bool ApplyToksvig {
        get => Settings.ToksvigEnabled;
        set => Settings.ToksvigEnabled = value;
    }

    [JsonIgnore]
    public AOProcessingMode AOProcessingMode {
        get => Settings.AOProcessing;
        set => Settings.AOProcessing = value;
    }

    [JsonIgnore]
    public float AOBias {
        get => Settings.AOBias;
        set => Settings.AOBias = value;
    }

    /// <summary>
    /// Создает настройки по умолчанию
    /// </summary>
    public static MaterialORMSettings CreateDefault() {
        return new MaterialORMSettings {
            UseGlobalSettings = true,
            PresetName = "Standard",
            Settings = ORMSettings.CreateStandard()
        };
    }

    /// <summary>
    /// Применяет пресет по имени
    /// </summary>
    public void ApplyPreset(string presetName) {
        var preset = ORMPresetManager.Instance.GetPreset(presetName);
        if (preset != null) {
            Settings = preset.Clone();
            PresetName = presetName;
        }
    }

    /// <summary>
    /// Получить эффективные настройки (с учётом глобальных)
    /// </summary>
    public ORMSettings GetEffectiveSettings() {
        if (UseGlobalSettings) {
            return ORMPresetManager.Instance.GetDefaultPreset();
        }

        if (!string.IsNullOrEmpty(PresetName)) {
            var preset = ORMPresetManager.Instance.GetPreset(PresetName);
            if (preset != null) {
                return preset;
            }
        }

        return Settings;
    }

    /// <summary>
    /// Конвертирует в ChannelPackingSettings для пайплайна
    /// </summary>
    public ChannelPackingSettings ToChannelPackingSettings(
        string? aoPath, string? glossPath, string? metalPath, string? heightPath = null) {

        return GetEffectiveSettings().ToChannelPackingSettings(aoPath, glossPath, metalPath, heightPath);
    }

    /// <summary>
    /// Получить CompressionSettings для KTX конвертации
    /// </summary>
    public CompressionSettings ToCompressionSettings() {
        return GetEffectiveSettings().ToCompressionSettings();
    }
}
