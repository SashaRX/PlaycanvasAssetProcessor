using AssetProcessor.Helpers;
using AssetProcessor.Resources;
using AssetProcessor.Services.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AssetProcessor.Services;

public class ProjectAssetService : IProjectAssetService {
    private readonly ILocalCacheService localCacheService;
    private readonly IPlayCanvasService playCanvasService;
    private readonly ILogService logService;

    public ProjectAssetService(
        ILocalCacheService localCacheService,
        IPlayCanvasService playCanvasService,
        ILogService logService) {
        this.localCacheService = localCacheService ?? throw new ArgumentNullException(nameof(localCacheService));
        this.playCanvasService = playCanvasService ?? throw new ArgumentNullException(nameof(playCanvasService));
        this.logService = logService ?? throw new ArgumentNullException(nameof(logService));
    }

    public Task<JArray?> LoadAssetsFromJsonAsync(string projectFolderPath, CancellationToken cancellationToken) {
        ArgumentException.ThrowIfNullOrEmpty(projectFolderPath);
        return localCacheService.LoadAssetsListAsync(projectFolderPath, cancellationToken);
    }

    public async Task<IReadOnlyList<PlayCanvasAssetSummary>> FetchAssetsFromApiAsync(
        string projectId,
        string branchId,
        string apiKey,
        CancellationToken cancellationToken) {
        ArgumentException.ThrowIfNullOrEmpty(projectId);
        ArgumentException.ThrowIfNullOrEmpty(branchId);
        ArgumentException.ThrowIfNullOrEmpty(apiKey);

        List<PlayCanvasAssetSummary> summaries = new();
        await foreach (PlayCanvasAssetSummary asset in playCanvasService
            .GetAssetsAsync(projectId, branchId, apiKey, cancellationToken)
            .ConfigureAwait(false)) {
            summaries.Add(asset);
        }

        logService.LogInfo($"Fetched {summaries.Count} asset summaries from API for project {projectId} / branch {branchId}");
        return summaries;
    }

    public async Task<bool> HasUpdatesAsync(ProjectUpdateContext context, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrEmpty(context.ProjectFolderPath);
        ArgumentException.ThrowIfNullOrEmpty(context.ProjectId);
        ArgumentException.ThrowIfNullOrEmpty(context.BranchId);
        ArgumentException.ThrowIfNullOrEmpty(context.ApiKey);

        JArray? localAssets = await LoadAssetsFromJsonAsync(context.ProjectFolderPath, cancellationToken).ConfigureAwait(false);
        if (localAssets == null) {
            logService.LogInfo("Local assets JSON was not found - updates required.");
            return true;
        }

        string localHash = ComputeHash(localAssets.ToString());
        IReadOnlyList<PlayCanvasAssetSummary> serverAssets = await FetchAssetsFromApiAsync(
            context.ProjectId,
            context.BranchId,
            context.ApiKey,
            cancellationToken).ConfigureAwait(false);

        JArray serverData = new();
        foreach (PlayCanvasAssetSummary asset in serverAssets) {
            serverData.Add(JToken.Parse(asset.ToJsonString()));
        }

        string serverHash = ComputeHash(serverData.ToString());
        bool hasChanges = !string.Equals(localHash, serverHash, StringComparison.OrdinalIgnoreCase);

        if (hasChanges) {
            logService.LogInfo($"Project has updates: local hash {localHash[..8]}... != server hash {serverHash[..8]}...");
        } else {
            logService.LogInfo("Project assets are up to date");
        }

        return hasChanges;
    }

    public string GetResourcePath(
        string projectsRoot,
        string projectName,
        IReadOnlyDictionary<int, string> folderPaths,
        string? fileName,
        int? parentId) {
        ArgumentException.ThrowIfNullOrEmpty(projectsRoot);
        ArgumentException.ThrowIfNullOrEmpty(projectName);
        ArgumentNullException.ThrowIfNull(folderPaths);

        string sanitizedFileName = PathSanitizer.SanitizePath(fileName);
        string fullPath = localCacheService.GetResourcePath(projectsRoot, projectName, folderPaths, sanitizedFileName, parentId);
        logService.LogInfo($"Generated resource path: {fullPath}");
        return fullPath;
    }

    private static string ComputeHash(string input) {
        using MD5 md5 = MD5.Create();
        byte[] inputBytes = Encoding.UTF8.GetBytes(input);
        byte[] hashBytes = md5.ComputeHash(inputBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
