using AssetProcessor.Infrastructure.Enums;
using AssetProcessor.Services.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;
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
            return new ConnectionWorkflowResult {
                State = ConnectionState.NeedsDownload,
                HasUpdates = false,
                HasMissingFiles = true,
                HasRequiredProjectData = false
            };
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


    public ConnectionProjectSelectionResult SelectProject(IReadOnlyCollection<KeyValuePair<string, string>> projects, string? preferredProjectId) {
        if (projects.Count == 0) {
            return new ConnectionProjectSelectionResult { HasProjects = false, SelectedProjectId = null };
        }

        if (!string.IsNullOrEmpty(preferredProjectId) && projects.Any(p => p.Key == preferredProjectId)) {
            return new ConnectionProjectSelectionResult { HasProjects = true, SelectedProjectId = preferredProjectId };
        }

        return new ConnectionProjectSelectionResult {
            HasProjects = true,
            SelectedProjectId = projects.First().Key
        };
    }


    public ConnectionProjectsBindingResult BuildProjectsBinding(IReadOnlyCollection<KeyValuePair<string, string>> projects, string? preferredProjectId) {
        var selected = SelectProject(projects, preferredProjectId);
        return new ConnectionProjectsBindingResult {
            HasProjects = selected.HasProjects,
            SelectedProjectId = selected.SelectedProjectId,
            Projects = projects.ToList().AsReadOnly()
        };
    }


    public ConnectionProjectsLoadResult ValidateProjectsLoad(ProjectSelectionResult projectsResult) {
        if (string.IsNullOrWhiteSpace(projectsResult.UserId)) {
            return new ConnectionProjectsLoadResult {
                IsValid = false,
                HasProjects = false,
                ErrorMessage = "User ID is null or empty"
            };
        }

        if (projectsResult.Projects.Count == 0) {
            return new ConnectionProjectsLoadResult {
                IsValid = true,
                HasProjects = false,
                UserId = projectsResult.UserId
            };
        }

        return new ConnectionProjectsLoadResult {
            IsValid = true,
            HasProjects = true,
            UserId = projectsResult.UserId
        };
    }

    private static ConnectionWorkflowResult BuildResult(bool hasUpdates, bool hasMissingFiles) {
        return new ConnectionWorkflowResult {
            HasUpdates = hasUpdates,
            HasMissingFiles = hasMissingFiles,
            State = hasUpdates || hasMissingFiles ? ConnectionState.NeedsDownload : ConnectionState.UpToDate
        };
    }
}
