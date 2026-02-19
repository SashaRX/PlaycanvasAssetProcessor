using AssetProcessor.Resources;
using AssetProcessor.Services.Models;
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;

namespace AssetProcessor.Services;

public sealed class PreviewWorkflowCoordinator : IPreviewWorkflowCoordinator {
    public void ApplyTextureGrouping(ICollectionView view, bool groupByType) {
        if (!view.CanGroup) return;

        if (groupByType) {
            using (view.DeferRefresh()) {
                view.GroupDescriptions.Clear();
                view.GroupDescriptions.Add(new PropertyGroupDescription("GroupName"));
                view.GroupDescriptions.Add(new PropertyGroupDescription("SubGroupName"));
            }
            return;
        }

        if (view.GroupDescriptions.Count > 0) {
            view.GroupDescriptions.Clear();
        }
    }

    public async Task<PreviewOrmLoadResult> LoadOrmPreviewAsync(
        ORMTextureResource ormTexture,
        bool isUsingD3D11Renderer,
        Func<ORMTextureResource, CancellationToken, Task<bool>> tryLoadKtx2ToD3D11Async,
        Func<ORMTextureResource, CancellationToken, Task<bool>> tryLoadKtx2PreviewAsync,
        CancellationToken cancellationToken,
        Action<string>? logInfo = null) {

        bool ktxLoaded;
        bool shouldExtractHistogram = false;

        if (isUsingD3D11Renderer) {
            logInfo?.Invoke($"[LoadORMPreviewAsync] Loading packed ORM to D3D11: {ormTexture.Name}");
            ktxLoaded = await tryLoadKtx2ToD3D11Async(ormTexture, cancellationToken);

            if (ktxLoaded && !cancellationToken.IsCancellationRequested) {
                shouldExtractHistogram = true;
            } else {
                logInfo?.Invoke($"[LoadORMPreviewAsync] Histogram skipped - ktxLoaded={ktxLoaded}, cancelled={cancellationToken.IsCancellationRequested}");
            }

            if (!ktxLoaded) {
                logInfo?.Invoke($"[LoadORMPreviewAsync] D3D11 native loading failed, trying PNG extraction: {ormTexture.Name}");
                ktxLoaded = await tryLoadKtx2PreviewAsync(ormTexture, cancellationToken);
            }
        } else {
            ktxLoaded = await tryLoadKtx2PreviewAsync(ormTexture, cancellationToken);
        }

        return new PreviewOrmLoadResult {
            Loaded = ktxLoaded,
            ShouldExtractHistogram = shouldExtractHistogram,
            WasCancelled = cancellationToken.IsCancellationRequested
        };
    }
}
