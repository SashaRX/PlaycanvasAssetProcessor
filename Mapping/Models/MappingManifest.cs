using System.Text.Json.Serialization;

namespace AssetProcessor.Mapping.Models;

/// <summary>
/// Корневая структура mapping.json
/// Связывает оригинальные PlayCanvas asset ID с обработанными файлами на CDN
/// </summary>
public class MappingManifest {
    /// <summary>
    /// Версия формата маппинга
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// Дата и время генерации маппинга (ISO 8601)
    /// </summary>
    [JsonPropertyName("generated")]
    public string Generated { get; set; } = DateTime.UtcNow.ToString("O");

    /// <summary>
    /// Базовый URL для CDN (например: "https://cdn.example.com/project-name")
    /// Все относительные пути строятся относительно этого URL
    /// </summary>
    [JsonPropertyName("baseUrl")]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Информация о проекте PlayCanvas
    /// </summary>
    [JsonPropertyName("project")]
    public ProjectInfo? Project { get; set; }

    /// <summary>
    /// Словарь master материалов: имя → asset ID
    /// Используется для связи instance материалов с их master-шаблонами
    /// </summary>
    [JsonPropertyName("masterMaterials")]
    public Dictionary<string, int> MasterMaterials { get; set; } = new();

    /// <summary>
    /// Словарь моделей: asset ID → информация о модели
    /// </summary>
    [JsonPropertyName("models")]
    public Dictionary<string, ModelEntry> Models { get; set; } = new();

    /// <summary>
    /// Словарь материалов: asset ID → путь к JSON файлу
    /// </summary>
    [JsonPropertyName("materials")]
    public Dictionary<string, string> Materials { get; set; } = new();

    /// <summary>
    /// Словарь текстур: asset ID → путь к KTX2 файлу
    /// </summary>
    [JsonPropertyName("textures")]
    public Dictionary<string, string> Textures { get; set; } = new();

    /// <summary>
    /// Статистика по обработанным ассетам
    /// </summary>
    [JsonPropertyName("stats")]
    public MappingStats? Stats { get; set; }
}

/// <summary>
/// Информация о проекте PlayCanvas
/// </summary>
public class ProjectInfo {
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("branchId")]
    public string? BranchId { get; set; }
}

/// <summary>
/// Статистика маппинга
/// </summary>
public class MappingStats {
    [JsonPropertyName("modelsCount")]
    public int ModelsCount { get; set; }

    [JsonPropertyName("materialsCount")]
    public int MaterialsCount { get; set; }

    [JsonPropertyName("texturesCount")]
    public int TexturesCount { get; set; }

    [JsonPropertyName("totalLodFiles")]
    public int TotalLodFiles { get; set; }

    [JsonPropertyName("totalSizeBytes")]
    public long TotalSizeBytes { get; set; }
}
