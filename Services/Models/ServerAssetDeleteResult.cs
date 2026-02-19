namespace AssetProcessor.Services.Models;

public sealed class ServerAssetDeleteResult {
    public bool Success { get; init; }
    public bool RequiresValidCredentials { get; init; }
    public bool RefreshedAfterDelete { get; init; }
    public string? ErrorMessage { get; init; }
}
