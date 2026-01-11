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
    /// Clears the project context
    /// </summary>
    public void ClearProjectContext()
    {
        _projectFolderPath = null;
        _config = null;
        MasterMaterials.Clear();
        Chunks.Clear();
        HasUnsavedChanges = false;
        StatusMessage = null;
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

            // Populate master materials (built-in + custom)
            MasterMaterials.Clear();
            foreach (var master in _masterMaterialService.GetAllMasters(_config))
            {
                MasterMaterials.Add(master);
            }

            // Populate chunks and load their content from files
            Chunks.Clear();
            foreach (var chunkMeta in _config.Chunks)
            {
                var loadedChunk = await _masterMaterialService.LoadChunkFromFileAsync(_projectFolderPath, chunkMeta.Id, ct);
                if (loadedChunk != null)
                {
                    Chunks.Add(loadedChunk);
                }
                else
                {
                    // Chunk file doesn't exist, use metadata only
                    Chunks.Add(chunkMeta);
                }
            }

            HasUnsavedChanges = false;
            StatusMessage = $"Loaded {MasterMaterials.Count} masters, {Chunks.Count} chunks";
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
            _config.Chunks = Chunks.Select(c => new ShaderChunk
            {
                Id = c.Id,
                Type = c.Type,
                Description = c.Description,
                SourceFile = $"{c.Id}.mjs"
            }).ToList();

            // Save config JSON
            await _masterMaterialService.SaveConfigAsync(_projectFolderPath, _config, ct);

            // Save all chunk files
            foreach (var chunk in Chunks)
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
    private void AddNewMaster()
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
    private void DeleteMaster(MasterMaterial? master)
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
        EditChunkRequested?.Invoke(this, chunk);
    }

    [RelayCommand]
    private async Task DeleteChunkAsync(ShaderChunk? chunk, CancellationToken ct = default)
    {
        if (chunk == null || string.IsNullOrEmpty(_projectFolderPath)) return;

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
    }
}
