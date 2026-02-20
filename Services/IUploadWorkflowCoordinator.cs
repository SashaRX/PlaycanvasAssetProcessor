using AssetProcessor.Data;
using AssetProcessor.Resources;
using AssetProcessor.Services.Models;
using AssetProcessor.Upload;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AssetProcessor.Services;

public interface IUploadWorkflowCoordinator {
    UploadValidationResult ValidateB2Configuration(string? keyId, string? bucketName);
    List<(TextureResource Texture, string Ktx2Path)> CollectConvertedTextures(IEnumerable<TextureResource> textures);

    List<(string LocalPath, string RemotePath)> BuildUploadFilePairs(
        IEnumerable<string> exportedFiles,
        string? serverPath,
        Action<string>? onMissingFile = null);

    Task<int> TryUploadMappingJsonAsync(
        IB2UploadService b2Service,
        IUploadStateService uploadStateService,
        string serverPath,
        string projectName,
        Action<string>? onInfo = null,
        Action<Exception, string>? onWarn = null);

    Task<UploadStatusUpdates> SaveUploadRecordsAsync(
        B2BatchUploadResult uploadResult,
        string serverPath,
        string projectName,
        IUploadStateService uploadStateService,
        Action<string>? onInfo = null,
        Action<Exception, string>? onError = null);

    void ApplyUploadStatuses<T>(Dictionary<int, (string CdnUrl, string Hash)> uploadedItems, IEnumerable<T> resources)
        where T : BaseResource;
}
