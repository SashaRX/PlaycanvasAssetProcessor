using System.Collections.ObjectModel;
using AssetProcessor.MasterMaterials.Models;
using AssetProcessor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLog;

namespace AssetProcessor.ViewModels;

/// <summary>
/// ViewModel for the Master Materials tab.
///
/// Storage structure:
/// {project}/server/assets/content/materials/
/// ├── {masterName}_master.json        # Master material definition
/// ├── mappings.json                   # Material ID to master mappings
/// └── {masterName}/
///     └── chunks/
///         └── {chunkName}.mjs         # Individual chunk files
/// </summary>
public partial class MasterMaterialsViewModel : ObservableObject
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly IMasterMaterialService _masterMaterialService;
    private readonly ILogService _logService;

    private string? _projectFolderPath;
    private MasterMaterialsConfig? _config;

    // Debounce mechanism for auto-save
    private CancellationTokenSource? _saveDebounceTokenSource;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private const int SaveDebounceDelayMs = 300;

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
        Logger.Info($"SetProjectContextAsync called with path: {projectFolderPath}");
        _projectFolderPath = projectFolderPath;
        await LoadConfigAsync(ct);
        Logger.Info($"SetProjectContextAsync completed. _config is {(_config == null ? "NULL" : "initialized")}");
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
            Logger.Info($"LoadConfigAsync: Loading config from {_projectFolderPath}");
            _config = await _masterMaterialService.LoadConfigAsync(_projectFolderPath, ct);
            Logger.Info($"LoadConfigAsync: _config loaded, {_config.Masters.Count} custom masters, {_config.MaterialInstanceMappings.Count} mappings");

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

            // Load custom chunks from each master's chunks folder
            foreach (var master in _config.Masters.Where(m => !m.IsBuiltIn))
            {
                foreach (var (chunkName, _) in master.Chunks)
                {
                    // Skip if already loaded
                    if (Chunks.Any(c => c.Id == chunkName))
                    {
                        continue;
                    }

                    var loadedChunk = await _masterMaterialService.LoadChunkFromFileAsync(_projectFolderPath, master.Name, chunkName, ct);
                    if (loadedChunk != null)
                    {
                        loadedChunk.IsBuiltIn = false;
                        Chunks.Add(loadedChunk);
                    }
                }
            }

            HasUnsavedChanges = false;

            // Notify UI about loaded default master material
            OnPropertyChanged(nameof(DefaultMasterMaterial));

            int builtInCount = Chunks.Count(c => c.IsBuiltIn);
            int customCount = Chunks.Count - builtInCount;
            StatusMessage = $"Loaded {MasterMaterials.Count} masters, {builtInCount} built-in chunks, {customCount} custom chunks";
            _logService.LogInfo(StatusMessage);
            if (!string.IsNullOrEmpty(_config?.DefaultMasterMaterial))
            {
                _logService.LogInfo($"Default master material: {_config.DefaultMasterMaterial}");
            }
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

    /// <summary>
    /// Schedules a debounced save operation. Multiple rapid calls will be coalesced.
    /// </summary>
    private void ScheduleDebouncedSave()
    {
        _saveDebounceTokenSource?.Cancel();
        _saveDebounceTokenSource = new CancellationTokenSource();
        var token = _saveDebounceTokenSource.Token;

        // Start debounce timer
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(SaveDebounceDelayMs, token);
                if (token.IsCancellationRequested) return;

                // Save on background thread - no need to marshal to UI
                await SaveConfigInternalAsync(token);
                Logger.Info("Debounced auto-save completed successfully");
            }
            catch (OperationCanceledException)
            {
                // Debounce cancelled - this is normal
            }
            catch (Exception ex)
            {
                _logService.LogError($"Debounced auto-save failed: {ex.Message}");
                Logger.Error(ex, "Debounced auto-save failed");
            }
        }, token);
    }

    /// <summary>
    /// Internal save method with locking to prevent concurrent saves
    /// </summary>
    private async Task SaveConfigInternalAsync(CancellationToken ct)
    {
        if (!await _saveLock.WaitAsync(0, ct))
        {
            return;
        }

        try
        {
            await SaveConfigAsync(ct);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    [RelayCommand]
    private async Task SaveConfigAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_projectFolderPath))
        {
            var msg = "Cannot save master materials config: no project selected. Select a project first.";
            StatusMessage = msg;
            Logger.Warn(msg);
            _logService.LogWarn(msg);
            return;
        }

        if (_config == null)
        {
            var msg = "Cannot save master materials config: config is null";
            StatusMessage = msg;
            Logger.Warn(msg);
            return;
        }

        IsLoading = true;
        try
        {
            // Update config from observable collections (excluding built-ins)
            var mastersToSave = MasterMaterials.Where(m => !m.IsBuiltIn).ToList();
            _config.Masters = mastersToSave;
            Logger.Debug($"Saving {mastersToSave.Count} custom masters, {_config.MaterialInstanceMappings.Count} material mappings");

            // Save config (which saves each master to its own file + mappings)
            await _masterMaterialService.SaveConfigAsync(_projectFolderPath, _config, ct);

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

        MasterMaterials.Add(newMaster);
        SelectedMaster = newMaster;
        HasUnsavedChanges = true;
        EditMasterRequested?.Invoke(this, newMaster);

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

        var clonedMaster = master.Clone();
        clonedMaster.Name = newName;
        clonedMaster.Description = $"Clone of {master.Name}";

        MasterMaterials.Add(clonedMaster);
        SelectedMaster = clonedMaster;
        HasUnsavedChanges = true;
        StatusMessage = $"Created clone: {newName}";
        _logService.LogInfo(StatusMessage);

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
            if (master.Chunks.ContainsKey(chunk.Id))
            {
                await _masterMaterialService.RemoveChunkFromMasterAsync(_projectFolderPath, master, chunk.Id, ct);
            }
        }

        Chunks.Remove(chunk);
        HasUnsavedChanges = true;
        StatusMessage = $"Deleted chunk: {chunk.Id}";
    }

    [RelayCommand]
    private async Task SaveChunkAsync(ShaderChunk chunk, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_projectFolderPath) || SelectedMaster == null || SelectedMaster.IsBuiltIn)
        {
            StatusMessage = "Select a custom master material first";
            return;
        }

        await _masterMaterialService.SaveChunkToFileAsync(_projectFolderPath, SelectedMaster.Name, chunk, ct);
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
    /// Gets or sets the default master material name for materials without explicit mapping
    /// </summary>
    public string? DefaultMasterMaterial
    {
        get => _config?.DefaultMasterMaterial;
        set
        {
            _config ??= new MasterMaterialsConfig();

            if (_config.DefaultMasterMaterial != value)
            {
                _config.DefaultMasterMaterial = value;
                OnPropertyChanged(nameof(DefaultMasterMaterial));
                HasUnsavedChanges = true;
                _ = SaveConfigAsync();
                _logService.LogInfo($"Default master material set to: {value ?? "(none)"}");
            }
        }
    }

    /// <summary>
    /// Gets the master material name for a given PlayCanvas material ID.
    /// Returns explicit mapping if exists, otherwise returns default master material.
    /// </summary>
    public string? GetMasterNameForMaterial(int materialId)
    {
        if (_config?.MaterialInstanceMappings.TryGetValue(materialId, out var name) == true)
            return name;

        return _config?.DefaultMasterMaterial;
    }

    /// <summary>
    /// Gets the explicit master material name for a material (ignoring default)
    /// </summary>
    public string? GetExplicitMasterNameForMaterial(int materialId)
    {
        return _config?.MaterialInstanceMappings.TryGetValue(materialId, out var name) == true ? name : null;
    }

    /// <summary>
    /// Sets the master material for a PlayCanvas material
    /// </summary>
    public void SetMasterForMaterial(int materialId, string? masterName)
    {
        _config ??= new MasterMaterialsConfig();

        if (string.IsNullOrEmpty(masterName))
        {
            _masterMaterialService.RemoveMaterialMaster(_config, materialId);
            Logger.Info($"Removed master mapping for material {materialId}");
        }
        else
        {
            _masterMaterialService.SetMaterialMaster(_config, materialId, masterName);
            Logger.Info($"Set master mapping: material {materialId} -> {masterName}");
        }

        HasUnsavedChanges = true;
        ScheduleDebouncedSave();
    }

    /// <summary>
    /// Sets the master material for multiple PlayCanvas materials at once
    /// </summary>
    public void SetMasterForMaterials(IEnumerable<int> materialIds, string? masterName)
    {
        _config ??= new MasterMaterialsConfig();

        int count = 0;
        foreach (var materialId in materialIds)
        {
            if (string.IsNullOrEmpty(masterName))
            {
                _masterMaterialService.RemoveMaterialMaster(_config, materialId);
            }
            else
            {
                _masterMaterialService.SetMaterialMaster(_config, materialId, masterName);
            }
            count++;
        }

        if (count > 0)
        {
            HasUnsavedChanges = true;
            _ = SaveConfigAsync();
            _logService.LogInfo($"Set master '{masterName ?? "(none)"}' for {count} materials");
        }
    }

    /// <summary>
    /// Adds a chunk to a master material
    /// </summary>
    public async Task AddChunkToMasterAsync(MasterMaterial master, ShaderChunk chunk)
    {
        if (master.IsBuiltIn || string.IsNullOrEmpty(_projectFolderPath)) return;

        await _masterMaterialService.AddChunkToMasterAsync(_projectFolderPath, master, chunk);
        HasUnsavedChanges = true;
        ScheduleDebouncedSave();
    }

    /// <summary>
    /// Removes a chunk from a master material
    /// </summary>
    public async Task RemoveChunkFromMasterAsync(MasterMaterial master, string chunkName)
    {
        if (master.IsBuiltIn || string.IsNullOrEmpty(_projectFolderPath)) return;

        await _masterMaterialService.RemoveChunkFromMasterAsync(_projectFolderPath, master, chunkName);
        HasUnsavedChanges = true;
        ScheduleDebouncedSave();
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
        // Run directly - no dispatcher needed since this is called from property setter which runs on UI thread
        RebuildChunkSlotCategoriesInternal();
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
                IsExpanded = categoryName == "Surface" || categoryName == "Vertex"
            };

            var slots = ShaderChunkSchema.GetSlotsByCategory(categoryName);
            foreach (var slot in slots)
            {
                // Check if this chunk is in the master's Chunks dictionary
                var isInMaster = SelectedMaster.Chunks.ContainsKey(slot.DefaultChunkId);
                var currentChunkId = isInMaster ? slot.DefaultChunkId : null;

                // For built-in standard material, all slots use defaults (enabled)
                var isEnabled = SelectedMaster.IsBuiltIn || isInMaster || slot.IsEnabledByDefault;

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
        if (SelectedMaster == null || SelectedMaster.IsBuiltIn || string.IsNullOrEmpty(_projectFolderPath))
            return;

        if (!string.IsNullOrEmpty(chunkId))
        {
            // Add chunk to master
            var chunk = Chunks.FirstOrDefault(c => c.Id == chunkId);
            if (chunk != null)
            {
                _ = AddChunkToMasterAsync(SelectedMaster, chunk);
                SelectedChunk = chunk;
            }
        }
        else
        {
            // Remove chunk from master (use default)
            var slot = ShaderChunkSchema.GetSlot(slotId);
            if (slot != null)
            {
                _ = RemoveChunkFromMasterAsync(SelectedMaster, slot.DefaultChunkId);
            }
        }
    }

    /// <summary>
    /// Called when a slot is enabled/disabled
    /// </summary>
    private void OnSlotEnabledChanged(string slotId, bool enabled)
    {
        if (SelectedMaster == null || SelectedMaster.IsBuiltIn)
            return;

        var slot = ShaderChunkSchema.GetSlot(slotId);
        if (slot == null) return;

        if (enabled)
        {
            // When enabling, add the default chunk
            var chunk = Chunks.FirstOrDefault(c => c.Id == slot.DefaultChunkId);
            if (chunk != null && !string.IsNullOrEmpty(_projectFolderPath))
            {
                _ = AddChunkToMasterAsync(SelectedMaster, chunk);
            }
        }
        else
        {
            // When disabling, remove the chunk
            if (!string.IsNullOrEmpty(_projectFolderPath))
            {
                _ = RemoveChunkFromMasterAsync(SelectedMaster, slot.DefaultChunkId);
            }
        }

        HasUnsavedChanges = true;
        ScheduleDebouncedSave();
    }
}
