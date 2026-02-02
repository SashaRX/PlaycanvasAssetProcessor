using AssetProcessor.Data;
using AssetProcessor.Resources;
using AssetProcessor.Services.Models;
using AssetProcessor.Settings;
using AssetProcessor.Upload;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AssetProcessor.Services {
    public class TextureUploadService : ITextureUploadService {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly IB2UploadService b2UploadService;
        private readonly IUploadStateService uploadStateService;
        private readonly ILogService logService;

        public TextureUploadService(
            IB2UploadService b2UploadService,
            IUploadStateService uploadStateService,
            ILogService logService) {
            this.b2UploadService = b2UploadService;
            this.uploadStateService = uploadStateService;
            this.logService = logService;
        }

        public async Task<TextureUploadBatchResult> UploadTexturesAsync(
            IReadOnlyList<TextureResource> textures,
            string projectName,
            IProgress<B2UploadProgress>? progress = null,
            CancellationToken cancellationToken = default) {
            if (string.IsNullOrWhiteSpace(AppSettings.Default.ProjectsFolderPath)) {
                return new TextureUploadBatchResult(
                    false,
                    TextureUploadFailureReason.MissingProjectsFolder,
                    "Не указана папка проектов. Откройте настройки и укажите Projects Folder Path.",
                    Array.Empty<TextureUploadCandidate>(),
                    null);
            }

            if (!AppSettings.Default.HasStoredB2Credentials) {
                return new TextureUploadBatchResult(
                    false,
                    TextureUploadFailureReason.MissingCredentials,
                    "Backblaze B2 credentials not configured. Go to Settings -> CDN/Upload to configure.",
                    Array.Empty<TextureUploadCandidate>(),
                    null);
            }

            if (!AppSettings.Default.TryGetDecryptedB2ApplicationKey(out string? applicationKey) ||
                string.IsNullOrWhiteSpace(applicationKey)) {
                logService.LogError("Failed to decrypt B2 application key.");
                return new TextureUploadBatchResult(
                    false,
                    TextureUploadFailureReason.DecryptionFailed,
                    "Не удалось расшифровать ключ доступа B2. Проверьте настройки.",
                    Array.Empty<TextureUploadCandidate>(),
                    null);
            }

            var candidates = textures
                .Where(t => !string.IsNullOrEmpty(t.Path))
                .Select(t => new TextureUploadCandidate(t, BuildKtx2Path(t.Path!)))
                .Where(c => File.Exists(c.Ktx2Path))
                .ToList();

            if (!candidates.Any()) {
                return new TextureUploadBatchResult(
                    false,
                    TextureUploadFailureReason.NoConvertedTextures,
                    "No converted textures found.\n\nEither select textures in the list, or mark them for export (Mark Related), then process them to KTX2.",
                    Array.Empty<TextureUploadCandidate>(),
                    null);
            }

            var settings = new B2UploadSettings {
                KeyId = AppSettings.Default.B2KeyId,
                ApplicationKey = applicationKey,
                BucketName = AppSettings.Default.B2BucketName,
                BucketId = AppSettings.Default.B2BucketId,
                PathPrefix = AppSettings.Default.B2PathPrefix,
                CdnBaseUrl = AppSettings.Default.CdnBaseUrl,
                MaxConcurrentUploads = AppSettings.Default.B2MaxConcurrentUploads
            };

            if (!await b2UploadService.AuthorizeAsync(settings, cancellationToken).ConfigureAwait(false)) {
                return new TextureUploadBatchResult(
                    false,
                    TextureUploadFailureReason.AuthorizationFailed,
                    "Failed to connect to Backblaze B2. Check your credentials in Settings.",
                    candidates,
                    null);
            }

            await uploadStateService.InitializeAsync(cancellationToken).ConfigureAwait(false);

            var files = candidates
                .Select(c => (LocalPath: c.Ktx2Path, RemotePath: $"{projectName}/textures/{Path.GetFileName(c.Ktx2Path)}"))
                .ToList();

            var batchResult = await b2UploadService.UploadBatchAsync(files, progress, cancellationToken)
                .ConfigureAwait(false);

            Logger.Debug($"Results count: {batchResult.Results.Count}, candidates count: {candidates.Count}");

            foreach (var candidate in candidates) {
                Logger.Debug($"Looking for path: '{candidate.Ktx2Path}'");
                foreach (var result in batchResult.Results) {
                    Logger.Debug($"  Result path: '{result.LocalPath}' Success={result.Success} Skipped={result.Skipped}");
                }

                var uploadResult = batchResult.Results.FirstOrDefault(r =>
                    string.Equals(r.LocalPath, candidate.Ktx2Path, StringComparison.OrdinalIgnoreCase));
                Logger.Debug($"uploadResult found: {uploadResult != null}, Success: {uploadResult?.Success}");

                if (uploadResult?.Success == true) {
                    candidate.Texture.UploadStatus = "Uploaded";
                    candidate.Texture.RemoteUrl = uploadResult.CdnUrl;
                    candidate.Texture.UploadedHash = uploadResult.ContentSha1;
                    candidate.Texture.LastUploadedAt = DateTime.UtcNow;

                    var remotePath = $"{projectName}/textures/{Path.GetFileName(candidate.Ktx2Path)}";
                    await uploadStateService.SaveUploadAsync(new UploadRecord {
                        LocalPath = candidate.Ktx2Path,
                        RemotePath = remotePath,
                        ContentSha1 = uploadResult.ContentSha1 ?? string.Empty,
                        ContentLength = new FileInfo(candidate.Ktx2Path).Length,
                        UploadedAt = DateTime.UtcNow,
                        CdnUrl = uploadResult.CdnUrl ?? string.Empty,
                        Status = "Uploaded",
                        FileId = uploadResult.FileId,
                        ProjectName = projectName
                    }, cancellationToken).ConfigureAwait(false);
                }
            }

            var message = batchResult.Success
                ? "Upload completed."
                : "Upload finished with errors.";

            return new TextureUploadBatchResult(
                batchResult.Success,
                batchResult.Success ? TextureUploadFailureReason.None : TextureUploadFailureReason.UploadFailed,
                message,
                candidates,
                batchResult);
        }

        private static string BuildKtx2Path(string sourcePath) {
            var sourceDir = Path.GetDirectoryName(sourcePath) ?? string.Empty;
            var fileName = Path.GetFileNameWithoutExtension(sourcePath);
            return Path.Combine(sourceDir, $"{fileName}.ktx2");
        }
    }
}
