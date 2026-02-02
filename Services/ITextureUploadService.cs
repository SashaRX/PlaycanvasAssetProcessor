using AssetProcessor.Resources;
using AssetProcessor.Services.Models;
using AssetProcessor.Upload;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AssetProcessor.Services {
    public interface ITextureUploadService {
        Task<TextureUploadBatchResult> UploadTexturesAsync(
            IReadOnlyList<TextureResource> textures,
            string projectName,
            IProgress<B2UploadProgress>? progress = null,
            CancellationToken cancellationToken = default);
    }
}
