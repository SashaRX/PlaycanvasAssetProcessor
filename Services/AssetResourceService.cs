using AssetProcessor.Helpers;
using AssetProcessor.Infrastructure.Enums;
using AssetProcessor.Resources;
using AssetProcessor.Services.Models;
using Assimp;
using Newtonsoft.Json.Linq;
using NLog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AssetProcessor.Services;

public sealed class AssetResourceService : IAssetResourceService {
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();
    private static readonly HashSet<string> SupportedTextureFormats = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg" };
    private static readonly HashSet<string> ExcludedTextureFormats = new(StringComparer.OrdinalIgnoreCase) { ".hdr", ".avif" };
    private static readonly HashSet<string> SupportedModelFormats = new(StringComparer.OrdinalIgnoreCase) { ".fbx", ".obj" };

    private readonly HashSet<string> ignoredAssetTypes = new(StringComparer.OrdinalIgnoreCase) { "script", "wasm", "cubemap" };
    private readonly HashSet<string> reportedIgnoredAssetTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object ignoredAssetTypesLock = new();

    private readonly IProjectAssetService projectAssetService;
    private readonly ILogService logService;

    public AssetResourceService(IProjectAssetService projectAssetService, ILogService logService) {
        this.projectAssetService = projectAssetService ?? throw new ArgumentNullException(nameof(projectAssetService));
        this.logService = logService ?? throw new ArgumentNullException(nameof(logService));
    }

    public void BuildFolderHierarchy(JArray assetsResponse, IDictionary<int, string> targetFolderPaths) {
        ArgumentNullException.ThrowIfNull(assetsResponse);
        ArgumentNullException.ThrowIfNull(targetFolderPaths);

        targetFolderPaths.Clear();

        IEnumerable<JToken> folders = assetsResponse.Where(asset => string.Equals(asset["type"]?.ToString(), "folder", StringComparison.OrdinalIgnoreCase));
        Dictionary<int, JToken> foldersById = new();
        foreach (JToken folder in folders) {
            int? folderId = folder["id"]?.Type == JTokenType.Integer ? (int?)folder["id"] : null;
            if (folderId.HasValue) {
                foldersById[folderId.Value] = folder;
            }
        }

        string BuildFolderPath(int folderId) {
            if (targetFolderPaths.TryGetValue(folderId, out string? existing)) {
                return existing;
            }

            if (!foldersById.TryGetValue(folderId, out JToken? folder)) {
                return string.Empty;
            }

            string folderName = PathSanitizer.SanitizePath(folder["name"]?.ToString());
            int? parentId = folder["parent"]?.Type == JTokenType.Integer ? (int?)folder["parent"] : null;

            string fullPath = folderName;
            if (parentId.HasValue && parentId.Value != 0) {
                string parentPath = BuildFolderPath(parentId.Value);
                fullPath = string.IsNullOrEmpty(parentPath) ? folderName : Path.Combine(parentPath, folderName);
            }

            fullPath = PathSanitizer.SanitizePath(fullPath);
            targetFolderPaths[folderId] = fullPath;
            return fullPath;
        }

        foreach (int folderId in foldersById.Keys) {
            BuildFolderPath(folderId);
        }

        logService.LogInfo($"Built folder hierarchy with {targetFolderPaths.Count} folders from assets list");
    }

    public async Task<AssetProcessingResult?> ProcessAssetAsync(JToken asset, AssetProcessingParameters parameters, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(parameters);
        cancellationToken.ThrowIfCancellationRequested();

        string? type = asset["type"]?.ToString() ?? string.Empty;
        string? assetPath = asset["path"]?.ToString() ?? string.Empty;
        logService.LogInfo($"Processing {type}, API path: {assetPath}");

        if (!string.IsNullOrEmpty(type) && ignoredAssetTypes.Contains(type)) {
            lock (ignoredAssetTypesLock) {
                if (reportedIgnoredAssetTypes.Add(type)) {
                    logService.LogInfo($"Asset type '{type}' is currently ignored (stub handler).");
                }
            }
            return null;
        }

        if (string.Equals(type, "material", StringComparison.OrdinalIgnoreCase)) {
            MaterialResource? material = await ProcessMaterialAssetAsync(asset, parameters, cancellationToken).ConfigureAwait(false);
            return material == null ? null : new AssetProcessingResult(AssetProcessingResultType.Material, material);
        }

        JToken? file = asset["file"];
        if (file == null || file.Type != JTokenType.Object) {
            logService.LogError("Invalid asset file format");
            return null;
        }

        string? fileUrl = MainWindowHelpers.GetFileUrl(file);
        if (string.IsNullOrEmpty(fileUrl)) {
            logService.LogError("File URL is null or empty");
            return null;
        }

        string? extension = MainWindowHelpers.GetFileExtension(fileUrl);
        if (string.IsNullOrEmpty(extension)) {
            logService.LogError("Unable to determine file extension");
            return null;
        }

        if (string.Equals(type, "texture", StringComparison.OrdinalIgnoreCase) && IsSupportedTextureFormat(extension)) {
            TextureResource? texture = await ProcessTextureAssetAsync(asset, parameters, fileUrl, extension, cancellationToken).ConfigureAwait(false);
            return texture == null ? null : new AssetProcessingResult(AssetProcessingResultType.Texture, texture);
        }

        if (string.Equals(type, "scene", StringComparison.OrdinalIgnoreCase) && IsSupportedModelFormat(extension)) {
            ModelResource? model = await ProcessModelAssetAsync(asset, parameters, fileUrl, extension, cancellationToken).ConfigureAwait(false);
            return model == null ? null : new AssetProcessingResult(AssetProcessingResultType.Model, model);
        }

        logService.LogError($"Unsupported asset type or format: {type} - {extension}");
        return null;
    }

    public async Task<MaterialResource?> LoadMaterialFromFileAsync(string filePath, CancellationToken cancellationToken) {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(filePath)) {
            logService.LogWarn($"Material file not found: {filePath}");
            return null;
        }

        return await ParseMaterialJsonAsync(filePath, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAssetsListAsync(JToken jsonResponse, string projectFolderPath, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(jsonResponse);
        ArgumentException.ThrowIfNullOrEmpty(projectFolderPath);
        cancellationToken.ThrowIfCancellationRequested();

        try {
            string jsonFilePath = Path.Combine(projectFolderPath, "assets_list.json");
            Directory.CreateDirectory(projectFolderPath);

            string jsonString = jsonResponse.ToString(Newtonsoft.Json.Formatting.Indented);
            await File.WriteAllTextAsync(jsonFilePath, jsonString, cancellationToken).ConfigureAwait(false);
            logService.LogInfo($"Assets list saved to {jsonFilePath}");
        } catch (ArgumentNullException ex) {
            logService.LogError($"Argument error: {ex.Message}");
        } catch (ArgumentException ex) {
            logService.LogError($"Argument error: {ex.Message}");
        } catch (Exception ex) {
            logService.LogError($"Error saving assets list to JSON: {ex.Message}");
        }
    }

    private async Task<TextureResource?> ProcessTextureAssetAsync(
        JToken asset,
        AssetProcessingParameters parameters,
        string fileUrl,
        string extension,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        string rawFileName = asset["name"]?.ToString() ?? "Unknown";
        string cleanFileName = PathSanitizer.SanitizePath(rawFileName);
        string textureName = cleanFileName.Split('.')[0];
        int? parentId = asset["parent"]?.Type == JTokenType.Integer ? (int?)asset["parent"] : null;

        string texturePath = projectAssetService.GetResourcePath(
            parameters.ProjectsRoot,
            parameters.ProjectName,
            parameters.FolderPaths,
            cleanFileName,
            parentId);

        texturePath = PathSanitizer.SanitizePath(texturePath);

        int[] resolution = new int[2];
        JToken? variants = asset["file"]?["variants"];
        if (variants != null && variants.Type == JTokenType.Object) {
            foreach (string variantName in new[] { "webp", "jpg", "png", "original" }) {
                JToken? variant = variants[variantName];
                if (variant != null && variant.Type == JTokenType.Object) {
                    int? width = variant["width"]?.Type == JTokenType.Integer ? (int?)variant["width"] : null;
                    int? height = variant["height"]?.Type == JTokenType.Integer ? (int?)variant["height"] : null;
                    if (width.HasValue && height.HasValue) {
                        resolution[0] = width.Value;
                        resolution[1] = height.Value;
                        logger.Info($"Resolution from JSON variant '{variantName}': {width}x{height} for {textureName}");
                        break;
                    }
                }
            }
        }
        if (resolution[0] == 0 && resolution[1] == 0) {
            logger.Warn($"No resolution in JSON variants for {textureName}");
        }

        TextureResource texture = new() {
            ID = asset["id"]?.Type == JTokenType.Integer ? (int)(asset["id"] ?? 0) : 0,
            Index = parameters.AssetIndex,
            Name = textureName,
            Size = int.TryParse(asset["file"]?["size"]?.ToString(), out int size) ? size : 0,
            Url = fileUrl.Split('?')[0],
            Path = texturePath,
            Extension = extension,
            Resolution = resolution,
            ResizeResolution = new int[2],
            Status = "On Server",
            Hash = asset["file"]?["hash"]?.ToString() ?? string.Empty,
            Parent = parentId,
            Type = asset["type"]?.ToString(),
            GroupName = TextureResource.ExtractBaseTextureName(textureName),
            TextureType = TextureResource.DetermineTextureType(textureName),
            ProjectId = parameters.ProjectId
        };

        await MainWindowHelpers.VerifyAndProcessResourceAsync(texture, async () => {
            switch (texture.Status) {
                case "Downloaded":
                    // Try to read resolution from local file
                    (int width, int height)? localResolution = MainWindowHelpers.GetLocalImageResolution(texture.Path, logService);
                    if (localResolution.HasValue) {
                        texture.Resolution = new[] { localResolution.Value.width, localResolution.Value.height };
                        logger.Info($"Resolution from local file: {localResolution.Value.width}x{localResolution.Value.height} for {texture.Name}");
                    } else {
                        logger.Warn($"Failed to read local file resolution for {texture.Name} at {texture.Path}, keeping JSON resolution: {texture.Resolution[0]}x{texture.Resolution[1]}");
                    }
                    break;
                case "On Server":
                    if (texture.Resolution[0] == 0 || texture.Resolution[1] == 0) {
                        await MainWindowHelpers.UpdateTextureResolutionAsync(texture, logService, cancellationToken).ConfigureAwait(false);
                    }
                    break;
            }
        }, logService).ConfigureAwait(false);

        return texture;
    }

    private async Task<ModelResource?> ProcessModelAssetAsync(
        JToken asset,
        AssetProcessingParameters parameters,
        string fileUrl,
        string extension,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        int? parentId = asset["parent"]?.Type == JTokenType.Integer ? (int?)asset["parent"] : null;
        string? rawName = asset["name"]?.ToString();

        string modelPath = projectAssetService.GetResourcePath(
            parameters.ProjectsRoot,
            parameters.ProjectName,
            parameters.FolderPaths,
            rawName,
            parentId);

        ModelResource model = new() {
            ID = asset["id"]?.Type == JTokenType.Integer ? (int)(asset["id"] ?? 0) : 0,
            Index = parameters.AssetIndex,
            Name = rawName?.Split('.')[0] ?? "Unknown",
            Size = int.TryParse(asset["file"]?["size"]?.ToString(), out int size) ? size : 0,
            Url = fileUrl.Split('?')[0],
            Path = modelPath,
            Extension = extension,
            Status = "On Server",
            Hash = asset["file"]?["hash"]?.ToString() ?? string.Empty,
            Parent = parentId,
            UVChannels = 0,
            ProjectId = parameters.ProjectId
        };

        await MainWindowHelpers.VerifyAndProcessResourceAsync(model, async () => {
            if (string.Equals(model.Status, "Downloaded", StringComparison.OrdinalIgnoreCase) && File.Exists(model.Path)) {
                AssimpContext context = new();
                Scene scene = context.ImportFile(model.Path, PostProcessSteps.Triangulate | PostProcessSteps.FlipUVs | PostProcessSteps.GenerateSmoothNormals);
                if (scene?.Meshes != null && scene.MeshCount > 0) {
                    Mesh? mesh = scene.Meshes.FirstOrDefault();
                    if (mesh != null) {
                        model.UVChannels = mesh.TextureCoordinateChannelCount;
                    }
                }
            }
            await Task.CompletedTask.ConfigureAwait(false);
        }, logService).ConfigureAwait(false);

        return model;
    }

    private async Task<MaterialResource?> ProcessMaterialAssetAsync(
        JToken asset,
        AssetProcessingParameters parameters,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        string name = asset["name"]?.ToString() ?? "Unknown";
        int? parentId = asset["parent"]?.Type == JTokenType.Integer ? (int?)asset["parent"] : null;

        string materialPath = projectAssetService.GetResourcePath(
            parameters.ProjectsRoot,
            parameters.ProjectName,
            parameters.FolderPaths,
            $"{name}.json",
            parentId);

        MaterialResource material = new() {
            ID = asset["id"]?.Type == JTokenType.Integer ? (int)(asset["id"] ?? 0) : 0,
            Index = parameters.AssetIndex,
            Name = name,
            Size = 0,
            Path = materialPath,
            Status = "On Server",
            Hash = string.Empty,
            Parent = parentId,
            ProjectId = parameters.ProjectId
        };

        await MainWindowHelpers.VerifyAndProcessResourceAsync(material, async () => {
            string? pathToUse = null;

            // Check expected path first
            if (material.Status == "Downloaded" && !string.IsNullOrEmpty(material.Path) && File.Exists(material.Path)) {
                pathToUse = material.Path;
            }
            // Fallback: check assets root folder (materials sometimes get saved there incorrectly)
            else if (!string.IsNullOrEmpty(material.Path)) {
                string assetsRoot = Path.Combine(parameters.ProjectsRoot, parameters.ProjectName, "assets");
                string fileName = Path.GetFileName(material.Path);
                string fallbackPath = Path.Combine(assetsRoot, fileName);
                if (File.Exists(fallbackPath)) {
                    pathToUse = fallbackPath;
                    material.Path = fallbackPath; // Update path to correct location
                    material.Status = "Downloaded";
                    logService.LogInfo($"Material '{name}' found at fallback path: {fallbackPath}");
                }
            }

            if (!string.IsNullOrEmpty(pathToUse)) {
                MaterialResource? detailedMaterial = await ParseMaterialJsonAsync(pathToUse, cancellationToken).ConfigureAwait(false);
                if (detailedMaterial != null) {
                    material.AOMapId = detailedMaterial.AOMapId;
                    material.GlossMapId = detailedMaterial.GlossMapId;
                    material.MetalnessMapId = detailedMaterial.MetalnessMapId;
                    material.SpecularMapId = detailedMaterial.SpecularMapId;
                    material.DiffuseMapId = detailedMaterial.DiffuseMapId;
                    material.NormalMapId = detailedMaterial.NormalMapId;
                    material.EmissiveMapId = detailedMaterial.EmissiveMapId;
                    material.OpacityMapId = detailedMaterial.OpacityMapId;
                    material.UseMetalness = detailedMaterial.UseMetalness;
                }
            }
        }, logService).ConfigureAwait(false);

        return material;
    }

    private static bool IsSupportedTextureFormat(string extension) {
        if (string.IsNullOrEmpty(extension)) {
            return false;
        }

        return SupportedTextureFormats.Contains(extension) && !ExcludedTextureFormats.Contains(extension);
    }

    private static bool IsSupportedModelFormat(string extension) {
        if (string.IsNullOrEmpty(extension)) {
            return false;
        }

        return SupportedModelFormats.Contains(extension);
    }

    private async Task<MaterialResource?> ParseMaterialJsonAsync(string filePath, CancellationToken cancellationToken) {
        try {
            cancellationToken.ThrowIfCancellationRequested();
            string jsonContent = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            JObject json = JObject.Parse(jsonContent);

            JToken? data = json["data"];
            if (data == null) {
                logService.LogWarn($"Material JSON missing 'data' section: {filePath}");
                return null;
            }

            string materialName = json["name"]?.ToString() ?? "Unknown";
            logService.LogDebug($"Parsing material '{materialName}' from JSON");

            MaterialResource materialResource = new() {
                ID = json["id"]?.ToObject<int>() ?? 0,
                Name = materialName,
                CreatedAt = json["createdAt"]?.ToString() ?? string.Empty,
                Shader = data["shader"]?.ToString() ?? string.Empty,
                BlendType = data["blendType"]?.ToString() ?? string.Empty,
                Cull = data["cull"]?.ToString() ?? string.Empty,
                UseLighting = data["useLighting"]?.ToObject<bool>() ?? false,
                TwoSidedLighting = data["twoSidedLighting"]?.ToObject<bool>() ?? false,
                DiffuseTint = data["diffuseTint"]?.ToObject<bool>() ?? false,
                Diffuse = data["diffuse"]?.Select(d => d.ToObject<float>()).ToList(),
                SpecularTint = data["specularTint"]?.ToObject<bool>() ?? false,
                Specular = data["specular"]?.Select(d => d.ToObject<float>()).ToList(),
                AOTint = data["aoTint"]?.ToObject<bool>() ?? false,
                AOColor = data["ao"]?.Select(d => d.ToObject<float>()).ToList(),
                UseMetalness = data["useMetalness"]?.ToObject<bool>() ?? false,
                MetalnessMapId = ParseTextureAssetId(data["metalnessMap"], "metalnessMap"),
                Metalness = data["metalness"]?.ToObject<float?>(),
                GlossMapId = ParseTextureAssetId(data["glossMap"], "glossMap"),
                Shininess = data["shininess"]?.ToObject<float?>(),
                OpacityMapId = ParseTextureAssetId(data["opacityMap"], "opacityMap"),
                Opacity = data["opacity"]?.ToObject<float?>(),
                AlphaTest = data["alphaTest"]?.ToObject<float?>(),
                NormalMapId = ParseTextureAssetId(data["normalMap"], "normalMap"),
                BumpMapFactor = data["bumpMapFactor"]?.ToObject<float?>(),
                DiffuseMapId = ParseTextureAssetId(data["diffuseMap"], "diffuseMap"),
                SpecularMapId = ParseTextureAssetId(data["specularMap"], "specularMap"),
                SpecularityFactor = data["specularityFactor"]?.ToObject<float?>(),
                EmissiveMapId = ParseTextureAssetId(data["emissiveMap"], "emissiveMap"),
                Emissive = data["emissive"]?.Select(d => d.ToObject<float>()).ToList(),
                AOMapId = ParseTextureAssetId(data["aoMap"], "aoMap"),
                EmissiveIntensity = data["emissiveIntensity"]?.ToObject<float?>(),
                FresnelModel = data["fresnelModel"]?.ToString(),
                Glossiness = data["gloss"]?.ToObject<float?>(),
                Reflectivity = data["reflectivity"]?.ToObject<float?>(),
                RefractionIndex = data["refraction"]?.ToObject<float?>(),
                OpacityFresnel = data["opacityFresnel"]?.ToObject<bool?>(),
                CavityMapIntensity = data["cavityMapIntensity"]?.ToObject<float?>(),
                UseSkybox = data["useSkybox"]?.ToObject<bool?>(),
                UseFog = data["useFog"]?.ToObject<bool?>(),
                UseGammaTonemap = data["useGammaTonemap"]?.ToObject<bool?>(),
                DiffuseColorChannel = ParseColorChannel(data["diffuseMapChannel"]?.ToString()),
                SpecularColorChannel = ParseColorChannel(data["specularMapChannel"]?.ToString()),
                MetalnessColorChannel = ParseColorChannel(data["metalnessMapChannel"]?.ToString()),
                GlossinessColorChannel = ParseColorChannel(data["glossMapChannel"]?.ToString()),
                AOChannel = ParseColorChannel(data["aoMapChannel"]?.ToString())
            };

            return materialResource;
        } catch (Exception ex) {
            logService.LogWarn($"Failed to parse material JSON '{filePath}': {ex.Message}");
            return null;
        }
    }

    private int? ParseTextureAssetId(JToken? token, string propertyName) {
        if (token == null || token.Type == JTokenType.Null) {
            logService.LogDebug($"Property {propertyName} missing or null while parsing material");
            return null;
        }

        int? ExtractAssetId(JToken? candidate) {
            if (candidate == null || candidate.Type == JTokenType.Null) {
                return null;
            }

            return candidate.Type switch {
                JTokenType.Integer => candidate.ToObject<int?>(),
                JTokenType.Float => candidate.ToObject<double?>() is double value
                    ? (int?)Convert.ToInt32(Math.Round(value, MidpointRounding.AwayFromZero))
                    : null,
                JTokenType.String => int.TryParse(candidate.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                    ? parsed
                    : null,
                JTokenType.Object => ExtractAssetId(candidate["asset"] ?? candidate["id"] ?? candidate["value"] ?? candidate["data"] ?? candidate["guid"] ?? candidate.FirstOrDefault()),
                _ => null,
            };
        }

        int? parsedId = ExtractAssetId(token);
        if (parsedId.HasValue) {
            logService.LogDebug($"Property {propertyName} resolved to texture ID {parsedId.Value}");
            return parsedId;
        }

        logService.LogWarn($"Failed to extract texture ID from property {propertyName} (token type {token.Type})");
        return null;
    }

    private static ColorChannel? ParseColorChannel(string? channel) {
        if (string.IsNullOrEmpty(channel)) {
            return null;
        }

        return channel.ToLowerInvariant() switch {
            "r" => ColorChannel.R,
            "g" => ColorChannel.G,
            "b" => ColorChannel.B,
            "a" => ColorChannel.A,
            "rgb" => ColorChannel.RGB,
            _ => null
        };
    }
}
