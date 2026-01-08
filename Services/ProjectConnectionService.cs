using AssetProcessor.Services.Models;

namespace AssetProcessor.Services;

/// <summary>
/// Coordinates PlayCanvas server connection operations.
/// Wraps IProjectSelectionService and IProjectAssetService to provide
/// unified connection API with proper error handling.
/// </summary>
public sealed class ProjectConnectionService : IProjectConnectionService {
    private readonly IProjectSelectionService projectSelectionService;
    private readonly IProjectAssetService projectAssetService;
    private readonly ILogService logService;

    public ProjectConnectionService(
        IProjectSelectionService projectSelectionService,
        IProjectAssetService projectAssetService,
        ILogService logService) {
        this.projectSelectionService = projectSelectionService ?? throw new ArgumentNullException(nameof(projectSelectionService));
        this.projectAssetService = projectAssetService ?? throw new ArgumentNullException(nameof(projectAssetService));
        this.logService = logService ?? throw new ArgumentNullException(nameof(logService));
    }

    public async Task<ProjectConnectionResult> ConnectAsync(
        string username,
        string apiKey,
        string? lastProjectId,
        CancellationToken cancellationToken) {
        try {
            if (string.IsNullOrWhiteSpace(username)) {
                return ProjectConnectionResult.Failed("Username is required");
            }

            if (string.IsNullOrWhiteSpace(apiKey)) {
                return ProjectConnectionResult.Failed("API key is required");
            }

            logService.LogInfo($"Connecting to PlayCanvas as {username}...");

            var result = await projectSelectionService.LoadProjectsAsync(
                username,
                apiKey,
                lastProjectId ?? string.Empty,
                cancellationToken);

            if (string.IsNullOrEmpty(result.UserId)) {
                return ProjectConnectionResult.Failed("Failed to get user ID from PlayCanvas");
            }

            logService.LogInfo($"Connected successfully. User ID: {result.UserId}, Projects: {result.Projects.Count}");

            return ProjectConnectionResult.Succeeded(
                result.UserId,
                result.UserName,
                result.Projects,
                result.SelectedProjectId);
        } catch (OperationCanceledException) {
            logService.LogInfo("Connection cancelled");
            return ProjectConnectionResult.Failed("Connection cancelled");
        } catch (Exception ex) {
            logService.LogError($"Connection failed: {ex.Message}");
            return ProjectConnectionResult.Failed($"Connection failed: {ex.Message}");
        }
    }

    public async Task<BranchLoadResult> LoadBranchesAsync(
        string projectId,
        string apiKey,
        string? lastBranchName,
        CancellationToken cancellationToken) {
        try {
            if (string.IsNullOrWhiteSpace(projectId)) {
                return BranchLoadResult.Failed("Project ID is required");
            }

            if (string.IsNullOrWhiteSpace(apiKey)) {
                return BranchLoadResult.Failed("API key is required");
            }

            logService.LogInfo($"Loading branches for project {projectId}...");

            var result = await projectSelectionService.LoadBranchesAsync(
                projectId,
                apiKey,
                lastBranchName,
                cancellationToken);

            logService.LogInfo($"Loaded {result.Branches.Count} branches");

            return BranchLoadResult.Succeeded(result.Branches, result.SelectedBranchId);
        } catch (OperationCanceledException) {
            logService.LogInfo("Branch loading cancelled");
            return BranchLoadResult.Failed("Branch loading cancelled");
        } catch (Exception ex) {
            logService.LogError($"Failed to load branches: {ex.Message}");
            return BranchLoadResult.Failed($"Failed to load branches: {ex.Message}");
        }
    }

    public async Task<ProjectStateCheckResult> CheckForUpdatesAsync(
        string projectFolderPath,
        string projectId,
        string branchId,
        string apiKey,
        CancellationToken cancellationToken) {
        try {
            if (string.IsNullOrWhiteSpace(projectFolderPath)) {
                return ProjectStateCheckResult.Failed("Project folder path is required");
            }

            if (string.IsNullOrWhiteSpace(projectId)) {
                return ProjectStateCheckResult.Failed("Project ID is required");
            }

            if (string.IsNullOrWhiteSpace(branchId)) {
                return ProjectStateCheckResult.Failed("Branch ID is required");
            }

            if (string.IsNullOrWhiteSpace(apiKey)) {
                return ProjectStateCheckResult.Failed("API key is required");
            }

            logService.LogInfo("Checking for project updates...");

            var context = new ProjectUpdateContext(projectFolderPath, projectId, branchId, apiKey);
            bool hasUpdates = await projectAssetService.HasUpdatesAsync(context, cancellationToken);

            string message = hasUpdates
                ? "Updates available on server"
                : "Project is up to date";

            logService.LogInfo(message);

            return ProjectStateCheckResult.Succeeded(hasUpdates, message);
        } catch (OperationCanceledException) {
            logService.LogInfo("Update check cancelled");
            return ProjectStateCheckResult.Failed("Update check cancelled");
        } catch (Exception ex) {
            logService.LogError($"Failed to check for updates: {ex.Message}");
            return ProjectStateCheckResult.Failed($"Failed to check for updates: {ex.Message}");
        }
    }
}
