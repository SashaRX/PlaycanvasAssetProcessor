using AssetProcessor.Resources;
using AssetProcessor.Services.Models;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AssetProcessor.Services;

public interface IAssetResourceService {
    void BuildFolderHierarchy(JArray assetsResponse, IDictionary<int, string> targetFolderPaths);

    Task<AssetProcessingResult?> ProcessAssetAsync(
        JToken asset,
        AssetProcessingParameters parameters,
        CancellationToken cancellationToken);

    Task<MaterialResource?> LoadMaterialFromFileAsync(string filePath, CancellationToken cancellationToken);

    Task SaveAssetsListAsync(JToken jsonResponse, string projectFolderPath, CancellationToken cancellationToken);
}
