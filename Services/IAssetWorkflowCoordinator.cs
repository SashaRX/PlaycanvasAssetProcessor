using AssetProcessor.Resources;
using AssetProcessor.Services.Models;
using System.Collections.Generic;

namespace AssetProcessor.Services;

public interface IAssetWorkflowCoordinator {
    string? ExtractRelativePathFromUrl(string? url);
    int ResetStatusesForDeletedPaths(IEnumerable<string> deletedPaths, IEnumerable<BaseResource> resources);
    int VerifyStatusesAgainstServerPaths(HashSet<string> serverPaths, IEnumerable<BaseResource> resources);
    ResourceNavigationResult ResolveNavigationTarget(
        string fileName,
        IEnumerable<TextureResource> textures,
        IEnumerable<ModelResource> models,
        IEnumerable<MaterialResource> materials);
}
