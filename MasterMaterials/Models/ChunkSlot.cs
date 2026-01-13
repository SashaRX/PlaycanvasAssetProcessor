using System.Text.Json.Serialization;

namespace AssetProcessor.MasterMaterials.Models;

/// <summary>
/// Defines a shader chunk slot in the material pipeline
/// </summary>
public class ChunkSlot
{
    /// <summary>
    /// Unique slot identifier (e.g., "diffuse", "normal", "specular")
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Display name for the UI
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Category for grouping in UI (e.g., "Surface", "Lighting", "Output")
    /// </summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>
    /// Shader type: "fragment" or "vertex"
    /// </summary>
    public string ShaderType { get; init; } = "fragment";

    /// <summary>
    /// Default built-in chunk ID for this slot
    /// </summary>
    public string DefaultChunkId { get; init; } = string.Empty;

    /// <summary>
    /// Description of what this slot controls
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Order for display in UI (lower = higher in list)
    /// </summary>
    public int Order { get; init; }

    /// <summary>
    /// Whether this slot is required (must have a chunk assigned)
    /// </summary>
    public bool IsRequired { get; init; }

    /// <summary>
    /// Whether this slot is enabled by default
    /// </summary>
    public bool IsEnabledByDefault { get; init; } = true;
}

/// <summary>
/// Represents a chunk assignment for a specific slot in a master material
/// </summary>
public class ChunkSlotAssignment
{
    /// <summary>
    /// Slot ID this assignment is for
    /// </summary>
    [JsonPropertyName("slotId")]
    public string SlotId { get; set; } = string.Empty;

    /// <summary>
    /// Chunk ID assigned to this slot (null = use default, empty = disabled)
    /// </summary>
    [JsonPropertyName("chunkId")]
    public string? ChunkId { get; set; }

    /// <summary>
    /// Whether this slot is enabled
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}
