using AssetProcessor.Helpers;
using AssetProcessor.Services.Models;
using AssetProcessor.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AssetProcessor.Services;

public class ProjectSelectionService : IProjectSelectionService {
    private readonly IPlayCanvasService playCanvasService;
    private readonly ILogService logService;

    public ProjectSelectionService(IPlayCanvasService playCanvasService, ILogService logService) {
        this.playCanvasService = playCanvasService ?? throw new ArgumentNullException(nameof(playCanvasService));
        this.logService = logService ?? throw new ArgumentNullException(nameof(logService));
    }

    public string? ProjectFolderPath { get; private set; }
    public string? ProjectName { get; private set; }
    public string? UserName { get; private set; }
    public string? UserId { get; private set; }
    public string? SelectedBranchId { get; private set; }
    public string? SelectedBranchName { get; private set; }
    public bool IsBranchInitializationInProgress { get; private set; }
    public bool IsProjectInitializationInProgress { get; private set; }

    public void InitializeProjectsFolder(string? projectsFolderPath) {
        ProjectFolderPath = projectsFolderPath;
    }

    public async Task<ProjectSelectionResult> LoadProjectsAsync(string userName, string apiKey, string lastSelectedProjectId, CancellationToken cancellationToken) {
        UserName = userName.ToLowerInvariant();
        UserId = await playCanvasService.GetUserIdAsync(UserName, apiKey, cancellationToken);

        Dictionary<string, string> projectsDict = await playCanvasService.GetProjectsAsync(UserId, apiKey, [], cancellationToken);
        string? projectIdToSelect = null;

        if (projectsDict.Count > 0) {
            projectIdToSelect = !string.IsNullOrEmpty(lastSelectedProjectId) && projectsDict.ContainsKey(lastSelectedProjectId)
                ? lastSelectedProjectId
                : projectsDict.Keys.FirstOrDefault();
        }

        return new ProjectSelectionResult(projectsDict, projectIdToSelect, UserId, UserName);
    }

    public async Task<BranchSelectionResult> LoadBranchesAsync(string projectId, string apiKey, string? lastSelectedBranchName, CancellationToken cancellationToken) {
        IsBranchInitializationInProgress = true;
        try {
            List<Branch> branchesList = await playCanvasService.GetBranchesAsync(projectId, apiKey, [], cancellationToken);
            string? branchIdToSelect = null;

            if (branchesList.Count > 0) {
                Branch? selectedBranch = null;
                if (!string.IsNullOrEmpty(lastSelectedBranchName)) {
                    selectedBranch = branchesList.FirstOrDefault(b => b.Name == lastSelectedBranchName);
                }

                selectedBranch ??= branchesList.FirstOrDefault();
                branchIdToSelect = selectedBranch?.Id;
            }

            return new BranchSelectionResult(branchesList, branchIdToSelect);
        } finally {
            IsBranchInitializationInProgress = false;
        }
    }

    public void UpdateProjectPath(string projectsRoot, KeyValuePair<string, string> selectedProject) {
        ArgumentException.ThrowIfNullOrEmpty(projectsRoot);

        ProjectName = MainWindowHelpers.CleanProjectName(selectedProject.Value);
        ProjectFolderPath = System.IO.Path.Combine(projectsRoot, ProjectName);

        logService.LogInfo($"Updated Project Folder Path: {ProjectFolderPath}");
    }

    public void SetProjectInitializationInProgress(bool value) {
        IsProjectInitializationInProgress = value;
    }

    public void UpdateSelectedBranch(Branch branch) {
        ArgumentNullException.ThrowIfNull(branch);

        SelectedBranchId = branch.Id;
        SelectedBranchName = branch.Name;
    }
}
