namespace AssetProcessor.Services.Models;

public sealed class ServerStatusSyncResult {
    public bool ServerWasEmpty { get; init; }
    public int ResetCount { get; init; }
}
