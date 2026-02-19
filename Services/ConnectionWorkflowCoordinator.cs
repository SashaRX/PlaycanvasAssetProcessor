using AssetProcessor.Infrastructure.Enums;
using System;
using System.Threading.Tasks;

namespace AssetProcessor.Services;

public sealed class ConnectionWorkflowCoordinator : IConnectionWorkflowCoordinator {
    public async Task<ConnectionState> DetermineRefreshStateAsync(Func<Task<bool>> hasServerUpdatesAsync, Func<bool> hasMissingFiles) {
        bool hasUpdates = await hasServerUpdatesAsync();
        return (hasUpdates || hasMissingFiles()) ? ConnectionState.NeedsDownload : ConnectionState.UpToDate;
    }

    public async Task<ConnectionState> DeterminePostDownloadStateAsync(Func<Task<bool>> hasServerUpdatesAsync, Func<bool> hasMissingFiles) {
        bool hasUpdates = await hasServerUpdatesAsync();
        return (hasUpdates || hasMissingFiles()) ? ConnectionState.NeedsDownload : ConnectionState.UpToDate;
    }
}
