using System.Collections.Generic;

namespace AssetProcessor.Services.Models;

public sealed class ConnectionProjectsBindingResult {
    public bool HasProjects { get; init; }
    public string? SelectedProjectId { get; init; }
    public IReadOnlyList<KeyValuePair<string, string>> Projects { get; init; } = [];
}
