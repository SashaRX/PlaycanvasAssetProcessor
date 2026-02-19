using AssetProcessor.Resources;
using AssetProcessor.Services.Models;
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using AssetProcessor.TextureViewer;

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


    Task ExtractOrmHistogramAsync(
        string? ormPath,
        string ormName,
        Func<string, CancellationToken, Task<List<KtxMipLevel>>> loadKtx2MipmapsAsync,
        Action<BitmapSource> onHistogramBitmapReady,
        CancellationToken cancellationToken,
        Action<string>? logInfo = null,
        Action<string>? logWarn = null,
        TimeSpan? startupDelay = null,
        TimeSpan? extractionTimeout = null);
}
