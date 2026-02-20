namespace AssetProcessor.Services.Models;

public sealed class ConnectionProjectSelectionResult {
    public bool HasProjects { get; init; }
    public string? SelectedProjectId { get; init; }
}
