using AssetProcessor.Services.Models;
using System.Collections.Concurrent;
using System.IO;

namespace AssetProcessor.Services;

/// <summary>
/// Monitors project folder for file system changes.
/// Handles debouncing and batching of deletion events.
/// </summary>
public sealed class ProjectFileWatcherService : IProjectFileWatcherService {
    private readonly ILogService logService;
    private FileSystemWatcher? watcher;
    private readonly ConcurrentQueue<string> pendingDeletedPaths = new();
    private int refreshPending; // 0 = no refresh pending, 1 = refresh scheduled
    private int fullRescanPending; // 0 = no rescan pending, 1 = rescan scheduled
    private bool disposed;

    // Debounce delay in milliseconds
    private const int DebounceDelayMs = 500;

    public ProjectFileWatcherService(ILogService logService) {
        this.logService = logService ?? throw new ArgumentNullException(nameof(logService));
    }

    public bool IsWatching => watcher?.EnableRaisingEvents == true;

    public string? WatchedPath { get; private set; }

    public event EventHandler<FilesDeletionDetectedEventArgs>? FilesDeletionDetected;

    public void Start(string projectFolderPath) {
        if (disposed) {
            throw new ObjectDisposedException(nameof(ProjectFileWatcherService));
        }

        Stop(); // Stop existing watcher if any

        if (string.IsNullOrEmpty(projectFolderPath) || !Directory.Exists(projectFolderPath)) {
            logService.LogWarn($"Cannot start file watcher: path is invalid or does not exist: {projectFolderPath}");
            return;
        }

        try {
            watcher = new FileSystemWatcher(projectFolderPath) {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                EnableRaisingEvents = true
            };

            watcher.Deleted += OnFileDeleted;
            watcher.Renamed += OnFileRenamed;

            WatchedPath = projectFolderPath;
            logService.LogInfo($"FileWatcher started for: {projectFolderPath}");
        } catch (Exception ex) {
            logService.LogError($"Failed to start FileSystemWatcher: {ex.Message}");
            watcher?.Dispose();
            watcher = null;
        }
    }

    public void Stop() {
        if (watcher != null) {
            watcher.EnableRaisingEvents = false;
            watcher.Deleted -= OnFileDeleted;
            watcher.Renamed -= OnFileRenamed;
            watcher.Dispose();
            watcher = null;
            WatchedPath = null;
            logService.LogInfo("FileWatcher stopped");
        }

        // Clear pending items
        while (pendingDeletedPaths.TryDequeue(out _)) { }
        Interlocked.Exchange(ref refreshPending, 0);
        Interlocked.Exchange(ref fullRescanPending, 0);
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e) {
        // Check if this might be a directory deletion
        // FileSystemWatcher doesn't distinguish file vs directory in the event
        // If the path has no extension, it's likely a directory
        bool isLikelyDirectory = string.IsNullOrEmpty(Path.GetExtension(e.FullPath));

        // Ignore build directories (created/deleted during model conversion)
        if (e.FullPath.Contains("\\build\\") || e.FullPath.EndsWith("\\build")) {
            return;
        }

        if (isLikelyDirectory) {
            logService.LogInfo($"Directory likely deleted: {e.FullPath}, scheduling full rescan");
            ScheduleFullRescan();
        } else {
            pendingDeletedPaths.Enqueue(e.FullPath);
            ScheduleDebouncedRefresh();
        }
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e) {
        // Moving to trash also triggers rename events
        // Ignore build directories
        if (e.OldFullPath.Contains("\\build\\") || e.OldFullPath.EndsWith("\\build")) {
            return;
        }

        pendingDeletedPaths.Enqueue(e.OldFullPath);
        ScheduleDebouncedRefresh();
    }

    private void ScheduleFullRescan() {
        if (Interlocked.CompareExchange(ref fullRescanPending, 1, 0) == 0) {
            Task.Delay(DebounceDelayMs).ContinueWith(_ => {
                Interlocked.Exchange(ref fullRescanPending, 0);

                // Also clear the file deletion queue since we're doing a full rescan
                while (pendingDeletedPaths.TryDequeue(out string? _)) { }
                Interlocked.Exchange(ref refreshPending, 0);

                logService.LogInfo("Performing full rescan due to directory deletion");

                // Raise event with RequiresFullRescan = true
                FilesDeletionDetected?.Invoke(this, new FilesDeletionDetectedEventArgs([], true));
            });
        }
    }

    private void ScheduleDebouncedRefresh() {
        if (Interlocked.CompareExchange(ref refreshPending, 1, 0) == 0) {
            Task.Delay(DebounceDelayMs).ContinueWith(_ => {
                ProcessPendingDeletions();
            });
        }
    }

    private void ProcessPendingDeletions() {
        // Reset the pending flag
        Interlocked.Exchange(ref refreshPending, 0);

        // Drain the queue
        var deletedPaths = new List<string>();
        while (pendingDeletedPaths.TryDequeue(out string? deletedPath)) {
            if (!string.IsNullOrEmpty(deletedPath)) {
                deletedPaths.Add(deletedPath);
            }
        }

        if (deletedPaths.Count == 0) {
            return;
        }

        logService.LogInfo($"File deletions detected: {deletedPaths.Count} files");

        // Raise event with the list of deleted paths
        FilesDeletionDetected?.Invoke(this, new FilesDeletionDetectedEventArgs(deletedPaths, false));
    }

    public void Dispose() {
        if (!disposed) {
            Stop();
            disposed = true;
        }
    }
}
