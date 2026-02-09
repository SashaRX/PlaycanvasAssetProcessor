using System.Reflection;
using AssetProcessor;
using Xunit;

namespace AssetProcessor.Tests.Settings;

public class SettingsWindowTests {
    [Theory]
    [InlineData("ktx version: v4.3.2", @"version:\s*(.+)", "v4.3.2")]
    [InlineData("FBX2glTF version 0.13.1", @"version[:\s]+(\S+)", "0.13.1")]
    [InlineData("meshoptimizer v0.22", @"v(\S+)", "0.22")]
    public void ExtractMatch_WhenPatternMatches_ReturnsCapturedValue(string input, string pattern, string expected) {
        var method = typeof(SettingsWindow).GetMethod("ExtractMatch", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        var result = method!.Invoke(null, [input, pattern]);

        Assert.Equal(expected, Assert.IsType<string>(result));
    }

    [Fact]
    public void ExtractMatch_WhenPatternDoesNotMatch_ReturnsUnknown() {
        var method = typeof(SettingsWindow).GetMethod("ExtractMatch", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        var result = method!.Invoke(null, ["no-version-text", @"version:\s*(.+)"]);

        Assert.Equal("unknown", Assert.IsType<string>(result));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ExtractMatch_WhenInputIsEmptyOrWhitespace_ReturnsUnknown(string input) {
        var method = typeof(SettingsWindow).GetMethod("ExtractMatch", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        var result = method!.Invoke(null, [input, @"version:\s*(.+)"]);

        Assert.Equal("unknown", Assert.IsType<string>(result));
    }


    [Fact]
    public void TryTerminateProcess_WithNull_DoesNotThrow() {
        var method = typeof(SettingsWindow).GetMethod("TryTerminateProcess", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        var ex = Record.Exception(() => method!.Invoke(null, [null]));

        Assert.Null(ex);
    }


    [Fact]
    public void RunToolVersionCheckAsync_Signature_ContainsTriggerButtonParameter() {
        var method = typeof(SettingsWindow).GetMethod("RunToolVersionCheckAsync", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        var parameters = method!.GetParameters();

        Assert.Equal(5, parameters.Length);
        Assert.Equal("triggerButton", parameters[3].Name);
        Assert.Equal("Button", Nullable.GetUnderlyingType(parameters[3].ParameterType)?.Name ?? parameters[3].ParameterType.Name);
    }


    [Fact]
    public void ExtractMatch_WhenPatternIsInvalid_ReturnsUnknown() {
        var method = typeof(SettingsWindow).GetMethod("ExtractMatch", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        var result = method!.Invoke(null, ["ktx version: v4.3.2", "("]);

        Assert.Equal("unknown", Assert.IsType<string>(result));
    }

    [Fact]
    public void ExtractMatch_WhenPatternIsWhitespace_ReturnsUnknown() {
        var method = typeof(SettingsWindow).GetMethod("ExtractMatch", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        var result = method!.Invoke(null, ["ktx version: v4.3.2", "   "]);

        Assert.Equal("unknown", Assert.IsType<string>(result));
    }

}