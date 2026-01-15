using AssetProcessor.Services;
using System.IO.Abstractions.TestingHelpers;
using Xunit;

namespace AssetProcessor.Tests.Services;

public class LogServiceTests {
    [Fact]
    public void LogInfo_WritesMessageToInfoFile() {
        MockFileSystem fileSystem = new();
        using LogService service = new(fileSystem);

        service.LogInfo("Test info");
        service.Dispose(); // Flush background writer

        string content = fileSystem.File.ReadAllText("info_log.txt");
        Assert.Contains("Test info", content);
    }

    [Fact]
    public void LogDebug_WritesMessageToDebugFile() {
        MockFileSystem fileSystem = new();
        using LogService service = new(fileSystem);

        service.LogDebug("Test debug");
        service.Dispose(); // Flush background writer

        string content = fileSystem.File.ReadAllText("debug_log.txt");
        Assert.Contains("Test debug", content);
    }

    [Fact]
    public void LogWarn_WritesMessageToWarningFile() {
        MockFileSystem fileSystem = new();
        using LogService service = new(fileSystem);

        service.LogWarn("Test warn");
        service.Dispose(); // Flush background writer

        string content = fileSystem.File.ReadAllText("warning_log.txt");
        Assert.Contains("Test warn", content);
    }

    [Fact]
    public void LogError_WritesMessageToErrorFile() {
        MockFileSystem fileSystem = new();
        using LogService service = new(fileSystem);

        service.LogError("Test error");
        service.Dispose(); // Flush background writer

        string content = fileSystem.File.ReadAllText("error_log.txt");
        Assert.Contains("Test error", content);
    }
}
