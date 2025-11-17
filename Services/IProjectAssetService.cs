using AssetProcessor.Resources;
using AssetProcessor.Services.Models;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AssetProcessor.Services;

public interface IProjectAssetService {
    Task<JArray?> LoadAssetsFromJsonAsync(string projectFolderPath, CancellationToken cancellationToken);

    Task<IReadOnlyList<PlayCanvasAssetSummary>> FetchAssetsFromApiAsync(
        string projectId,
        string branchId,
        string apiKey,
        CancellationToken cancellationToken);

    Task<bool> HasUpdatesAsync(ProjectUpdateContext context, CancellationToken cancellationToken);

    string GetResourcePath(
        string projectsRoot,
        string projectName,
        IReadOnlyDictionary<int, string> folderPaths,
        string? fileName,
        int? parentId);
}
