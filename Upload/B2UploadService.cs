using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NLog;

namespace AssetProcessor.Upload;

/// <summary>
/// Реализация сервиса загрузки на Backblaze B2
/// Использует B2 Native API: https://www.backblaze.com/apidocs/
/// </summary>
public class B2UploadService : IB2UploadService, IDisposable {
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly HttpClient _httpClient;
    private B2UploadSettings? _settings;
    private B2AuthorizationResponse? _authResponse;
    private B2GetUploadUrlResponse? _uploadUrl;
    private readonly SemaphoreSlim _uploadSemaphore;
    private readonly object _uploadUrlLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// MIME типы по расширению файла
    /// </summary>
    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase) {
        { ".json", "application/json" },
        { ".glb", "model/gltf-binary" },
        { ".gltf", "model/gltf+json" },
        { ".ktx2", "image/ktx2" },
        { ".basis", "image/basis" },
        { ".png", "image/png" },
        { ".jpg", "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".webp", "image/webp" },
        { ".bin", "application/octet-stream" },
        { ".txt", "text/plain" },
        { ".html", "text/html" },
        { ".css", "text/css" },
        { ".js", "application/javascript" }
    };

    public B2UploadService() {
        _httpClient = new HttpClient {
            Timeout = TimeSpan.FromSeconds(300)
        };
        _uploadSemaphore = new SemaphoreSlim(4);
    }

    public B2UploadSettings? Settings => _settings;
    public bool IsAuthorized => _authResponse != null && !string.IsNullOrEmpty(_authResponse.AuthorizationToken);

    public async Task<bool> AuthorizeAsync(B2UploadSettings settings, CancellationToken cancellationToken = default) {
        if (!settings.IsValid) {
            Logger.Error("B2 settings are invalid");
            return false;
        }

        _settings = settings;
        _uploadSemaphore.Dispose();

        try {
            Logger.Info("Authorizing with B2...");

            // b2_authorize_account
            var authString = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{settings.KeyId}:{settings.ApplicationKey}"));

            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.backblazeb2.com/b2api/v2/b2_authorize_account");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authString);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode) {
                Logger.Error($"B2 authorization failed: {response.StatusCode} - {content}");
                return false;
            }

            _authResponse = JsonSerializer.Deserialize<B2AuthorizationResponse>(content, JsonOptions);

            if (_authResponse == null) {
                Logger.Error("Failed to parse B2 authorization response");
                return false;
            }

            Logger.Info($"B2 authorized. API URL: {_authResponse.ApiUrl}");

            // Получаем bucket ID если не указан
            if (string.IsNullOrEmpty(settings.BucketId)) {
                settings.BucketId = await GetBucketIdAsync(settings.BucketName, cancellationToken);
            }

            return true;

        } catch (Exception ex) {
            Logger.Error(ex, "B2 authorization error");
            return false;
        }
    }

    public async Task<B2UploadResult> UploadFileAsync(
        string localPath,
        string remotePath,
        string? contentType = null,
        CancellationToken cancellationToken = default) {

        var result = new B2UploadResult {
            LocalPath = localPath,
            RemotePath = remotePath
        };

        try {
            if (!IsAuthorized || _settings == null) {
                result.ErrorMessage = "Not authorized";
                return result;
            }

            if (!File.Exists(localPath)) {
                result.ErrorMessage = $"File not found: {localPath}";
                return result;
            }

            var fullRemotePath = _settings.BuildFullPath(remotePath);

            // Проверяем существование файла если включено
            if (_settings.SkipExistingFiles) {
                var existingFile = await GetFileInfoAsync(fullRemotePath, cancellationToken);
                if (existingFile != null) {
                    var localHash = ComputeFileSha1(localPath);
                    if (existingFile.ContentSha1 == localHash) {
                        Logger.Debug($"Skipping {remotePath} - file exists with same hash");
                        result.Success = true;
                        result.Skipped = true;
                        result.FileId = existingFile.FileId;
                        result.ContentSha1 = existingFile.ContentSha1;
                        result.CdnUrl = _settings.BuildCdnUrl(remotePath);
                        return result;
                    }
                }
            }

            // Получаем upload URL
            var uploadUrl = await GetUploadUrlAsync(cancellationToken);
            if (uploadUrl == null) {
                result.ErrorMessage = "Failed to get upload URL";
                return result;
            }

            // Читаем файл
            var fileBytes = await File.ReadAllBytesAsync(localPath, cancellationToken);
            var sha1 = ComputeSha1(fileBytes);

            // Определяем content type
            contentType ??= GetContentType(localPath);

            // Загружаем
            var request = new HttpRequestMessage(HttpMethod.Post, uploadUrl.UploadUrl);
            request.Headers.Add("Authorization", uploadUrl.AuthorizationToken);
            request.Headers.Add("X-Bz-File-Name", Uri.EscapeDataString(fullRemotePath));
            request.Headers.Add("X-Bz-Content-Sha1", sha1);

            request.Content = new ByteArrayContent(fileBytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            request.Content.Headers.ContentLength = fileBytes.Length;

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode) {
                // Если upload URL expired, сбрасываем и пробуем снова
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                    responseContent.Contains("expired")) {
                    lock (_uploadUrlLock) {
                        _uploadUrl = null;
                    }
                    result.ErrorMessage = $"Upload failed (will retry): {responseContent}";
                    return result;
                }

                result.ErrorMessage = $"Upload failed: {response.StatusCode} - {responseContent}";
                return result;
            }

            var uploadResponse = JsonSerializer.Deserialize<B2UploadFileResponse>(responseContent, JsonOptions);

            result.Success = true;
            result.FileId = uploadResponse?.FileId;
            result.ContentSha1 = sha1;
            result.ContentLength = fileBytes.Length;
            result.CdnUrl = _settings.BuildCdnUrl(remotePath);

            Logger.Info($"Uploaded: {remotePath} ({fileBytes.Length} bytes)");

        } catch (Exception ex) {
            Logger.Error(ex, $"Upload error: {localPath}");
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    public async Task<B2BatchUploadResult> UploadBatchAsync(
        IEnumerable<(string LocalPath, string RemotePath)> files,
        IProgress<B2UploadProgress>? progress = null,
        CancellationToken cancellationToken = default) {

        var fileList = files.ToList();
        var result = new B2BatchUploadResult();
        var stopwatch = Stopwatch.StartNew();

        var semaphore = new SemaphoreSlim(_settings?.MaxConcurrentUploads ?? 4);
        var progressLock = new object();
        int processedCount = 0;
        long totalBytes = 0;

        // Подсчитываем общий размер
        foreach (var (localPath, _) in fileList) {
            if (File.Exists(localPath)) {
                totalBytes += new FileInfo(localPath).Length;
            }
        }

        var tasks = fileList.Select(async file => {
            await semaphore.WaitAsync(cancellationToken);
            try {
                var uploadResult = await UploadFileAsync(file.LocalPath, file.RemotePath, null, cancellationToken);

                lock (progressLock) {
                    processedCount++;
                    result.Results.Add(uploadResult);

                    if (uploadResult.Success) {
                        if (uploadResult.Skipped) {
                            result.SkippedCount++;
                        } else {
                            result.SuccessCount++;
                            result.TotalBytesUploaded += uploadResult.ContentLength;
                        }
                    } else {
                        result.FailedCount++;
                        if (!string.IsNullOrEmpty(uploadResult.ErrorMessage)) {
                            result.Errors.Add($"{file.LocalPath}: {uploadResult.ErrorMessage}");
                        }
                    }

                    progress?.Report(new B2UploadProgress {
                        CurrentFile = file.LocalPath,
                        CurrentFileIndex = processedCount,
                        TotalFiles = fileList.Count,
                        BytesUploaded = result.TotalBytesUploaded,
                        TotalBytes = totalBytes,
                        BytesPerSecond = result.TotalBytesUploaded / Math.Max(1, stopwatch.Elapsed.TotalSeconds),
                        Status = uploadResult.Success ? "Uploaded" : "Failed"
                    });
                }

                return uploadResult;

            } finally {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        stopwatch.Stop();
        result.Duration = stopwatch.Elapsed;

        Logger.Info($"Batch upload complete: {result.SuccessCount} uploaded, {result.SkippedCount} skipped, {result.FailedCount} failed in {result.Duration.TotalSeconds:F1}s");

        return result;
    }

    public async Task<B2BatchUploadResult> UploadDirectoryAsync(
        string localDirectory,
        string remotePrefix,
        string searchPattern = "*",
        bool recursive = true,
        IProgress<B2UploadProgress>? progress = null,
        CancellationToken cancellationToken = default) {

        if (!Directory.Exists(localDirectory)) {
            return new B2BatchUploadResult {
                Errors = { $"Directory not found: {localDirectory}" }
            };
        }

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.GetFiles(localDirectory, searchPattern, searchOption);

        var filePairs = files.Select(f => {
            var relativePath = Path.GetRelativePath(localDirectory, f).Replace('\\', '/');
            var remotePath = string.IsNullOrEmpty(remotePrefix)
                ? relativePath
                : $"{remotePrefix.TrimEnd('/')}/{relativePath}";
            return (LocalPath: f, RemotePath: remotePath);
        });

        return await UploadBatchAsync(filePairs, progress, cancellationToken);
    }

    public async Task<bool> FileExistsAsync(string remotePath, CancellationToken cancellationToken = default) {
        var info = await GetFileInfoAsync(remotePath, cancellationToken);
        return info != null;
    }

    public async Task<B2FileInfo?> GetFileInfoAsync(string remotePath, CancellationToken cancellationToken = default) {
        try {
            if (!IsAuthorized || _authResponse == null || _settings == null) {
                return null;
            }

            var fullPath = _settings.BuildFullPath(remotePath);

            // Используем b2_list_file_names с prefix
            var requestBody = new {
                bucketId = _settings.BucketId,
                prefix = fullPath,
                maxFileCount = 1
            };

            var request = new HttpRequestMessage(HttpMethod.Post,
                $"{_authResponse.ApiUrl}/b2api/v2/b2_list_file_names");
            request.Headers.Add("Authorization", _authResponse.AuthorizationToken);
            request.Content = new StringContent(
                JsonSerializer.Serialize(requestBody, JsonOptions),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode) {
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var listResponse = JsonSerializer.Deserialize<B2ListFilesResponse>(content, JsonOptions);

            var file = listResponse?.Files?.FirstOrDefault(f => f.FileName == fullPath);
            if (file == null) {
                return null;
            }

            return new B2FileInfo {
                FileId = file.FileId ?? string.Empty,
                FileName = file.FileName ?? string.Empty,
                ContentLength = file.ContentLength,
                ContentType = file.ContentType ?? string.Empty,
                ContentSha1 = file.ContentSha1 ?? string.Empty,
                UploadTimestamp = file.UploadTimestamp
            };

        } catch (Exception ex) {
            Logger.Warn(ex, $"Failed to get file info: {remotePath}");
            return null;
        }
    }

    public async Task<bool> DeleteFileAsync(string remotePath, CancellationToken cancellationToken = default) {
        try {
            if (!IsAuthorized || _authResponse == null || _settings == null) {
                return false;
            }

            var fileInfo = await GetFileInfoAsync(remotePath, cancellationToken);
            if (fileInfo == null) {
                return true; // Already doesn't exist
            }

            var requestBody = new {
                fileId = fileInfo.FileId,
                fileName = fileInfo.FileName
            };

            var request = new HttpRequestMessage(HttpMethod.Post,
                $"{_authResponse.ApiUrl}/b2api/v2/b2_delete_file_version");
            request.Headers.Add("Authorization", _authResponse.AuthorizationToken);
            request.Content = new StringContent(
                JsonSerializer.Serialize(requestBody, JsonOptions),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;

        } catch (Exception ex) {
            Logger.Error(ex, $"Failed to delete file: {remotePath}");
            return false;
        }
    }

    public async Task<IReadOnlyList<B2FileInfo>> ListFilesAsync(
        string prefix,
        int maxCount = 1000,
        CancellationToken cancellationToken = default) {

        var files = new List<B2FileInfo>();

        try {
            if (!IsAuthorized || _authResponse == null || _settings == null) {
                return files;
            }

            var fullPrefix = _settings.BuildFullPath(prefix);
            string? startFileName = null;

            while (files.Count < maxCount) {
                var requestBody = new {
                    bucketId = _settings.BucketId,
                    prefix = fullPrefix,
                    maxFileCount = Math.Min(1000, maxCount - files.Count),
                    startFileName
                };

                var request = new HttpRequestMessage(HttpMethod.Post,
                    $"{_authResponse.ApiUrl}/b2api/v2/b2_list_file_names");
                request.Headers.Add("Authorization", _authResponse.AuthorizationToken);
                request.Content = new StringContent(
                    JsonSerializer.Serialize(requestBody, JsonOptions),
                    Encoding.UTF8,
                    "application/json");

                var response = await _httpClient.SendAsync(request, cancellationToken);

                if (!response.IsSuccessStatusCode) {
                    break;
                }

                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var listResponse = JsonSerializer.Deserialize<B2ListFilesResponse>(content, JsonOptions);

                if (listResponse?.Files == null || listResponse.Files.Count == 0) {
                    break;
                }

                foreach (var file in listResponse.Files) {
                    files.Add(new B2FileInfo {
                        FileId = file.FileId ?? string.Empty,
                        FileName = file.FileName ?? string.Empty,
                        ContentLength = file.ContentLength,
                        ContentType = file.ContentType ?? string.Empty,
                        ContentSha1 = file.ContentSha1 ?? string.Empty,
                        UploadTimestamp = file.UploadTimestamp
                    });
                }

                if (string.IsNullOrEmpty(listResponse.NextFileName)) {
                    break;
                }

                startFileName = listResponse.NextFileName;
            }

        } catch (Exception ex) {
            Logger.Error(ex, $"Failed to list files: {prefix}");
        }

        return files;
    }

    #region Private Methods

    private async Task<string?> GetBucketIdAsync(string bucketName, CancellationToken cancellationToken) {
        try {
            if (_authResponse == null) return null;

            var requestBody = new {
                accountId = _authResponse.AccountId,
                bucketName
            };

            var request = new HttpRequestMessage(HttpMethod.Post,
                $"{_authResponse.ApiUrl}/b2api/v2/b2_list_buckets");
            request.Headers.Add("Authorization", _authResponse.AuthorizationToken);
            request.Content = new StringContent(
                JsonSerializer.Serialize(requestBody, JsonOptions),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode) {
                Logger.Error($"Failed to get bucket ID: {content}");
                return null;
            }

            var listResponse = JsonSerializer.Deserialize<B2ListBucketsResponse>(content, JsonOptions);
            var bucket = listResponse?.Buckets?.FirstOrDefault(b => b.BucketName == bucketName);

            return bucket?.BucketId;

        } catch (Exception ex) {
            Logger.Error(ex, "Failed to get bucket ID");
            return null;
        }
    }

    private async Task<B2GetUploadUrlResponse?> GetUploadUrlAsync(CancellationToken cancellationToken) {
        lock (_uploadUrlLock) {
            if (_uploadUrl != null) {
                return _uploadUrl;
            }
        }

        try {
            if (_authResponse == null || _settings == null) return null;

            var requestBody = new { bucketId = _settings.BucketId };

            var request = new HttpRequestMessage(HttpMethod.Post,
                $"{_authResponse.ApiUrl}/b2api/v2/b2_get_upload_url");
            request.Headers.Add("Authorization", _authResponse.AuthorizationToken);
            request.Content = new StringContent(
                JsonSerializer.Serialize(requestBody, JsonOptions),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode) {
                Logger.Error($"Failed to get upload URL: {content}");
                return null;
            }

            var uploadUrl = JsonSerializer.Deserialize<B2GetUploadUrlResponse>(content, JsonOptions);

            lock (_uploadUrlLock) {
                _uploadUrl = uploadUrl;
            }

            return uploadUrl;

        } catch (Exception ex) {
            Logger.Error(ex, "Failed to get upload URL");
            return null;
        }
    }

    private static string GetContentType(string filePath) {
        var ext = Path.GetExtension(filePath);
        return MimeTypes.TryGetValue(ext, out var type) ? type : "application/octet-stream";
    }

    private static string ComputeFileSha1(string filePath) {
        using var sha1 = SHA1.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha1.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private static string ComputeSha1(byte[] data) {
        using var sha1 = SHA1.Create();
        var hash = sha1.ComputeHash(data);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    #endregion

    public void Dispose() {
        _httpClient.Dispose();
        _uploadSemaphore.Dispose();
    }

    #region Response Classes

    private class B2AuthorizationResponse {
        public string? AccountId { get; set; }
        public string? AuthorizationToken { get; set; }
        public string? ApiUrl { get; set; }
        public string? DownloadUrl { get; set; }
        public long AbsoluteMinimumPartSize { get; set; }
        public long RecommendedPartSize { get; set; }
    }

    private class B2GetUploadUrlResponse {
        public string? BucketId { get; set; }
        public string? UploadUrl { get; set; }
        public string? AuthorizationToken { get; set; }
    }

    private class B2UploadFileResponse {
        public string? FileId { get; set; }
        public string? FileName { get; set; }
        public string? ContentSha1 { get; set; }
        public long ContentLength { get; set; }
    }

    private class B2ListBucketsResponse {
        public List<B2BucketInfo>? Buckets { get; set; }
    }

    private class B2BucketInfo {
        public string? BucketId { get; set; }
        public string? BucketName { get; set; }
        public string? BucketType { get; set; }
    }

    private class B2ListFilesResponse {
        public List<B2FileResponse>? Files { get; set; }
        public string? NextFileName { get; set; }
    }

    private class B2FileResponse {
        public string? FileId { get; set; }
        public string? FileName { get; set; }
        public long ContentLength { get; set; }
        public string? ContentType { get; set; }
        public string? ContentSha1 { get; set; }
        public long UploadTimestamp { get; set; }
    }

    #endregion
}
