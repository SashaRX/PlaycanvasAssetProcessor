using AssetProcessor.Services;
using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AssetProcessor.Tests.Services;

public class DownloadServiceTests {
    [Fact]
    public async Task DownloadFileAsync_SavesContentToFile() {
        MockFileSystem fileSystem = new();
        TestLogService logService = new();
        HttpMessageHandler handler = new SuccessMessageHandler("test");
        DownloadService service = new(new StubHttpClientFactory(handler), fileSystem, logService);

        bool result = await service.DownloadFileAsync("https://example.com/file", "output.bin", CancellationToken.None);

        Assert.True(result);
        Assert.True(fileSystem.FileExists("output.bin"));
        Assert.Equal("test", fileSystem.File.ReadAllText("output.bin"));
        Assert.Null(logService.LastError);
    }

    [Fact]
    public async Task DownloadFileAsync_ReturnsFalseOnFailure() {
        MockFileSystem fileSystem = new();
        TestLogService logService = new();
        HttpMessageHandler handler = new FailureMessageHandler();
        DownloadService service = new(new StubHttpClientFactory(handler), fileSystem, logService);

        bool result = await service.DownloadFileAsync("https://example.com/file", "output.bin", CancellationToken.None);

        Assert.False(result);
        Assert.False(fileSystem.FileExists("output.bin"));
        Assert.NotNull(logService.LastError);
        Assert.Contains("Error downloading file", logService.LastError);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory {
        private readonly HttpClient client;

        public StubHttpClientFactory(HttpMessageHandler handler) {
            client = new HttpClient(handler, disposeHandler: true);
        }

        public HttpClient CreateClient(string name) => client;
    }

    private sealed class SuccessMessageHandler : HttpMessageHandler {
        private readonly string content;

        public SuccessMessageHandler(string content) {
            this.content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            HttpResponseMessage response = new(HttpStatusCode.OK) {
                Content = new StringContent(content)
            };
            return Task.FromResult(response);
        }
    }

    private sealed class FailureMessageHandler : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            HttpResponseMessage response = new(HttpStatusCode.InternalServerError);
            return Task.FromResult(response);
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
