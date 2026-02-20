using AssetProcessor.Infrastructure.Enums;
using AssetProcessor.Services.Models;
using System;
using System.Collections.Generic;
using System.Linq;
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

    public ConnectionWorkflowResult EvaluateProjectState(bool hasProjectFolder, bool hasProjectName, bool assetsListExists, bool hasUpdates, bool hasMissingFiles) {
        if (!hasProjectFolder || !hasProjectName || !assetsListExists) {
            return BuildResult(hasUpdates: false, hasMissingFiles: true);
        }

        return BuildResult(hasUpdates, hasMissingFiles);
    }

    public ConnectionState EvaluateSmartLoadState(bool hasSelection, bool hasProjectPath, bool assetsLoaded, bool updatesCheckSucceeded, bool hasUpdates) {
        if (!hasSelection) {
            return ConnectionState.Disconnected;
        }

        if (!hasProjectPath || !assetsLoaded || !updatesCheckSucceeded) {
            return ConnectionState.NeedsDownload;
        }

        return hasUpdates ? ConnectionState.NeedsDownload : ConnectionState.UpToDate;
    }


    public string? ResolveSelectedProjectId(IReadOnlyCollection<KeyValuePair<string, string>> projects, string? preferredProjectId) {
        if (!string.IsNullOrEmpty(preferredProjectId) && projects.Any(p => p.Key == preferredProjectId)) {
            return preferredProjectId;
        }

        return projects.FirstOrDefault().Key;
    }

    private static ConnectionWorkflowResult BuildResult(bool hasUpdates, bool hasMissingFiles) {
        return new ConnectionWorkflowResult {
            HasUpdates = hasUpdates,
            HasMissingFiles = hasMissingFiles,
            State = hasUpdates || hasMissingFiles ? ConnectionState.NeedsDownload : ConnectionState.UpToDate
        };
    }
}
