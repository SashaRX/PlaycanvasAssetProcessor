using AssetProcessor.Resources;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace AssetProcessor.Services.Models;

public sealed record ProjectSyncRequest(
    string ProjectId,
    string BranchId,
    string ApiKey,
    string ProjectName,
    string ProjectsRoot);

public enum ProjectSyncStage {
    FetchingAssets,
    ProcessingAssets,
    Completed
}

public sealed record ProjectSyncProgress(ProjectSyncStage Stage, int Processed, int Total);

public sealed record ProjectSyncResult(
    IReadOnlyList<PlayCanvasAssetSummary> Assets,
    JArray AssetsJson,
    IReadOnlyDictionary<int, string> FolderPaths,
    string ProjectFolderPath,
    string ProjectName);

public sealed record ProjectDownloadRequest(
    IReadOnlyCollection<BaseResource> Resources,
    string ApiKey,
    string ProjectName,
    string ProjectsRoot,
    IReadOnlyDictionary<int, string> FolderPaths);

public sealed record ResourceDownloadContext(
    BaseResource Resource,
    string ApiKey,
    string ProjectName,
    string ProjectsRoot,
    IReadOnlyDictionary<int, string> FolderPaths);

public sealed record MaterialDownloadContext(
    MaterialResource Resource,
    string ApiKey,
    string ProjectName,
    string ProjectsRoot,
    IReadOnlyDictionary<int, string> FolderPaths);

public sealed record ResourceDownloadProgress(BaseResource Resource, int Completed, int Total);

public sealed record ResourceDownloadBatchResult(int Succeeded, int Failed, int Total);
