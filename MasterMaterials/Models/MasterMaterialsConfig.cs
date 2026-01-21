using System.Text.Json.Serialization;

namespace AssetProcessor.MasterMaterials.Models;

/// <summary>
/// Configuration for Master Materials in a project.
///
/// Note: This is primarily used for in-memory operations.
/// Master materials are stored as individual files: {name}_master.json
/// Mappings are stored in: mappings.json
/// </summary>
public class MasterMaterialsConfig
{
    /// <summary>
    /// Config file version for migration support
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; set; } = 2;

    /// <summary>
    /// List of master materials loaded from project folder.
    /// Each master is stored in its own file: {name}_master.json
    /// </summary>
    [JsonIgnore]
    public List<MasterMaterial> Masters { get; set; } = [];

    /// <summary>
    /// Mapping of PlayCanvas material ID to master material name.
    /// Stored in mappings.json
    /// </summary>
    [JsonPropertyName("materialInstanceMappings")]
    public Dictionary<int, string> MaterialInstanceMappings { get; set; } = [];

    /// <summary>
    /// Default master material name applied to materials without explicit mapping
    /// </summary>
    [JsonPropertyName("defaultMasterMaterial")]
    public string? DefaultMasterMaterial { get; set; }
}
