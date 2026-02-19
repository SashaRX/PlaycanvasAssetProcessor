using AssetProcessor.Resources;
using AssetProcessor.Services.Models;
using AssetProcessor.Upload;
using AssetProcessor.ViewModels;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;

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

    Task<ServerAssetDeleteResult> DeleteServerAssetAsync(
        ServerAssetViewModel asset,
        string keyId,
        string bucketName,
        string bucketId,
        Func<string?> getApplicationKey,
        Func<IB2UploadService> createB2Service,
        Func<Task> refreshServerAssetsAsync,
        Action<string>? onInfo = null,
        Action<string>? onError = null);
}
