using AssetProcessor.Settings;
using System;
using Xunit;

namespace AssetProcessor.Tests;

public class SecureSettingsTests : IDisposable {
    private const string MasterPasswordVariable = "PLAYCANVAS_MASTER_PASSWORD";
    private readonly string? previousMasterPassword;

    public SecureSettingsTests() {
        previousMasterPassword = Environment.GetEnvironmentVariable(MasterPasswordVariable);
        if (!OperatingSystem.IsWindows()) {
            Environment.SetEnvironmentVariable(MasterPasswordVariable, "UnitTestSecret!");
        }
    }

    [Fact]
    public void SettingApiKey_EncryptsAndDecryptsSuccessfully() {
        const string plainApiKey = "test-api-key-123";

        AppSettings.Default.PlaycanvasApiKey = plainApiKey;

        Assert.NotEqual(plainApiKey, AppSettings.Default.PlaycanvasApiKey);
        Assert.True(AppSettings.Default.TryGetDecryptedPlaycanvasApiKey(out string? decrypted));
        Assert.Equal(plainApiKey, decrypted);
    }

    public void Dispose() {
        AppSettings.Default.PlaycanvasApiKey = string.Empty;
        AppSettings.Default.Save();
        Environment.SetEnvironmentVariable(MasterPasswordVariable, previousMasterPassword);
    }
}
