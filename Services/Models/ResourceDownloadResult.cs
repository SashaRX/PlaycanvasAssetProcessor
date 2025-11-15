namespace AssetProcessor.Services;

public sealed record ResourceDownloadResult(bool IsSuccess, string Status, int Attempts, string? ErrorMessage = null);
