using System.Text.Json.Serialization;

namespace AssetProcessor.TextureConversion.Core;

/// <summary>
/// Полные настройки ORM упаковки (унифицированные)
/// </summary>
public class ORMSettings {
    // === Метаданные пресета ===

    /// <summary>
    /// Имя пресета
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "Custom";

    /// <summary>
    /// Является ли встроенным пресетом
    /// </summary>
    [JsonPropertyName("isBuiltIn")]
    public bool IsBuiltIn { get; set; } = false;

    /// <summary>
    /// Описание пресета
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    // === Основные ===

    /// <summary>
    /// Включить генерацию ORM
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Режим упаковки каналов
    /// </summary>
    [JsonPropertyName("packingMode")]
    public ChannelPackingMode PackingMode { get; set; } = ChannelPackingMode.Auto;

    // === AO Channel ===

    [JsonPropertyName("aoFilter")]
    public FilterType AOFilter { get; set; } = FilterType.Kaiser;

    [JsonPropertyName("aoProcessing")]
    public AOProcessingMode AOProcessing { get; set; } = AOProcessingMode.BiasedDarkening;

    [JsonPropertyName("aoBias")]
    public float AOBias { get; set; } = 0.5f;

    [JsonPropertyName("aoDefault")]
    public float AODefault { get; set; } = 1.0f;

    // === Gloss Channel ===

    [JsonPropertyName("glossFilter")]
    public FilterType GlossFilter { get; set; } = FilterType.Kaiser;

    [JsonPropertyName("toksvigEnabled")]
    public bool ToksvigEnabled { get; set; } = true;

    [JsonPropertyName("toksvigMode")]
    public ToksvigCalculationMode ToksvigMode { get; set; } = ToksvigCalculationMode.Classic;

    [JsonPropertyName("toksvigPower")]
    public float ToksvigPower { get; set; } = 4.0f;

    [JsonPropertyName("toksvigMinMip")]
    public int ToksvigMinMip { get; set; } = 0;

    [JsonPropertyName("toksvigEnergyPreserving")]
    public bool ToksvigEnergyPreserving { get; set; } = true;

    [JsonPropertyName("toksvigSmoothVariance")]
    public bool ToksvigSmoothVariance { get; set; } = true;

    [JsonPropertyName("glossDefault")]
    public float GlossDefault { get; set; } = 0.5f;

    // === Metallic Channel ===

    [JsonPropertyName("metallicFilter")]
    public FilterType MetallicFilter { get; set; } = FilterType.Box;

    [JsonPropertyName("metallicProcessing")]
    public AOProcessingMode MetallicProcessing { get; set; } = AOProcessingMode.None;

    [JsonPropertyName("metallicDefault")]
    public float MetallicDefault { get; set; } = 0.0f;

    // === Height Channel ===

    [JsonPropertyName("heightDefault")]
    public float HeightDefault { get; set; } = 0.5f;

    // === Compression ===

    [JsonPropertyName("compressionFormat")]
    public CompressionFormat CompressionFormat { get; set; } = CompressionFormat.ETC1S;

    [JsonPropertyName("etc1sCompressLevel")]
    public int ETC1SCompressLevel { get; set; } = 1;

    [JsonPropertyName("etc1sQuality")]
    public int ETC1SQuality { get; set; } = 128;

    [JsonPropertyName("etc1sPerceptual")]
    public bool ETC1SPerceptual { get; set; } = false;

    [JsonPropertyName("uastcQuality")]
    public int UASTCQuality { get; set; } = 2;

    [JsonPropertyName("uastcRDO")]
    public bool UASTCRDO { get; set; } = false;

    [JsonPropertyName("uastcRDOLambda")]
    public float UASTCRDOLambda { get; set; } = 1.0f;

    [JsonPropertyName("uastcZstd")]
    public bool UASTCZstd { get; set; } = true;

    [JsonPropertyName("uastcZstdLevel")]
    public int UASTCZstdLevel { get; set; } = 3;

    /// <summary>
    /// Возвращает встроенные пресеты
    /// </summary>
    public static List<ORMSettings> GetBuiltInPresets() {
        return new List<ORMSettings> {
            CreateStandard(),
            CreateHighQuality(),
            CreateFast(),
            CreateMobile()
        };
    }

    public static ORMSettings CreateStandard() => new() {
        Name = "Standard",
        IsBuiltIn = true,
        Description = "Balanced quality and size. Good for most assets.",
        PackingMode = ChannelPackingMode.Auto,
        AOFilter = FilterType.Kaiser,
        AOProcessing = AOProcessingMode.BiasedDarkening,
        AOBias = 0.5f,
        GlossFilter = FilterType.Kaiser,
        ToksvigEnabled = true,
        ToksvigPower = 4.0f,
        MetallicFilter = FilterType.Box,
        CompressionFormat = CompressionFormat.ETC1S,
        ETC1SQuality = 128
    };

    public static ORMSettings CreateHighQuality() => new() {
        Name = "High Quality",
        IsBuiltIn = true,
        Description = "Best quality for hero assets. Larger file size.",
        PackingMode = ChannelPackingMode.Auto,
        AOFilter = FilterType.Kaiser,
        AOProcessing = AOProcessingMode.BiasedDarkening,
        AOBias = 0.5f,
        GlossFilter = FilterType.Kaiser,
        ToksvigEnabled = true,
        ToksvigPower = 4.0f,
        MetallicFilter = FilterType.Box,
        CompressionFormat = CompressionFormat.UASTC,
        UASTCQuality = 2,
        UASTCZstd = true,
        UASTCZstdLevel = 3
    };

    public static ORMSettings CreateFast() => new() {
        Name = "Fast",
        IsBuiltIn = true,
        Description = "Quick processing for batch operations.",
        PackingMode = ChannelPackingMode.Auto,
        AOFilter = FilterType.Box,
        AOProcessing = AOProcessingMode.None,
        GlossFilter = FilterType.Box,
        ToksvigEnabled = false,
        MetallicFilter = FilterType.Box,
        CompressionFormat = CompressionFormat.ETC1S,
        ETC1SQuality = 64,
        ETC1SCompressLevel = 1
    };

    public static ORMSettings CreateMobile() => new() {
        Name = "Mobile",
        IsBuiltIn = true,
        Description = "Optimized for mobile: smallest size, ETC1S.",
        PackingMode = ChannelPackingMode.OGM,
        AOFilter = FilterType.Bilinear,
        AOProcessing = AOProcessingMode.BiasedDarkening,
        AOBias = 0.6f,
        GlossFilter = FilterType.Bilinear,
        ToksvigEnabled = true,
        ToksvigPower = 3.0f,
        MetallicFilter = FilterType.Box,
        CompressionFormat = CompressionFormat.ETC1S,
        ETC1SQuality = 96,
        ETC1SCompressLevel = 2
    };

    /// <summary>
    /// Создать копию настроек
    /// </summary>
    public ORMSettings Clone() {
        return new ORMSettings {
            Name = Name,
            IsBuiltIn = false, // Копия всегда пользовательская
            Description = Description,
            Enabled = Enabled,
            PackingMode = PackingMode,
            AOFilter = AOFilter,
            AOProcessing = AOProcessing,
            AOBias = AOBias,
            AODefault = AODefault,
            GlossFilter = GlossFilter,
            ToksvigEnabled = ToksvigEnabled,
            ToksvigMode = ToksvigMode,
            ToksvigPower = ToksvigPower,
            ToksvigMinMip = ToksvigMinMip,
            ToksvigEnergyPreserving = ToksvigEnergyPreserving,
            ToksvigSmoothVariance = ToksvigSmoothVariance,
            GlossDefault = GlossDefault,
            MetallicFilter = MetallicFilter,
            MetallicProcessing = MetallicProcessing,
            MetallicDefault = MetallicDefault,
            HeightDefault = HeightDefault,
            CompressionFormat = CompressionFormat,
            ETC1SCompressLevel = ETC1SCompressLevel,
            ETC1SQuality = ETC1SQuality,
            ETC1SPerceptual = ETC1SPerceptual,
            UASTCQuality = UASTCQuality,
            UASTCRDO = UASTCRDO,
            UASTCRDOLambda = UASTCRDOLambda,
            UASTCZstd = UASTCZstd,
            UASTCZstdLevel = UASTCZstdLevel
        };
    }

    /// <summary>
    /// Конвертирует в ChannelPackingSettings для пайплайна
    /// </summary>
    public ChannelPackingSettings ToChannelPackingSettings(
        string? aoPath, string? glossPath, string? metalPath, string? heightPath = null) {

        var mode = PackingMode;

        // Auto-detect mode
        if (mode == ChannelPackingMode.Auto) {
            mode = DeterminePackingMode(aoPath, glossPath, metalPath, heightPath);
        }

        var settings = new ChannelPackingSettings { Mode = mode };

        if (mode == ChannelPackingMode.None) {
            return settings;
        }

        // AO Channel (Red)
        if (mode is ChannelPackingMode.OG or ChannelPackingMode.OGM or ChannelPackingMode.OGMH) {
            settings.RedChannel = new ChannelSourceSettings {
                ChannelType = ChannelType.AmbientOcclusion,
                SourcePath = aoPath,
                DefaultValue = AODefault,
                FilterType = AOFilter,
                AOProcessingMode = AOProcessing,
                AOBias = AOBias
            };
        }

        // Gloss Channel
        if (mode is ChannelPackingMode.OG) {
            settings.AlphaChannel = CreateGlossChannel(glossPath);
        } else if (mode is ChannelPackingMode.OGM or ChannelPackingMode.OGMH) {
            settings.GreenChannel = CreateGlossChannel(glossPath);

            // Metallic Channel (Blue)
            settings.BlueChannel = new ChannelSourceSettings {
                ChannelType = ChannelType.Metallic,
                SourcePath = metalPath,
                DefaultValue = MetallicDefault,
                FilterType = MetallicFilter,
                AOProcessingMode = MetallicProcessing
            };
        }

        // Height Channel (Alpha for OGMH)
        if (mode is ChannelPackingMode.OGMH) {
            settings.AlphaChannel = new ChannelSourceSettings {
                ChannelType = ChannelType.Height,
                SourcePath = heightPath,
                DefaultValue = HeightDefault
            };
        }

        return settings;
    }

    private ChannelSourceSettings CreateGlossChannel(string? glossPath) {
        return new ChannelSourceSettings {
            ChannelType = ChannelType.Gloss,
            SourcePath = glossPath,
            DefaultValue = GlossDefault,
            FilterType = GlossFilter,
            ApplyToksvig = ToksvigEnabled,
            ToksvigSettings = ToksvigEnabled ? new ToksvigSettings {
                CalculationMode = ToksvigMode,
                Power = ToksvigPower,
                MinMipLevel = ToksvigMinMip,
                EnergyPreserving = ToksvigEnergyPreserving,
                SmoothVariance = ToksvigSmoothVariance
            } : null
        };
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

    /// <summary>
    /// Получить CompressionSettings для KTX конвертации
    /// </summary>
    public CompressionSettings ToCompressionSettings() {
        return new CompressionSettings {
            CompressionFormat = CompressionFormat,
            CompressionLevel = ETC1SCompressLevel,
            QualityLevel = ETC1SQuality,
            UsePerceptualMetrics = ETC1SPerceptual,
            UASTCQuality = UASTCQuality,
            EnableRDO = UASTCRDO,
            RDOLambda = UASTCRDOLambda,
            EnableZstdSupercompression = UASTCZstd,
            ZstdCompressionLevel = UASTCZstdLevel
        };
    }
}
