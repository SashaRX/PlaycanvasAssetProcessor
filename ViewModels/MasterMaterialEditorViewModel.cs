using System.Collections.ObjectModel;
using AssetProcessor.MasterMaterials.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AssetProcessor.ViewModels;

/// <summary>
/// ViewModel for the Master Material Editor window
/// </summary>
public partial class MasterMaterialEditorViewModel : ObservableObject
{
    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string? description;

    [ObservableProperty]
    private string baseMaterialIdText = string.Empty;

    [ObservableProperty]
    private string blendType = "opaque";

    [ObservableProperty]
    private ObservableCollection<string> attachedChunkIds = [];

    [ObservableProperty]
    private string? selectedChunkId;

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
        BaseMaterialIdText = master.BaseMaterialId?.ToString() ?? string.Empty;
        BlendType = master.BlendType;

        AttachedChunkIds.Clear();
        foreach (var chunkId in master.ChunkIds)
        {
            AttachedChunkIds.Add(chunkId);
        }

        HasUnsavedChanges = false;
    }

    /// <summary>
    /// Creates a new MasterMaterial from the current values
    /// </summary>
    public MasterMaterial ToMasterMaterial()
    {
        int? baseMaterialId = null;
        if (int.TryParse(BaseMaterialIdText, out var id))
        {
            baseMaterialId = id;
        }

        return new MasterMaterial
        {
            Name = Name,
            Description = Description,
            BaseMaterialId = baseMaterialId,
            BlendType = BlendType,
            ChunkIds = [.. AttachedChunkIds],
            IsBuiltIn = false
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
    /// Adds a chunk ID to the attached list
    /// </summary>
    public void AddChunk(string chunkId)
    {
        if (!AttachedChunkIds.Contains(chunkId))
        {
            AttachedChunkIds.Add(chunkId);
            HasUnsavedChanges = true;
        }
    }

    /// <summary>
    /// Removes the selected chunk from the attached list
    /// </summary>
    public void RemoveSelectedChunk()
    {
        if (!string.IsNullOrEmpty(SelectedChunkId))
        {
            AttachedChunkIds.Remove(SelectedChunkId);
            SelectedChunkId = null;
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

        // Validate base material ID if provided
        if (!string.IsNullOrWhiteSpace(BaseMaterialIdText) && !int.TryParse(BaseMaterialIdText, out _))
        {
            return (false, "Base Material ID must be a valid integer");
        }

        return (true, null);
    }

    partial void OnNameChanged(string value)
    {
        HasUnsavedChanges = true;
    }

    partial void OnDescriptionChanged(string? value)
    {
        HasUnsavedChanges = true;
    }

    partial void OnBaseMaterialIdTextChanged(string value)
    {
        HasUnsavedChanges = true;
    }

    partial void OnBlendTypeChanged(string value)
    {
        HasUnsavedChanges = true;
    }
}
