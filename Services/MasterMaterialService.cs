using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using AssetProcessor.MasterMaterials.Models;
using NLog;

namespace AssetProcessor.Services;

/// <summary>
/// Service for managing Master Materials and Shader Chunks
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
    /// Built-in master materials (always available)
    /// </summary>
    private static readonly List<MasterMaterial> BuiltInMasters =
    [
        new() { Name = "pbr_opaque", Description = "Standard PBR opaque material", BlendType = "opaque", IsBuiltIn = true },
        new() { Name = "pbr_alpha", Description = "Standard PBR with alpha blending", BlendType = "alpha", IsBuiltIn = true },
        new() { Name = "pbr_additive", Description = "Additive blend material", BlendType = "additive", IsBuiltIn = true },
        new() { Name = "pbr_premul", Description = "Premultiplied alpha material", BlendType = "premul", IsBuiltIn = true }
    ];

    public async Task<MasterMaterialsConfig> LoadConfigAsync(string projectFolderPath, CancellationToken ct = default)
    {
        var configPath = GetConfigPath(projectFolderPath);

        if (!File.Exists(configPath))
        {
            Logger.Info($"Creating default MasterMaterials config at {configPath}");
            var defaultConfig = new MasterMaterialsConfig();
            await SaveConfigAsync(projectFolderPath, defaultConfig, ct);
            return defaultConfig;
        }

        try
        {
            var json = await File.ReadAllTextAsync(configPath, ct);
            var config = JsonSerializer.Deserialize<MasterMaterialsConfig>(json, JsonOptions);

            if (config == null)
            {
                Logger.Warn("Failed to deserialize MasterMaterials config, using default");
                return new MasterMaterialsConfig();
            }

            Logger.Info($"Loaded MasterMaterials config from {configPath}: {config.Masters.Count} masters, {config.Chunks.Count} chunks");
            return config;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Error loading MasterMaterials config from {configPath}");
            return new MasterMaterialsConfig();
        }
    }

    public async Task SaveConfigAsync(string projectFolderPath, MasterMaterialsConfig config, CancellationToken ct = default)
    {
        var configPath = GetConfigPath(projectFolderPath);
        var directory = Path.GetDirectoryName(configPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(config, JsonOptions);
        await File.WriteAllTextAsync(configPath, json, ct);
        Logger.Info($"Saved MasterMaterials config to {configPath}");
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

    public async Task<ShaderChunk?> LoadChunkFromFileAsync(string projectFolderPath, string chunkId, CancellationToken ct = default)
    {
        var filePath = GetChunkFilePath(projectFolderPath, chunkId);

        if (!File.Exists(filePath))
        {
            Logger.Warn($"Chunk file not found: {filePath}");
            return null;
        }

        try
        {
            var content = await File.ReadAllTextAsync(filePath, ct);
            return ParseMjsChunk(content, chunkId);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Error loading chunk from {filePath}");
            return null;
        }
    }

    public async Task SaveChunkToFileAsync(string projectFolderPath, ShaderChunk chunk, CancellationToken ct = default)
    {
        var filePath = GetChunkFilePath(projectFolderPath, chunk.Id);
        var directory = Path.GetDirectoryName(filePath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var mjsContent = GenerateMjsContent(chunk);
        await File.WriteAllTextAsync(filePath, mjsContent, ct);
        Logger.Info($"Saved chunk '{chunk.Id}' to {filePath}");
    }

    public async Task DeleteChunkFileAsync(string projectFolderPath, string chunkId, CancellationToken ct = default)
    {
        var filePath = GetChunkFilePath(projectFolderPath, chunkId);

        if (File.Exists(filePath))
        {
            await Task.Run(() => File.Delete(filePath), ct);
            Logger.Info($"Deleted chunk file: {filePath}");
        }
    }

    public async Task<string> GenerateConsolidatedChunksAsync(string projectFolderPath, MasterMaterial master, CancellationToken ct = default)
    {
        var chunks = new List<ShaderChunk>();

        foreach (var chunkId in master.ChunkIds)
        {
            var chunk = await LoadChunkFromFileAsync(projectFolderPath, chunkId, ct);
            if (chunk != null)
            {
                chunks.Add(chunk);
            }
        }

        return GenerateConsolidatedMjs(chunks);
    }

    public string GetChunksFolderPath(string projectFolderPath)
    {
        return Path.Combine(projectFolderPath, "MasterMaterials", "chunks");
    }

    #region Private Helpers

    private static string GetConfigPath(string projectFolderPath)
        => Path.Combine(projectFolderPath, "MasterMaterials", "config.json");

    private static string GetChunkFilePath(string projectFolderPath, string chunkId)
        => Path.Combine(projectFolderPath, "MasterMaterials", "chunks", $"{chunkId}.mjs");

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
        var escapedDesc = chunk.Description?.Replace("'", "\\'") ?? string.Empty;

        return $@"export const chunks = {{
  {chunk.Id}: {{
    glsl: `{escapedGlsl}`,
    wgsl: `{escapedWgsl}`,
    type: '{chunk.Type}',
    description: '{escapedDesc}'
  }}
}};
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

    [GeneratedRegex(@"glsl:\s*`([^`]*)`", RegexOptions.Singleline)]
    private static partial Regex GlslRegex();

    [GeneratedRegex(@"wgsl:\s*`([^`]*)`", RegexOptions.Singleline)]
    private static partial Regex WgslRegex();

    [GeneratedRegex(@"type:\s*['""](\w+)['""]")]
    private static partial Regex TypeRegex();

    [GeneratedRegex(@"description:\s*['""]([^'""]*)['""]")]
    private static partial Regex DescriptionRegex();

    #endregion
}
