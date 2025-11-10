using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AssetProcessor.Exceptions;
using AssetProcessor.Services.Models;
using Polly;
using Polly.Retry;

namespace AssetProcessor.Services {
    public class PlayCanvasService : IPlayCanvasService, IDisposable {
        private const int DefaultPageSize = 200;

        private readonly HttpClient client;
        private readonly bool disposeClient;
        private readonly AsyncPolicy<HttpResponseMessage> retryPolicy;
        private bool disposed;

        public PlayCanvasService(HttpClient? httpClient = null, AsyncPolicy<HttpResponseMessage>? retryPolicy = null, TimeSpan? timeout = null) {
            client = httpClient ?? new HttpClient();
            disposeClient = httpClient is null;
            if (timeout.HasValue) {
                client.Timeout = timeout.Value;
            }

            this.retryPolicy = retryPolicy ?? CreateDefaultRetryPolicy();
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing) {
            if (!disposed) {
                if (disposing && disposeClient) {
                    client.Dispose();
                }

                disposed = true;
            }
        }

        private static AsyncPolicy<HttpResponseMessage> CreateDefaultRetryPolicy() {
            return Policy<HttpResponseMessage>
                .Handle<HttpRequestException>()
                .OrResult(response => (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
                .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)));
        }

        private Task<HttpResponseMessage> GetAsync(string url, CancellationToken cancellationToken) {
            return retryPolicy.ExecuteAsync(ct => client.GetAsync(url, ct), cancellationToken);
        }

        private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, string url, CancellationToken cancellationToken) {
            try {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            } catch (JsonException ex) {
                throw new PlayCanvasApiException($"Invalid JSON returned from '{url}'", url, (int)response.StatusCode, ex);
            }
        }

        private static string ReadIdAsString(JsonElement element, string propertyName, string url) {
            if (!element.TryGetProperty(propertyName, out JsonElement property)) {
                throw new PlayCanvasApiException($"Missing required property '{propertyName}'", url);
            }

            return property.ValueKind switch {
                JsonValueKind.String => property.GetString() ?? throw new PlayCanvasApiException($"Property '{propertyName}' cannot be null", url),
                JsonValueKind.Number => property.TryGetInt64(out long number) ? number.ToString(System.Globalization.CultureInfo.InvariantCulture) : throw new PlayCanvasApiException($"Property '{propertyName}' is not a valid number", url),
                _ => throw new PlayCanvasApiException($"Property '{propertyName}' must be a string or number", url)
            };
        }

        private static int ReadIdAsInt(JsonElement element, string propertyName, string url) {
            if (!element.TryGetProperty(propertyName, out JsonElement property)) {
                throw new PlayCanvasApiException($"Missing required property '{propertyName}'", url);
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int value)) {
                return value;
            }

            if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out int parsed)) {
                return parsed;
            }

            throw new PlayCanvasApiException($"Property '{propertyName}' must be an integer", url);
        }

        private static string ReadRequiredString(JsonElement element, string propertyName, string url) {
            if (!element.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind != JsonValueKind.String) {
                throw new PlayCanvasApiException($"Missing required string property '{propertyName}'", url);
            }

            return property.GetString() ?? throw new PlayCanvasApiException($"Property '{propertyName}' cannot be null", url);
        }

        private static string? ReadOptionalString(JsonElement element, string propertyName) {
            if (!element.TryGetProperty(propertyName, out JsonElement property)) {
                return null;
            }

            return property.ValueKind switch {
                JsonValueKind.Null => null,
                JsonValueKind.String => property.GetString(),
                JsonValueKind.Number => property.TryGetInt64(out long number) ? number.ToString(System.Globalization.CultureInfo.InvariantCulture) : property.GetRawText(),
                _ => property.GetRawText()
            };
        }

        private static int? ReadOptionalInt(JsonElement element, string propertyName) {
            if (!element.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind == JsonValueKind.Null) {
                return null;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int value)) {
                return value;
            }

            if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out int parsed)) {
                return parsed;
            }

            return null;
        }

        private static PlayCanvasAssetFileInfo? ReadAssetFileInfo(JsonElement element) {
            if (!element.TryGetProperty("file", out JsonElement fileElement) || fileElement.ValueKind != JsonValueKind.Object) {
                return null;
            }

            long? size = null;
            if (fileElement.TryGetProperty("size", out JsonElement sizeElement) && sizeElement.ValueKind == JsonValueKind.Number && sizeElement.TryGetInt64(out long rawSize)) {
                size = rawSize;
            }

            string? hash = fileElement.TryGetProperty("hash", out JsonElement hashElement) && hashElement.ValueKind == JsonValueKind.String
                ? hashElement.GetString()
                : null;

            string? filename = fileElement.TryGetProperty("filename", out JsonElement filenameElement) && filenameElement.ValueKind == JsonValueKind.String
                ? filenameElement.GetString()
                : null;

            string? url = fileElement.TryGetProperty("url", out JsonElement urlElement) && urlElement.ValueKind == JsonValueKind.String
                ? urlElement.GetString()
                : null;

            return new PlayCanvasAssetFileInfo(size, hash, filename, url);
        }

        private void AddAuthorizationHeader(string? apiKey) {
            if (string.IsNullOrEmpty(apiKey)) {
                throw new InvalidConfigurationException(
                    "API key is required but was not provided",
                    "PlaycanvasApiKey",
                    apiKey);
            }

            if (client.DefaultRequestHeaders.Contains("Authorization")) {
                client.DefaultRequestHeaders.Remove("Authorization");
            }

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        public async Task<string> GetUserIdAsync(string? username, string? apiKey, CancellationToken cancellationToken) {
            ArgumentException.ThrowIfNullOrEmpty(username);

            string url = $"https://playcanvas.com/api/users/{username}";
            AddAuthorizationHeader(apiKey);

            try {
                using HttpResponseMessage response = await GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode) {
                    throw new PlayCanvasApiException(
                        $"Failed to get user ID for username '{username}'",
                        url,
                        (int)response.StatusCode);
                }

                using JsonDocument document = await ReadJsonAsync(response, url, cancellationToken);
                string id = ReadIdAsString(document.RootElement, "id", url);
                return id;
            } catch (HttpRequestException ex) {
                throw new NetworkException(
                    $"Network error while fetching user ID for '{username}'",
                    url,
                    0,
                    ex);
            }
        }

        public async Task<Dictionary<string, string>> GetProjectsAsync(string? userId, string? apiKey, Dictionary<string, string> projects, CancellationToken cancellationToken) {
            ArgumentException.ThrowIfNullOrEmpty(userId);

            string url = $"https://playcanvas.com/api/users/{userId}/projects";
            AddAuthorizationHeader(apiKey);

            try {
                using HttpResponseMessage response = await GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode) {
                    throw new PlayCanvasApiException(
                        $"Failed to get projects for user ID '{userId}'",
                        url,
                        (int)response.StatusCode);
                }

                using JsonDocument document = await ReadJsonAsync(response, url, cancellationToken);
                if (!document.RootElement.TryGetProperty("result", out JsonElement resultElement) || resultElement.ValueKind != JsonValueKind.Array) {
                    throw new PlayCanvasApiException("Projects array is null in API response", url);
                }

                foreach (JsonElement project in resultElement.EnumerateArray()) {
                    string id = ReadIdAsString(project, "id", url);
                    string name = ReadRequiredString(project, "name", url);
                    projects[id] = name;
                }

                return projects;
            } catch (HttpRequestException ex) {
                throw new NetworkException(
                    $"Network error while fetching projects for user ID '{userId}'",
                    url,
                    0,
                    ex);
            }
        }

        public async Task<List<Branch>> GetBranchesAsync(string projectId, string apiKey, List<Branch> branches, CancellationToken cancellationToken) {
            ArgumentException.ThrowIfNullOrEmpty(projectId);

            string url = $"https://playcanvas.com/api/projects/{projectId}/branches";
            AddAuthorizationHeader(apiKey);

            try {
                using HttpResponseMessage response = await GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode) {
                    throw new PlayCanvasApiException(
                        $"Failed to get branches for project ID '{projectId}'",
                        url,
                        (int)response.StatusCode);
                }

                using JsonDocument document = await ReadJsonAsync(response, url, cancellationToken);
                if (!document.RootElement.TryGetProperty("result", out JsonElement resultElement) || resultElement.ValueKind != JsonValueKind.Array) {
                    throw new PlayCanvasApiException("Branches array is null in API response", url);
                }

                foreach (JsonElement branch in resultElement.EnumerateArray()) {
                    string id = ReadIdAsString(branch, "id", url);
                    string name = ReadRequiredString(branch, "name", url);
                    branches.Add(new Branch { Id = id, Name = name });
                }

                return branches;
            } catch (HttpRequestException ex) {
                throw new NetworkException(
                    $"Network error while fetching branches for project ID '{projectId}'",
                    url,
                    0,
                    ex);
            }
        }

        public async IAsyncEnumerable<PlayCanvasAssetSummary> GetAssetsAsync(string projectId, string branchId, string apiKey, [EnumeratorCancellation] CancellationToken cancellationToken) {
            ArgumentException.ThrowIfNullOrEmpty(projectId);
            ArgumentException.ThrowIfNullOrEmpty(branchId);

            AddAuthorizationHeader(apiKey);

            int skip = 0;
            while (true) {
                string url = $"https://playcanvas.com/api/projects/{projectId}/assets?branch={branchId}&skip={skip}&limit={DefaultPageSize}";

                JsonElement resultElement;
                int itemsCount;

                try {
                    using HttpResponseMessage response = await GetAsync(url, cancellationToken);
                    if (!response.IsSuccessStatusCode) {
                        throw new PlayCanvasApiException(
                            $"Failed to get assets for project ID '{projectId}' and branch ID '{branchId}'",
                            url,
                            (int)response.StatusCode);
                    }

                    using JsonDocument document = await ReadJsonAsync(response, url, cancellationToken);
                    if (!document.RootElement.TryGetProperty("result", out resultElement) || resultElement.ValueKind != JsonValueKind.Array) {
                        throw new PlayCanvasApiException("Assets array is null in API response", url);
                    }

                    itemsCount = 0;
                    foreach (JsonElement assetElement in resultElement.EnumerateArray()) {
                        cancellationToken.ThrowIfCancellationRequested();
                        PlayCanvasAssetSummary asset = ParseAsset(assetElement, url);
                        itemsCount++;
                        yield return asset;
                    }
                } catch (HttpRequestException ex) {
                    throw new NetworkException(
                        $"Network error while fetching assets for project ID '{projectId}'",
                        $"https://playcanvas.com/api/projects/{projectId}/assets",
                        0,
                        ex);
                }

                if (itemsCount < DefaultPageSize) {
                    yield break;
                }

                skip += itemsCount;
            }
        }

        public async Task<PlayCanvasAssetDetail> GetAssetByIdAsync(string assetId, string apiKey, CancellationToken cancellationToken) {
            ArgumentException.ThrowIfNullOrEmpty(assetId);

            string url = $"https://playcanvas.com/api/assets/{assetId}";
            AddAuthorizationHeader(apiKey);

            try {
                using HttpResponseMessage response = await GetAsync(url, cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound) {
                    throw new AssetNotFoundException(
                        $"Asset with ID '{assetId}' was not found",
                        assetId);
                }

                if (!response.IsSuccessStatusCode) {
                    throw new PlayCanvasApiException(
                        $"Failed to get asset with ID '{assetId}'",
                        url,
                        (int)response.StatusCode);
                }

                using JsonDocument document = await ReadJsonAsync(response, url, cancellationToken);
                JsonElement root = document.RootElement;
                int id = ReadIdAsInt(root, "id", url);
                string type = ReadRequiredString(root, "type", url);
                string? name = ReadOptionalString(root, "name");
                return new PlayCanvasAssetDetail(id, type, name, root.Clone());
            } catch (HttpRequestException ex) {
                throw new NetworkException(
                    $"Network error while fetching asset with ID '{assetId}'",
                    url,
                    0,
                    ex);
            }
        }

        private static PlayCanvasAssetSummary ParseAsset(JsonElement element, string url) {
            int id = ReadIdAsInt(element, "id", url);
            string type = ReadRequiredString(element, "type", url);
            string? name = ReadOptionalString(element, "name");
            string? path = ReadOptionalString(element, "path");
            int? parent = ReadOptionalInt(element, "parent");
            PlayCanvasAssetFileInfo? file = ReadAssetFileInfo(element);
            return new PlayCanvasAssetSummary(id, type, name, path, parent, file, element.Clone());
        }
    }

    public class Branch {
        public required string Id { get; set; }
        public required string Name { get; set; }
    }
}
