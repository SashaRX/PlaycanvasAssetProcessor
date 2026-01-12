using System.Text.Json.Serialization;

namespace AssetProcessor.MasterMaterials.Models;

/// <summary>
/// Root configuration stored in {ProjectFolder}/MasterMaterials/config.json
/// </summary>
public class MasterMaterialsConfig
{
    /// <summary>
    /// Config file version for migration support
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    /// <summary>
    /// List of master materials defined in this project
    /// </summary>
    [JsonPropertyName("masters")]
    public List<MasterMaterial> Masters { get; set; } = [];

    /// <summary>
    /// List of all shader chunks metadata (actual code in .mjs files)
    /// </summary>
    [JsonPropertyName("chunks")]
    public List<ShaderChunk> Chunks { get; set; } = [];

    /// <summary>
    /// Mapping of PlayCanvas material ID to master material name
    /// </summary>
    [JsonPropertyName("materialInstanceMappings")]
    public Dictionary<int, string> MaterialInstanceMappings { get; set; } = [];
}
