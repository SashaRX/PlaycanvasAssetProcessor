using AssetProcessor.Resources;
using System;
using System.Collections.Generic;

namespace AssetProcessor.Services.Models;

public sealed record AssetDownloadContext(
    IReadOnlyCollection<BaseResource> Resources,
    string ApiKey,
    string ProjectName,
    string ProjectsRoot,
    IReadOnlyDictionary<int, string> FolderPaths);

public sealed record AssetDownloadProgress(int Completed, int Total, BaseResource? Resource);

public sealed record AssetDownloadOptions(
    Action<AssetDownloadProgress>? ProgressCallback = null,
    Action<BaseResource>? ResourceStatusCallback = null);

public sealed record AssetDownloadResult(bool IsSuccess, string Message, ResourceDownloadBatchResult BatchResult);

public sealed class ResourceStatusChangedEventArgs : EventArgs {
    public ResourceStatusChangedEventArgs(BaseResource resource, string? status) {
        Resource = resource ?? throw new ArgumentNullException(nameof(resource));
        Status = status;
    }

    public BaseResource Resource { get; }

    public string? Status { get; }
}
