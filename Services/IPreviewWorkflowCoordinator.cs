using AssetProcessor.Resources;
using AssetProcessor.Services.Models;
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace AssetProcessor.Services;

public interface IPreviewWorkflowCoordinator {
    void ApplyTextureGrouping(ICollectionView view, bool groupByType);

    Task<PreviewOrmLoadResult> LoadOrmPreviewAsync(
        ORMTextureResource ormTexture,
        bool isUsingD3D11Renderer,
        Func<ORMTextureResource, CancellationToken, Task<bool>> tryLoadKtx2ToD3D11Async,
        Func<ORMTextureResource, CancellationToken, Task<bool>> tryLoadKtx2PreviewAsync,
        CancellationToken cancellationToken,
        Action<string>? logInfo = null);
}
