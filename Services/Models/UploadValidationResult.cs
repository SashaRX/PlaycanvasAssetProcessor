namespace AssetProcessor.Services.Models;

public sealed class UploadValidationResult {
    public bool IsValid { get; init; }
    public string? ErrorMessage { get; init; }
}
