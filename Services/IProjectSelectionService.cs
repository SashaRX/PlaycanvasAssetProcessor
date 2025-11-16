using AssetProcessor.Services.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AssetProcessor.Services;

public interface IProjectSelectionService {
    string? ProjectFolderPath { get; }
    string? ProjectName { get; }
    string? UserName { get; }
    string? UserId { get; }
    bool IsBranchInitializationInProgress { get; }
    bool IsProjectInitializationInProgress { get; }

    void InitializeProjectsFolder(string? projectsFolderPath);
    Task<ProjectSelectionResult> LoadProjectsAsync(string userName, string apiKey, string lastSelectedProjectId, CancellationToken cancellationToken);
    Task<BranchSelectionResult> LoadBranchesAsync(string projectId, string apiKey, string? lastSelectedBranchName, CancellationToken cancellationToken);
    void UpdateProjectPath(string projectsRoot, KeyValuePair<string, string> selectedProject);
    void SetProjectInitializationInProgress(bool value);
}
