using AssetProcessor.Resources;
// cspell:ignore Newtonsoft
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Abstractions;
using System.Security.Cryptography;

namespace AssetProcessor.Services;

public class LocalCacheService(IHttpClientFactory httpClientFactory, IFileSystem fileSystem, ILogService logService) : ILocalCacheService {
    public const string AssetsDirectoryName = "assets";

    private readonly IHttpClientFactory httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    private readonly IFileSystem fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly ILogService logService = logService ?? throw new ArgumentNullException(nameof(logService));

    private static string GetAssetsListPath(string projectFolderPath) => Path.Combine(projectFolderPath, "assets_list.json");

    public string SanitizePath(string? path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return string.Empty;
        }

        return path
            .Replace("\r", string.Empty)
            .Replace("\n", string.Empty)
            .Trim();
    }

    public string GetResourcePath(string projectsRoot, string projectName, IReadOnlyDictionary<int, string> folderPaths, string? fileName, int? parentId) {
        ArgumentException.ThrowIfNullOrEmpty(projectsRoot);
        ArgumentException.ThrowIfNullOrEmpty(projectName);

        string assetsFolder = Path.Combine(projectsRoot, projectName, AssetsDirectoryName);
        string targetFolder = assetsFolder;

        if (parentId.HasValue && folderPaths.TryGetValue(parentId.Value, out string? folderPath) && !string.IsNullOrEmpty(folderPath)) {
            targetFolder = Path.Combine(targetFolder, folderPath);
        }

        if (!fileSystem.Directory.Exists(targetFolder)) {
            fileSystem.Directory.CreateDirectory(targetFolder);
        }

        return Path.Combine(targetFolder, fileName ?? "Unknown");
    }

    public async Task SaveAssetsListAsync(JToken jsonResponse, string projectFolderPath, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(jsonResponse);
        ArgumentException.ThrowIfNullOrEmpty(projectFolderPath);

        string jsonFilePath = GetAssetsListPath(projectFolderPath);

        if (!fileSystem.Directory.Exists(projectFolderPath)) {
            fileSystem.Directory.CreateDirectory(projectFolderPath);
        }

        string jsonString = jsonResponse.ToString(Formatting.Indented);
        await WriteTextAsync(jsonFilePath, jsonString, cancellationToken).ConfigureAwait(false);
        logService.LogInfo($"Assets list saved to {jsonFilePath}");
    }

    public async Task<JArray?> LoadAssetsListAsync(string projectFolderPath, CancellationToken cancellationToken) {
        ArgumentException.ThrowIfNullOrEmpty(projectFolderPath);

        string jsonFilePath = GetAssetsListPath(projectFolderPath);
        if (!fileSystem.File.Exists(jsonFilePath)) {
            return null;
        }

        using Stream stream = fileSystem.File.OpenRead(jsonFilePath);
        using StreamReader reader = new(stream);
        string jsonContent = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(jsonContent) ? null : JArray.Parse(jsonContent);
    }

    public async Task DownloadMaterialAsync(MaterialResource materialResource, Func<CancellationToken, Task<JObject>> fetchMaterialJsonAsync, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(materialResource);
        ArgumentNullException.ThrowIfNull(fetchMaterialJsonAsync);

        string? directoryPath = Path.GetDirectoryName(materialResource.Path);
        if (string.IsNullOrEmpty(directoryPath)) {
            throw new InvalidOperationException("Material path must have a directory.");
        }

        fileSystem.Directory.CreateDirectory(directoryPath);
        JObject materialJson = await fetchMaterialJsonAsync(cancellationToken).ConfigureAwait(false);

        string materialPath = Path.Combine(directoryPath, $"{materialResource.Name}.json");
        await WriteTextAsync(materialPath, materialJson.ToString(), cancellationToken).ConfigureAwait(false);
        materialResource.Status = "Downloaded";
    }

    public async Task<ResourceDownloadResult> DownloadFileAsync(BaseResource resource, string apiKey, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrEmpty(apiKey);

        if (string.IsNullOrEmpty(resource.Path)) {
            return new ResourceDownloadResult(false, "Error", 0, "Resource path is missing");
        }

        const int maxRetries = 5;
        const int delayMilliseconds = 2000;

        for (int attempt = 1; attempt <= maxRetries; attempt++) {
            try {
                HttpClient client = httpClientFactory.CreateClient("Downloads");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                resource.Status = "Downloading";
                resource.DownloadProgress = 0;

                using HttpResponseMessage response = await client.GetAsync(resource.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) {
                    throw new HttpRequestException($"Failed to download resource: {response.StatusCode}");
                }

                long totalBytes = response.Content.Headers.ContentLength ?? 0L;
                byte[] buffer = new byte[8192];

                await using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                string? directoryPath = Path.GetDirectoryName(resource.Path);
                if (!string.IsNullOrEmpty(directoryPath)) {
                    fileSystem.Directory.CreateDirectory(directoryPath);
                }

                await using (Stream fileStream = await OpenFileStreamWithRetryAsync(resource.Path, cancellationToken).ConfigureAwait(false)) {
                    int bytesRead;
                    while ((bytesRead = await responseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0) {
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                        if (totalBytes > 0) {
                            resource.DownloadProgress = Math.Round((double)fileStream.Position / totalBytes * 100, 2);
                        }
                    }

                    await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                if (!fileSystem.File.Exists(resource.Path)) {
                    string message = $"File was expected but not found: {resource.Path}";
                    resource.Status = "Error";
                    logService.LogError(message);
                    return new ResourceDownloadResult(false, resource.Status, attempt, message);
                }

                string status = await DetermineStatusAsync(resource, totalBytes, cancellationToken).ConfigureAwait(false);
                resource.Status = status;
                resource.DownloadProgress = 100;
                return new ResourceDownloadResult(string.Equals(status, "Downloaded", StringComparison.OrdinalIgnoreCase), status, attempt);
            } catch (IOException ex) {
                if (attempt == maxRetries) {
                    resource.Status = "Error";
                    logService.LogError($"Error downloading resource after {maxRetries} attempts: {ex.Message}");
                    return new ResourceDownloadResult(false, resource.Status, attempt, ex.Message);
                } else {
                    logService.LogError($"Attempt {attempt} failed with IOException: {ex.Message}. Retrying in {delayMilliseconds}ms...");
                    await Task.Delay(delayMilliseconds, cancellationToken).ConfigureAwait(false);
                }
            } catch (Exception ex) when (attempt < maxRetries) {
                logService.LogError($"Attempt {attempt} failed: {ex.Message}. Retrying in {delayMilliseconds}ms...");
                await Task.Delay(delayMilliseconds, cancellationToken).ConfigureAwait(false);
            } catch (Exception ex) {
                resource.Status = "Error";
                logService.LogError($"Error downloading resource: {ex.Message}");
                return new ResourceDownloadResult(false, resource.Status, attempt, ex.Message);
            }
        }

        return new ResourceDownloadResult(false, resource.Status ?? "Error", maxRetries);
    }

    private async Task WriteTextAsync(string path, string content, CancellationToken cancellationToken) {
        await using Stream stream = fileSystem.File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using StreamWriter writer = new(stream);
        await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private async Task<Stream> OpenFileStreamWithRetryAsync(string path, CancellationToken cancellationToken, int maxRetries = 5, int delayMilliseconds = 2000) {
        for (int attempt = 1; attempt <= maxRetries; attempt++) {
            try {
                return fileSystem.File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
            } catch (IOException) when (attempt < maxRetries) {
                await Task.Delay(delayMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new IOException($"Failed to open file after {maxRetries} attempts: {path}");
    }

    private async Task<string> DetermineStatusAsync(BaseResource resource, long totalBytes, CancellationToken cancellationToken) {
        IFileInfo fileInfo = fileSystem.FileInfo.New(resource.Path!);
        long fileSizeInBytes = fileInfo.Length;

        if (fileSizeInBytes == 0) {
            return "Empty File";
        }

        if (!string.IsNullOrEmpty(resource.Hash)) {
            bool hashMatches = await VerifyFileHashAsync(resource.Path!, resource.Hash, cancellationToken).ConfigureAwait(false);
            return hashMatches ? "Downloaded" : "Corrupted";
        }

        const double tolerance = 0.05;

        if (resource.Size > 0) {
            double lowerBound = resource.Size * (1 - tolerance);
            double upperBound = resource.Size * (1 + tolerance);
            return fileSizeInBytes >= lowerBound && fileSizeInBytes <= upperBound ? "Downloaded" : "Size Mismatch";
        }

        if (totalBytes > 0) {
            double lowerBound = totalBytes * (1 - tolerance);
            double upperBound = totalBytes * (1 + tolerance);
            if (fileSizeInBytes >= lowerBound && fileSizeInBytes <= upperBound) {
                resource.Size = (int)totalBytes;
                return "Downloaded";
            }

            return "Size Mismatch";
        }

        resource.Size = (int)fileSizeInBytes;
        return "Downloaded";
    }

    private async Task<bool> VerifyFileHashAsync(string path, string expectedHash, CancellationToken cancellationToken) {
        await using Stream stream = fileSystem.File.OpenRead(path);
        using MD5 md5 = MD5.Create();
        byte[] hash = await md5.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        string fileHash = Convert.ToHexStringLower(hash);
        return fileHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
    }
}
