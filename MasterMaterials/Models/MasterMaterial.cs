using System.Text.Json.Serialization;

namespace AssetProcessor.MasterMaterials.Models;

/// <summary>
/// Represents a Master Material configuration
/// </summary>
public class MasterMaterial
{
    /// <summary>
    /// Unique name for the master material
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional description
    /// </summary>
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    /// <summary>
    /// PlayCanvas material ID to use as base template
    /// </summary>
    [JsonPropertyName("baseMaterialId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? BaseMaterialId { get; set; }

    /// <summary>
    /// List of chunk IDs used by this master material
    /// </summary>
    [JsonPropertyName("chunkIds")]
    public List<string> ChunkIds { get; set; } = [];

    /// <summary>
    /// Default parameter values for instances
    /// </summary>
    [JsonPropertyName("defaultParams")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object>? DefaultParams { get; set; }

    /// <summary>
    /// Blend type: "opaque", "alpha", "additive", "premul"
    /// </summary>
    [JsonPropertyName("blendType")]
    public string BlendType { get; set; } = "opaque";

    /// <summary>
    /// Whether this is a built-in system master (pbr_opaque, etc.)
    /// </summary>
    [JsonIgnore]
    public bool IsBuiltIn { get; set; }
}
