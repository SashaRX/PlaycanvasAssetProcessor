using AssetProcessor.Infrastructure.Enums;

namespace AssetProcessor.Services.Models;

public sealed class ConnectionWorkflowResult {
    public ConnectionState State { get; init; }
    public bool HasUpdates { get; init; }
    public bool HasMissingFiles { get; init; }

    public string Message => State == ConnectionState.UpToDate
        ? "Project is up to date!"
        : HasUpdates && HasMissingFiles
            ? "Updates available and missing files found! Click Download to get them."
            : HasUpdates
                ? "Updates available! Click Download to get them."
                : "Missing files found! Click Download to get them.";
}
