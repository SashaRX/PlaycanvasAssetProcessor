using AssetProcessor.Services.Models;

namespace AssetProcessor.Services {
    /// <summary>
    /// Interface for PlayCanvas API service operations
    /// </summary>
    public interface IPlayCanvasService : IDisposable {
        /// <summary>
        /// Gets the user ID for a given username
        /// </summary>
        Task<string> GetUserIdAsync(string? username, string? apiKey, CancellationToken cancellationToken);

        /// <summary>
        /// Gets all projects for a user
        /// </summary>
        Task<Dictionary<string, string>> GetProjectsAsync(string? userId, string? apiKey, Dictionary<string, string> projects, CancellationToken cancellationToken);

        /// <summary>
        /// Gets all branches for a project
        /// </summary>
        Task<List<Branch>> GetBranchesAsync(string projectId, string apiKey, List<Branch> branches, CancellationToken cancellationToken);

        /// <summary>
        /// Streams assets for a project and branch using PlayCanvas pagination
        /// </summary>
        IAsyncEnumerable<PlayCanvasAssetSummary> GetAssetsAsync(string projectId, string branchId, string apiKey, CancellationToken cancellationToken);

        /// <summary>
        /// Gets a specific asset by ID
        /// </summary>
        Task<PlayCanvasAssetDetail> GetAssetByIdAsync(string assetId, string apiKey, CancellationToken cancellationToken);

        /// <summary>
        /// Creates a new branch for a project
        /// </summary>
        Task<Branch> CreateBranchAsync(string projectId, string branchName, string apiKey, CancellationToken cancellationToken);
    }
}
