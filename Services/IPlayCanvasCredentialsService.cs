namespace AssetProcessor.Services {
    public interface IPlayCanvasCredentialsService {
        bool HasStoredCredentials { get; }
        string? Username { get; }
        string? GetApiKeyOrNull();
        bool TryGetApiKey(out string? apiKey);
    }
}
