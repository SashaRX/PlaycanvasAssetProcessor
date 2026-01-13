using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Abstractions;

namespace AssetProcessor.Services;

public sealed class LogService : ILogService, IDisposable {
    private readonly IFileSystem fileSystem;
    private readonly BlockingCollection<LogEntry> logQueue = new(1000);
    private readonly Thread writerThread;
    private volatile bool disposed;

    private record LogEntry(string FileName, string Message, bool IsWarning, bool WriteToDebug);

    public LogService(IFileSystem fileSystem) {
        this.fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

        // Start background writer thread
        writerThread = new Thread(ProcessLogQueue) {
            IsBackground = true,
            Name = "LogService Writer"
        };
        writerThread.Start();
    }

    public void LogDebug(string message) {
        ArgumentNullException.ThrowIfNull(message);
        QueueLog("debug_log.txt", message, writeToDebug: true);
    }

    public void LogInfo(string message) {
        ArgumentNullException.ThrowIfNull(message);
        QueueLog("info_log.txt", message);
    }

    public void LogWarn(string message) {
        ArgumentNullException.ThrowIfNull(message);
        QueueLog("warning_log.txt", message, isWarning: true);
    }

    public void LogError(string? message) {
        QueueLog("error_log.txt", message ?? string.Empty, writeToDebug: true);
    }

    private void QueueLog(string fileName, string message, bool isWarning = false, bool writeToDebug = false) {
        if (disposed) return;

        string formattedMessage = $"{DateTime.Now}: {message}{Environment.NewLine}";

        // Non-blocking add - if queue is full, message is dropped
        logQueue.TryAdd(new LogEntry(fileName, formattedMessage, isWarning, writeToDebug));
    }

    private void ProcessLogQueue() {
        // Group writes by file for efficiency
        var pendingWrites = new Dictionary<string, List<string>>();

        while (!disposed) {
            try {
                // Wait for entries with timeout to allow periodic flushing
                if (logQueue.TryTake(out var entry, TimeSpan.FromMilliseconds(100))) {
                    if (!pendingWrites.TryGetValue(entry.FileName, out var messages)) {
                        messages = new List<string>();
                        pendingWrites[entry.FileName] = messages;
                    }
                    messages.Add(entry.Message);

                    // Drain more entries if available (batch writes)
                    while (logQueue.TryTake(out var moreEntry)) {
                        if (!pendingWrites.TryGetValue(moreEntry.FileName, out messages)) {
                            messages = new List<string>();
                            pendingWrites[moreEntry.FileName] = messages;
                        }
                        messages.Add(moreEntry.Message);
                    }
                }

                // Flush pending writes
                foreach (var (fileName, messages) in pendingWrites) {
                    if (messages.Count > 0) {
                        try {
                            fileSystem.File.AppendAllText(fileName, string.Join("", messages));
                        } catch {
                            // Ignore file write errors to prevent log loop
                        }
                        messages.Clear();
                    }
                }
            } catch (InvalidOperationException) {
                // Queue was marked complete
                break;
            }
        }

        // Final flush
        foreach (var (fileName, messages) in pendingWrites) {
            if (messages.Count > 0) {
                try {
                    fileSystem.File.AppendAllText(fileName, string.Join("", messages));
                } catch {
                    // Ignore
                }
            }
        }
    }

    public void Dispose() {
        if (disposed) return;
        disposed = true;

        logQueue.CompleteAdding();
        writerThread.Join(TimeSpan.FromSeconds(2));
        logQueue.Dispose();
    }
}
