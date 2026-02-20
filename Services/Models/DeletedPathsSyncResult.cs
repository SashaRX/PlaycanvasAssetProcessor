namespace AssetProcessor.Services.Models;

public sealed class DeletedPathsSyncResult {
    public bool HasDeletedPaths { get; init; }
    public int DeletedPathCount { get; init; }
    public int ResetCount { get; init; }
}
