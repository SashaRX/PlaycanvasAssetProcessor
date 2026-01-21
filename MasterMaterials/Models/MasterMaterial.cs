using System.Text.Json.Serialization;

namespace AssetProcessor.MasterMaterials.Models;

/// <summary>
/// Represents a Master Material configuration.
/// Stored as {name}_master.json in materials/ folder.
/// Contains chunk overrides and default parameters for runtime material creation.
/// </summary>
public class MasterMaterial
{
    /// <summary>
    /// Unique name for the master material (e.g., "pbr_opaque")
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Blend type: "opaque", "alpha", "additive", "premul"
    /// </summary>
    [JsonPropertyName("blendType")]
    public string BlendType { get; set; } = "opaque";

    /// <summary>
    /// Chunk overrides: key = chunk name to replace, value = path to .mjs file (relative to materials folder)
    /// Example: { "diffusePS": "pbr_opaque/chunks/diffusePS.mjs" }
    /// </summary>
    [JsonPropertyName("chunks")]
    public Dictionary<string, string> Chunks { get; set; } = new();

    /// <summary>
    /// Default parameter values for material instances.
    /// Instances can override these values.
    /// </summary>
    [JsonPropertyName("defaultParams")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object>? DefaultParams { get; set; }

    /// <summary>
    /// Optional description
    /// </summary>
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    /// <summary>
    /// Whether this is a built-in system master (not editable)
    /// </summary>
    [JsonIgnore]
    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// Gets the folder name for this master's chunks
    /// </summary>
    [JsonIgnore]
    public string ChunksFolderName => $"{Name}/chunks";

    /// <summary>
    /// Gets the filename for this master material
    /// </summary>
    [JsonIgnore]
    public string FileName => $"{Name}_master.json";

    /// <summary>
    /// Adds or updates a chunk override
    /// </summary>
    public void SetChunk(string chunkName, string mjsPath)
    {
        Chunks[chunkName] = mjsPath;
    }

    /// <summary>
    /// Removes a chunk override
    /// </summary>
    public bool RemoveChunk(string chunkName)
    {
        return Chunks.Remove(chunkName);
    }

    /// <summary>
    /// Gets the path to a chunk file, or null if not overridden
    /// </summary>
    public string? GetChunkPath(string chunkName)
    {
        return Chunks.TryGetValue(chunkName, out var path) ? path : null;
    }

    /// <summary>
    /// Creates a deep copy of this master material
    /// </summary>
    public MasterMaterial Clone()
    {
        return new MasterMaterial
        {
            Name = Name,
            BlendType = BlendType,
            Chunks = new Dictionary<string, string>(Chunks),
            DefaultParams = DefaultParams != null
                ? new Dictionary<string, object>(DefaultParams)
                : null,
            Description = Description,
            IsBuiltIn = false // Clones are never built-in
        };
    }
}
