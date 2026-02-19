using AssetProcessor.Infrastructure.Enums;
using AssetProcessor.Services.Models;
using System;
using System.Threading.Tasks;

namespace AssetProcessor.Services;

public sealed class ConnectionWorkflowCoordinator : IConnectionWorkflowCoordinator {
    public async Task<ConnectionWorkflowResult> EvaluateRefreshAsync(Func<Task<bool>> hasServerUpdatesAsync, Func<bool> hasMissingFiles) {
        bool hasUpdates = await hasServerUpdatesAsync();
        bool missingFiles = hasMissingFiles();
        return BuildResult(hasUpdates, missingFiles);
    }

    public async Task<ConnectionWorkflowResult> EvaluatePostDownloadAsync(Func<Task<bool>> hasServerUpdatesAsync, Func<bool> hasMissingFiles) {
        bool hasUpdates = await hasServerUpdatesAsync();
        bool missingFiles = hasMissingFiles();
        return BuildResult(hasUpdates, missingFiles);
    }

    private static ConnectionWorkflowResult BuildResult(bool hasUpdates, bool hasMissingFiles) {
        return new ConnectionWorkflowResult {
            HasUpdates = hasUpdates,
            HasMissingFiles = hasMissingFiles,
            State = hasUpdates || hasMissingFiles ? ConnectionState.NeedsDownload : ConnectionState.UpToDate
        };
    }
}
