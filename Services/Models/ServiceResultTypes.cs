using AssetProcessor.Resources;

namespace AssetProcessor.Services.Models;

/// <summary>
/// Result of project connection attempt
/// </summary>
public sealed record ProjectConnectionResult {
    public bool Success { get; init; }
    public string? UserId { get; init; }
    public string? UserName { get; init; }
    public string? Error { get; init; }
    public IReadOnlyDictionary<string, string> Projects { get; init; } = new Dictionary<string, string>();
    public string? SelectedProjectId { get; init; }

    public static ProjectConnectionResult Failed(string error) =>
        new() { Success = false, Error = error };

    public static ProjectConnectionResult Succeeded(
        string userId,
        string userName,
        IReadOnlyDictionary<string, string> projects,
        string? selectedProjectId) =>
        new() {
            Success = true,
            UserId = userId,
            UserName = userName,
            Projects = projects,
            SelectedProjectId = selectedProjectId
        };
}

/// <summary>
/// Result of branch loading operation
/// </summary>
public sealed record BranchLoadResult {
    public bool Success { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<Branch> Branches { get; init; } = [];
    public string? SelectedBranchId { get; init; }

    public static BranchLoadResult Failed(string error) =>
        new() { Success = false, Error = error };

    public static BranchLoadResult Succeeded(IReadOnlyList<Branch> branches, string? selectedBranchId) =>
        new() { Success = true, Branches = branches, SelectedBranchId = selectedBranchId };
}

/// <summary>
/// Result of asset loading operation from JSON
/// </summary>
public sealed record AssetLoadResult {
    public bool Success { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<TextureResource> Textures { get; init; } = [];
    public IReadOnlyList<ModelResource> Models { get; init; } = [];
    public IReadOnlyList<MaterialResource> Materials { get; init; } = [];
    public IReadOnlyDictionary<int, string> FolderPaths { get; init; } = new Dictionary<int, string>();

    public static AssetLoadResult Failed(string error) =>
        new() { Success = false, Error = error };

    public static AssetLoadResult Succeeded(
        IReadOnlyList<TextureResource> textures,
        IReadOnlyList<ModelResource> models,
        IReadOnlyList<MaterialResource> materials,
        IReadOnlyDictionary<int, string> folderPaths) =>
        new() {
            Success = true,
            Textures = textures,
            Models = models,
            Materials = materials,
            FolderPaths = folderPaths
        };
}

/// <summary>
/// Progress report for asset loading
/// </summary>
public readonly record struct AssetLoadProgress(int Processed, int Total) {
    public double Percentage => Total > 0 ? (double)Processed / Total * 100 : 0;
}

/// <summary>
/// Event args for file deletion detection
/// </summary>
public sealed record FilesDeletionDetectedEventArgs {
    public IReadOnlyList<string> DeletedPaths { get; init; } = [];
    public bool RequiresFullRescan { get; init; }

    public FilesDeletionDetectedEventArgs(IReadOnlyList<string> deletedPaths, bool requiresFullRescan) {
        DeletedPaths = deletedPaths;
        RequiresFullRescan = requiresFullRescan;
    }
}

/// <summary>
/// Result of project state check (for updates)
/// </summary>
public sealed record ProjectStateCheckResult {
    public bool Success { get; init; }
    public string? Error { get; init; }
    public bool HasUpdates { get; init; }
    public string? Message { get; init; }

    public static ProjectStateCheckResult Failed(string error) =>
        new() { Success = false, Error = error };

    public static ProjectStateCheckResult Succeeded(bool hasUpdates, string? message = null) =>
        new() { Success = true, HasUpdates = hasUpdates, Message = message };
}
