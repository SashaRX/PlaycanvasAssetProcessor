using System.Collections.ObjectModel;
using AssetProcessor.MasterMaterials.Models;
using AssetProcessor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLog;

namespace AssetProcessor.ViewModels;

/// <summary>
/// ViewModel for the Master Materials tab
/// </summary>
public partial class MasterMaterialsViewModel : ObservableObject
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly IMasterMaterialService _masterMaterialService;
    private readonly ILogService _logService;

    private string? _projectFolderPath;
    private MasterMaterialsConfig? _config;

    [ObservableProperty]
    private ObservableCollection<MasterMaterial> masterMaterials = [];

    [ObservableProperty]
    private ObservableCollection<ShaderChunk> chunks = [];

    [ObservableProperty]
    private MasterMaterial? selectedMaster;

    [ObservableProperty]
    private ShaderChunk? selectedChunk;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private bool hasUnsavedChanges;

    [ObservableProperty]
    private ObservableCollection<ChunkSlotCategoryViewModel> chunkSlotCategories = [];

    /// <summary>
    /// Event raised when chunk editing is requested
    /// </summary>
    public event EventHandler<ShaderChunk>? EditChunkRequested;

    /// <summary>
    /// Event raised when master material editing is requested
    /// </summary>
    public event EventHandler<MasterMaterial>? EditMasterRequested;

    /// <summary>
    /// Event raised when configuration changes
    /// </summary>
    public event EventHandler? ConfigurationChanged;

    public MasterMaterialsViewModel(IMasterMaterialService masterMaterialService, ILogService logService)
    {
        _masterMaterialService = masterMaterialService ?? throw new ArgumentNullException(nameof(masterMaterialService));
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));

        // Load built-in chunks and masters immediately (without project context)
        LoadBuiltInData();
    }

    /// <summary>
    /// Loads built-in chunks and masters (available without project context)
    /// </summary>
    private void LoadBuiltInData()
    {
        // Add built-in PlayCanvas shader chunks
        foreach (var builtInChunk in DefaultShaderChunks.GetAllChunks())
        {
            Chunks.Add(builtInChunk);
        }

        // Add built-in master materials (using empty config to get only built-ins)
        var emptyConfig = new MasterMaterialsConfig();
        foreach (var master in _masterMaterialService.GetAllMasters(emptyConfig))
        {
            MasterMaterials.Add(master);
        }

        int builtInChunksCount = Chunks.Count;
        int builtInMastersCount = MasterMaterials.Count;
        StatusMessage = $"Loaded {builtInMastersCount} built-in masters, {builtInChunksCount} built-in chunks";
        _logService.LogInfo(StatusMessage);
    }

    /// <summary>
    /// Gets the current project folder path
    /// </summary>
    public string? ProjectFolderPath => _projectFolderPath;

    /// <summary>
    /// Gets the current configuration
    /// </summary>
    public MasterMaterialsConfig? Config => _config;

    /// <summary>
    /// Sets the project context and loads config
    /// </summary>
    public async Task SetProjectContextAsync(string projectFolderPath, CancellationToken ct = default)
    {
        _projectFolderPath = projectFolderPath;
        await LoadConfigAsync(ct);
    }

    /// <summary>
    /// Clears the project context (keeps built-in data)
    /// </summary>
    public void ClearProjectContext()
    {
        _projectFolderPath = null;
        _config = null;

        // Clear and reload only built-in data
        MasterMaterials.Clear();
        Chunks.Clear();
        LoadBuiltInData();

        HasUnsavedChanges = false;
    }

    [RelayCommand]
    private async Task LoadConfigAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_projectFolderPath))
        {
            StatusMessage = "No project folder set";
            return;
        }

        IsLoading = true;
        try
        {
            _config = await _masterMaterialService.LoadConfigAsync(_projectFolderPath, ct);

            // Clear and reload master materials (built-in + custom)
            MasterMaterials.Clear();
            foreach (var master in _masterMaterialService.GetAllMasters(_config))
            {
                MasterMaterials.Add(master);
            }

            // Clear and reload chunks: first add built-in PlayCanvas chunks
            Chunks.Clear();
            foreach (var builtInChunk in DefaultShaderChunks.GetAllChunks())
            {
                Chunks.Add(builtInChunk);
            }

            // Then load custom chunks from files
            foreach (var chunkMeta in _config.Chunks)
            {
                // Skip if a built-in chunk with same ID exists
                if (Chunks.Any(c => c.Id == chunkMeta.Id && c.IsBuiltIn))
                {
                    continue;
                }

                var loadedChunk = await _masterMaterialService.LoadChunkFromFileAsync(_projectFolderPath, chunkMeta.Id, ct);
                if (loadedChunk != null)
                {
                    loadedChunk.IsBuiltIn = false;
                    Chunks.Add(loadedChunk);
                }
                else
                {
                    // Chunk file doesn't exist, use metadata only
                    chunkMeta.IsBuiltIn = false;
                    Chunks.Add(chunkMeta);
                }
            }

            HasUnsavedChanges = false;
            int builtInCount = Chunks.Count(c => c.IsBuiltIn);
            int customCount = Chunks.Count - builtInCount;
            StatusMessage = $"Loaded {MasterMaterials.Count} masters, {builtInCount} built-in chunks, {customCount} custom chunks";
            _logService.LogInfo(StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading config: {ex.Message}";
            _logService.LogError(StatusMessage);
            Logger.Error(ex, "Error loading MasterMaterials config");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SaveConfigAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_projectFolderPath) || _config == null)
        {
            StatusMessage = "Cannot save: no project folder or config";
            return;
        }

        IsLoading = true;
        try
        {
            // Update config from observable collections (excluding built-ins)
            _config.Masters = MasterMaterials.Where(m => !m.IsBuiltIn).ToList();

            // Only save custom chunks (not built-in ones)
            var customChunks = Chunks.Where(c => !c.IsBuiltIn).ToList();
            _config.Chunks = customChunks.Select(c => new ShaderChunk
            {
                Id = c.Id,
                Type = c.Type,
                Description = c.Description,
                SourceFile = $"{c.Id}.mjs"
            }).ToList();

            // Save config JSON
            await _masterMaterialService.SaveConfigAsync(_projectFolderPath, _config, ct);

            // Save only custom chunk files (not built-in)
            foreach (var chunk in customChunks)
            {
                await _masterMaterialService.SaveChunkToFileAsync(_projectFolderPath, chunk, ct);
            }

            HasUnsavedChanges = false;
            StatusMessage = "Configuration saved";
            _logService.LogInfo(StatusMessage);
            ConfigurationChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error saving config: {ex.Message}";
            _logService.LogError(StatusMessage);
            Logger.Error(ex, "Error saving MasterMaterials config");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task AddNewMasterAsync()
    {
        var index = MasterMaterials.Count(m => !m.IsBuiltIn) + 1;
        var newMaster = new MasterMaterial
        {
            Name = $"custom_material_{index}",
            Description = "New custom material",
            BlendType = "opaque",
            IsBuiltIn = false
        };

        // Initialize slot assignments with defaults
        newMaster.InitializeSlotAssignments();

        MasterMaterials.Add(newMaster);
        SelectedMaster = newMaster;
        HasUnsavedChanges = true;
        EditMasterRequested?.Invoke(this, newMaster);

        // Auto-save
        await SaveConfigAsync();
    }

    [RelayCommand]
    private void EditMaster(MasterMaterial? master)
    {
        if (master == null) return;

        if (master.IsBuiltIn)
        {
            StatusMessage = "Cannot edit built-in master materials";
            return;
        }

        EditMasterRequested?.Invoke(this, master);
    }

    [RelayCommand]
    private async Task CloneMasterAsync(MasterMaterial? master)
    {
        if (master == null) return;

        // Generate unique name for the clone
        string baseName = master.Name.EndsWith("_copy") ? master.Name : $"{master.Name}_copy";
        string newName = baseName;
        int counter = 1;
        while (MasterMaterials.Any(m => m.Name == newName))
        {
            newName = $"{baseName}_{counter++}";
        }

        var clonedMaster = new MasterMaterial
        {
            Name = newName,
            Description = $"Clone of {master.Name}",
            BlendType = master.BlendType,
            IsBuiltIn = false,
            ChunkIds = [.. master.ChunkIds], // Copy all chunk IDs
            SlotAssignments = master.SlotAssignments?.Select(a => new ChunkSlotAssignment
            {
                SlotId = a.SlotId,
                ChunkId = a.ChunkId,
                Enabled = a.Enabled
            }).ToList()
        };

        MasterMaterials.Add(clonedMaster);
        SelectedMaster = clonedMaster;
        HasUnsavedChanges = true;
        StatusMessage = $"Created clone: {newName}";
        _logService.LogInfo(StatusMessage);

        // Auto-save after cloning
        await SaveConfigAsync();
    }

    [RelayCommand]
    private async Task DeleteMasterAsync(MasterMaterial? master)
    {
        if (master == null) return;

        if (master.IsBuiltIn)
        {
            StatusMessage = "Cannot delete built-in master materials";
            return;
        }

        MasterMaterials.Remove(master);
        HasUnsavedChanges = true;
        StatusMessage = $"Deleted master material: {master.Name}";

        // Auto-save
        await SaveConfigAsync();
    }

    [RelayCommand]
    private void AddNewChunk()
    {
        var index = Chunks.Count + 1;
        var newChunk = new ShaderChunk
        {
            Id = $"customChunk{index}",
            Type = "fragment",
            Description = "New shader chunk",
            Glsl = "// GLSL code here\n",
            Wgsl = "// WGSL code here\n"
        };

        Chunks.Add(newChunk);
        SelectedChunk = newChunk;
        HasUnsavedChanges = true;
        EditChunkRequested?.Invoke(this, newChunk);
    }

    [RelayCommand]
    private void EditChunk(ShaderChunk? chunk)
    {
        if (chunk == null) return;

        if (chunk.IsBuiltIn)
        {
            StatusMessage = "Cannot edit built-in chunks. Use 'Copy' to create an editable version.";
            return;
        }

        EditChunkRequested?.Invoke(this, chunk);
    }

    [RelayCommand]
    private void CopyChunk(ShaderChunk? chunk)
    {
        if (chunk == null) return;

        // Generate unique ID for the copy
        string baseId = chunk.Id.EndsWith("_custom") ? chunk.Id : $"{chunk.Id}_custom";
        string newId = baseId;
        int counter = 1;
        while (Chunks.Any(c => c.Id == newId))
        {
            newId = $"{baseId}_{counter++}";
        }

        var copiedChunk = chunk.Clone(newId);
        Chunks.Add(copiedChunk);
        SelectedChunk = copiedChunk;
        HasUnsavedChanges = true;
        StatusMessage = $"Created copy: {newId}";

        // Open editor for the new chunk
        EditChunkRequested?.Invoke(this, copiedChunk);
    }

    [RelayCommand]
    private async Task DeleteChunkAsync(ShaderChunk? chunk, CancellationToken ct = default)
    {
        if (chunk == null || string.IsNullOrEmpty(_projectFolderPath)) return;

        if (chunk.IsBuiltIn)
        {
            StatusMessage = "Cannot delete built-in chunks";
            return;
        }

        // Remove from all masters using this chunk
        foreach (var master in MasterMaterials.Where(m => !m.IsBuiltIn))
        {
            master.ChunkIds.Remove(chunk.Id);
        }

        Chunks.Remove(chunk);

        // Delete the file
        await _masterMaterialService.DeleteChunkFileAsync(_projectFolderPath, chunk.Id, ct);

        HasUnsavedChanges = true;
        StatusMessage = $"Deleted chunk: {chunk.Id}";
    }

    [RelayCommand]
    private async Task SaveChunkAsync(ShaderChunk chunk, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_projectFolderPath)) return;

        await _masterMaterialService.SaveChunkToFileAsync(_projectFolderPath, chunk, ct);
        HasUnsavedChanges = true;
        StatusMessage = $"Chunk '{chunk.Id}' saved";
    }

    /// <summary>
    /// Updates a chunk in the collection
    /// </summary>
    public void UpdateChunk(ShaderChunk updatedChunk)
    {
        var index = -1;
        for (int i = 0; i < Chunks.Count; i++)
        {
            if (Chunks[i].Id == updatedChunk.Id)
            {
                index = i;
                break;
            }
        }

        if (index >= 0)
        {
            Chunks[index] = updatedChunk;
        }
        else
        {
            Chunks.Add(updatedChunk);
        }

        HasUnsavedChanges = true;
    }

    /// <summary>
    /// Gets the master material name for a given PlayCanvas material ID
    /// </summary>
    public string? GetMasterNameForMaterial(int materialId)
    {
        return _config?.MaterialInstanceMappings.TryGetValue(materialId, out var name) == true ? name : null;
    }

    /// <summary>
    /// Sets the master material for a PlayCanvas material
    /// </summary>
    public void SetMasterForMaterial(int materialId, string? masterName)
    {
        if (_config == null) return;

        if (string.IsNullOrEmpty(masterName))
        {
            _masterMaterialService.RemoveMaterialMaster(_config, materialId);
        }
        else
        {
            _masterMaterialService.SetMaterialMaster(_config, materialId, masterName);
        }

        HasUnsavedChanges = true;

        // Auto-save
        _ = SaveConfigAsync();
    }

    /// <summary>
    /// Adds a chunk to a master material
    /// </summary>
    public void AddChunkToMaster(MasterMaterial master, string chunkId)
    {
        if (master.IsBuiltIn) return;

        if (!master.ChunkIds.Contains(chunkId))
        {
            master.ChunkIds.Add(chunkId);
            HasUnsavedChanges = true;

            // Auto-save
            _ = SaveConfigAsync();
        }
    }

    /// <summary>
    /// Removes a chunk from a master material
    /// </summary>
    public void RemoveChunkFromMaster(MasterMaterial master, string chunkId)
    {
        if (master.IsBuiltIn) return;

        master.ChunkIds.Remove(chunkId);
        HasUnsavedChanges = true;

        // Auto-save
        _ = SaveConfigAsync();
    }

    /// <summary>
    /// Called when SelectedMaster changes - rebuilds the chunk slot categories
    /// </summary>
    partial void OnSelectedMasterChanged(MasterMaterial? value)
    {
        RebuildChunkSlotCategories();
    }

    /// <summary>
    /// Rebuilds the chunk slot categories based on the selected master material
    /// </summary>
    private void RebuildChunkSlotCategories()
    {
        // Use Dispatcher to avoid virtualization issues during layout
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null)
        {
            dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(RebuildChunkSlotCategoriesInternal));
        }
        else
        {
            // Fallback for design-time or tests
            RebuildChunkSlotCategoriesInternal();
        }
    }

    private void RebuildChunkSlotCategoriesInternal()
    {
        ChunkSlotCategories.Clear();

        if (SelectedMaster == null)
        {
            return;
        }

        // Group slots by category
        var categories = ShaderChunkSchema.GetCategories();

        foreach (var categoryName in categories)
        {
            var categoryVm = new ChunkSlotCategoryViewModel
            {
                CategoryName = categoryName,
                IsExpanded = categoryName == "Surface" || categoryName == "Vertex" // Expand important ones by default
            };

            var slots = ShaderChunkSchema.GetSlotsByCategory(categoryName);
            foreach (var slot in slots)
            {
                // Get current chunk assignment for this slot
                var currentChunkId = SelectedMaster.SlotAssignments?
                    .FirstOrDefault(a => a.SlotId == slot.Id)?.ChunkId;

                var isEnabled = SelectedMaster.IsSlotEnabled(slot.Id);

                var slotVm = new ChunkSlotViewModel(
                    slot,
                    Chunks,
                    currentChunkId,
                    isEnabled,
                    OnSlotChunkChanged,
                    OnSlotEnabledChanged
                );

                categoryVm.Slots.Add(slotVm);
            }

            ChunkSlotCategories.Add(categoryVm);
        }
    }

    /// <summary>
    /// Called when a chunk is selected for a slot
    /// </summary>
    private void OnSlotChunkChanged(string slotId, string? chunkId)
    {
        if (SelectedMaster == null || SelectedMaster.IsBuiltIn) return;

        SelectedMaster.SetChunkForSlot(slotId, chunkId);
        HasUnsavedChanges = true;

        // If a chunk was selected, also select it in the editor
        if (!string.IsNullOrEmpty(chunkId))
        {
            var chunk = Chunks.FirstOrDefault(c => c.Id == chunkId);
            if (chunk != null)
            {
                SelectedChunk = chunk;
            }
        }

        // Auto-save
        _ = SaveConfigAsync();
    }

    /// <summary>
    /// Called when a slot is enabled/disabled
    /// </summary>
    private void OnSlotEnabledChanged(string slotId, bool enabled)
    {
        if (SelectedMaster == null || SelectedMaster.IsBuiltIn) return;

        SelectedMaster.SetSlotEnabled(slotId, enabled);
        HasUnsavedChanges = true;

        // Auto-save
        _ = SaveConfigAsync();
    }
}
