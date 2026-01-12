using AssetProcessor.MasterMaterials.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AssetProcessor.ViewModels;

/// <summary>
/// ViewModel for the Shader Chunk Editor window
/// </summary>
public partial class ChunkEditorViewModel : ObservableObject
{
    [ObservableProperty]
    private string chunkId = string.Empty;

    [ObservableProperty]
    private string glslCode = string.Empty;

    [ObservableProperty]
    private string wgslCode = string.Empty;

    [ObservableProperty]
    private string chunkType = "fragment";

    [ObservableProperty]
    private string? description;

    [ObservableProperty]
    private bool hasUnsavedChanges;

    /// <summary>
    /// The original chunk that was loaded for editing
    /// </summary>
    public ShaderChunk? OriginalChunk { get; private set; }

    /// <summary>
    /// Available chunk types
    /// </summary>
    public static IReadOnlyList<string> ChunkTypes { get; } = ["fragment", "vertex"];

    /// <summary>
    /// Loads a chunk for editing
    /// </summary>
    public void LoadChunk(ShaderChunk chunk)
    {
        OriginalChunk = chunk;
        ChunkId = chunk.Id;
        GlslCode = chunk.Glsl;
        WgslCode = chunk.Wgsl;
        ChunkType = chunk.Type;
        Description = chunk.Description;
        HasUnsavedChanges = false;
    }

    /// <summary>
    /// Creates a new ShaderChunk from the current values
    /// </summary>
    public ShaderChunk ToChunk()
    {
        return new ShaderChunk
        {
            Id = ChunkId,
            Glsl = GlslCode,
            Wgsl = WgslCode,
            Type = ChunkType,
            Description = Description,
            SourceFile = $"{ChunkId}.mjs"
        };
    }

    /// <summary>
    /// Resets to original values
    /// </summary>
    public void Reset()
    {
        if (OriginalChunk != null)
        {
            LoadChunk(OriginalChunk);
        }
    }

    /// <summary>
    /// Validates the current chunk data
    /// </summary>
    public (bool IsValid, string? ErrorMessage) Validate()
    {
        if (string.IsNullOrWhiteSpace(ChunkId))
        {
            return (false, "Chunk ID is required");
        }

        // Validate ID format (alphanumeric and underscores only)
        if (!System.Text.RegularExpressions.Regex.IsMatch(ChunkId, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
        {
            return (false, "Chunk ID must start with a letter or underscore and contain only alphanumeric characters and underscores");
        }

        if (string.IsNullOrWhiteSpace(GlslCode) && string.IsNullOrWhiteSpace(WgslCode))
        {
            return (false, "At least one of GLSL or WGSL code is required");
        }

        return (true, null);
    }

    partial void OnChunkIdChanged(string value)
    {
        HasUnsavedChanges = true;
    }

    partial void OnGlslCodeChanged(string value)
    {
        HasUnsavedChanges = true;
    }

    partial void OnWgslCodeChanged(string value)
    {
        HasUnsavedChanges = true;
    }

    partial void OnChunkTypeChanged(string value)
    {
        HasUnsavedChanges = true;
    }

    partial void OnDescriptionChanged(string? value)
    {
        HasUnsavedChanges = true;
    }
}
