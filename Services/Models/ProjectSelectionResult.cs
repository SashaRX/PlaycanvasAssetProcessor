using System.Collections.Generic;

namespace AssetProcessor.Services.Models;

public sealed class ProjectSelectionResult {
    public ProjectSelectionResult(Dictionary<string, string> projects, string? selectedProjectId, string userId, string userName) {
        Projects = projects;
        SelectedProjectId = selectedProjectId;
        UserId = userId;
        UserName = userName;
    }

    public Dictionary<string, string> Projects { get; }
    public string? SelectedProjectId { get; }
    public string UserId { get; }
    public string UserName { get; }
}
