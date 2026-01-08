using AssetProcessor.Services.Models;

namespace AssetProcessor.Services;

/// <summary>
/// Monitors project folder for file system changes.
/// Events are used here as these are non-critical notifications.
/// </summary>
public interface IProjectFileWatcherService : IDisposable {
    /// <summary>
    /// Gets whether the watcher is currently active.
    /// </summary>
    bool IsWatching { get; }

    /// <summary>
    /// Gets the path currently being watched.
    /// </summary>
    string? WatchedPath { get; }

    /// <summary>
    /// Starts watching the specified folder for file deletions.
    /// </summary>
    /// <param name="projectFolderPath">Path to the project folder to watch</param>
    void Start(string projectFolderPath);

    /// <summary>
    /// Stops watching the current folder.
    /// </summary>
    void Stop();

    /// <summary>
    /// Raised when file deletions are detected (after debounce).
    /// Contains list of deleted file paths.
    /// </summary>
    event EventHandler<FilesDeletionDetectedEventArgs>? FilesDeletionDetected;
}
