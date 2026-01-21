using AssetProcessor.MasterMaterials.Models;

namespace AssetProcessor.Services;

/// <summary>
/// Service for managing Master Materials and Shader Chunks.
///
/// Storage structure:
/// {project}/server/assets/content/materials/
/// ├── {masterName}_master.json        # Master material definition
/// ├── mappings.json                   # Material ID to master mappings
/// └── {masterName}/
///     └── chunks/
///         ├── diffusePS.mjs           # Individual chunk files
///         └── glossPS.mjs
/// </summary>
public interface IMasterMaterialService
{
    /// <summary>
    /// Loads config from project folder (all master materials and mappings)
    /// </summary>
    Task<MasterMaterialsConfig> LoadConfigAsync(string projectFolderPath, CancellationToken ct = default);

    /// <summary>
    /// Saves all master materials and mappings to project folder
    /// </summary>
    Task SaveConfigAsync(string projectFolderPath, MasterMaterialsConfig config, CancellationToken ct = default);

    /// <summary>
    /// Saves a single master material to its file
    /// </summary>
    Task SaveMasterAsync(string projectFolderPath, MasterMaterial master, CancellationToken ct = default);

    /// <summary>
    /// Loads a single master material by name
    /// </summary>
    Task<MasterMaterial?> LoadMasterAsync(string projectFolderPath, string masterName, CancellationToken ct = default);

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
    Task<ShaderChunk?> LoadChunkFromFileAsync(string projectFolderPath, string masterName, string chunkName, CancellationToken ct = default);

    /// <summary>
    /// Saves chunk to MJS file
    /// </summary>
    Task SaveChunkToFileAsync(string projectFolderPath, string masterName, ShaderChunk chunk, CancellationToken ct = default);

    /// <summary>
    /// Deletes chunk MJS file
    /// </summary>
    Task DeleteChunkFileAsync(string projectFolderPath, string masterName, string chunkName, CancellationToken ct = default);

    /// <summary>
    /// Generates consolidated chunks.mjs content for export
    /// </summary>
    Task<string> GenerateConsolidatedChunksAsync(string projectFolderPath, MasterMaterial master, CancellationToken ct = default);

    /// <summary>
    /// Adds a chunk to a master material and saves the chunk file
    /// </summary>
    Task AddChunkToMasterAsync(string projectFolderPath, MasterMaterial master, ShaderChunk chunk, CancellationToken ct = default);

    /// <summary>
    /// Removes a chunk from a master material and deletes the chunk file
    /// </summary>
    Task RemoveChunkFromMasterAsync(string projectFolderPath, MasterMaterial master, string chunkName, CancellationToken ct = default);

    /// <summary>
    /// Gets the materials folder path: {project}/server/assets/content/materials/
    /// </summary>
    string GetMaterialsFolderPath(string projectFolderPath);

    /// <summary>
    /// Gets the master material file path: {materials}/{name}_master.json
    /// </summary>
    string GetMasterFilePath(string projectFolderPath, string masterName);

    /// <summary>
    /// Gets the chunks folder path for a specific master: {materials}/{masterName}/chunks/
    /// </summary>
    string GetChunksFolderPath(string projectFolderPath, string masterName);

    /// <summary>
    /// Gets the chunk file path: {materials}/{masterName}/chunks/{chunkName}.mjs
    /// </summary>
    string GetChunkFilePath(string projectFolderPath, string masterName, string chunkName);

    /// <summary>
    /// Gets the relative server path for a chunk: {masterName}/chunks/{chunkName}.mjs
    /// </summary>
    string GetChunkServerPath(string masterName, string chunkName);

    #region Legacy methods (deprecated)

    /// <summary>
    /// [DEPRECATED] Gets the chunks folder path for a project
    /// </summary>
    [Obsolete("Use GetChunksFolderPath(projectFolderPath, masterName) instead")]
    string GetChunksFolderPath(string projectFolderPath);

    /// <summary>
    /// [DEPRECATED] Loads chunk by ID only
    /// </summary>
    [Obsolete("Use LoadChunkFromFileAsync(projectFolderPath, masterName, chunkName) instead")]
    Task<ShaderChunk?> LoadChunkFromFileAsync(string projectFolderPath, string chunkId, CancellationToken ct = default);

    /// <summary>
    /// [DEPRECATED] Saves chunk without master context
    /// </summary>
    [Obsolete("Use SaveChunkToFileAsync(projectFolderPath, masterName, chunk) instead")]
    Task SaveChunkToFileAsync(string projectFolderPath, ShaderChunk chunk, CancellationToken ct = default);

    /// <summary>
    /// [DEPRECATED] Deletes chunk without master context
    /// </summary>
    [Obsolete("Use DeleteChunkFileAsync(projectFolderPath, masterName, chunkName) instead")]
    Task DeleteChunkFileAsync(string projectFolderPath, string chunkId, CancellationToken ct = default);

    #endregion
}
