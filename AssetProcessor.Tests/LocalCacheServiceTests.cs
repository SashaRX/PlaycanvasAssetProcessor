using AssetProcessor.Resources;
using AssetProcessor.Services;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AssetProcessor.Tests;

public class LocalCacheServiceTests {
    [Fact]
    public void SanitizePath_RemovesNewLinesAndTrims() {
        LocalCacheService service = CreateService();
        string sanitized = service.SanitizePath("  sample\n");
        Assert.Equal("sample", sanitized);
    }

    [Fact]
    public void GetResourcePath_CreatesDirectoriesAndBuildsPath() {
        LocalCacheService service = CreateService();
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try {
            Dictionary<int, string> folders = new() { [1] = "Folder" };
            string result = service.GetResourcePath(root, "Project", folders, "file.txt", 1);

            Assert.EndsWith(Path.Combine("Project", "Folder", "file.txt"), result);
            Assert.True(Directory.Exists(Path.GetDirectoryName(result)!));
        } finally {
            if (Directory.Exists(root)) {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task DownloadMaterialAsync_WritesJsonFile() {
        LocalCacheService service = CreateService();
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);
        try {
            MaterialResource material = new() {
                Name = "Material",
                Path = Path.Combine(root, "Material.json")
            };

            await service.DownloadMaterialAsync(
                material,
                _ => Task.FromResult(new JObject { ["name"] = "Material" }),
                CancellationToken.None);

            Assert.Equal("Downloaded", material.Status);
            Assert.True(File.Exists(Path.Combine(root, "Material.json")));
        } finally {
            if (Directory.Exists(root)) {
                Directory.Delete(root, true);
            }
        }
    }

    private static LocalCacheService CreateService() {
        return new LocalCacheService(new StubHttpClientFactory(), new TestLogService());
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class TestLogService : ILogService {
        public void LogError(string? message) { }
        public void LogInfo(string message) { }
        public void LogWarn(string message) { }
    }
}
