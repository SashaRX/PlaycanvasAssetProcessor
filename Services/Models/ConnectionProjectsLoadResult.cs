namespace AssetProcessor.Services.Models;

public sealed class ConnectionProjectsLoadResult {
    public bool IsValid { get; init; }
    public bool HasProjects { get; init; }
    public string UserId { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
}
