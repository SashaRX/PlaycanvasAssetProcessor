using AssetProcessor.TextureConversion.Core;
using System.Text.Json.Serialization;

namespace AssetProcessor.Resources;

/// <summary>
/// Настройки ORM упаковки для материала
/// Используются при экспорте для генерации packed ORM текстур
/// </summary>
public class MaterialORMSettings {
    /// <summary>
    /// Режим упаковки каналов
    /// </summary>
    [JsonPropertyName("packingMode")]
    public ChannelPackingMode PackingMode { get; set; } = ChannelPackingMode.Auto;

    /// <summary>
    /// Применять Toksvig коррекцию к Gloss каналу
    /// </summary>
    [JsonPropertyName("applyToksvig")]
    public bool ApplyToksvig { get; set; } = true;

    /// <summary>
    /// Режим обработки AO при генерации мипмапов
    /// </summary>
    [JsonPropertyName("aoProcessingMode")]
    public AOProcessingMode AOProcessingMode { get; set; } = AOProcessingMode.BiasedDarkening;

    /// <summary>
    /// Bias для AO обработки (0.0-1.0)
    /// </summary>
    [JsonPropertyName("aoBias")]
    public float AOBias { get; set; } = 0.5f;

    /// <summary>
    /// Значение AO по умолчанию если текстура отсутствует (0.0-1.0)
    /// </summary>
    [JsonPropertyName("aoDefault")]
    public float AODefault { get; set; } = 1.0f;

    /// <summary>
    /// Значение Gloss по умолчанию если текстура отсутствует (0.0-1.0)
    /// </summary>
    [JsonPropertyName("glossDefault")]
    public float GlossDefault { get; set; } = 0.5f;

    /// <summary>
    /// Значение Metalness по умолчанию если текстура отсутствует (0.0-1.0)
    /// </summary>
    [JsonPropertyName("metalnessDefault")]
    public float MetalnessDefault { get; set; } = 0.0f;

    /// <summary>
    /// Значение Height по умолчанию если текстура отсутствует (0.0-1.0)
    /// </summary>
    [JsonPropertyName("heightDefault")]
    public float HeightDefault { get; set; } = 0.5f;

    /// <summary>
    /// Включить экспорт ORM текстуры для этого материала
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Создает настройки по умолчанию
    /// </summary>
    public static MaterialORMSettings CreateDefault() {
        return new MaterialORMSettings();
    }

    /// <summary>
    /// Конвертирует в ChannelPackingSettings для пайплайна
    /// </summary>
    public ChannelPackingSettings ToChannelPackingSettings(
        string? aoPath, string? glossPath, string? metalPath, string? heightPath = null) {

        var mode = PackingMode;

        // Auto-detect mode based on available textures
        if (mode == ChannelPackingMode.Auto) {
            mode = DeterminePackingMode(aoPath, glossPath, metalPath, heightPath);
        }

        var settings = new ChannelPackingSettings { Mode = mode };

        if (mode == ChannelPackingMode.None) {
            return settings;
        }

        // Configure channels based on mode
        if (mode is ChannelPackingMode.OG or ChannelPackingMode.OGM or ChannelPackingMode.OGMH) {
            settings.RedChannel = new ChannelSourceSettings {
                ChannelType = ChannelType.AmbientOcclusion,
                SourcePath = aoPath,
                DefaultValue = AODefault,
                AOProcessingMode = AOProcessingMode,
                AOBias = AOBias
            };
        }

        if (mode is ChannelPackingMode.OG) {
            settings.AlphaChannel = new ChannelSourceSettings {
                ChannelType = ChannelType.Gloss,
                SourcePath = glossPath,
                DefaultValue = GlossDefault,
                ApplyToksvig = ApplyToksvig,
                AOProcessingMode = AOProcessingMode.None
            };
        } else if (mode is ChannelPackingMode.OGM or ChannelPackingMode.OGMH) {
            settings.GreenChannel = new ChannelSourceSettings {
                ChannelType = ChannelType.Gloss,
                SourcePath = glossPath,
                DefaultValue = GlossDefault,
                ApplyToksvig = ApplyToksvig,
                AOProcessingMode = AOProcessingMode.None
            };

            settings.BlueChannel = new ChannelSourceSettings {
                ChannelType = ChannelType.Metallic,
                SourcePath = metalPath,
                DefaultValue = MetalnessDefault,
                AOProcessingMode = AOProcessingMode.None
            };
        }

        if (mode is ChannelPackingMode.OGMH) {
            settings.AlphaChannel = new ChannelSourceSettings {
                ChannelType = ChannelType.Height,
                SourcePath = heightPath,
                DefaultValue = HeightDefault,
                AOProcessingMode = AOProcessingMode.None
            };
        }

        return settings;
    }

    private static ChannelPackingMode DeterminePackingMode(
        string? aoPath, string? glossPath, string? metalPath, string? heightPath) {

        bool hasAO = !string.IsNullOrEmpty(aoPath);
        bool hasGloss = !string.IsNullOrEmpty(glossPath);
        bool hasMetal = !string.IsNullOrEmpty(metalPath);
        bool hasHeight = !string.IsNullOrEmpty(heightPath);

        if (hasAO && hasGloss && hasMetal && hasHeight) return ChannelPackingMode.OGMH;
        if (hasAO && hasGloss && hasMetal) return ChannelPackingMode.OGM;
        if (hasAO && hasGloss) return ChannelPackingMode.OG;

        return ChannelPackingMode.None;
    }
}
