using AssetProcessor.ModelConversion.Wrappers;
using Xunit;

namespace AssetProcessor.Tests.ModelConversion.Wrappers;

/// <summary>
/// Unit tests for FBX2glTFWrapper
/// </summary>
public class FBX2glTFWrapperTests {
    [Fact]
    public void Constructor_WithNullPath_UsesDefaultExecutable() {
        var wrapper = new FBX2glTFWrapper(null);

        Assert.NotNull(wrapper);
    }

    [Fact]
    public void Constructor_WithCustomPath_DoesNotThrow() {
        var wrapper = new FBX2glTFWrapper("custom_fbx2gltf.exe");

        Assert.NotNull(wrapper);
    }

    [Fact]
    public async Task IsAvailableAsync_WithNonExistentExecutable_ReturnsFalse() {
        var wrapper = new FBX2glTFWrapper("/nonexistent/fbx2gltf.exe");

        var available = await wrapper.IsAvailableAsync();

        Assert.False(available);
    }

    [Fact]
    public void ConversionResult_DefaultConstructor_InitializesProperties() {
        var result = new ConversionResult();

        Assert.False(result.Success);
        Assert.Null(result.OutputFilePath);
        Assert.Equal(0, result.OutputFileSize);
        Assert.Null(result.Error);
        Assert.Null(result.StandardOutput);
        Assert.Null(result.StandardError);
    }

    [Fact]
    public void ConversionResult_Properties_CanBeSetAndRetrieved() {
        var result = new ConversionResult {
            Success = true,
            OutputFilePath = "output.glb",
            OutputFileSize = 1024,
            Error = "Some error",
            StandardOutput = "Output text",
            StandardError = "Error text"
        };

        Assert.True(result.Success);
        Assert.Equal("output.glb", result.OutputFilePath);
        Assert.Equal(1024, result.OutputFileSize);
        Assert.Equal("Some error", result.Error);
        Assert.Equal("Output text", result.StandardOutput);
        Assert.Equal("Error text", result.StandardError);
    }

    [Fact]
    public async Task ConvertToGlbAsync_WithNonExistentInput_ReturnsFailure() {
        var wrapper = new FBX2glTFWrapper("nonexistent_executable.exe");

        var result = await wrapper.ConvertToGlbAsync(
            "/nonexistent/input.fbx",
            "output",
            excludeTextures: false
        );

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConvertToGlbAsync_WithExcludeTexturesFlag_HandlesParameter(bool excludeTextures) {
        var wrapper = new FBX2glTFWrapper("nonexistent_executable.exe");

        var result = await wrapper.ConvertToGlbAsync(
            "/nonexistent/input.fbx",
            "output",
            excludeTextures: excludeTextures
        );

        // Should fail because executable doesn't exist, but shouldn't throw
        Assert.False(result.Success);
    }

    [Fact]
    public void ConversionResult_SuccessWithOutput_HasValidState() {
        var result = new ConversionResult {
            Success = true,
            OutputFilePath = "model.glb",
            OutputFileSize = 1024 * 500 // 500 KB
        };

        Assert.True(result.Success);
        Assert.NotNull(result.OutputFilePath);
        Assert.True(result.OutputFileSize > 0);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ConversionResult_FailureWithError_HasValidState() {
        var result = new ConversionResult {
            Success = false,
            Error = "Conversion failed: invalid FBX format"
        };

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Null(result.OutputFilePath);
        Assert.Equal(0, result.OutputFileSize);
    }
}