using AssetProcessor.Services.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AssetProcessor.Services;

public interface IAssetDownloadCoordinator {
    event EventHandler<ResourceStatusChangedEventArgs>? ResourceStatusChanged;

    Task<AssetDownloadResult> DownloadAssetsAsync(
        AssetDownloadContext context,
        AssetDownloadOptions? options,
        CancellationToken cancellationToken);
}
