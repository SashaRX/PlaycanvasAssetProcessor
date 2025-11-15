using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AssetProcessor.Resources;
using AssetProcessor.Services.Models;
using AssetProcessor.Settings;
using Newtonsoft.Json.Linq;

namespace AssetProcessor.Services;

public sealed class ProjectSyncService : IProjectSyncService {
    private static readonly Uri BasePlayCanvasUri = new("https://playcanvas.com");

    private readonly IPlayCanvasService playCanvasService;
    private readonly ILocalCacheService localCacheService;
    private readonly ILogService logService;
    private readonly SemaphoreSlim downloadSemaphore;

    public ProjectSyncService(
        IPlayCanvasService playCanvasService,
        ILocalCacheService localCacheService,
        ILogService logService) {
        this.playCanvasService = playCanvasService ?? throw new ArgumentNullException(nameof(playCanvasService));
        this.localCacheService = localCacheService ?? throw new ArgumentNullException(nameof(localCacheService));
        this.logService = logService ?? throw new ArgumentNullException(nameof(logService));
        downloadSemaphore = new SemaphoreSlim(AppSettings.Default.DownloadSemaphoreLimit);
    }

    public async Task<ProjectSyncResult> SyncProjectAsync(ProjectSyncRequest request, IProgress<ProjectSyncProgress>? progress, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(request);

        logService.LogInfo($"SyncProjectAsync: Fetching assets for project {request.ProjectId} / branch {request.BranchId}");
        List<PlayCanvasAssetSummary> summaries = new();

        await foreach (PlayCanvasAssetSummary asset in playCanvasService.GetAssetsAsync(request.ProjectId, request.BranchId, request.ApiKey, cancellationToken).ConfigureAwait(false)) {
            summaries.Add(asset);
            progress?.Report(new ProjectSyncProgress(ProjectSyncStage.FetchingAssets, summaries.Count, summaries.Count));
        }

        JArray assetsJson = new();
        foreach (PlayCanvasAssetSummary asset in summaries) {
            assetsJson.Add(JToken.Parse(asset.ToJsonString()));
        }

        IReadOnlyDictionary<int, string> folderPaths = BuildFolderHierarchyFromAssets(assetsJson);

        string projectFolderPath = Path.Combine(request.ProjectsRoot, request.ProjectName);
        await localCacheService.SaveAssetsListAsync(assetsJson, projectFolderPath, cancellationToken).ConfigureAwait(false);

        progress?.Report(new ProjectSyncProgress(ProjectSyncStage.Completed, summaries.Count, summaries.Count));

        return new ProjectSyncResult(summaries, assetsJson, folderPaths, projectFolderPath, request.ProjectName);
    }

    public async Task<ResourceDownloadBatchResult> DownloadAsync(ProjectDownloadRequest request, IProgress<ResourceDownloadProgress>? progress, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Resources.Count == 0) {
            return new ResourceDownloadBatchResult(0, 0, 0);
        }

        int succeeded = 0;
        int failed = 0;
        int completed = 0;
        int total = request.Resources.Count;

        List<Task> tasks = new();
        foreach (BaseResource resource in request.Resources) {
            tasks.Add(DownloadResourceInternalAsync(
                resource,
                request,
                progress,
                total,
                () => Interlocked.Increment(ref succeeded),
                () => Interlocked.Increment(ref failed),
                () => Interlocked.Increment(ref completed),
                cancellationToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);

        return new ResourceDownloadBatchResult(succeeded, failed, total);
    }

    public Task<ResourceDownloadResult> DownloadResourceAsync(ResourceDownloadContext context, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(context);

        ProjectDownloadRequest request = new(
            new List<BaseResource> { context.Resource },
            context.ApiKey,
            context.ProjectName,
            context.ProjectsRoot,
            context.FolderPaths);

        int completed = 0;
        return DownloadResourceInternalAsync(
            context.Resource,
            request,
            progress: null,
            total: 1,
            onSuccess: () => { },
            onFailure: () => { },
            onCompleted: () => Interlocked.Increment(ref completed),
            cancellationToken);
    }

    public Task<ResourceDownloadResult> DownloadMaterialByIdAsync(MaterialDownloadContext context, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(context);
        return DownloadMaterialInternalAsync(context.Resource, new ProjectDownloadRequest(new List<BaseResource> { context.Resource }, context.ApiKey, context.ProjectName, context.ProjectsRoot, context.FolderPaths), cancellationToken);
    }

    public Task<ResourceDownloadResult> DownloadFileAsync(ResourceDownloadContext context, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(context);
        return DownloadFileInternalAsync(context.Resource, new ProjectDownloadRequest(new List<BaseResource> { context.Resource }, context.ApiKey, context.ProjectName, context.ProjectsRoot, context.FolderPaths), cancellationToken);
    }

    private async Task<ResourceDownloadResult> DownloadResourceInternalAsync(
        BaseResource resource,
        ProjectDownloadRequest request,
        IProgress<ResourceDownloadProgress>? progress,
        int total,
        Action onSuccess,
        Action onFailure,
        Func<int> onCompleted,
        CancellationToken cancellationToken) {
        await downloadSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            ResourceDownloadResult result = resource is MaterialResource material
                ? await DownloadMaterialInternalAsync(material, request, cancellationToken).ConfigureAwait(false)
                : await DownloadFileInternalAsync(resource, request, cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess) {
                onSuccess();
            } else {
                onFailure();
            }

            int finished = onCompleted();
            progress?.Report(new ResourceDownloadProgress(resource, finished, total));

            return result;
        } finally {
            downloadSemaphore.Release();
        }
    }

    private async Task<ResourceDownloadResult> DownloadMaterialInternalAsync(MaterialResource material, ProjectDownloadRequest request, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(material);

        string sanitizedName = localCacheService.SanitizePath(material.Name);
        material.Path = localCacheService.GetResourcePath(request.ProjectsRoot, request.ProjectName, request.FolderPaths, $"{sanitizedName}.json", material.Parent);

        try {
            PlayCanvasAssetDetail materialJson = await playCanvasService.GetAssetByIdAsync(material.ID.ToString(), request.ApiKey, cancellationToken).ConfigureAwait(false);
            JObject parsed = JObject.Parse(materialJson.ToJsonString());

            await localCacheService.DownloadMaterialAsync(material, _ => Task.FromResult(parsed), cancellationToken).ConfigureAwait(false);
            material.Status = "Downloaded";
            return new ResourceDownloadResult(true, material.Status, 1);
        } catch (Exception ex) {
            logService.LogError($"DownloadMaterialInternalAsync: {ex.Message}");
            material.Status = "Error";
            return new ResourceDownloadResult(false, material.Status ?? "Error", 1, ex.Message);
        }
    }

    private async Task<ResourceDownloadResult> DownloadFileInternalAsync(BaseResource resource, ProjectDownloadRequest request, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(resource);

        if (string.IsNullOrEmpty(resource.Url)) {
            await PopulateResourceMetadataAsync(resource, request.ApiKey, cancellationToken).ConfigureAwait(false);
        }

        string sanitizedName = localCacheService.SanitizePath(resource.Name);
        if (string.IsNullOrEmpty(resource.Path)) {
            resource.Path = localCacheService.GetResourcePath(request.ProjectsRoot, request.ProjectName, request.FolderPaths, sanitizedName, resource.Parent);
        }

        ResourceDownloadResult result = await localCacheService.DownloadFileAsync(resource, request.ApiKey, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async Task PopulateResourceMetadataAsync(BaseResource resource, string apiKey, CancellationToken cancellationToken) {
        PlayCanvasAssetDetail assetDetail = await playCanvasService.GetAssetByIdAsync(resource.ID.ToString(), apiKey, cancellationToken).ConfigureAwait(false);
        JObject json = JObject.Parse(assetDetail.ToJsonString());
        JObject? file = json["file"] as JObject;
        if (file == null) {
            return;
        }

        string? relativeUrl = file.Value<string>("url");
        if (!string.IsNullOrEmpty(relativeUrl)) {
            resource.Url = new Uri(BasePlayCanvasUri, relativeUrl).ToString();
        }

        if (file.TryGetValue("hash", out JToken? hashToken)) {
            resource.Hash = hashToken?.ToString();
        }

        if (file.TryGetValue("size", out JToken? sizeToken) && int.TryParse(sizeToken?.ToString(), out int size)) {
            resource.Size = size;
        }
    }

    private IReadOnlyDictionary<int, string> BuildFolderHierarchyFromAssets(JArray assetsResponse) {
        Dictionary<int, string> folderPaths = new();

        IEnumerable<JToken> folders = assetsResponse.Where(asset => string.Equals(asset["type"]?.ToString(), "folder", StringComparison.OrdinalIgnoreCase));
        Dictionary<int, JToken> foldersById = new();
        foreach (JToken folder in folders) {
            int? folderId = folder["id"]?.Type == JTokenType.Integer ? (int?)folder["id"] : null;
            if (folderId.HasValue) {
                foldersById[folderId.Value] = folder;
            }
        }

        string BuildFolderPath(int folderId) {
            if (folderPaths.TryGetValue(folderId, out string? existing)) {
                return existing;
            }

            if (!foldersById.TryGetValue(folderId, out JToken? folder)) {
                return string.Empty;
            }

            string folderName = localCacheService.SanitizePath(folder["name"]?.ToString());
            int? parentId = folder["parent"]?.Type == JTokenType.Integer ? (int?)folder["parent"] : null;

            string fullPath = folderName;
            if (parentId.HasValue && parentId.Value != 0) {
                string parentPath = BuildFolderPath(parentId.Value);
                fullPath = string.IsNullOrEmpty(parentPath) ? folderName : Path.Combine(parentPath, folderName);
            }

            fullPath = localCacheService.SanitizePath(fullPath);
            folderPaths[folderId] = fullPath;
            return fullPath;
        }

        foreach (int folderId in foldersById.Keys) {
            BuildFolderPath(folderId);
        }

        logService.LogInfo($"BuildFolderHierarchyFromAssets: Created {folderPaths.Count} folder paths");
        return folderPaths;
    }
}
