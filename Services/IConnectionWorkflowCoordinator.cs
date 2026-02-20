using AssetProcessor.Infrastructure.Enums;
using AssetProcessor.Services.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AssetProcessor.Services;

public interface IConnectionWorkflowCoordinator {
    Task<ConnectionWorkflowResult> EvaluateRefreshAsync(Func<Task<bool>> hasServerUpdatesAsync, Func<bool> hasMissingFiles);
    Task<ConnectionWorkflowResult> EvaluatePostDownloadAsync(Func<Task<bool>> hasServerUpdatesAsync, Func<bool> hasMissingFiles);
    ConnectionWorkflowResult EvaluateProjectState(bool hasProjectFolder, bool hasProjectName, bool assetsListExists, bool hasUpdates, bool hasMissingFiles);
    ConnectionState EvaluateSmartLoadState(bool hasSelection, bool hasProjectPath, bool assetsLoaded, bool updatesCheckSucceeded, bool hasUpdates);
    ConnectionProjectSelectionResult SelectProject(IReadOnlyCollection<KeyValuePair<string, string>> projects, string? preferredProjectId);
    ConnectionProjectsBindingResult BuildProjectsBinding(IReadOnlyCollection<KeyValuePair<string, string>> projects, string? preferredProjectId);
    ConnectionProjectsLoadResult ValidateProjectsLoad(ProjectSelectionResult projectsResult);
}
