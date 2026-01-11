using System.Text.Json.Serialization;

namespace AssetProcessor.MasterMaterials.Models;

/// <summary>
/// Represents a shader chunk with both GLSL and WGSL variants
/// </summary>
public class ShaderChunk
{
    /// <summary>
    /// Unique identifier for the chunk (e.g., "diffusePS", "customLightingVS")
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// GLSL shader code
    /// </summary>
    [JsonPropertyName("glsl")]
    public string Glsl { get; set; } = string.Empty;

    /// <summary>
    /// WGSL shader code (for WebGPU)
    /// </summary>
    [JsonPropertyName("wgsl")]
    public string Wgsl { get; set; } = string.Empty;

    /// <summary>
    /// Shader type: "vertex" or "fragment"
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "fragment";

    /// <summary>
    /// Human-readable description
    /// </summary>
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    /// <summary>
    /// Source file path (relative to chunks folder)
    /// </summary>
    [JsonPropertyName("sourceFile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceFile { get; set; }
}
