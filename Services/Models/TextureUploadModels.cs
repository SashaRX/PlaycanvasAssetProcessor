using AssetProcessor.Resources;
using AssetProcessor.Upload;
using System.Collections.Generic;

namespace AssetProcessor.Services.Models {
    public enum TextureUploadFailureReason {
        None,
        MissingProjectsFolder,
        MissingCredentials,
        DecryptionFailed,
        NoConvertedTextures,
        AuthorizationFailed,
        UploadFailed
    }

    public record TextureUploadCandidate(TextureResource Texture, string Ktx2Path);

    public record TextureUploadBatchResult(
        bool Success,
        TextureUploadFailureReason FailureReason,
        string Message,
        IReadOnlyList<TextureUploadCandidate> Candidates,
        B2BatchUploadResult? BatchResult);
}
