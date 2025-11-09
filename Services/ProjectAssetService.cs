using AssetProcessor.Helpers;
using AssetProcessor.Resources;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AssetProcessor.Services;

public class ProjectAssetService {
    private readonly IPlayCanvasService playCanvasService;
    private readonly LocalCacheService localCacheService;
    private readonly AssetQueueService assetQueueService;
    private readonly HashSet<string> ignoredAssetTypes = new(StringComparer.OrdinalIgnoreCase) { "script", "wasm", "cubemap" };
    private readonly HashSet<string> reportedIgnoredAssetTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object ignoredAssetTypesLock = new();

    public ProjectAssetService(IPlayCanvasService playCanvasService, LocalCacheService localCacheService, AssetQueueService assetQueueService) {
        this.playCanvasService = playCanvasService ?? throw new ArgumentNullException(nameof(playCanvasService));
        this.localCacheService = localCacheService ?? throw new ArgumentNullException(nameof(localCacheService));
        this.assetQueueService = assetQueueService ?? throw new ArgumentNullException(nameof(assetQueueService));
    }

    public Dictionary<int, string> BuildFolderHierarchy(JArray assetsResponse) {
        ArgumentNullException.ThrowIfNull(assetsResponse);

        Dictionary<int, string> folderPaths = new();
        List<JToken> folders = assetsResponse.Where(asset => asset["type"]?.ToString() == "folder").ToList();
        Dictionary<int, JToken> foldersById = new();

        foreach (JToken folder in folders) {
            int? folderId = folder["id"]?.Type == JTokenType.Integer ? (int?)folder["id"] : null;
            if (folderId.HasValue) {
                foldersById[folderId.Value] = folder;
            }
        }

        string BuildFolderPath(int folderId) {
            if (folderPaths.ContainsKey(folderId)) {
                return folderPaths[folderId];
            }

            if (!foldersById.TryGetValue(folderId, out JToken? folderToken)) {
                return string.Empty;
            }

            string folderName = localCacheService.SanitizePath(folderToken["name"]?.ToString());
            int? parentId = folderToken["parent"]?.Type == JTokenType.Integer ? (int?)folderToken["parent"] : null;

            string fullPath;
            if (parentId.HasValue && parentId.Value != 0) {
                string parentPath = BuildFolderPath(parentId.Value);
                fullPath = string.IsNullOrEmpty(parentPath) ? folderName : Path.Combine(parentPath, folderName);
            } else {
                fullPath = folderName;
            }

            fullPath = localCacheService.SanitizePath(fullPath);
            folderPaths[folderId] = fullPath;
            return fullPath;
        }

        foreach (int folderId in foldersById.Keys) {
            BuildFolderPath(folderId);
        }

        MainWindowHelpers.LogInfo($"Built folder hierarchy with {folderPaths.Count} folders from assets list");
        return folderPaths;
    }

    public async Task<ProjectAssetsResult> ProcessAssetsAsync(
        JArray assetsResponse,
        string projectsRoot,
        string projectName,
        IReadOnlyDictionary<int, string> folderPaths,
        string apiKey,
        CancellationToken cancellationToken,
        IProgress<int>? progress = null,
        bool fetchTextureResolution = true) {

        ArgumentNullException.ThrowIfNull(assetsResponse);
        ArgumentException.ThrowIfNullOrEmpty(projectsRoot);
        ArgumentException.ThrowIfNullOrEmpty(projectName);
        ArgumentException.ThrowIfNullOrEmpty(apiKey);

        List<JToken> supportedAssets = [.. assetsResponse.Where(asset => asset["file"] != null || string.Equals(asset["type"]?.ToString(), "material", StringComparison.OrdinalIgnoreCase))];
        ProjectAssetsResult result = new(folderPaths);

        ConcurrentBag<TextureResource> textureResults = new();
        ConcurrentBag<ModelResource> modelResults = new();
        ConcurrentBag<MaterialResource> materialResults = new();

        List<Task> tasks = new();
        foreach (JToken asset in supportedAssets) {
            tasks.Add(assetQueueService.RunAssetQueueAsync(async token => {
                await ProcessAssetAsync(asset, projectsRoot, projectName, folderPaths, apiKey, textureResults, modelResults, materialResults, token, fetchTextureResolution).ConfigureAwait(false);
                progress?.Report(1);
            }, cancellationToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);

        result.Textures.AddRange(textureResults.OrderBy(t => t.Name));
        result.Models.AddRange(modelResults.OrderBy(m => m.Name));
        result.Materials.AddRange(materialResults.OrderBy(m => m.Name));
        return result;
    }

    private async Task ProcessAssetAsync(
        JToken asset,
        string projectsRoot,
        string projectName,
        IReadOnlyDictionary<int, string> folderPaths,
        string apiKey,
        ConcurrentBag<TextureResource> textureResults,
        ConcurrentBag<ModelResource> modelResults,
        ConcurrentBag<MaterialResource> materialResults,
        CancellationToken cancellationToken,
        bool fetchTextureResolution) {

        try {
            string? type = asset["type"]?.ToString() ?? string.Empty;
            string? assetPath = asset["path"]?.ToString() ?? string.Empty;
            MainWindowHelpers.LogInfo($"Processing {type}, API path: {assetPath}");

            if (!string.IsNullOrEmpty(type) && ignoredAssetTypes.Contains(type)) {
                lock (ignoredAssetTypesLock) {
                    if (reportedIgnoredAssetTypes.Add(type)) {
                        MainWindowHelpers.LogInfo($"Asset type '{type}' is currently ignored (stub handler).");
                    }
                }
                return;
            }

            if (string.Equals(type, "material", StringComparison.OrdinalIgnoreCase)) {
                MaterialResource? material = await ProcessMaterialAssetAsync(asset, projectsRoot, projectName, folderPaths, apiKey, cancellationToken).ConfigureAwait(false);
                if (material != null) {
                    materialResults.Add(material);
                }
                return;
            }

            JToken? file = asset["file"];
            if (file == null || file.Type != JTokenType.Object) {
                MainWindowHelpers.LogError("Invalid asset file format");
                return;
            }

            string? fileUrl = MainWindowHelpers.GetFileUrl(file);
            if (string.IsNullOrEmpty(fileUrl)) {
                throw new InvalidOperationException("File URL is null or empty");
            }

            string? extension = MainWindowHelpers.GetFileExtension(fileUrl);
            if (string.IsNullOrEmpty(extension)) {
                throw new InvalidOperationException("Unable to determine file extension");
            }

            switch (type) {
                case "texture" when IsSupportedTextureFormat(extension):
                    TextureResource? texture = await ProcessTextureAssetAsync(asset, fileUrl, extension, projectsRoot, projectName, folderPaths, cancellationToken, fetchTextureResolution).ConfigureAwait(false);
                    if (texture != null) {
                        textureResults.Add(texture);
                    }
                    break;
                case "scene" when IsSupportedModelFormat(extension):
                    ModelResource? model = await ProcessModelAssetAsync(asset, fileUrl, extension, projectsRoot, projectName, folderPaths, cancellationToken).ConfigureAwait(false);
                    if (model != null) {
                        modelResults.Add(model);
                    }
                    break;
                default:
                    MainWindowHelpers.LogError($"Unsupported asset type or format: {type} - {extension}");
                    break;
            }
        } catch (Exception ex) {
            MainWindowHelpers.LogError($"Error in ProcessAssetAsync: {ex}");
        }
    }

    private async Task<ModelResource?> ProcessModelAssetAsync(
        JToken asset,
        string fileUrl,
        string extension,
        string projectsRoot,
        string projectName,
        IReadOnlyDictionary<int, string> folderPaths,
        CancellationToken cancellationToken) {

        int? parentId = asset["parent"]?.Type == JTokenType.Integer ? (int?)asset["parent"] : null;
        string resourcePath = localCacheService.GetResourcePath(projectsRoot, projectName, folderPaths, asset["name"]?.ToString(), parentId);

        ModelResource model = new() {
            ID = asset["id"]?.Type == JTokenType.Integer ? (int)(asset["id"] ?? 0) : 0,
            Name = (asset["name"]?.ToString() ?? "Unknown").Split('.')[0],
            Size = int.TryParse(asset["file"]?["size"]?.ToString(), out int size) ? size : 0,
            Url = fileUrl.Split('?')[0],
            Path = resourcePath,
            Extension = extension,
            Status = "On Server",
            Hash = asset["file"]?["hash"]?.ToString() ?? string.Empty,
            Parent = parentId,
            UVChannels = 0
        };

        await MainWindowHelpers.VerifyAndProcessResourceAsync(model, async () => {
            if (string.Equals(model.Status, "Downloaded", StringComparison.OrdinalIgnoreCase) && File.Exists(model.Path)) {
                using Assimp.AssimpContext context = new();
                Assimp.Scene scene = context.ImportFile(model.Path, Assimp.PostProcessSteps.Triangulate | Assimp.PostProcessSteps.FlipUVs | Assimp.PostProcessSteps.GenerateSmoothNormals);
                Assimp.Mesh? mesh = scene.Meshes?.FirstOrDefault();
                if (mesh != null) {
                    model.UVChannels = mesh.TextureCoordinateChannelCount;
                }
            }

            await Task.CompletedTask;
        }).ConfigureAwait(false);

        return model;
    }

    private async Task<TextureResource?> ProcessTextureAssetAsync(
        JToken asset,
        string fileUrl,
        string extension,
        string projectsRoot,
        string projectName,
        IReadOnlyDictionary<int, string> folderPaths,
        CancellationToken cancellationToken,
        bool fetchTextureResolution) {

        string rawFileName = asset["name"]?.ToString() ?? "Unknown";
        string cleanFileName = localCacheService.SanitizePath(rawFileName);
        string textureName = cleanFileName.Split('.')[0];
        int? parentId = asset["parent"]?.Type == JTokenType.Integer ? (int?)asset["parent"] : null;
        string texturePath = localCacheService.SanitizePath(localCacheService.GetResourcePath(projectsRoot, projectName, folderPaths, cleanFileName, parentId));

        TextureResource texture = new() {
            ID = asset["id"]?.Type == JTokenType.Integer ? (int)(asset["id"] ?? 0) : 0,
            Name = textureName,
            Size = int.TryParse(asset["file"]?["size"]?.ToString(), out int size) ? size : 0,
            Url = fileUrl.Split('?')[0],
            Path = texturePath,
            Extension = extension,
            Resolution = new int[2],
            ResizeResolution = new int[2],
            Status = "On Server",
            Hash = asset["file"]?["hash"]?.ToString() ?? string.Empty,
            Parent = parentId,
            Type = asset["type"]?.ToString(),
            GroupName = TextureResource.ExtractBaseTextureName(textureName),
            TextureType = TextureResource.DetermineTextureType(textureName)
        };

        await MainWindowHelpers.VerifyAndProcessResourceAsync(texture, async () => {
            switch (texture.Status) {
                case "Downloaded":
                    (int width, int height)? resolution = MainWindowHelpers.GetLocalImageResolution(texture.Path);
                    if (resolution.HasValue) {
                        texture.Resolution[0] = resolution.Value.width;
                        texture.Resolution[1] = resolution.Value.height;
                    }
                    break;
                case "On Server" when fetchTextureResolution:
                    await MainWindowHelpers.UpdateTextureResolutionAsync(texture, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }).ConfigureAwait(false);

        return texture;
    }

    private async Task<MaterialResource?> ProcessMaterialAssetAsync(
        JToken asset,
        string projectsRoot,
        string projectName,
        IReadOnlyDictionary<int, string> folderPaths,
        string apiKey,
        CancellationToken cancellationToken) {

        string name = asset["name"]?.ToString() ?? "Unknown";
        int? parentId = asset["parent"]?.Type == JTokenType.Integer ? (int?)asset["parent"] : null;
        string materialPath = localCacheService.GetResourcePath(projectsRoot, projectName, folderPaths, $"{name}.json", parentId);

        MaterialResource material = new() {
            ID = asset["id"]?.Type == JTokenType.Integer ? (int)(asset["id"] ?? 0) : 0,
            Name = name,
            Size = 0,
            Path = materialPath,
            Status = "On Server",
            Hash = string.Empty,
            Parent = parentId
        };

        await MainWindowHelpers.VerifyAndProcessResourceAsync(material, async () => {
            await playCanvasService.GetAssetByIdAsync(material.ID.ToString(), apiKey, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

        return material;
    }

    public async Task<ProjectAssetsResult?> LoadAssetsFromJsonAsync(
        string projectFolderPath,
        string projectsRoot,
        string projectName,
        string apiKey,
        CancellationToken cancellationToken,
        IProgress<int>? progress = null,
        bool fetchTextureResolution = true) {

        ArgumentException.ThrowIfNullOrEmpty(projectFolderPath);
        ArgumentException.ThrowIfNullOrEmpty(projectsRoot);
        ArgumentException.ThrowIfNullOrEmpty(projectName);
        ArgumentException.ThrowIfNullOrEmpty(apiKey);

        JArray? assetsResponse = await localCacheService.LoadAssetsListAsync(projectFolderPath, cancellationToken).ConfigureAwait(false);
        if (assetsResponse == null) {
            return null;
        }

        Dictionary<int, string> folderPaths = BuildFolderHierarchy(assetsResponse);
        return await ProcessAssetsAsync(assetsResponse, projectsRoot, projectName, folderPaths, apiKey, cancellationToken, progress, fetchTextureResolution).ConfigureAwait(false);
    }

    private static bool IsSupportedTextureFormat(string extension) {
        string[] supportedFormats = [".png", ".jpg", ".jpeg"];
        string[] excludedFormats = [".hdr", ".avif"];
        return supportedFormats.Contains(extension) && !excludedFormats.Contains(extension);
    }

    private static bool IsSupportedModelFormat(string extension) {
        string[] supportedModelFormats = [".fbx", ".obj"];
        string[] excludedFormats = [".hdr", ".avif"];
        return supportedModelFormats.Contains(extension) && !excludedFormats.Contains(extension);
    }
}

public sealed class ProjectAssetsResult {
    public ProjectAssetsResult(IReadOnlyDictionary<int, string> folderPaths) {
        FolderPaths = new Dictionary<int, string>(folderPaths);
    }

    public List<TextureResource> Textures { get; } = new();
    public List<ModelResource> Models { get; } = new();
    public List<MaterialResource> Materials { get; } = new();
    public Dictionary<int, string> FolderPaths { get; }
}
