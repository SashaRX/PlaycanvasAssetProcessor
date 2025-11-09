using AssetProcessor.Resources;
using AssetProcessor.Services;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AssetProcessor.Tests;

public class ProjectAssetServiceTests {
    [Fact]
    public void BuildFolderHierarchy_CleansFolderNames() {
        var service = CreateService();
        JArray assets = JArray.Parse("""
            [
                {"id":1,"type":"folder","name":" Root \n","parent":0},
                {"id":2,"type":"folder","name":"Child","parent":1}
            ]
            """);

        Dictionary<int, string> result = service.BuildFolderHierarchy(assets);
        Assert.Equal("Root", result[1]);
        Assert.Equal(Path.Combine("Root", "Child"), result[2]);
    }

    [Fact]
    public async Task ProcessAssetsAsync_ReturnsSanitizedResources() {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);
        try {
            var localCacheService = new LocalCacheService();
            var queueService = new AssetQueueService(4, 2);
            var playCanvasService = new FakePlayCanvasService();
            var service = new ProjectAssetService(playCanvasService, localCacheService, queueService);

            JArray assets = JArray.Parse("""
                [
                    {
                        "id": 1,
                        "type": "texture",
                        "name": "Texture\n.png",
                        "parent": 0,
                        "file": {"url": "https://example.com/texture.png?cache=1", "size": "100", "hash": ""}
                    },
                    {
                        "id": 2,
                        "type": "scene",
                        "name": "Model.fbx",
                        "parent": 0,
                        "file": {"url": "https://example.com/model.fbx", "size": "200", "hash": ""}
                    },
                    {
                        "id": 3,
                        "type": "material",
                        "name": "Material",
                        "parent": 0
                    }
                ]
                """);

            Dictionary<int, string> folders = service.BuildFolderHierarchy(assets);
            ProjectAssetsResult result = await service.ProcessAssetsAsync(
                assets,
                root,
                "Project",
                folders,
                "api-key",
                CancellationToken.None,
                progress: null,
                fetchTextureResolution: false);

            Assert.Single(result.Textures);
            TextureResource texture = result.Textures.Single();
            Assert.Equal("Texture", texture.Name);
            Assert.True(texture.Path.EndsWith(Path.Combine("Project", "Texture.png")));

            Assert.Single(result.Models);
            ModelResource model = result.Models.Single();
            Assert.Equal("Model", model.Name);
            Assert.True(model.Path.EndsWith(Path.Combine("Project", "Model.fbx")));

            Assert.Single(result.Materials);
            MaterialResource material = result.Materials.Single();
            Assert.Equal("Material", material.Name);
            Assert.True(material.Path.EndsWith(Path.Combine("Project", "Material.json")));
        } finally {
            if (Directory.Exists(root)) {
                Directory.Delete(root, true);
            }
        }
    }

    private static ProjectAssetService CreateService() {
        return new ProjectAssetService(new FakePlayCanvasService(), new LocalCacheService(), new AssetQueueService(2, 2));
    }

    private sealed class FakePlayCanvasService : IPlayCanvasService {
        public Task<JObject> GetAssetByIdAsync(string assetId, string apiKey, CancellationToken cancellationToken) => Task.FromResult(new JObject { ["id"] = assetId });

        public Task<JArray> GetAssetsAsync(string projectId, string branchId, string apiKey, CancellationToken cancellationToken) => Task.FromResult(new JArray());

        public Task<List<Branch>> GetBranchesAsync(string projectId, string apiKey, List<Branch> branches, CancellationToken cancellationToken) => Task.FromResult(new List<Branch>());

        public Task<Dictionary<string, string>> GetProjectsAsync(string? userId, string? apiKey, Dictionary<string, string> projects, CancellationToken cancellationToken) => Task.FromResult(new Dictionary<string, string>());

        public Task<string> GetUserIdAsync(string? username, string? apiKey, CancellationToken cancellationToken) => Task.FromResult(string.Empty);
    }
}
