using AssetProcessor.Resources;
using AssetProcessor.Services;
using AssetProcessor.Services.Models;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AssetProcessor.Tests.Services;

public class AssetDownloadCoordinatorTests {
    [Fact]
    public async Task DownloadAssetsAsync_FiltersStatusesBeforeRequest() {
        StubProjectSyncService syncService = new();
        StubLocalCacheService cacheService = new();
        AssetDownloadCoordinator coordinator = new(syncService, cacheService, LogManager.CreateNullLogger());

        List<BaseResource> assets = new() {
            new TextureResource { ID = 1, Name = "ready", Status = "Ready", Parent = 0 },
            new TextureResource { ID = 2, Name = "downloaded", Status = "Downloaded", Parent = 0 },
            new TextureResource { ID = 3, Name = "error", Status = "Error", Parent = 0 }
        };

        AssetDownloadContext context = new(
            assets,
            "api",
            "Project",
            "C:/Projects",
            new Dictionary<int, string>());

        await coordinator.DownloadAssetsAsync(context, options: null, CancellationToken.None);

        Assert.NotNull(syncService.LastRequest);
        Assert.Equal(2, syncService.LastRequest!.Resources.Count);
        Assert.DoesNotContain(syncService.LastRequest.Resources, r => r.Name == "downloaded");
    }

    [Fact]
    public async Task DownloadAssetsAsync_ReportsProgressForEachResource() {
        StubProjectSyncService syncService = new();
        StubLocalCacheService cacheService = new();
        AssetDownloadCoordinator coordinator = new(syncService, cacheService, LogManager.CreateNullLogger());

        List<AssetDownloadProgress> events = new();
        AssetDownloadOptions options = new(progress => events.Add(progress), resource => { });

        List<BaseResource> assets = new() {
            new TextureResource { ID = 1, Name = "one", Status = "Ready" },
            new TextureResource { ID = 2, Name = "two", Status = "Ready" }
        };

        AssetDownloadContext context = new(
            assets,
            "api",
            "Project",
            "C:/Projects",
            new Dictionary<int, string>());

        AssetDownloadResult result = await coordinator.DownloadAssetsAsync(context, options, CancellationToken.None);

        Assert.Equal(2, result.BatchResult.Total);
        Assert.Equal(3, events.Count); // initial + two updates
        Assert.Contains(events, e => e.Completed == 0 && e.Total == 2);
        Assert.Contains(events, e => e.Completed == 2 && e.Total == 2);
    }

    [Fact]
    public async Task DownloadAssetsAsync_ThrowsOnCancellation() {
        StubProjectSyncService syncService = new() { DelayPerCall = TimeSpan.FromMilliseconds(50) };
        StubLocalCacheService cacheService = new();
        AssetDownloadCoordinator coordinator = new(syncService, cacheService, LogManager.CreateNullLogger());

        List<BaseResource> assets = new() {
            new TextureResource { ID = 1, Name = "one", Status = "Ready" }
        };

        AssetDownloadContext context = new(
            assets,
            "api",
            "Project",
            "C:/Projects",
            new Dictionary<int, string>());

        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => coordinator.DownloadAssetsAsync(context, options: null, cts.Token));
    }

    private sealed class StubProjectSyncService : IProjectSyncService {
        public ProjectDownloadRequest? LastRequest { get; private set; }
        public TimeSpan DelayPerCall { get; set; }

        public Task<ResourceDownloadBatchResult> DownloadAsync(ProjectDownloadRequest request, IProgress<ResourceDownloadProgress>? progress, CancellationToken cancellationToken) {
            LastRequest = request;
            return DownloadAsyncInternalAsync(request, progress, cancellationToken);
        }

        private async Task<ResourceDownloadBatchResult> DownloadAsyncInternalAsync(ProjectDownloadRequest request, IProgress<ResourceDownloadProgress>? progress, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();
            int completed = 0;
            foreach (BaseResource resource in request.Resources) {
                cancellationToken.ThrowIfCancellationRequested();
                resource.Status = "Downloaded";
                completed++;
                progress?.Report(new ResourceDownloadProgress(resource, completed, request.Resources.Count));
            }

            if (DelayPerCall > TimeSpan.Zero) {
                await Task.Delay(DelayPerCall, cancellationToken);
            }

            return new ResourceDownloadBatchResult(request.Resources.Count, 0, request.Resources.Count);
        }

        public Task<ResourceDownloadResult> DownloadFileAsync(ResourceDownloadContext context, CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task<ResourceDownloadResult> DownloadMaterialByIdAsync(MaterialDownloadContext context, CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task<ResourceDownloadResult> DownloadResourceAsync(ResourceDownloadContext context, CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task<ProjectSyncResult> SyncProjectAsync(ProjectSyncRequest request, IProgress<ProjectSyncProgress>? progress, CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    private sealed class StubLocalCacheService : ILocalCacheService {
        public Task<ResourceDownloadResult> DownloadFileAsync(BaseResource resource, string apiKey, CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task DownloadMaterialAsync(MaterialResource materialResource, Func<CancellationToken, Task<Newtonsoft.Json.Linq.JObject>> fetchMaterialJsonAsync, CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task<Newtonsoft.Json.Linq.JArray?> LoadAssetsListAsync(string projectFolderPath, CancellationToken cancellationToken) => throw new NotImplementedException();

        public string GetResourcePath(string projectsRoot, string projectName, IReadOnlyDictionary<int, string> folderPaths, string? fileName, int? parentId) =>
            System.IO.Path.Combine(projectsRoot, projectName, LocalCacheService.AssetsDirectoryName, fileName ?? string.Empty);

        public Task SaveAssetsListAsync(Newtonsoft.Json.Linq.JToken jsonResponse, string projectFolderPath, CancellationToken cancellationToken) => throw new NotImplementedException();
    }
}
