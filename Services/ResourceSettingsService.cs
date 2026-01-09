using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using NLog;

namespace AssetProcessor.Services;

/// <summary>
/// Сервис для хранения настроек ресурсов (текстур, материалов, моделей)
/// Настройки сохраняются в JSON файл и восстанавливаются при перезапуске
/// </summary>
public class ResourceSettingsService {
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static ResourceSettingsService? _instance;
    private static readonly object _lock = new();

    public static ResourceSettingsService Instance {
        get {
            if (_instance == null) {
                lock (_lock) {
                    _instance ??= new ResourceSettingsService();
                }
            }
            return _instance;
        }
    }

    private readonly string _settingsDirectory;
    private readonly Dictionary<string, ProjectSettings> _projectSettings = new();
    private readonly JsonSerializerOptions _jsonOptions;

    private ResourceSettingsService() {
        _settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TexTool",
            "ResourceSettings"
        );

        Directory.CreateDirectory(_settingsDirectory);

        _jsonOptions = new JsonSerializerOptions {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    /// <summary>
    /// Получить путь к файлу настроек проекта
    /// </summary>
    private string GetProjectSettingsPath(int projectId) {
        return Path.Combine(_settingsDirectory, $"project_{projectId}.json");
    }

    /// <summary>
    /// Загрузить настройки проекта
    /// </summary>
    public ProjectSettings LoadProjectSettings(int projectId) {
        var key = projectId.ToString();

        if (_projectSettings.TryGetValue(key, out var cached)) {
            return cached;
        }

        var path = GetProjectSettingsPath(projectId);

        if (File.Exists(path)) {
            try {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<ProjectSettings>(json, _jsonOptions);
                if (settings != null) {
                    _projectSettings[key] = settings;
                    return settings;
                }
            } catch (Exception ex) {
                Logger.Error(ex, $"Failed to load settings for project {projectId}");
            }
        }

        var newSettings = new ProjectSettings { ProjectId = projectId };
        _projectSettings[key] = newSettings;
        return newSettings;
    }

    /// <summary>
    /// Сохранить настройки проекта
    /// </summary>
    public void SaveProjectSettings(int projectId) {
        var key = projectId.ToString();

        if (!_projectSettings.TryGetValue(key, out var settings)) {
            return;
        }

        var path = GetProjectSettingsPath(projectId);

        try {
            var json = JsonSerializer.Serialize(settings, _jsonOptions);
            File.WriteAllText(path, json);
            Logger.Debug($"Saved settings for project {projectId}");
        } catch (Exception ex) {
            Logger.Error(ex, $"Failed to save settings for project {projectId}");
        }
    }

    /// <summary>
    /// Получить настройки текстуры
    /// </summary>
    public TextureSettings? GetTextureSettings(int projectId, int textureId) {
        var projectSettings = LoadProjectSettings(projectId);
        return projectSettings.TextureSettings.GetValueOrDefault(textureId.ToString());
    }

    /// <summary>
    /// Сохранить настройки текстуры
    /// </summary>
    public void SaveTextureSettings(int projectId, int textureId, TextureSettings settings) {
        var projectSettings = LoadProjectSettings(projectId);
        projectSettings.TextureSettings[textureId.ToString()] = settings;
        SaveProjectSettings(projectId);
    }

    /// <summary>
    /// Получить настройки ORM текстуры
    /// </summary>
    public ORMTextureSettings? GetORMTextureSettings(int projectId, string ormKey) {
        var projectSettings = LoadProjectSettings(projectId);
        return projectSettings.ORMTextureSettings.GetValueOrDefault(ormKey);
    }

    /// <summary>
    /// Сохранить настройки ORM текстуры
    /// </summary>
    public void SaveORMTextureSettings(int projectId, string ormKey, ORMTextureSettings settings) {
        var projectSettings = LoadProjectSettings(projectId);
        projectSettings.ORMTextureSettings[ormKey] = settings;
        SaveProjectSettings(projectId);
    }

    /// <summary>
    /// Получить настройки материала
    /// </summary>
    public MaterialSettings? GetMaterialSettings(int projectId, int materialId) {
        var projectSettings = LoadProjectSettings(projectId);
        return projectSettings.MaterialSettings.GetValueOrDefault(materialId.ToString());
    }

    /// <summary>
    /// Сохранить настройки материала
    /// </summary>
    public void SaveMaterialSettings(int projectId, int materialId, MaterialSettings settings) {
        var projectSettings = LoadProjectSettings(projectId);
        projectSettings.MaterialSettings[materialId.ToString()] = settings;
        SaveProjectSettings(projectId);
    }

    /// <summary>
    /// Получить настройки модели
    /// </summary>
    public ModelSettings? GetModelSettings(int projectId, int modelId) {
        var projectSettings = LoadProjectSettings(projectId);
        return projectSettings.ModelSettings.GetValueOrDefault(modelId.ToString());
    }

    /// <summary>
    /// Сохранить настройки модели
    /// </summary>
    public void SaveModelSettings(int projectId, int modelId, ModelSettings settings) {
        var projectSettings = LoadProjectSettings(projectId);
        projectSettings.ModelSettings[modelId.ToString()] = settings;
        SaveProjectSettings(projectId);
    }
}

/// <summary>
/// Настройки всего проекта
/// </summary>
public class ProjectSettings {
    [JsonPropertyName("projectId")]
    public int ProjectId { get; set; }

    [JsonPropertyName("textureSettings")]
    public Dictionary<string, TextureSettings> TextureSettings { get; set; } = new();

    [JsonPropertyName("ormTextureSettings")]
    public Dictionary<string, ORMTextureSettings> ORMTextureSettings { get; set; } = new();

    [JsonPropertyName("materialSettings")]
    public Dictionary<string, MaterialSettings> MaterialSettings { get; set; } = new();

    [JsonPropertyName("modelSettings")]
    public Dictionary<string, ModelSettings> ModelSettings { get; set; } = new();
}

/// <summary>
/// Настройки отдельной текстуры - полные настройки конвертации
/// </summary>
public class TextureSettings {
    // === Основные ===
    [JsonPropertyName("presetName")]
    public string? PresetName { get; set; }

    [JsonPropertyName("textureType")]
    public string TextureType { get; set; } = "Auto"; // Auto, Albedo, Normal, Roughness, Metallic, AO, Emissive, Gloss, Generic

    [JsonPropertyName("exportEnabled")]
    public bool ExportEnabled { get; set; } = true;

    // === Mipmap Generation ===
    [JsonPropertyName("generateMipmaps")]
    public bool GenerateMipmaps { get; set; } = true;

    [JsonPropertyName("useCustomMipmaps")]
    public bool UseCustomMipmaps { get; set; } = true;

    [JsonPropertyName("filterType")]
    public string FilterType { get; set; } = "Kaiser"; // Box, Bilinear, Bicubic, Lanczos3, Mitchell, Kaiser

    [JsonPropertyName("applyGammaCorrection")]
    public bool ApplyGammaCorrection { get; set; } = true;

    [JsonPropertyName("gamma")]
    public float Gamma { get; set; } = 2.2f;

    [JsonPropertyName("blurRadius")]
    public float BlurRadius { get; set; } = 0.0f;

    [JsonPropertyName("minMipSize")]
    public int MinMipSize { get; set; } = 1;

    [JsonPropertyName("normalizeNormals")]
    public bool NormalizeNormals { get; set; } = false;

    [JsonPropertyName("useEnergyPreserving")]
    public bool UseEnergyPreserving { get; set; } = false;

    // === Compression ===
    [JsonPropertyName("compressionFormat")]
    public string CompressionFormat { get; set; } = "ETC1S"; // ETC1S, UASTC

    [JsonPropertyName("outputFormat")]
    public string OutputFormat { get; set; } = "KTX2"; // KTX2, Basis

    [JsonPropertyName("colorSpace")]
    public string ColorSpace { get; set; } = "Auto"; // Auto, SRGB, Linear

    [JsonPropertyName("compressionLevel")]
    public int CompressionLevel { get; set; } = 1; // ETC1S: 0-5

    [JsonPropertyName("qualityLevel")]
    public int QualityLevel { get; set; } = 128; // ETC1S: 1-255

    [JsonPropertyName("uastcQuality")]
    public int UASTCQuality { get; set; } = 2; // UASTC: 0-4

    [JsonPropertyName("useUastcRdo")]
    public bool UseUASTCRDO { get; set; } = true;

    [JsonPropertyName("uastcRdoQuality")]
    public float UASTCRDOQuality { get; set; } = 1.0f; // 0.001-10.0

    [JsonPropertyName("useEtc1sRdo")]
    public bool UseETC1SRDO { get; set; } = true;

    [JsonPropertyName("etc1sRdoLambda")]
    public float ETC1SRDOLambda { get; set; } = 1.0f;

    // === Supercompression ===
    [JsonPropertyName("ktx2Supercompression")]
    public string KTX2Supercompression { get; set; } = "Zstandard"; // None, Zstandard

    [JsonPropertyName("ktx2ZstdLevel")]
    public int KTX2ZstdLevel { get; set; } = 3; // 1-22

    // === Normal Map specific ===
    [JsonPropertyName("convertToNormalMap")]
    public bool ConvertToNormalMap { get; set; } = false;

    [JsonPropertyName("normalizeVectors")]
    public bool NormalizeVectors { get; set; } = false;

    // === Histogram Analysis ===
    [JsonPropertyName("histogramEnabled")]
    public bool HistogramEnabled { get; set; } = false;

    [JsonPropertyName("histogramMode")]
    public string HistogramMode { get; set; } = "Percentile"; // Off, Percentile, PercentileWithKnee

    [JsonPropertyName("histogramQuality")]
    public string HistogramQuality { get; set; } = "HighQuality"; // HighQuality, Fast

    [JsonPropertyName("histogramChannelMode")]
    public string HistogramChannelMode { get; set; } = "PerChannel"; // AverageLuminance, PerChannel

    [JsonPropertyName("histogramPercentileLow")]
    public float HistogramPercentileLow { get; set; } = 5.0f;

    [JsonPropertyName("histogramPercentileHigh")]
    public float HistogramPercentileHigh { get; set; } = 95.0f;

    [JsonPropertyName("histogramKneeWidth")]
    public float HistogramKneeWidth { get; set; } = 0.02f;

    // === Toksvig (for Gloss/Roughness) ===
    [JsonPropertyName("toksvigEnabled")]
    public bool ToksvigEnabled { get; set; } = false;

    [JsonPropertyName("toksvigPower")]
    public float ToksvigPower { get; set; } = 4.0f;

    [JsonPropertyName("toksvigCalculationMode")]
    public string ToksvigCalculationMode { get; set; } = "Classic"; // Classic, Improved

    [JsonPropertyName("toksvigMinMipLevel")]
    public int ToksvigMinMipLevel { get; set; } = 0;

    [JsonPropertyName("toksvigEnergyPreserving")]
    public bool ToksvigEnergyPreserving { get; set; } = true;

    // === Advanced ===
    [JsonPropertyName("perceptualMode")]
    public bool PerceptualMode { get; set; } = true;

    [JsonPropertyName("separateAlpha")]
    public bool SeparateAlpha { get; set; } = false;

    [JsonPropertyName("forceAlphaChannel")]
    public bool ForceAlphaChannel { get; set; } = false;

    [JsonPropertyName("removeAlphaChannel")]
    public bool RemoveAlphaChannel { get; set; } = false;

    [JsonPropertyName("wrapMode")]
    public string WrapMode { get; set; } = "Clamp"; // Clamp, Wrap

    // === Status ===
    [JsonPropertyName("lastConvertedPath")]
    public string? LastConvertedPath { get; set; }

    [JsonPropertyName("lastConvertedTime")]
    public DateTime? LastConvertedTime { get; set; }
}

/// <summary>
/// Настройки ORM текстуры
/// </summary>
public class ORMTextureSettings {
    [JsonPropertyName("packingMode")]
    public string PackingMode { get; set; } = "OGM";

    // Source texture IDs
    [JsonPropertyName("aoSourceId")]
    public int? AOSourceId { get; set; }

    [JsonPropertyName("glossSourceId")]
    public int? GlossSourceId { get; set; }

    [JsonPropertyName("metallicSourceId")]
    public int? MetallicSourceId { get; set; }

    [JsonPropertyName("heightSourceId")]
    public int? HeightSourceId { get; set; }

    // AO settings
    [JsonPropertyName("aoProcessingMode")]
    public string AOProcessingMode { get; set; } = "None";

    [JsonPropertyName("aoBias")]
    public float AOBias { get; set; } = 0.5f;

    [JsonPropertyName("aoPercentile")]
    public float AOPercentile { get; set; } = 10.0f;

    [JsonPropertyName("aoFilterType")]
    public string AOFilterType { get; set; } = "Kaiser";

    // Gloss settings
    [JsonPropertyName("glossToksvigEnabled")]
    public bool GlossToksvigEnabled { get; set; } = true;

    [JsonPropertyName("glossToksvigPower")]
    public float GlossToksvigPower { get; set; } = 4.0f;

    [JsonPropertyName("glossToksvigCalculationMode")]
    public string GlossToksvigCalculationMode { get; set; } = "Classic";

    [JsonPropertyName("glossToksvigMinMipLevel")]
    public int GlossToksvigMinMipLevel { get; set; } = 0;

    [JsonPropertyName("glossToksvigEnergyPreserving")]
    public bool GlossToksvigEnergyPreserving { get; set; } = true;

    [JsonPropertyName("glossToksvigSmoothVariance")]
    public bool GlossToksvigSmoothVariance { get; set; } = true;

    [JsonPropertyName("glossFilterType")]
    public string GlossFilterType { get; set; } = "Kaiser";

    // Metallic settings
    [JsonPropertyName("metallicProcessingMode")]
    public string MetallicProcessingMode { get; set; } = "None";

    [JsonPropertyName("metallicBias")]
    public float MetallicBias { get; set; } = 0.5f;

    [JsonPropertyName("metallicPercentile")]
    public float MetallicPercentile { get; set; } = 10.0f;

    [JsonPropertyName("metallicFilterType")]
    public string MetallicFilterType { get; set; } = "Box";

    // Compression settings
    [JsonPropertyName("compressionFormat")]
    public string CompressionFormat { get; set; } = "ETC1S";

    [JsonPropertyName("compressLevel")]
    public int CompressLevel { get; set; } = 1;

    [JsonPropertyName("qualityLevel")]
    public int QualityLevel { get; set; } = 128;

    [JsonPropertyName("uastcQuality")]
    public int UASTCQuality { get; set; } = 2;

    [JsonPropertyName("enableRDO")]
    public bool EnableRDO { get; set; } = false;

    [JsonPropertyName("rdoLambda")]
    public float RDOLambda { get; set; } = 1.0f;

    [JsonPropertyName("perceptual")]
    public bool Perceptual { get; set; } = false;

    [JsonPropertyName("enableSupercompression")]
    public bool EnableSupercompression { get; set; } = false;

    [JsonPropertyName("supercompressionLevel")]
    public int SupercompressionLevel { get; set; } = 3;

    // Status
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("outputPath")]
    public string? OutputPath { get; set; }
}

/// <summary>
/// Настройки материала - overrides и экспорт
/// </summary>
public class MaterialSettings {
    // === Основные ===
    [JsonPropertyName("exportEnabled")]
    public bool ExportEnabled { get; set; } = true;

    [JsonPropertyName("masterMaterial")]
    public string? MasterMaterial { get; set; } // pbr_opaque, pbr_alpha, pbr_additive, pbr_premul

    // === ORM Packing ===
    [JsonPropertyName("ormEnabled")]
    public bool ORMEnabled { get; set; } = true;

    [JsonPropertyName("ormPresetName")]
    public string? ORMPresetName { get; set; }

    // === Color Overrides (null = use from PlayCanvas) ===
    [JsonPropertyName("diffuseOverride")]
    public float[]? DiffuseOverride { get; set; } // [R, G, B]

    [JsonPropertyName("emissiveOverride")]
    public float[]? EmissiveOverride { get; set; } // [R, G, B]

    [JsonPropertyName("specularOverride")]
    public float[]? SpecularOverride { get; set; } // [R, G, B]

    // === Float Overrides (null = use from PlayCanvas) ===
    [JsonPropertyName("glossOverride")]
    public float? GlossOverride { get; set; }

    [JsonPropertyName("metalnessOverride")]
    public float? MetalnessOverride { get; set; }

    [JsonPropertyName("opacityOverride")]
    public float? OpacityOverride { get; set; }

    [JsonPropertyName("alphaTestOverride")]
    public float? AlphaTestOverride { get; set; }

    [JsonPropertyName("bumpinessOverride")]
    public float? BumpinessOverride { get; set; }

    [JsonPropertyName("emissiveIntensityOverride")]
    public float? EmissiveIntensityOverride { get; set; }

    [JsonPropertyName("reflectivityOverride")]
    public float? ReflectivityOverride { get; set; }

    [JsonPropertyName("refractionOverride")]
    public float? RefractionOverride { get; set; }

    // === Boolean Overrides ===
    [JsonPropertyName("useMetalnessOverride")]
    public bool? UseMetalnessOverride { get; set; }

    [JsonPropertyName("diffuseTintOverride")]
    public bool? DiffuseTintOverride { get; set; }

    [JsonPropertyName("specularTintOverride")]
    public bool? SpecularTintOverride { get; set; }

    [JsonPropertyName("aoTintOverride")]
    public bool? AOTintOverride { get; set; }

    [JsonPropertyName("twoSidedLightingOverride")]
    public bool? TwoSidedLightingOverride { get; set; }

    // === Cull Mode ===
    [JsonPropertyName("cullModeOverride")]
    public string? CullModeOverride { get; set; } // "0" = None, "1" = Back, "2" = Front

    // === Texture Remapping (ID -> new ID or path) ===
    [JsonPropertyName("textureRemapping")]
    public Dictionary<string, string>? TextureRemapping { get; set; } // "diffuseMap" -> "path/to/texture.ktx2"

    // === Status ===
    [JsonPropertyName("lastExportedPath")]
    public string? LastExportedPath { get; set; }

    [JsonPropertyName("lastExportedTime")]
    public DateTime? LastExportedTime { get; set; }
}

/// <summary>
/// Настройки модели - экспорт и LOD
/// </summary>
public class ModelSettings {
    // === Основные ===
    [JsonPropertyName("exportEnabled")]
    public bool ExportEnabled { get; set; } = true;

    [JsonPropertyName("exportFormat")]
    public string ExportFormat { get; set; } = "GLB"; // GLB, GLTF

    [JsonPropertyName("exportPath")]
    public string? ExportPath { get; set; }

    // === LOD Generation ===
    [JsonPropertyName("generateLods")]
    public bool GenerateLODs { get; set; } = true;

    [JsonPropertyName("lodCount")]
    public int LODCount { get; set; } = 3; // Includes LOD0

    [JsonPropertyName("lodDistances")]
    public int[]? LODDistances { get; set; } // [50, 150, null] - null = loadFirst

    [JsonPropertyName("lodSimplificationRatios")]
    public float[]? LODSimplificationRatios { get; set; } // [1.0, 0.5, 0.25]

    // === Mesh Optimization ===
    [JsonPropertyName("compressionMode")]
    public string CompressionMode { get; set; } = "MeshOpt"; // None, Draco, MeshOpt

    [JsonPropertyName("vertexCacheOptimization")]
    public bool VertexCacheOptimization { get; set; } = true;

    [JsonPropertyName("overdrawOptimization")]
    public bool OverdrawOptimization { get; set; } = true;

    [JsonPropertyName("vertexFetchOptimization")]
    public bool VertexFetchOptimization { get; set; } = true;

    // === Vertex Quantization ===
    [JsonPropertyName("quantizePosition")]
    public bool QuantizePosition { get; set; } = true;

    [JsonPropertyName("positionQuantizationBits")]
    public int PositionQuantizationBits { get; set; } = 14; // 8-16

    [JsonPropertyName("quantizeTexCoord")]
    public bool QuantizeTexCoord { get; set; } = true;

    [JsonPropertyName("texCoordQuantizationBits")]
    public int TexCoordQuantizationBits { get; set; } = 12; // 8-16

    [JsonPropertyName("quantizeNormal")]
    public bool QuantizeNormal { get; set; } = true;

    [JsonPropertyName("normalQuantizationBits")]
    public int NormalQuantizationBits { get; set; } = 8; // 8-16

    // === Export Options ===
    [JsonPropertyName("excludeTextures")]
    public bool ExcludeTextures { get; set; } = true; // We handle textures separately

    [JsonPropertyName("excludeMaterials")]
    public bool ExcludeMaterials { get; set; } = true; // We handle materials separately

    [JsonPropertyName("preserveNodeNames")]
    public bool PreserveNodeNames { get; set; } = true;

    [JsonPropertyName("flattenHierarchy")]
    public bool FlattenHierarchy { get; set; } = false;

    // === Animation ===
    [JsonPropertyName("exportAnimations")]
    public bool ExportAnimations { get; set; } = true;

    [JsonPropertyName("animationSampleRate")]
    public int AnimationSampleRate { get; set; } = 30;

    // === Transform Overrides ===
    [JsonPropertyName("scaleOverride")]
    public float? ScaleOverride { get; set; }

    [JsonPropertyName("rotationOverride")]
    public float[]? RotationOverride { get; set; } // [X, Y, Z] degrees

    // === Material Assignment Override ===
    [JsonPropertyName("materialIds")]
    public int[]? MaterialIds { get; set; } // Override material assignment

    // === Status ===
    [JsonPropertyName("lastExportedPath")]
    public string? LastExportedPath { get; set; }

    [JsonPropertyName("lastExportTime")]
    public DateTime? LastExportTime { get; set; }
}
