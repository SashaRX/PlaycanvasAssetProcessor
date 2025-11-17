namespace AssetProcessor.Services;

public interface ILogService {
    void LogDebug(string message);
    void LogInfo(string message);
    void LogWarn(string message);
    void LogError(string? message);
}
