using AssetProcessor.Resources;
using AssetProcessor.Services.Models;
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using AssetProcessor.TextureViewer;

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


    public async Task ExtractOrmHistogramAsync(
        string? ormPath,
        string ormName,
        Func<string, CancellationToken, Task<List<KtxMipLevel>>> loadKtx2MipmapsAsync,
        Action<BitmapSource> onHistogramBitmapReady,
        CancellationToken cancellationToken,
        Action<string>? logInfo = null,
        Action<string>? logWarn = null,
        TimeSpan? startupDelay = null,
        TimeSpan? extractionTimeout = null) {

        try {
            var delay = startupDelay ?? TimeSpan.FromMilliseconds(200);
            var timeout = extractionTimeout ?? TimeSpan.FromSeconds(10);

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrEmpty(ormPath)) {
                logWarn?.Invoke($"[LoadORMPreviewAsync] ORM path is empty for: {ormName}");
                return;
            }

            logInfo?.Invoke($"[LoadORMPreviewAsync] Extracting mipmaps from: {ormPath}");

            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var mipmaps = await loadKtx2MipmapsAsync(ormPath, linkedCts.Token).ConfigureAwait(false);
            logInfo?.Invoke($"[LoadORMPreviewAsync] Extracted {mipmaps.Count} mipmaps for: {ormName}");

            if (mipmaps.Count <= 0 || linkedCts.Token.IsCancellationRequested) {
                logWarn?.Invoke($"[LoadORMPreviewAsync] No mipmaps or cancelled for: {ormName}");
                return;
            }

            var mip0Bitmap = mipmaps[0].Bitmap;
            logInfo?.Invoke($"[LoadORMPreviewAsync] Got mip0 bitmap {mip0Bitmap.PixelWidth}x{mip0Bitmap.PixelHeight} for: {ormName}");

            onHistogramBitmapReady(mip0Bitmap);
        } catch (OperationCanceledException) {
            logInfo?.Invoke($"[LoadORMPreviewAsync] Histogram extraction cancelled/timeout for: {ormName}");
        } catch (Exception ex) {
            logWarn?.Invoke($"[LoadORMPreviewAsync] Failed to extract bitmap for histogram: {ormName}. {ex.Message}");
        }
    }

}
