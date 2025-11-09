using AssetProcessor.Helpers;
using AssetProcessor.Resources;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace AssetProcessor.Services;

public class LocalCacheService {
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

        string projectFolder = Path.Combine(projectsRoot, projectName);
        string targetFolder = projectFolder;

        if (parentId.HasValue && folderPaths.TryGetValue(parentId.Value, out string? folderPath) && !string.IsNullOrEmpty(folderPath)) {
            targetFolder = Path.Combine(targetFolder, folderPath);
        }

        if (!Directory.Exists(targetFolder)) {
            Directory.CreateDirectory(targetFolder);
        }

        return Path.Combine(targetFolder, fileName ?? "Unknown");
    }

    public async Task SaveAssetsListAsync(JToken jsonResponse, string projectFolderPath, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(jsonResponse);
        ArgumentException.ThrowIfNullOrEmpty(projectFolderPath);

        string jsonFilePath = Path.Combine(projectFolderPath, "assets_list.json");

        if (!Directory.Exists(projectFolderPath)) {
            Directory.CreateDirectory(projectFolderPath);
        }

        string jsonString = jsonResponse.ToString(Formatting.Indented);
        await File.WriteAllTextAsync(jsonFilePath, jsonString, cancellationToken).ConfigureAwait(false);
        MainWindowHelpers.LogInfo($"Assets list saved to {jsonFilePath}");
    }

    public async Task<JArray?> LoadAssetsListAsync(string projectFolderPath, CancellationToken cancellationToken) {
        ArgumentException.ThrowIfNullOrEmpty(projectFolderPath);

        string jsonFilePath = Path.Combine(projectFolderPath, "assets_list.json");
        if (!File.Exists(jsonFilePath)) {
            return null;
        }

        string jsonContent = await File.ReadAllTextAsync(jsonFilePath, cancellationToken).ConfigureAwait(false);
        return JArray.Parse(jsonContent);
    }

    public async Task DownloadMaterialAsync(MaterialResource materialResource, Func<CancellationToken, Task<JObject>> fetchMaterialJsonAsync, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(materialResource);
        ArgumentNullException.ThrowIfNull(fetchMaterialJsonAsync);

        string? directoryPath = Path.GetDirectoryName(materialResource.Path);
        if (string.IsNullOrEmpty(directoryPath)) {
            throw new InvalidOperationException("Material path must have a directory.");
        }

        Directory.CreateDirectory(directoryPath);
        JObject materialJson = await fetchMaterialJsonAsync(cancellationToken).ConfigureAwait(false);

        string materialPath = Path.Combine(directoryPath, $"{materialResource.Name}.json");
        await File.WriteAllTextAsync(materialPath, materialJson.ToString(), cancellationToken).ConfigureAwait(false);
        materialResource.Status = "Downloaded";
    }

    public async Task DownloadFileAsync(BaseResource resource, string apiKey, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrEmpty(apiKey);

        if (string.IsNullOrEmpty(resource.Path)) {
            return;
        }

        const int maxRetries = 5;
        const int delayMilliseconds = 2000;

        for (int attempt = 1; attempt <= maxRetries; attempt++) {
            try {
                using HttpClient client = new();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                using HttpResponseMessage response = await client.GetAsync(resource.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) {
                    throw new HttpRequestException($"Failed to download resource: {response.StatusCode}");
                }

                long totalBytes = response.Content.Headers.ContentLength ?? 0L;
                byte[] buffer = new byte[8192];

                using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using FileStream fileStream = await FileHelper.OpenFileStreamWithRetryAsync(resource.Path, FileMode.Create, FileAccess.Write, FileShare.None).ConfigureAwait(false);

                int bytesRead;
                while ((bytesRead = await responseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0) {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                    if (totalBytes > 0) {
                        resource.DownloadProgress = (double)fileStream.Length / totalBytes * 100;
                    }
                }

                if (!File.Exists(resource.Path)) {
                    resource.Status = "Error";
                    MainWindowHelpers.LogError($"File was expected but not found: {resource.Path}");
                    return;
                }

                FileInfo fileInfo = new(resource.Path);
                long fileSizeInBytes = fileInfo.Length;
                long resourceSizeInBytes = resource.Size;

                if (fileInfo.Length == 0) {
                    resource.Status = "Empty File";
                } else if (!string.IsNullOrEmpty(resource.Hash) && FileHelper.VerifyFileHash(resource.Path, resource.Hash)) {
                    resource.Status = "Downloaded";
                } else {
                    double tolerance = 0.05;
                    double lowerBound = resourceSizeInBytes * (1 - tolerance);
                    double upperBound = resourceSizeInBytes * (1 + tolerance);

                    if (fileSizeInBytes >= lowerBound && fileSizeInBytes <= upperBound) {
                        resource.Status = "Size Mismatch";
                    } else {
                        resource.Status = "Corrupted";
                    }
                }

                return;
            } catch (IOException ex) {
                if (attempt == maxRetries) {
                    resource.Status = "Error";
                    MainWindowHelpers.LogError($"Error downloading resource after {maxRetries} attempts: {ex.Message}");
                } else {
                    MainWindowHelpers.LogError($"Attempt {attempt} failed with IOException: {ex.Message}. Retrying in {delayMilliseconds}ms...");
                    await Task.Delay(delayMilliseconds, cancellationToken).ConfigureAwait(false);
                }
            } catch (Exception ex) when (attempt < maxRetries) {
                MainWindowHelpers.LogError($"Attempt {attempt} failed: {ex.Message}. Retrying in {delayMilliseconds}ms...");
                await Task.Delay(delayMilliseconds, cancellationToken).ConfigureAwait(false);
            } catch (Exception ex) {
                resource.Status = "Error";
                MainWindowHelpers.LogError($"Error downloading resource: {ex.Message}");
                return;
            }
        }
    }
}
