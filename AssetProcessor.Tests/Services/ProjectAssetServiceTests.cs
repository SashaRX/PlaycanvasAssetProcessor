using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AssetProcessor.Services;
using AssetProcessor.Services.Models;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AssetProcessor.Tests.Services;

public class ProjectAssetServiceTests {
    [Fact]
    public async Task LoadAssetsFromJsonAsync_DelegatesToLocalCache() {
        FakeLocalCacheService cache = new() {
            AssetsToReturn = new JArray(new JObject { ["id"] = 7 })
        };
        ProjectAssetService service = CreateService(cache, new FakePlayCanvasService(), new RecordingLogService());

        JArray? result = await service.LoadAssetsFromJsonAsync("/projects/demo", CancellationToken.None);

        Assert.Same(cache.AssetsToReturn, result);
        Assert.Equal("/projects/demo", cache.LastLoadPath);
    }

    [Fact]
    public async Task FetchAssetsFromApiAsync_EnumeratesAllAssetsAndLogsCount() {
        List<PlayCanvasAssetSummary> assets = [
            CreateAssetSummary(1),
            CreateAssetSummary(2)
        ];
        FakePlayCanvasService playCanvas = new(assets);
        RecordingLogService logService = new();
        ProjectAssetService service = CreateService(new FakeLocalCacheService(), playCanvas, logService);

        IReadOnlyList<PlayCanvasAssetSummary> result = await service.FetchAssetsFromApiAsync(
            "project",
            "branch",
            "token",
            CancellationToken.None);

        Assert.Equal(assets, result);
        Assert.Contains(logService.InfoMessages, message =>
            message.Contains("Fetched 2 asset summaries", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HasUpdatesAsync_WhenLocalJsonMissing_ReturnsTrue() {
        FakeLocalCacheService cache = new() { AssetsToReturn = null };
        ProjectAssetService service = CreateService(cache, new FakePlayCanvasService(), new RecordingLogService());

        bool hasUpdates = await service.HasUpdatesAsync(
            new ProjectUpdateContext("/projects/demo", "p1", "b1", "token"),
            CancellationToken.None);

        Assert.True(hasUpdates);
    }

    [Fact]
    public async Task HasUpdatesAsync_ComparesHashesAndDetectsChanges() {
        FakeLocalCacheService cache = new() {
            AssetsToReturn = new JArray(JObject.Parse(CreateAssetSummary(1).ToJsonString()))
        };

        List<PlayCanvasAssetSummary> apiAssets = [
            CreateAssetSummary(1),
            CreateAssetSummary(2)
        ];
        FakePlayCanvasService playCanvas = new(apiAssets);
        RecordingLogService logService = new();
        ProjectAssetService service = CreateService(cache, playCanvas, logService);

        bool hasUpdates = await service.HasUpdatesAsync(
            new ProjectUpdateContext("/projects/demo", "p1", "b1", "token"),
            CancellationToken.None);

        Assert.True(hasUpdates);
        Assert.Contains(logService.InfoMessages, message =>
            message.Contains("Project has updates", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HasUpdatesAsync_WhenHashesMatch_ReturnsFalse() {
        PlayCanvasAssetSummary asset = CreateAssetSummary(42);
        FakeLocalCacheService cache = new() {
            AssetsToReturn = new JArray(JObject.Parse(asset.ToJsonString()))
        };
        FakePlayCanvasService playCanvas = new([asset]);
        ProjectAssetService service = CreateService(cache, playCanvas, new RecordingLogService());

        bool hasUpdates = await service.HasUpdatesAsync(
            new ProjectUpdateContext("/projects/demo", "p1", "b1", "token"),
            CancellationToken.None);

        Assert.False(hasUpdates);
    }

    private static ProjectAssetService CreateService(
        ILocalCacheService cache,
        IPlayCanvasService playCanvas,
        ILogService log) => new(cache, playCanvas, log);

    private static PlayCanvasAssetSummary CreateAssetSummary(int id) {
        using JsonDocument document = JsonDocument.Parse($$"""{"id":{{id}},"type":"texture","name":"Asset{{id}}"}""");
        return new PlayCanvasAssetSummary(
            id,
            "texture",
            $"Asset{id}",
            $"assets/{id}",
            parent: null,
            new PlayCanvasAssetFileInfo(128, $"hash{id}", $"asset{id}.png", $"https://cdn/assets/{id}", 64, 64),
            document.RootElement.Clone());
    }

    private sealed class FakeLocalCacheService : ILocalCacheService {
        public JArray? AssetsToReturn { get; set; }
        public string? LastLoadPath { get; private set; }

        public string SanitizePath(string? path) => path ?? string.Empty;

        public string GetResourcePath(string projectsRoot, string projectName, IReadOnlyDictionary<int, string> folderPaths, string? fileName, int? parentId) =>
            $"{projectsRoot}/{projectName}/{fileName}";

        public Task SaveAssetsListAsync(JToken jsonResponse, string projectFolderPath, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<JArray?> LoadAssetsListAsync(string projectFolderPath, CancellationToken cancellationToken) {
            LastLoadPath = projectFolderPath;
            return Task.FromResult(AssetsToReturn);
        }

        public Task DownloadMaterialAsync(
            MaterialResource materialResource,
            Func<CancellationToken, Task<JObject>> fetchMaterialJsonAsync,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ResourceDownloadResult> DownloadFileAsync(BaseResource resource, string apiKey, CancellationToken cancellationToken) =>
            Task.FromResult(new ResourceDownloadResult(true, resource.Path));
    }

    private sealed class FakePlayCanvasService : IPlayCanvasService {
        private readonly IReadOnlyList<PlayCanvasAssetSummary> assets;

        public FakePlayCanvasService()
            : this(Array.Empty<PlayCanvasAssetSummary>()) {
        }

        public FakePlayCanvasService(IReadOnlyList<PlayCanvasAssetSummary> assets) {
            this.assets = assets;
        }

        public Task<Dictionary<string, string>> GetProjectsAsync(string? userId, string? apiKey, Dictionary<string, string> projects, CancellationToken cancellationToken) =>
            Task.FromResult(projects);

        public Task<List<Branch>> GetBranchesAsync(string projectId, string apiKey, List<Branch> branches, CancellationToken cancellationToken) =>
            Task.FromResult(branches);

        public IAsyncEnumerable<PlayCanvasAssetSummary> GetAssetsAsync(string projectId, string branchId, string apiKey, CancellationToken cancellationToken) =>
            GetAssetsAsyncCore();

        private async IAsyncEnumerable<PlayCanvasAssetSummary> GetAssetsAsyncCore() {
            foreach (PlayCanvasAssetSummary asset in assets) {
                yield return asset;
                await Task.Yield();
            }
        }

        public Task<PlayCanvasAssetDetail> GetAssetByIdAsync(string assetId, string apiKey, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<string> GetUserIdAsync(string? username, string? apiKey, CancellationToken cancellationToken) =>
            Task.FromResult("user");

        public void Dispose() {
        }
    }

    private sealed class RecordingLogService : ILogService {
        public List<string> InfoMessages { get; } = new();

        public void LogDebug(string message) {
        }

        public void LogError(string? message) {
        }

        public void LogInfo(string message) => InfoMessages.Add(message);

        public void LogWarn(string message) {
        }
    }
}
