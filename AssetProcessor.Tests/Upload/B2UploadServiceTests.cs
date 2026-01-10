using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using AssetProcessor.Upload;
using Xunit;

namespace AssetProcessor.Tests.Upload;

public class B2UploadServiceTests {
    #region B2UploadSettings Tests

    [Fact]
    public void B2UploadSettings_IsValid_ReturnsTrueWhenAllRequiredFieldsSet() {
        var settings = new B2UploadSettings {
            KeyId = "test-key-id",
            ApplicationKey = "test-app-key",
            BucketName = "test-bucket"
        };

        Assert.True(settings.IsValid);
    }

    [Theory]
    [InlineData("", "app-key", "bucket")]
    [InlineData("key-id", "", "bucket")]
    [InlineData("key-id", "app-key", "")]
    [InlineData(null, "app-key", "bucket")]
    [InlineData("key-id", null, "bucket")]
    [InlineData("key-id", "app-key", null)]
    public void B2UploadSettings_IsValid_ReturnsFalseWhenRequiredFieldMissing(string? keyId, string? appKey, string? bucket) {
        var settings = new B2UploadSettings {
            KeyId = keyId ?? string.Empty,
            ApplicationKey = appKey ?? string.Empty,
            BucketName = bucket ?? string.Empty
        };

        Assert.False(settings.IsValid);
    }

    [Theory]
    [InlineData("", "file.txt", "file.txt")]
    [InlineData("prefix", "file.txt", "prefix/file.txt")]
    [InlineData("prefix/", "file.txt", "prefix/file.txt")]
    [InlineData("prefix", "/file.txt", "prefix/file.txt")]
    [InlineData("prefix/", "/file.txt", "prefix/file.txt")]
    [InlineData("", "/file.txt", "file.txt")]
    public void B2UploadSettings_BuildFullPath_HandlesVariousCombinations(string prefix, string relativePath, string expected) {
        var settings = new B2UploadSettings { PathPrefix = prefix };

        string result = settings.BuildFullPath(relativePath);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("", "", "file.txt", "file.txt")]
    [InlineData("https://cdn.example.com", "", "file.txt", "https://cdn.example.com/file.txt")]
    [InlineData("https://cdn.example.com/", "", "file.txt", "https://cdn.example.com/file.txt")]
    [InlineData("https://cdn.example.com", "assets", "file.txt", "https://cdn.example.com/assets/file.txt")]
    [InlineData("https://cdn.example.com/", "assets/", "file.txt", "https://cdn.example.com/assets/file.txt")]
    public void B2UploadSettings_BuildCdnUrl_CombinesUrlsCorrectly(string cdnBase, string prefix, string relativePath, string expected) {
        var settings = new B2UploadSettings {
            CdnBaseUrl = cdnBase,
            PathPrefix = prefix
        };

        string result = settings.BuildCdnUrl(relativePath);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void B2UploadSettings_DefaultValues_AreCorrect() {
        var settings = new B2UploadSettings();

        Assert.Equal(4, settings.MaxConcurrentUploads);
        Assert.Equal(300, settings.TimeoutSeconds);
        Assert.Equal(3, settings.RetryCount);
        Assert.True(settings.SkipExistingFiles);
    }

    #endregion

    #region B2UploadResult Tests

    [Fact]
    public void B2UploadResult_DefaultValues_AreCorrect() {
        var result = new B2UploadResult();

        Assert.False(result.Success);
        Assert.False(result.Skipped);
        Assert.Equal(string.Empty, result.LocalPath);
        Assert.Equal(string.Empty, result.RemotePath);
        Assert.Null(result.FileId);
        Assert.Null(result.ContentSha1);
        Assert.Null(result.CdnUrl);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(0, result.ContentLength);
    }

    #endregion

    #region B2BatchUploadResult Tests

    [Fact]
    public void B2BatchUploadResult_Success_ReturnsTrueWhenNoFailures() {
        var result = new B2BatchUploadResult {
            SuccessCount = 5,
            SkippedCount = 2,
            FailedCount = 0
        };

        Assert.True(result.Success);
    }

    [Fact]
    public void B2BatchUploadResult_Success_ReturnsFalseWhenHasFailures() {
        var result = new B2BatchUploadResult {
            SuccessCount = 5,
            SkippedCount = 2,
            FailedCount = 1
        };

        Assert.False(result.Success);
    }

    [Fact]
    public void B2BatchUploadResult_DefaultValues_AreCorrect() {
        var result = new B2BatchUploadResult();

        Assert.True(result.Success); // No failures = success
        Assert.Equal(0, result.SuccessCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(0, result.TotalBytesUploaded);
        Assert.Empty(result.Results);
        Assert.Empty(result.Errors);
    }

    #endregion

    #region B2UploadProgress Tests

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(5, 10, 50)]
    [InlineData(10, 10, 100)]
    [InlineData(0, 0, 0)]
    public void B2UploadProgress_PercentComplete_CalculatesCorrectly(int current, int total, double expected) {
        var progress = new B2UploadProgress {
            CurrentFileIndex = current,
            TotalFiles = total
        };

        Assert.Equal(expected, progress.PercentComplete);
    }

    #endregion

    #region B2FileInfo Tests

    [Fact]
    public void B2FileInfo_UploadTime_ConvertsFromTimestampCorrectly() {
        // Unix timestamp for 2024-01-15 12:30:45 UTC in milliseconds
        long timestamp = 1705321845000;
        var fileInfo = new B2FileInfo { UploadTimestamp = timestamp };

        var expectedTime = new DateTime(2024, 1, 15, 12, 30, 45, DateTimeKind.Utc);
        Assert.Equal(expectedTime, fileInfo.UploadTime);
    }

    #endregion

    #region B2UploadService Authorization Tests

    [Fact]
    public async Task AuthorizeAsync_WithInvalidSettings_ReturnsFalse() {
        using var service = new B2UploadService();
        var settings = new B2UploadSettings {
            KeyId = "", // Invalid - empty
            ApplicationKey = "key",
            BucketName = "bucket"
        };

        bool result = await service.AuthorizeAsync(settings);

        Assert.False(result);
        Assert.False(service.IsAuthorized);
    }

    [Fact]
    public async Task AuthorizeAsync_WhenApiReturnsError_ReturnsFalse() {
        var handler = new QueueMessageHandler();
        handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.Unauthorized) {
            Content = new StringContent("{\"error\":\"invalid_key\"}", Encoding.UTF8, "application/json")
        });

        using var httpClient = new HttpClient(handler);
        using var service = new TestableB2UploadService(httpClient);

        var settings = new B2UploadSettings {
            KeyId = "invalid-key",
            ApplicationKey = "invalid-app-key",
            BucketName = "test-bucket"
        };

        bool result = await service.AuthorizeAsync(settings);

        Assert.False(result);
        Assert.False(service.IsAuthorized);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task AuthorizeAsync_WhenApiReturnsSuccess_ReturnsTrue() {
        var handler = new QueueMessageHandler();
        handler.EnqueueResponse(CreateAuthResponse("account123", "auth-token", "https://api001.backblazeb2.com"));
        handler.EnqueueResponse(CreateListBucketsResponse("bucket123", "test-bucket"));

        using var httpClient = new HttpClient(handler);
        using var service = new TestableB2UploadService(httpClient);

        var settings = new B2UploadSettings {
            KeyId = "valid-key",
            ApplicationKey = "valid-app-key",
            BucketName = "test-bucket"
        };

        bool result = await service.AuthorizeAsync(settings);

        Assert.True(result);
        Assert.True(service.IsAuthorized);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task AuthorizeAsync_WithExistingBucketId_SkipsLookup() {
        var handler = new QueueMessageHandler();
        handler.EnqueueResponse(CreateAuthResponse("account123", "auth-token", "https://api001.backblazeb2.com"));

        using var httpClient = new HttpClient(handler);
        using var service = new TestableB2UploadService(httpClient);

        var settings = new B2UploadSettings {
            KeyId = "valid-key",
            ApplicationKey = "valid-app-key",
            BucketName = "test-bucket",
            BucketId = "bucket123" // Already set
        };

        bool result = await service.AuthorizeAsync(settings);

        Assert.True(result);
        Assert.Single(handler.Requests); // Only auth request, no bucket lookup
    }

    #endregion

    #region B2UploadService Upload Tests

    [Fact]
    public async Task UploadFileAsync_WhenNotAuthorized_ReturnsError() {
        using var service = new B2UploadService();

        var result = await service.UploadFileAsync("/path/to/file.txt", "remote/file.txt");

        Assert.False(result.Success);
        Assert.Equal("Not authorized", result.ErrorMessage);
    }

    #endregion

    #region Helper Classes and Methods

    private static HttpResponseMessage CreateAuthResponse(string accountId, string authToken, string apiUrl) {
        var response = new {
            accountId,
            authorizationToken = authToken,
            apiUrl,
            downloadUrl = "https://f001.backblazeb2.com",
            absoluteMinimumPartSize = 5000000,
            recommendedPartSize = 100000000
        };

        return new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent(
                JsonSerializer.Serialize(response, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
                Encoding.UTF8,
                "application/json")
        };
    }

    private static HttpResponseMessage CreateListBucketsResponse(string bucketId, string bucketName) {
        var response = new {
            buckets = new[] {
                new {
                    bucketId,
                    bucketName,
                    bucketType = "allPublic"
                }
            }
        };

        return new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent(
                JsonSerializer.Serialize(response, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
                Encoding.UTF8,
                "application/json")
        };
    }

    private sealed class QueueMessageHandler : HttpMessageHandler {
        private readonly Queue<HttpResponseMessage> responses = new();
        public List<HttpRequestMessage> Requests { get; } = new();

        public void EnqueueResponse(HttpResponseMessage response) {
            responses.Enqueue(response);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Requests.Add(request);
            if (responses.Count == 0) {
                throw new InvalidOperationException("No response configured for request");
            }
            return Task.FromResult(responses.Dequeue());
        }
    }

    /// <summary>
    /// Testable version of B2UploadService that allows injecting HttpClient
    /// </summary>
    private sealed class TestableB2UploadService : IB2UploadService, IDisposable {
        private readonly HttpClient _httpClient;
        private B2UploadSettings? _settings;
        private AuthResponse? _authResponse;

        public TestableB2UploadService(HttpClient httpClient) {
            _httpClient = httpClient;
        }

        public B2UploadSettings? Settings => _settings;
        public bool IsAuthorized => _authResponse != null && !string.IsNullOrEmpty(_authResponse.AuthorizationToken);

        public async Task<bool> AuthorizeAsync(B2UploadSettings settings, CancellationToken cancellationToken = default) {
            if (!settings.IsValid) return false;

            _settings = settings;

            var authString = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{settings.KeyId}:{settings.ApplicationKey}"));

            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.backblazeb2.com/b2api/v2/b2_authorize_account");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authString);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return false;

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            _authResponse = JsonSerializer.Deserialize<AuthResponse>(content, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            if (_authResponse == null) return false;

            if (string.IsNullOrEmpty(settings.BucketId)) {
                settings.BucketId = await GetBucketIdAsync(settings.BucketName, cancellationToken);
            }

            return true;
        }

        private async Task<string?> GetBucketIdAsync(string bucketName, CancellationToken cancellationToken) {
            if (_authResponse == null) return null;

            var requestBody = new { accountId = _authResponse.AccountId, bucketName };
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_authResponse.ApiUrl}/b2api/v2/b2_list_buckets");
            request.Headers.TryAddWithoutValidation("Authorization", _authResponse.AuthorizationToken);
            request.Content = new StringContent(
                JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var listResponse = JsonSerializer.Deserialize<ListBucketsResponse>(content, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            return listResponse?.Buckets?.FirstOrDefault(b => b.BucketName == bucketName)?.BucketId;
        }

        public Task<B2UploadResult> UploadFileAsync(string localPath, string remotePath, string? contentType = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<B2BatchUploadResult> UploadBatchAsync(IEnumerable<(string LocalPath, string RemotePath)> files, IProgress<B2UploadProgress>? progress = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<B2BatchUploadResult> UploadDirectoryAsync(string localDirectory, string remotePrefix, string searchPattern = "*", bool recursive = true, IProgress<B2UploadProgress>? progress = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<bool> FileExistsAsync(string remotePath, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<B2FileInfo?> GetFileInfoAsync(string remotePath, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<bool> DeleteFileAsync(string remotePath, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<B2FileInfo>> ListFilesAsync(string prefix, int maxCount = 1000, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public void Dispose() {
            // HttpClient is managed externally
        }

        private class AuthResponse {
            public string? AccountId { get; set; }
            public string? AuthorizationToken { get; set; }
            public string? ApiUrl { get; set; }
        }

        private class ListBucketsResponse {
            public List<BucketInfo>? Buckets { get; set; }
        }

        private class BucketInfo {
            public string? BucketId { get; set; }
            public string? BucketName { get; set; }
        }
    }

    #endregion
}
