using AssetProcessor.MasterMaterials.Models;

namespace AssetProcessor.Services;

/// <summary>
/// Service for managing Master Materials and Shader Chunks
/// </summary>
public interface IMasterMaterialService
{
    /// <summary>
    /// Loads config from project folder, creating default if not exists
    /// </summary>
    Task<MasterMaterialsConfig> LoadConfigAsync(string projectFolderPath, CancellationToken ct = default);

    /// <summary>
    /// Saves config to project folder
    /// </summary>
    Task SaveConfigAsync(string projectFolderPath, MasterMaterialsConfig config, CancellationToken ct = default);

    /// <summary>
    /// Gets all available master materials (built-in + custom)
    /// </summary>
    IEnumerable<MasterMaterial> GetAllMasters(MasterMaterialsConfig config);

    /// <summary>
    /// Gets master material for a PlayCanvas material ID
    /// </summary>
    MasterMaterial? GetMasterForMaterial(MasterMaterialsConfig config, int materialId);

    /// <summary>
    /// Sets master material assignment for a PlayCanvas material
    /// </summary>
    void SetMaterialMaster(MasterMaterialsConfig config, int materialId, string masterName);

    /// <summary>
    /// Removes master material assignment for a PlayCanvas material
    /// </summary>
    void RemoveMaterialMaster(MasterMaterialsConfig config, int materialId);

    /// <summary>
    /// Loads chunk content from MJS file
    /// </summary>
    Task<ShaderChunk?> LoadChunkFromFileAsync(string projectFolderPath, string chunkId, CancellationToken ct = default);

    /// <summary>
    /// Saves chunk to MJS file
    /// </summary>
    Task SaveChunkToFileAsync(string projectFolderPath, ShaderChunk chunk, CancellationToken ct = default);

    /// <summary>
    /// Deletes chunk MJS file
    /// </summary>
    Task DeleteChunkFileAsync(string projectFolderPath, string chunkId, CancellationToken ct = default);

    /// <summary>
    /// Generates consolidated chunks.mjs content for export
    /// </summary>
    Task<string> GenerateConsolidatedChunksAsync(string projectFolderPath, MasterMaterial master, CancellationToken ct = default);

    /// <summary>
    /// Gets the chunks folder path for a project
    /// </summary>
    string GetChunksFolderPath(string projectFolderPath);
}
