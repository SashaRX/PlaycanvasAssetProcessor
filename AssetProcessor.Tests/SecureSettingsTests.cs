using AssetProcessor.Helpers;
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

    [Fact]
    public void AesEncryption_UsesRandomSalt() {
        Skip.If(OperatingSystem.IsWindows(), "DPAPI path is used on Windows.");

        Environment.SetEnvironmentVariable(MasterPasswordVariable, "UnitTestSecret!");

        string first = SecureStorageHelper.Protect("test-api-key-123");
        string second = SecureStorageHelper.Protect("test-api-key-123");

        Assert.StartsWith("aes:", first, StringComparison.Ordinal);
        Assert.StartsWith("aes:", second, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void AesDecryption_WithWrongMasterPassword_Fails() {
        Skip.If(OperatingSystem.IsWindows(), "DPAPI path is used on Windows.");

        Environment.SetEnvironmentVariable(MasterPasswordVariable, "UnitTestSecret!");
        string cipher = SecureStorageHelper.Protect("test-api-key-123");

        Environment.SetEnvironmentVariable(MasterPasswordVariable, "AnotherSecret!");

        bool success = SecureStorageHelper.TryUnprotect(cipher, out string? decrypted, out bool wasProtected);

        Assert.True(wasProtected);
        Assert.False(success);
        Assert.Null(decrypted);
    }

    public void Dispose() {
        AppSettings.Default.PlaycanvasApiKey = string.Empty;
        AppSettings.Default.Save();
        Environment.SetEnvironmentVariable(MasterPasswordVariable, previousMasterPassword);
    }
}
