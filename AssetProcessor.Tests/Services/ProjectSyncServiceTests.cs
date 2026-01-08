using AssetProcessor.Resources;
using AssetProcessor.Services;
using AssetProcessor.Services.Models;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AssetProcessor.Tests.Services;

public class ProjectSyncServiceTests {
    [Fact]
    public async Task SyncProjectAsync_BuildsFolderHierarchyAndSavesAssets() {
        StubPlayCanvasService playCanvas = new();
        StubLocalCacheService cache = new();
        StubAssetResourceService assetResource = new();
        TestLogService log = new();
        ProjectSyncService service = new(playCanvas, cache, assetResource, log);

        ProjectSyncRequest request = new(
            "project",
            "branch",
            "api",
            "TestProject",
            "C:/Projects");

        ProjectSyncResult result = await service.SyncProjectAsync(request, progress: null, CancellationToken.None);

        Assert.Equal(2, result.Assets.Count);
        Assert.NotEmpty(result.FolderPaths);
        Assert.True(cache.SaveAssetsListCalled);
        Assert.Equal("C:/Projects/TestProject", result.ProjectFolderPath);
    }

    [Fact]
    public async Task DownloadAsync_TracksSuccessAndFailure() {
        StubPlayCanvasService playCanvas = new();
        StubLocalCacheService cache = new();
        cache.FileDownloadResults.Enqueue(new ResourceDownloadResult(true, "Downloaded", 1));
        cache.FileDownloadResults.Enqueue(new ResourceDownloadResult(false, "Error", 1, "boom"));
        StubAssetResourceService assetResource = new();
        TestLogService log = new();
        ProjectSyncService service = new(playCanvas, cache, assetResource, log);

        TextureResource texture = new() { ID = 1, Name = "texture", Url = "https://example.com/texture.png", Path = "texture.png" };
        ModelResource model = new() { ID = 2, Name = "model", Url = "https://example.com/model.fbx", Path = "model.fbx" };

        ProjectDownloadRequest request = new(
            new List<BaseResource> { texture, model },
            "api",
            "TestProject",
            "C:/Projects",
            new Dictionary<int, string>());

        ResourceDownloadBatchResult result = await service.DownloadAsync(request, progress: null, CancellationToken.None);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.Equal(2, result.Total);
    }

    private sealed class StubPlayCanvasService : IPlayCanvasService {
        public void Dispose() { }

        public Task<PlayCanvasAssetDetail> GetAssetByIdAsync(string assetId, string apiKey, CancellationToken cancellationToken) {
                        JObject json = new() {
                ["file"] = new JObject {
                    ["url"] = "/files/sample.bin",
                    ["hash"] = "abc",
                    ["size"] = 10
                }
            };
            using JsonDocument document = JsonDocument.Parse(json.ToString());
            return Task.FromResult(new PlayCanvasAssetDetail(1, "texture", "sample", document.RootElement.Clone()));
        }

        public IAsyncEnumerable<PlayCanvasAssetSummary> GetAssetsAsync(string projectId, string branchId, string apiKey, CancellationToken cancellationToken) {
            return GetAssetsAsyncCore();
        }

        private async IAsyncEnumerable<PlayCanvasAssetSummary> GetAssetsAsyncCore() {
            JObject folder = new() {
                ["id"] = 1,
                ["type"] = "folder",
                ["name"] = "Textures",
                ["parent"] = 0
            };
            JObject texture = new() {
                ["id"] = 2,
                ["type"] = "texture",
                ["name"] = "brick.png",
                ["parent"] = 1,
                ["file"] = new JObject {
                    ["url"] = "/files/brick.png"
                }
            };

            using JsonDocument folderDoc = JsonDocument.Parse(folder.ToString());
            using JsonDocument textureDoc = JsonDocument.Parse(texture.ToString());

            yield return new PlayCanvasAssetSummary(1, "folder", "Textures", "/", 0, null, folderDoc.RootElement.Clone());
            yield return new PlayCanvasAssetSummary(2, "texture", "brick.png", "/brick.png", 1, new PlayCanvasAssetFileInfo(10, "hash", "brick.png", "/files/brick.png", null, null), textureDoc.RootElement.Clone());
            await Task.CompletedTask;
        }

        public Task<List<Branch>> GetBranchesAsync(string projectId, string apiKey, List<Branch> branches, CancellationToken cancellationToken) =>
            Task.FromResult(branches);

        public Task<Dictionary<string, string>> GetProjectsAsync(string? userId, string? apiKey, Dictionary<string, string> projects, CancellationToken cancellationToken) =>
            Task.FromResult(projects);

        public Task<string> GetUserIdAsync(string? username, string? apiKey, CancellationToken cancellationToken) =>
            Task.FromResult(string.Empty);

        public Task<Branch> CreateBranchAsync(string projectId, string branchName, string apiKey, CancellationToken cancellationToken) =>
            Task.FromResult(new Branch { Id = "new-branch-id", Name = branchName });
    }

    private sealed class StubLocalCacheService : ILocalCacheService {
        public bool SaveAssetsListCalled { get; private set; }
        public Queue<ResourceDownloadResult> FileDownloadResults { get; } = new();

        public Task DownloadMaterialAsync(MaterialResource materialResource, Func<CancellationToken, Task<JObject>> fetchMaterialJsonAsync, CancellationToken cancellationToken) {
            materialResource.Status = "Downloaded";
            return Task.CompletedTask;
        }

        public Task<ResourceDownloadResult> DownloadFileAsync(BaseResource resource, string apiKey, CancellationToken cancellationToken) {
            if (FileDownloadResults.TryDequeue(out ResourceDownloadResult? result)) {
                return Task.FromResult(result);
            }

            return Task.FromResult(new ResourceDownloadResult(true, "Downloaded", 1));
        }

        public string GetResourcePath(string projectsRoot, string projectName, IReadOnlyDictionary<int, string> folderPaths, string? fileName, int? parentId) {
            return Path.Combine(projectsRoot, projectName, LocalCacheService.AssetsDirectoryName, folderPaths.TryGetValue(parentId ?? 0, out string? folder) ? folder : string.Empty, fileName ?? string.Empty);
        }

        public Task<JArray?> LoadAssetsListAsync(string projectFolderPath, CancellationToken cancellationToken) => Task.FromResult<JArray?>(null);

        public Task SaveAssetsListAsync(JToken jsonResponse, string projectFolderPath, CancellationToken cancellationToken) {
            SaveAssetsListCalled = true;
            return Task.CompletedTask;
        }

        public string SanitizePath(string? path) => path ?? string.Empty;
    }

    private sealed class TestLogService : ILogService {
        public void LogError(string? message) { }
        public void LogInfo(string message) { }
        public void LogWarn(string message) { }
        public void LogDebug(string message) { }
    }

    private sealed class StubAssetResourceService : IAssetResourceService {
        public void BuildFolderHierarchy(JArray assetsResponse, IDictionary<int, string> targetFolderPaths) {
            // Simulate building folder hierarchy from assets
            foreach (var asset in assetsResponse) {
                if (asset["type"]?.ToString() == "folder") {
                    int? id = asset["id"]?.Type == JTokenType.Integer ? (int?)asset["id"] : null;
                    string? name = asset["name"]?.ToString();
                    if (id.HasValue && !string.IsNullOrEmpty(name)) {
                        targetFolderPaths[id.Value] = name;
                    }
                }
            }
        }

        public Task<AssetProcessingResult?> ProcessAssetAsync(JToken asset, AssetProcessingParameters parameters, CancellationToken cancellationToken) =>
            Task.FromResult<AssetProcessingResult?>(null);

        public Task<MaterialResource?> LoadMaterialFromFileAsync(string filePath, CancellationToken cancellationToken) =>
            Task.FromResult<MaterialResource?>(null);

        public Task SaveAssetsListAsync(JToken jsonResponse, string projectFolderPath, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
