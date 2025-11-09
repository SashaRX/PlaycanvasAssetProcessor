using System;
using System.Threading;
using System.Threading.Tasks;

namespace AssetProcessor.Services;

public class AssetQueueService {
    private readonly SemaphoreSlim assetProcessingSemaphore;
    private readonly SemaphoreSlim downloadSemaphore;

    public AssetQueueService(int assetProcessingLimit, int downloadLimit) {
        if (assetProcessingLimit <= 0) {
            throw new ArgumentOutOfRangeException(nameof(assetProcessingLimit));
        }

        if (downloadLimit <= 0) {
            throw new ArgumentOutOfRangeException(nameof(downloadLimit));
        }

        assetProcessingSemaphore = new SemaphoreSlim(assetProcessingLimit, assetProcessingLimit);
        downloadSemaphore = new SemaphoreSlim(downloadLimit, downloadLimit);
    }

    public async Task RunAssetQueueAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(work);

        await assetProcessingSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            await work(cancellationToken).ConfigureAwait(false);
        } finally {
            assetProcessingSemaphore.Release();
        }
    }

    public async Task RunDownloadQueueAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(work);

        await downloadSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            await work(cancellationToken).ConfigureAwait(false);
        } finally {
            downloadSemaphore.Release();
        }
    }
}
