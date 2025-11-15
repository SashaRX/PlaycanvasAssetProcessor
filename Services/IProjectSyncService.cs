using AssetProcessor.Resources;
using AssetProcessor.Services.Models;
using System.Threading;
using System.Threading.Tasks;

namespace AssetProcessor.Services;

public interface IProjectSyncService {
    Task<ProjectSyncResult> SyncProjectAsync(ProjectSyncRequest request, IProgress<ProjectSyncProgress>? progress, CancellationToken cancellationToken);

    Task<ResourceDownloadBatchResult> DownloadAsync(ProjectDownloadRequest request, IProgress<ResourceDownloadProgress>? progress, CancellationToken cancellationToken);

    Task<ResourceDownloadResult> DownloadResourceAsync(ResourceDownloadContext context, CancellationToken cancellationToken);

    Task<ResourceDownloadResult> DownloadMaterialByIdAsync(MaterialDownloadContext context, CancellationToken cancellationToken);

    Task<ResourceDownloadResult> DownloadFileAsync(ResourceDownloadContext context, CancellationToken cancellationToken);
}
