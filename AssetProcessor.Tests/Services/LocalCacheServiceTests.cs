using AssetProcessor.Resources;
using AssetProcessor.Services;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AssetProcessor.Tests.Services;

public class LocalCacheServiceTests {
    [Fact]
    public void SanitizePath_RemovesNewLinesAndTrims() {
        LocalCacheService service = CreateService(out _);
        string sanitized = service.SanitizePath("  sample\n");
        Assert.Equal("sample", sanitized);
    }

    [Fact]
    public void GetResourcePath_CreatesDirectoriesAndBuildsPath() {
        LocalCacheService service = CreateService(out MockFileSystem fileSystem);
        string root = "c:/projects";
        string projectName = "TestProject";
        Dictionary<int, string> folders = new() { [1] = "Textures" };

        string result = service.GetResourcePath(root, projectName, folders, "file.png", 1);

        Assert.Equal("c:/projects/TestProject/Textures/file.png".Replace('/', fileSystem.Path.DirectorySeparatorChar), result.Replace('/', fileSystem.Path.DirectorySeparatorChar));
        Assert.True(fileSystem.Directory.Exists("c:/projects/TestProject/Textures".Replace('/', fileSystem.Path.DirectorySeparatorChar)));
    }

    [Fact]
    public async Task SaveAndLoadAssetsListAsync_PersistsJson() {
        LocalCacheService service = CreateService(out MockFileSystem fileSystem);
        string projectFolder = "c:/projects/TestProject";

        JArray json = new() { new JObject { ["id"] = 1 } };
        await service.SaveAssetsListAsync(json, projectFolder, CancellationToken.None);

        JArray? loaded = await service.LoadAssetsListAsync(projectFolder, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Single(loaded);
        Assert.True(fileSystem.File.Exists("c:/projects/TestProject/assets_list.json".Replace('/', fileSystem.Path.DirectorySeparatorChar)));
    }

    [Fact]
    public async Task DownloadMaterialAsync_WritesJsonFile() {
        LocalCacheService service = CreateService(out MockFileSystem fileSystem);
        string root = "c:/materials";
        fileSystem.Directory.CreateDirectory(root);

        MaterialResource material = new() {
            Name = "Material",
            Path = "c:/materials/Material.json"
        };

        await service.DownloadMaterialAsync(
            material,
            _ => Task.FromResult(new JObject { ["name"] = "Material" }),
            CancellationToken.None);

        Assert.Equal("Downloaded", material.Status);
        Assert.True(fileSystem.File.Exists("c:/materials/Material.json"));
    }

    [Fact]
    public async Task DownloadFileAsync_ReturnsDownloadedStatus_WhenHashMatches() {
        const string content = "texture-data";
        string hash = ComputeHash(content);

        BaseResource resource = new TextureResource {
            Url = "https://example.com/file",
            Path = "c:/cache/file.bin",
            Hash = hash,
            Size = Encoding.UTF8.GetBytes(content).Length
        };

        SuccessAfterRetriesHandler handler = new(successAttempt: 1, contentBytes: Encoding.UTF8.GetBytes(content));
        LocalCacheService service = CreateService(handler, out MockFileSystem fileSystem);
        fileSystem.Directory.CreateDirectory("c:/cache");

        ResourceDownloadResult result = await service.DownloadFileAsync(resource, "api", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Downloaded", resource.Status);
        Assert.Equal(1, result.Attempts);
        Assert.True(fileSystem.File.Exists(resource.Path!));
    }

    [Fact]
    public async Task DownloadFileAsync_RetriesUntilSuccess() {
        byte[] content = Encoding.UTF8.GetBytes("retry");
        SuccessAfterRetriesHandler handler = new(successAttempt: 3, contentBytes: content);
        LocalCacheService service = CreateService(handler, out MockFileSystem fileSystem);
        fileSystem.Directory.CreateDirectory("c:/cache");

        BaseResource resource = new TextureResource {
            Url = "https://example.com/file",
            Path = "c:/cache/file.bin"
        };

        ResourceDownloadResult result = await service.DownloadFileAsync(resource, "api", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Attempts);
        Assert.Equal("Downloaded", resource.Status);
        Assert.True(fileSystem.File.Exists(resource.Path!));
    }

    [Fact]
    public async Task DownloadFileAsync_ReturnsSizeMismatch_WhenOutsideTolerance() {
        byte[] content = new byte[100];
        SuccessAfterRetriesHandler handler = new(successAttempt: 1, contentBytes: content);
        LocalCacheService service = CreateService(handler, out MockFileSystem fileSystem);
        fileSystem.Directory.CreateDirectory("c:/cache");

        BaseResource resource = new TextureResource {
            Url = "https://example.com/file",
            Path = "c:/cache/file.bin",
            Size = 10 // Intentionally wrong size
        };

        ResourceDownloadResult result = await service.DownloadFileAsync(resource, "api", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Size Mismatch", resource.Status);
    }

    [Fact]
    public async Task DownloadFileAsync_ReturnsError_WhenAllRetriesFail() {
        FailureMessageHandler handler = new();
        LocalCacheService service = CreateService(handler, out MockFileSystem fileSystem);
        fileSystem.Directory.CreateDirectory("c:/cache");

        BaseResource resource = new TextureResource {
            Url = "https://example.com/file",
            Path = "c:/cache/file.bin"
        };

        ResourceDownloadResult result = await service.DownloadFileAsync(resource, "api", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Error", resource.Status);
        Assert.Equal(5, result.Attempts);
    }

    private static LocalCacheService CreateService(out MockFileSystem fileSystem) {
        return CreateService(handler: null, out fileSystem);
    }

    private static LocalCacheService CreateService(HttpMessageHandler? handler, out MockFileSystem fileSystem) {
        fileSystem = new MockFileSystem();
        handler ??= new SuccessAfterRetriesHandler(1, Encoding.UTF8.GetBytes("default"));
        StubHttpClientFactory factory = new(handler);
        TestLogService logService = new();
        return new LocalCacheService(factory, fileSystem, logService);
    }

    private static string ComputeHash(string content) {
        using MD5 md5 = MD5.Create();
        byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexStringLower(hash);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory {
        private readonly HttpClient client;

        public StubHttpClientFactory(HttpMessageHandler handler) {
            client = new HttpClient(handler, disposeHandler: true);
        }

        public HttpClient CreateClient(string name) => client;
    }

    private sealed class SuccessAfterRetriesHandler : HttpMessageHandler {
        private readonly int successAttempt;
        private readonly byte[] contentBytes;
        private int currentAttempt;

        public SuccessAfterRetriesHandler(int successAttempt, byte[] contentBytes) {
            this.successAttempt = successAttempt;
            this.contentBytes = contentBytes;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            currentAttempt++;
            if (currentAttempt < successAttempt) {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }

            HttpResponseMessage response = new(HttpStatusCode.OK) {
                Content = new ByteArrayContent(contentBytes)
            };
            response.Content.Headers.ContentLength = contentBytes.Length;
            return Task.FromResult(response);
        }
    }

    private sealed class FailureMessageHandler : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }

    private sealed class TestLogService : ILogService {
        public string? LastError { get; private set; }

        public void LogError(string? message) {
            LastError = message;
        }

        public void LogInfo(string message) { }

        public void LogWarn(string message) { }
    }
}
