using AssetProcessor.Services.Models;

namespace AssetProcessor.Services;

/// <summary>
/// High-level service for coordinating PlayCanvas server connection.
/// Wraps IProjectSelectionService and IProjectAssetService to provide
/// a unified connection API with proper error handling.
/// </summary>
public interface IProjectConnectionService {
    /// <summary>
    /// Validates credentials and loads projects from PlayCanvas server.
    /// </summary>
    /// <param name="username">PlayCanvas username</param>
    /// <param name="apiKey">Decrypted API key</param>
    /// <param name="lastProjectId">Last selected project ID for auto-selection</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Connection result with projects list or error</returns>
    Task<ProjectConnectionResult> ConnectAsync(
        string username,
        string apiKey,
        string? lastProjectId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads branches for a specific project.
    /// </summary>
    /// <param name="projectId">PlayCanvas project ID</param>
    /// <param name="apiKey">Decrypted API key</param>
    /// <param name="lastBranchName">Last selected branch name for auto-selection</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Branch list result or error</returns>
    Task<BranchLoadResult> LoadBranchesAsync(
        string projectId,
        string apiKey,
        string? lastBranchName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Checks if the local project has updates available on the server.
    /// </summary>
    /// <param name="projectFolderPath">Local project folder path</param>
    /// <param name="projectId">PlayCanvas project ID</param>
    /// <param name="branchId">PlayCanvas branch ID</param>
    /// <param name="apiKey">Decrypted API key</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Check result with update status</returns>
    Task<ProjectStateCheckResult> CheckForUpdatesAsync(
        string projectFolderPath,
        string projectId,
        string branchId,
        string apiKey,
        CancellationToken cancellationToken);
}
