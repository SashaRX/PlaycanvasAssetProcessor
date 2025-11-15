namespace AssetProcessor.Services;

public interface ILogService {
    void LogInfo(string message);
    void LogWarn(string message);
    void LogError(string? message);
}
