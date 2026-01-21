using System.Collections.ObjectModel;
using AssetProcessor.MasterMaterials.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AssetProcessor.ViewModels;

/// <summary>
/// ViewModel for the Master Material Editor window.
///
/// Edits master materials with the new simplified structure:
/// - Name: unique identifier
/// - BlendType: "opaque", "alpha", "additive", "premul"
/// - Chunks: Dictionary of chunk name to server path
/// - DefaultParams: optional default parameter values
/// </summary>
public partial class MasterMaterialEditorViewModel : ObservableObject
{
    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string? description;

    [ObservableProperty]
    private string blendType = "opaque";

    [ObservableProperty]
    private ObservableCollection<ChunkEntry> attachedChunks = [];

    [ObservableProperty]
    private ChunkEntry? selectedChunk;

    [ObservableProperty]
    private bool hasUnsavedChanges;

    /// <summary>
    /// The original master material that was loaded for editing
    /// </summary>
    public MasterMaterial? OriginalMaster { get; private set; }

    /// <summary>
    /// Available blend types
    /// </summary>
    public static IReadOnlyList<string> BlendTypes { get; } = ["opaque", "alpha", "additive", "premul"];

    /// <summary>
    /// Available chunks to add (set by the window)
    /// </summary>
    public ObservableCollection<ShaderChunk> AvailableChunks { get; } = [];

    /// <summary>
    /// Loads a master material for editing
    /// </summary>
    public void LoadMaster(MasterMaterial master)
    {
        OriginalMaster = master;
        Name = master.Name;
        Description = master.Description;
        BlendType = master.BlendType;

        AttachedChunks.Clear();
        foreach (var (chunkName, serverPath) in master.Chunks)
        {
            AttachedChunks.Add(new ChunkEntry { ChunkName = chunkName, ServerPath = serverPath });
        }

        HasUnsavedChanges = false;
    }

    /// <summary>
    /// Creates a new MasterMaterial from the current values
    /// </summary>
    public MasterMaterial ToMasterMaterial()
    {
        var chunks = new Dictionary<string, string>();
        foreach (var entry in AttachedChunks)
        {
            chunks[entry.ChunkName] = entry.ServerPath;
        }

        return new MasterMaterial
        {
            Name = Name,
            Description = Description,
            BlendType = BlendType,
            Chunks = chunks,
            IsBuiltIn = false,
            DefaultParams = OriginalMaster?.DefaultParams != null
                ? new Dictionary<string, object>(OriginalMaster.DefaultParams)
                : null
        };
    }

    /// <summary>
    /// Resets to original values
    /// </summary>
    public void Reset()
    {
        if (OriginalMaster != null)
        {
            LoadMaster(OriginalMaster);
        }
    }

    /// <summary>
    /// Adds a chunk to the attached list
    /// </summary>
    public void AddChunk(string chunkName)
    {
        if (AttachedChunks.Any(c => c.ChunkName == chunkName))
        {
            return;
        }

        // Server path will be: {masterName}/chunks/{chunkName}.mjs
        var serverPath = $"{Name}/chunks/{chunkName}.mjs";
        AttachedChunks.Add(new ChunkEntry { ChunkName = chunkName, ServerPath = serverPath });
        HasUnsavedChanges = true;
    }

    /// <summary>
    /// Removes the selected chunk from the attached list
    /// </summary>
    public void RemoveSelectedChunk()
    {
        if (SelectedChunk != null)
        {
            AttachedChunks.Remove(SelectedChunk);
            SelectedChunk = null;
            HasUnsavedChanges = true;
        }
    }

    /// <summary>
    /// Validates the current master material data
    /// </summary>
    public (bool IsValid, string? ErrorMessage) Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return (false, "Name is required");
        }

        // Validate name format (alphanumeric, underscores, and hyphens only)
        if (!System.Text.RegularExpressions.Regex.IsMatch(Name, @"^[a-zA-Z_][a-zA-Z0-9_\-]*$"))
        {
            return (false, "Name must start with a letter or underscore and contain only alphanumeric characters, underscores, and hyphens");
        }

        return (true, null);
    }

    partial void OnNameChanged(string value)
    {
        HasUnsavedChanges = true;

        // Update server paths in chunks when name changes
        foreach (var chunk in AttachedChunks)
        {
            chunk.ServerPath = $"{value}/chunks/{chunk.ChunkName}.mjs";
        }
    }

    partial void OnDescriptionChanged(string? value)
    {
        HasUnsavedChanges = true;
    }

    partial void OnBlendTypeChanged(string value)
    {
        HasUnsavedChanges = true;
    }
}

/// <summary>
/// Represents a chunk entry in the master material
/// </summary>
public class ChunkEntry : ObservableObject
{
    private string _chunkName = string.Empty;
    private string _serverPath = string.Empty;

    /// <summary>
    /// The chunk name (e.g., "diffusePS", "glossPS")
    /// </summary>
    public string ChunkName
    {
        get => _chunkName;
        set => SetProperty(ref _chunkName, value);
    }

    /// <summary>
    /// The server path to the chunk file (e.g., "pbr_opaque/chunks/diffusePS.mjs")
    /// </summary>
    public string ServerPath
    {
        get => _serverPath;
        set => SetProperty(ref _serverPath, value);
    }
}
