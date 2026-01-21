using System.Collections.ObjectModel;
using AssetProcessor.MasterMaterials.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AssetProcessor.ViewModels;

/// <summary>
/// ViewModel for a chunk slot with dropdown selection
/// </summary>
public partial class ChunkSlotViewModel : ObservableObject
{
    private readonly ChunkSlot _slot;
    private readonly Action<string, string?> _onChunkChanged;
    private readonly Action<string, bool> _onEnabledChanged;
    private bool _isInitializing = true;

    [ObservableProperty]
    private bool isEnabled;

    [ObservableProperty]
    private ShaderChunk? selectedChunk;

    [ObservableProperty]
    private ObservableCollection<ShaderChunk> availableChunks = [];

    /// <summary>
    /// Slot definition
    /// </summary>
    public ChunkSlot Slot => _slot;

    /// <summary>
    /// Slot ID
    /// </summary>
    public string SlotId => _slot.Id;

    /// <summary>
    /// Display name
    /// </summary>
    public string DisplayName => _slot.DisplayName;

    /// <summary>
    /// Category for grouping
    /// </summary>
    public string Category => _slot.Category;

    /// <summary>
    /// Description
    /// </summary>
    public string Description => _slot.Description;

    /// <summary>
    /// Whether this slot is required (cannot be disabled)
    /// </summary>
    public bool IsRequired => _slot.IsRequired;

    /// <summary>
    /// Default chunk ID
    /// </summary>
    public string DefaultChunkId => _slot.DefaultChunkId;

    public ChunkSlotViewModel(
        ChunkSlot slot,
        IEnumerable<ShaderChunk> allChunks,
        string? currentChunkId,
        bool enabled,
        Action<string, string?> onChunkChanged,
        Action<string, bool> onEnabledChanged)
    {
        _slot = slot;
        _onChunkChanged = onChunkChanged;
        _onEnabledChanged = onEnabledChanged;
        isEnabled = enabled;

        // Populate available chunks
        // Add "(Default)" option first
        var defaultChunk = allChunks.FirstOrDefault(c => c.Id == slot.DefaultChunkId);

        // Filter compatible chunks (same shader type)
        var compatible = allChunks
            .Where(c => c.Type == slot.ShaderType)
            .OrderBy(c => c.IsBuiltIn ? 0 : 1) // Built-in first
            .ThenBy(c => c.Id);

        foreach (var chunk in compatible)
        {
            AvailableChunks.Add(chunk);
        }

        // Set current selection
        if (!string.IsNullOrEmpty(currentChunkId))
        {
            SelectedChunk = AvailableChunks.FirstOrDefault(c => c.Id == currentChunkId);
        }
        else
        {
            // Select default
            SelectedChunk = AvailableChunks.FirstOrDefault(c => c.Id == slot.DefaultChunkId);
        }

        // Initialization complete - now callbacks will fire
        _isInitializing = false;
    }

    partial void OnSelectedChunkChanged(ShaderChunk? value)
    {
        // Don't fire callback during initialization
        if (_isInitializing) return;

        // If selected chunk is the default, pass null (means use default)
        var chunkId = value?.Id == _slot.DefaultChunkId ? null : value?.Id;
        _onChunkChanged(_slot.Id, chunkId);
    }

    partial void OnIsEnabledChanged(bool value)
    {
        // Don't fire callback during initialization
        if (_isInitializing) return;

        _onEnabledChanged(_slot.Id, value);
    }
}

/// <summary>
/// ViewModel for a category of chunk slots
/// </summary>
public partial class ChunkSlotCategoryViewModel : ObservableObject
{
    [ObservableProperty]
    private string categoryName = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ChunkSlotViewModel> slots = [];

    [ObservableProperty]
    private bool isExpanded = true;
}
