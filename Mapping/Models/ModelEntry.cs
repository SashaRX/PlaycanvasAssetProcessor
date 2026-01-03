using System.Text.Json.Serialization;

namespace AssetProcessor.Mapping.Models;

/// <summary>
/// Запись модели в mapping.json
/// </summary>
public class ModelEntry {
    /// <summary>
    /// Имя модели из редактора PlayCanvas
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Полный путь папок в иерархии PlayCanvas
    /// Например: "Architecture/Buildings/Building"
    /// </summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Список ID материалов, используемых моделью
    /// </summary>
    [JsonPropertyName("materials")]
    public List<int> Materials { get; set; } = new();

    /// <summary>
    /// Список LOD уровней модели
    /// </summary>
    [JsonPropertyName("lods")]
    public List<LodEntry> Lods { get; set; } = new();
}

/// <summary>
/// Запись LOD уровня для модели
/// </summary>
public class LodEntry {
    /// <summary>
    /// Уровень LOD (0 = наивысшее качество)
    /// </summary>
    [JsonPropertyName("level")]
    public int Level { get; set; }

    /// <summary>
    /// Относительный путь к GLB файлу
    /// Например: "Architecture/Buildings/Building_lod0.glb"
    /// </summary>
    [JsonPropertyName("file")]
    public string File { get; set; } = string.Empty;

    /// <summary>
    /// Дистанция переключения LOD (в метрах)
    /// LOD0 = 0, остальные пропорционально настройкам
    /// </summary>
    [JsonPropertyName("distance")]
    public float Distance { get; set; }

    /// <summary>
    /// Размер файла в байтах (опционально)
    /// </summary>
    [JsonPropertyName("size")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Size { get; set; }
}
