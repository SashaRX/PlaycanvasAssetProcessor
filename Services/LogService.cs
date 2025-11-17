using System.Diagnostics;
using System.IO.Abstractions;

namespace AssetProcessor.Services;

public sealed class LogService : ILogService {
    private readonly IFileSystem fileSystem;
    private readonly object logLock = new();

    public LogService(IFileSystem fileSystem) {
        this.fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public void LogDebug(string message) {
        ArgumentNullException.ThrowIfNull(message);
        WriteLog("debug_log.txt", message, writeToDebug: true);
    }

    public void LogInfo(string message) {
        ArgumentNullException.ThrowIfNull(message);
        WriteLog("info_log.txt", message);
    }

    public void LogWarn(string message) {
        ArgumentNullException.ThrowIfNull(message);
        WriteLog("warning_log.txt", message, isWarning: true);
    }

    public void LogError(string? message) {
        WriteLog("error_log.txt", message ?? string.Empty, writeToDebug: true);
    }

    private void WriteLog(string fileName, string message, bool isWarning = false, bool writeToDebug = false) {
        string formattedMessage = $"{DateTime.Now}: {message}{Environment.NewLine}";

        lock (logLock) {
            fileSystem.File.AppendAllText(fileName, formattedMessage);

            if (isWarning || writeToDebug) {
                string prefix = isWarning ? "WARNING: " : string.Empty;
                Debug.WriteLine($"{DateTime.Now}: {prefix}{message}");
            }
        }
    }
}
