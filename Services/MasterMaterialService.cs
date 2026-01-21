using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using AssetProcessor.MasterMaterials.Models;
using NLog;

namespace AssetProcessor.Services;

/// <summary>
/// Service for managing Master Materials and Shader Chunks.
///
/// Storage structure:
/// {project}/server/assets/content/materials/
/// ├── {masterName}_master.json        # Master material definition
/// └── {masterName}/
///     └── chunks/
///         ├── diffusePS.mjs           # Individual chunk files
///         └── glossPS.mjs
/// </summary>
public partial class MasterMaterialService : IMasterMaterialService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Standard PlayCanvas material chunks (real PlayCanvas StandardMaterial)
    /// </summary>
    private static readonly List<string> StandardMaterialChunks =
    [
        // ===== VERTEX SHADER =====
        "transformVS",
        "normalVS",

        // ===== FRONTEND - Material Properties =====
        "diffusePS",
        "normalMapPS",
        "TBNPS",
        "metalnessPS",
        "glossPS",
        "specularPS",
        "aoPS",
        "emissivePS",
        "opacityPS",
        "alphaTestPS",

        // ===== BACKEND - Lighting =====
        "viewDirPS",
        "reflDirPS",
        "fresnelSchlickPS",
        "lightDiffuseLambertPS",
        "lightSpecularBlinnPS",
        "aoDiffuseOccPS",
        "aoSpecOccPS",

        // ===== BACKEND - Reflections =====
        "sphericalPS",
        "decodePS",
        "envAtlasPS",
        "cubeMapRotatePS",
        "cubeMapProjectPS",
        "envProcPS",
        "reflectionEnvPS",

        // ===== OUTPUT =====
        "gammaPS",
        "tonemappingAcesPS",
        "fogPS",
        "combinePS",
        "outputPS",
        "outputAlphaPS"
    ];

    /// <summary>
    /// Built-in master materials (always available)
    /// </summary>
    private static readonly List<MasterMaterial> BuiltInMasters =
    [
        new()
        {
            Name = "standard",
            Description = "PlayCanvas StandardMaterial - full PBR with lighting, reflections, all material properties",
            BlendType = "opaque",
            IsBuiltIn = true
            // No chunks - standard uses engine defaults
        }
    ];

    /// <summary>
    /// Gets the materials folder path: {project}/server/assets/content/materials/
    /// </summary>
    public string GetMaterialsFolderPath(string projectFolderPath)
    {
        return Path.Combine(projectFolderPath, "server", "assets", "content", "materials");
    }

    /// <summary>
    /// Gets the master material file path: {materials}/{name}_master.json
    /// </summary>
    public string GetMasterFilePath(string projectFolderPath, string masterName)
    {
        return Path.Combine(GetMaterialsFolderPath(projectFolderPath), $"{masterName}_master.json");
    }

    /// <summary>
    /// Gets the chunks folder path for a specific master: {materials}/{masterName}/chunks/
    /// </summary>
    public string GetChunksFolderPath(string projectFolderPath, string masterName)
    {
        return Path.Combine(GetMaterialsFolderPath(projectFolderPath), masterName, "chunks");
    }

    /// <summary>
    /// Gets the chunk file path: {materials}/{masterName}/chunks/{chunkName}.mjs
    /// </summary>
    public string GetChunkFilePath(string projectFolderPath, string masterName, string chunkName)
    {
        return Path.Combine(GetChunksFolderPath(projectFolderPath, masterName), $"{chunkName}.mjs");
    }

    /// <summary>
    /// Gets the relative server path for a chunk: {masterName}/chunks/{chunkName}.mjs
    /// </summary>
    public string GetChunkServerPath(string masterName, string chunkName)
    {
        return $"{masterName}/chunks/{chunkName}.mjs";
    }

    public async Task<MasterMaterialsConfig> LoadConfigAsync(string projectFolderPath, CancellationToken ct = default)
    {
        var config = new MasterMaterialsConfig();

        if (string.IsNullOrEmpty(projectFolderPath))
        {
            Logger.Warn("LoadConfigAsync: projectFolderPath is null or empty");
            return config;
        }

        var materialsFolder = GetMaterialsFolderPath(projectFolderPath);
        Logger.Debug($"LoadConfigAsync: Looking for materials in {materialsFolder}");

        if (!Directory.Exists(materialsFolder))
        {
            Logger.Info($"Materials folder not found, returning empty config: {materialsFolder}");
            return config;
        }

        // Load all *_master.json files
        string[] masterFiles;
        try
        {
            masterFiles = Directory.GetFiles(materialsFolder, "*_master.json", SearchOption.TopDirectoryOnly);
            Logger.Debug($"LoadConfigAsync: Found {masterFiles.Length} master files");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Error enumerating master files in {materialsFolder}");
            return config;
        }

        foreach (var masterFile in masterFiles)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var json = await File.ReadAllTextAsync(masterFile, ct);
                var master = JsonSerializer.Deserialize<MasterMaterial>(json, JsonOptions);

                if (master != null)
                {
                    config.Masters.Add(master);
                    Logger.Debug($"Loaded master material: {master.Name} with {master.Chunks.Count} chunks");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error loading master material from {masterFile}");
            }
        }

        // Load mappings config if exists
        var mappingsPath = Path.Combine(materialsFolder, "mappings.json");
        if (File.Exists(mappingsPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(mappingsPath, ct);
                var mappingsConfig = JsonSerializer.Deserialize<MaterialMappingsConfig>(json, JsonOptions);
                if (mappingsConfig != null)
                {
                    config.MaterialInstanceMappings = mappingsConfig.MaterialInstanceMappings;
                    config.DefaultMasterMaterial = mappingsConfig.DefaultMasterMaterial;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error loading mappings from {mappingsPath}");
            }
        }

        Logger.Info($"Loaded {config.Masters.Count} master materials from {materialsFolder}");
        return config;
    }

    public async Task SaveConfigAsync(string projectFolderPath, MasterMaterialsConfig config, CancellationToken ct = default)
    {
        var materialsFolder = GetMaterialsFolderPath(projectFolderPath);
        Directory.CreateDirectory(materialsFolder);

        // Save each master material to its own file
        foreach (var master in config.Masters.Where(m => !m.IsBuiltIn))
        {
            ct.ThrowIfCancellationRequested();

            var masterPath = GetMasterFilePath(projectFolderPath, master.Name);
            var json = JsonSerializer.Serialize(master, JsonOptions);
            await File.WriteAllTextAsync(masterPath, json, ct);
            Logger.Debug($"Saved master material: {master.Name}");
        }

        // Save mappings to separate file
        var mappingsPath = Path.Combine(materialsFolder, "mappings.json");
        var mappingsConfig = new MaterialMappingsConfig
        {
            MaterialInstanceMappings = config.MaterialInstanceMappings,
            DefaultMasterMaterial = config.DefaultMasterMaterial
        };
        var mappingsJson = JsonSerializer.Serialize(mappingsConfig, JsonOptions);
        await File.WriteAllTextAsync(mappingsPath, mappingsJson, ct);

        Logger.Info($"Saved {config.Masters.Count(m => !m.IsBuiltIn)} master materials to {materialsFolder}");
    }

    /// <summary>
    /// Saves a single master material to its file
    /// </summary>
    public async Task SaveMasterAsync(string projectFolderPath, MasterMaterial master, CancellationToken ct = default)
    {
        if (master.IsBuiltIn)
        {
            Logger.Warn($"Cannot save built-in master material: {master.Name}");
            return;
        }

        var materialsFolder = GetMaterialsFolderPath(projectFolderPath);
        Directory.CreateDirectory(materialsFolder);

        var masterPath = GetMasterFilePath(projectFolderPath, master.Name);
        var json = JsonSerializer.Serialize(master, JsonOptions);
        await File.WriteAllTextAsync(masterPath, json, ct);

        Logger.Info($"Saved master material: {master.Name} to {masterPath}");
    }

    /// <summary>
    /// Loads a single master material by name
    /// </summary>
    public async Task<MasterMaterial?> LoadMasterAsync(string projectFolderPath, string masterName, CancellationToken ct = default)
    {
        // Check built-in first
        var builtIn = BuiltInMasters.FirstOrDefault(m => m.Name == masterName);
        if (builtIn != null)
        {
            return builtIn;
        }

        var masterPath = GetMasterFilePath(projectFolderPath, masterName);
        if (!File.Exists(masterPath))
        {
            Logger.Warn($"Master material not found: {masterPath}");
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(masterPath, ct);
            return JsonSerializer.Deserialize<MasterMaterial>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Error loading master material from {masterPath}");
            return null;
        }
    }

    public IEnumerable<MasterMaterial> GetAllMasters(MasterMaterialsConfig config)
    {
        // Return built-in masters first, then custom ones
        return BuiltInMasters.Concat(config.Masters.Where(m => !m.IsBuiltIn));
    }

    public MasterMaterial? GetMasterForMaterial(MasterMaterialsConfig config, int materialId)
    {
        if (config.MaterialInstanceMappings.TryGetValue(materialId, out var masterName))
        {
            return GetAllMasters(config).FirstOrDefault(m => m.Name == masterName);
        }
        return null;
    }

    public void SetMaterialMaster(MasterMaterialsConfig config, int materialId, string masterName)
    {
        config.MaterialInstanceMappings[materialId] = masterName;
    }

    public void RemoveMaterialMaster(MasterMaterialsConfig config, int materialId)
    {
        config.MaterialInstanceMappings.Remove(materialId);
    }

    public async Task<ShaderChunk?> LoadChunkFromFileAsync(string projectFolderPath, string masterName, string chunkName, CancellationToken ct = default)
    {
        var filePath = GetChunkFilePath(projectFolderPath, masterName, chunkName);

        if (!File.Exists(filePath))
        {
            Logger.Warn($"Chunk file not found: {filePath}");
            return null;
        }

        try
        {
            var content = await File.ReadAllTextAsync(filePath, ct);
            return ParseMjsChunk(content, chunkName);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Error loading chunk from {filePath}");
            return null;
        }
    }

    public async Task SaveChunkToFileAsync(string projectFolderPath, string masterName, ShaderChunk chunk, CancellationToken ct = default)
    {
        var filePath = GetChunkFilePath(projectFolderPath, masterName, chunk.Id);
        var directory = Path.GetDirectoryName(filePath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var mjsContent = GenerateMjsContent(chunk);
        await File.WriteAllTextAsync(filePath, mjsContent, ct);
        Logger.Info($"Saved chunk '{chunk.Id}' to {filePath}");
    }

    public async Task DeleteChunkFileAsync(string projectFolderPath, string masterName, string chunkName, CancellationToken ct = default)
    {
        var filePath = GetChunkFilePath(projectFolderPath, masterName, chunkName);

        if (File.Exists(filePath))
        {
            await Task.Run(() => File.Delete(filePath), ct);
            Logger.Info($"Deleted chunk file: {filePath}");
        }
    }

    /// <summary>
    /// Generates consolidated chunks.mjs for a master material (for export)
    /// </summary>
    public async Task<string> GenerateConsolidatedChunksAsync(string projectFolderPath, MasterMaterial master, CancellationToken ct = default)
    {
        var chunks = new List<ShaderChunk>();

        foreach (var (chunkName, _) in master.Chunks)
        {
            var chunk = await LoadChunkFromFileAsync(projectFolderPath, master.Name, chunkName, ct);
            if (chunk != null)
            {
                chunks.Add(chunk);
            }
        }

        return GenerateConsolidatedMjs(chunks);
    }

    /// <summary>
    /// Adds a chunk to a master material and saves the chunk file
    /// </summary>
    public async Task AddChunkToMasterAsync(string projectFolderPath, MasterMaterial master, ShaderChunk chunk, CancellationToken ct = default)
    {
        // Save the chunk file
        await SaveChunkToFileAsync(projectFolderPath, master.Name, chunk, ct);

        // Update master's chunk reference
        var serverPath = GetChunkServerPath(master.Name, chunk.Id);
        master.SetChunk(chunk.Id, serverPath);

        // Save master material
        await SaveMasterAsync(projectFolderPath, master, ct);
    }

    /// <summary>
    /// Removes a chunk from a master material and deletes the chunk file
    /// </summary>
    public async Task RemoveChunkFromMasterAsync(string projectFolderPath, MasterMaterial master, string chunkName, CancellationToken ct = default)
    {
        // Remove from master
        master.RemoveChunk(chunkName);

        // Delete chunk file
        await DeleteChunkFileAsync(projectFolderPath, master.Name, chunkName, ct);

        // Save master material
        await SaveMasterAsync(projectFolderPath, master, ct);
    }

    // Legacy interface implementation (keeping for compatibility)
    public string GetChunksFolderPath(string projectFolderPath)
    {
        // Legacy path - returns the materials folder
        return GetMaterialsFolderPath(projectFolderPath);
    }

    // Legacy - loads chunk by ID only (deprecated)
    public Task<ShaderChunk?> LoadChunkFromFileAsync(string projectFolderPath, string chunkId, CancellationToken ct = default)
    {
        Logger.Warn("LoadChunkFromFileAsync with single chunkId is deprecated. Use the version with masterName parameter.");
        return Task.FromResult<ShaderChunk?>(null);
    }

    // Legacy - saves chunk without master context (deprecated)
    public Task SaveChunkToFileAsync(string projectFolderPath, ShaderChunk chunk, CancellationToken ct = default)
    {
        Logger.Warn("SaveChunkToFileAsync without masterName is deprecated. Use the version with masterName parameter.");
        return Task.CompletedTask;
    }

    // Legacy - deletes chunk without master context (deprecated)
    public Task DeleteChunkFileAsync(string projectFolderPath, string chunkId, CancellationToken ct = default)
    {
        Logger.Warn("DeleteChunkFileAsync without masterName is deprecated. Use the version with masterName parameter.");
        return Task.CompletedTask;
    }

    #region Private Helpers

    private static ShaderChunk? ParseMjsChunk(string content, string expectedId)
    {
        try
        {
            // Parse MJS format using regex
            var glslMatch = GlslRegex().Match(content);
            var wgslMatch = WgslRegex().Match(content);
            var typeMatch = TypeRegex().Match(content);
            var descMatch = DescriptionRegex().Match(content);

            return new ShaderChunk
            {
                Id = expectedId,
                Glsl = glslMatch.Success ? glslMatch.Groups[1].Value.Trim() : string.Empty,
                Wgsl = wgslMatch.Success ? wgslMatch.Groups[1].Value.Trim() : string.Empty,
                Type = typeMatch.Success ? typeMatch.Groups[1].Value : "fragment",
                Description = descMatch.Success ? descMatch.Groups[1].Value : null,
                SourceFile = $"{expectedId}.mjs"
            };
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Error parsing MJS chunk: {expectedId}");
            return null;
        }
    }

    private static string GenerateMjsContent(ShaderChunk chunk)
    {
        var escapedGlsl = EscapeTemplateString(chunk.Glsl);
        var escapedWgsl = EscapeTemplateString(chunk.Wgsl);

        return $@"// Shader chunk: {chunk.Id}
// Type: {chunk.Type}
{(chunk.Description != null ? $"// Description: {chunk.Description}\n" : "")}
export const glsl = `{escapedGlsl}`;

export const wgsl = `{escapedWgsl}`;
";
    }

    private static string GenerateConsolidatedMjs(List<ShaderChunk> chunks)
    {
        if (chunks.Count == 0)
        {
            return "export const chunks = {};\n";
        }

        var entries = chunks.Select(c =>
        {
            var escapedGlsl = EscapeTemplateString(c.Glsl);
            var escapedWgsl = EscapeTemplateString(c.Wgsl);
            return $@"  {c.Id}: {{
    glsl: `{escapedGlsl}`,
    wgsl: `{escapedWgsl}`,
    type: '{c.Type}'
  }}";
        });

        return $@"export const chunks = {{
{string.Join(",\n", entries)}
}};
";
    }

    private static string EscapeTemplateString(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        // Escape backticks and ${} in template strings
        return input
            .Replace("\\", "\\\\")
            .Replace("`", "\\`")
            .Replace("${", "\\${");
    }

    [GeneratedRegex(@"glsl\s*[=:]\s*`([^`]*)`", RegexOptions.Singleline)]
    private static partial Regex GlslRegex();

    [GeneratedRegex(@"wgsl\s*[=:]\s*`([^`]*)`", RegexOptions.Singleline)]
    private static partial Regex WgslRegex();

    [GeneratedRegex(@"type:\s*['""](\w+)['""]")]
    private static partial Regex TypeRegex();

    [GeneratedRegex(@"description:\s*['""]([^'""]*)['""]")]
    private static partial Regex DescriptionRegex();

    #endregion
}

/// <summary>
/// Separate config file for material-to-master mappings
/// Stored in {materials}/mappings.json
/// </summary>
public class MaterialMappingsConfig
{
    /// <summary>
    /// Mapping of PlayCanvas material ID to master material name
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("materialInstanceMappings")]
    public Dictionary<int, string> MaterialInstanceMappings { get; set; } = [];

    /// <summary>
    /// Default master material name applied to materials without explicit mapping
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("defaultMasterMaterial")]
    public string? DefaultMasterMaterial { get; set; }
}
