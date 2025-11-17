namespace AssetProcessor.Services.Models;

public sealed record ProjectUpdateContext(
    string ProjectFolderPath,
    string ProjectId,
    string BranchId,
    string ApiKey);
