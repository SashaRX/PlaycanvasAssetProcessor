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
                    Logger.Info($"Loaded settings for project {projectId}: {settings.TextureSettings.Count} textures, {settings.MaterialSettings.Count} materials");
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
/// Настройки отдельной текстуры
/// </summary>
public class TextureSettings {
    [JsonPropertyName("presetName")]
    public string? PresetName { get; set; }

    [JsonPropertyName("customSettings")]
    public Dictionary<string, object>? CustomSettings { get; set; }
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
/// Настройки материала
/// </summary>
public class MaterialSettings {
    [JsonPropertyName("ormEnabled")]
    public bool ORMEnabled { get; set; } = true;

    [JsonPropertyName("ormPresetName")]
    public string? ORMPresetName { get; set; }
}

/// <summary>
/// Настройки модели
/// </summary>
public class ModelSettings {
    [JsonPropertyName("exportFormat")]
    public string? ExportFormat { get; set; }

    [JsonPropertyName("exportPath")]
    public string? ExportPath { get; set; }

    [JsonPropertyName("lastExportTime")]
    public DateTime? LastExportTime { get; set; }
}
