using AssetProcessor.TextureConversion.Core;
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
    /// Использовать глобальные настройки (из панели текстур)
    /// Если true - игнорируем локальные Settings
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
    public ORMPresetType Preset {
        get => Settings.Preset;
        set {
            Settings.Preset = value;
            if (value != ORMPresetType.Custom) {
                Settings = ORMSettings.FromPreset(value);
            }
        }
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

    [JsonIgnore]
    public float AODefault {
        get => Settings.AODefault;
        set => Settings.AODefault = value;
    }

    [JsonIgnore]
    public float GlossDefault {
        get => Settings.GlossDefault;
        set => Settings.GlossDefault = value;
    }

    [JsonIgnore]
    public float MetalnessDefault {
        get => Settings.MetallicDefault;
        set => Settings.MetallicDefault = value;
    }

    [JsonIgnore]
    public float HeightDefault {
        get => Settings.HeightDefault;
        set => Settings.HeightDefault = value;
    }

    /// <summary>
    /// Создает настройки по умолчанию
    /// </summary>
    public static MaterialORMSettings CreateDefault() {
        return new MaterialORMSettings {
            UseGlobalSettings = true,
            Settings = ORMSettings.CreateStandard()
        };
    }

    /// <summary>
    /// Конвертирует в ChannelPackingSettings для пайплайна
    /// </summary>
    public ChannelPackingSettings ToChannelPackingSettings(
        string? aoPath, string? glossPath, string? metalPath, string? heightPath = null) {

        return Settings.ToChannelPackingSettings(aoPath, glossPath, metalPath, heightPath);
    }

    /// <summary>
    /// Получить CompressionSettings для KTX конвертации
    /// </summary>
    public CompressionSettings ToCompressionSettings() {
        return Settings.ToCompressionSettings();
    }
}
