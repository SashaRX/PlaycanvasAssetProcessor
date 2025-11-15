using AssetProcessor.Resources;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AssetProcessor.Services;

public interface ILocalCacheService {
    string SanitizePath(string? path);

    string GetResourcePath(
        string projectsRoot,
        string projectName,
        IReadOnlyDictionary<int, string> folderPaths,
        string? fileName,
        int? parentId);

    Task SaveAssetsListAsync(JToken jsonResponse, string projectFolderPath, CancellationToken cancellationToken);

    Task<JArray?> LoadAssetsListAsync(string projectFolderPath, CancellationToken cancellationToken);

    Task DownloadMaterialAsync(
        MaterialResource materialResource,
        Func<CancellationToken, Task<JObject>> fetchMaterialJsonAsync,
        CancellationToken cancellationToken);

    Task<ResourceDownloadResult> DownloadFileAsync(BaseResource resource, string apiKey, CancellationToken cancellationToken);
}
