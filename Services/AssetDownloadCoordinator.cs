using AssetProcessor.Resources;
using AssetProcessor.Services.Models;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AssetProcessor.Services;

public sealed class AssetDownloadCoordinator : IAssetDownloadCoordinator {
    private const int MaxRetryAttempts = 3;
    private static readonly TimeSpan BaseRetryDelay = TimeSpan.FromSeconds(1);

    private static readonly HashSet<string> DownloadableStatuses = new(
        new[] { "Ready", "On Server", "Error", "Size Mismatch", "Corrupted", "Empty File", "Hash ERROR", string.Empty },
        StringComparer.OrdinalIgnoreCase);

    private readonly IProjectSyncService projectSyncService;
    private readonly ILocalCacheService localCacheService;
    private readonly ILogger logger;

    public AssetDownloadCoordinator(
        IProjectSyncService projectSyncService,
        ILocalCacheService localCacheService,
        ILogger logger) {
        this.projectSyncService = projectSyncService ?? throw new ArgumentNullException(nameof(projectSyncService));
        this.localCacheService = localCacheService ?? throw new ArgumentNullException(nameof(localCacheService));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public event EventHandler<ResourceStatusChangedEventArgs>? ResourceStatusChanged;

    public async Task<AssetDownloadResult> DownloadAssetsAsync(
        AssetDownloadContext context,
        IProgress<AssetDownloadProgress>? progress,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrEmpty(context.ApiKey);
        ArgumentException.ThrowIfNullOrEmpty(context.ProjectName);
        ArgumentException.ThrowIfNullOrEmpty(context.ProjectsRoot);

        cancellationToken.ThrowIfCancellationRequested();

        List<BaseResource> pendingResources = PrepareResources(context);
        if (pendingResources.Count == 0) {
            logger.Info("AssetDownloadCoordinator: no assets require download");
            return new AssetDownloadResult(false, "No assets need downloading", new ResourceDownloadBatchResult(0, 0, 0));
        }

        int total = pendingResources.Count;
        progress?.Report(new AssetDownloadProgress(0, total, null));

        int overallCompleted = 0;
        int attempt = 0;

        List<BaseResource> remaining = pendingResources;
        while (remaining.Count > 0 && attempt < MaxRetryAttempts) {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;

            logger.Info($"AssetDownloadCoordinator: starting attempt {attempt} for {remaining.Count} assets");
            ProjectDownloadRequest request = new(
                remaining,
                context.ApiKey,
                context.ProjectName,
                context.ProjectsRoot,
                context.FolderPaths);

            int attemptCompleted = 0;
            IProgress<ResourceDownloadProgress> internalProgress = new Progress<ResourceDownloadProgress>(progressUpdate => {
                if (progressUpdate == null) {
                    return;
                }

                int delta = Math.Max(0, progressUpdate.Completed - attemptCompleted);
                attemptCompleted = progressUpdate.Completed;
                overallCompleted = Math.Min(total, overallCompleted + delta);

                progress?.Report(new AssetDownloadProgress(overallCompleted, total, progressUpdate.Resource));
                OnResourceStatusChanged(progressUpdate.Resource);
            });

            try {
                await projectSyncService.DownloadAsync(request, internalProgress, cancellationToken).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                logger.Warn("AssetDownloadCoordinator: download cancelled");
                throw;
            } catch (Exception ex) {
                logger.Error(ex, "AssetDownloadCoordinator: unexpected error during download");
                return BuildResult(pendingResources, false, $"Download error: {ex.Message}");
            }

            remaining = remaining.Where(RequiresDownload).ToList();
            if (remaining.Count > 0 && attempt < MaxRetryAttempts) {
                TimeSpan delay = TimeSpan.FromMilliseconds(BaseRetryDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
                logger.Warn($"AssetDownloadCoordinator: retrying {remaining.Count} assets after {delay.TotalMilliseconds}ms delay");
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        bool success = remaining.Count == 0 && pendingResources.All(r => string.Equals(r.Status, "Downloaded", StringComparison.OrdinalIgnoreCase));
        string message = success
            ? $"Downloaded {pendingResources.Count} assets"
            : $"Downloaded {pendingResources.Count - remaining.Count} assets. Failed: {remaining.Count}";

        return BuildResult(pendingResources, success, message);
    }

    private AssetDownloadResult BuildResult(List<BaseResource> resources, bool success, string message) {
        int succeeded = resources.Count(r => string.Equals(r.Status, "Downloaded", StringComparison.OrdinalIgnoreCase));
        int failed = resources.Count - succeeded;
        ResourceDownloadBatchResult batch = new(succeeded, failed, resources.Count);
        return new AssetDownloadResult(success, message, batch);
    }

    private List<BaseResource> PrepareResources(AssetDownloadContext context) {
        List<BaseResource> filtered = new();
        foreach (BaseResource resource in context.Resources) {
            if (!RequiresDownload(resource)) {
                continue;
            }

            string sanitizedName = localCacheService.SanitizePath(resource.Name);
            if (string.IsNullOrEmpty(resource.Path)) {
                resource.Path = localCacheService.GetResourcePath(
                    context.ProjectsRoot,
                    context.ProjectName,
                    context.FolderPaths,
                    sanitizedName,
                    resource.Parent);
            }

            filtered.Add(resource);
        }

        return filtered;
    }

    private static bool RequiresDownload(BaseResource resource) {
        if (string.IsNullOrWhiteSpace(resource.Status)) {
            return true;
        }

        return DownloadableStatuses.Contains(resource.Status);
    }

    private void OnResourceStatusChanged(BaseResource? resource) {
        if (resource == null) {
            return;
        }

        ResourceStatusChanged?.Invoke(this, new ResourceStatusChangedEventArgs(resource, resource.Status));
    }
}
