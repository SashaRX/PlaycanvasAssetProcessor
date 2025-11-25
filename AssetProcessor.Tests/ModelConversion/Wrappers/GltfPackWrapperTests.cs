using AssetProcessor.ModelConversion.Core;
using AssetProcessor.ModelConversion.Wrappers;
using Xunit;

namespace AssetProcessor.Tests.ModelConversion.Wrappers;

/// <summary>
/// Unit tests for GltfPackWrapper
/// </summary>
public class GltfPackWrapperTests {
    [Fact]
    public void Constructor_WithNullPath_UsesDefaultExecutable() {
        var wrapper = new GltfPackWrapper(null);

        Assert.NotNull(wrapper);
        Assert.NotNull(wrapper.ExecutablePath);
    }

    [Fact]
    public void Constructor_WithCustomPath_StoresPath() {
        var customPath = "custom_gltfpack.exe";
        var wrapper = new GltfPackWrapper(customPath);

        Assert.Equal(customPath, wrapper.ExecutablePath);
    }

    [Fact]
    public async Task IsAvailableAsync_WithNonExistentExecutable_ReturnsFalse() {
        var wrapper = new GltfPackWrapper("/nonexistent/gltfpack.exe");

        var available = await wrapper.IsAvailableAsync();

        Assert.False(available);
    }

    [Fact]
    public void GltfPackResult_DefaultConstructor_InitializesProperties() {
        var result = new GltfPackResult();

        Assert.False(result.Success);
        Assert.Null(result.OutputFilePath);
        Assert.Equal(0, result.OutputFileSize);
        Assert.Equal(0, result.TriangleCount);
        Assert.Equal(0, result.VertexCount);
        Assert.Null(result.Error);
        Assert.Null(result.StandardOutput);
        Assert.Null(result.StandardError);
    }

    [Fact]
    public void GltfPackResult_Properties_CanBeSetAndRetrieved() {
        var result = new GltfPackResult {
            Success = true,
            OutputFilePath = "optimized.glb",
            OutputFileSize = 2048,
            TriangleCount = 1500,
            VertexCount = 900,
            Error = "Warning message",
            StandardOutput = "Processing...",
            StandardError = "stderr output"
        };

        Assert.True(result.Success);
        Assert.Equal("optimized.glb", result.OutputFilePath);
        Assert.Equal(2048, result.OutputFileSize);
        Assert.Equal(1500, result.TriangleCount);
        Assert.Equal(900, result.VertexCount);
        Assert.Equal("Warning message", result.Error);
        Assert.Equal("Processing...", result.StandardOutput);
        Assert.Equal("stderr output", result.StandardError);
    }

    [Fact]
    public async Task OptimizeAsync_WithNonExistentInput_ReturnsFailure() {
        var wrapper = new GltfPackWrapper("nonexistent_executable.exe");
        var lodSettings = new LodSettings {
            Level = LodLevel.LOD0,
            SimplificationRatio = 1.0,
            AggressiveSimplification = false
        };

        var result = await wrapper.OptimizeAsync(
            "/nonexistent/input.glb",
            "output.glb",
            lodSettings,
            CompressionMode.None
        );

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData(CompressionMode.None)]
    [InlineData(CompressionMode.Quantization)]
    [InlineData(CompressionMode.MeshOpt)]
    [InlineData(CompressionMode.MeshOptAggressive)]
    public async Task OptimizeAsync_WithDifferentCompressionModes_HandlesParameter(CompressionMode mode) {
        var wrapper = new GltfPackWrapper("nonexistent_executable.exe");
        var lodSettings = new LodSettings {
            Level = LodLevel.LOD0,
            SimplificationRatio = 1.0,
            AggressiveSimplification = false
        };

        var result = await wrapper.OptimizeAsync(
            "/nonexistent/input.glb",
            "output.glb",
            lodSettings,
            mode
        );

        // Should fail because executable doesn't exist, but shouldn't throw
        Assert.False(result.Success);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OptimizeAsync_WithExcludeTexturesFlag_HandlesParameter(bool excludeTextures) {
        var wrapper = new GltfPackWrapper("nonexistent_executable.exe");
        var lodSettings = new LodSettings {
            Level = LodLevel.LOD0,
            SimplificationRatio = 1.0,
            AggressiveSimplification = false
        };

        var result = await wrapper.OptimizeAsync(
            "/nonexistent/input.glb",
            "output.glb",
            lodSettings,
            CompressionMode.None,
            excludeTextures: excludeTextures
        );

        // Should fail because executable doesn't exist, but shouldn't throw
        Assert.False(result.Success);
    }

    [Fact]
    public async Task OptimizeAsync_WithQuantizationSettings_HandlesParameter() {
        var wrapper = new GltfPackWrapper("nonexistent_executable.exe");
        var lodSettings = new LodSettings {
            Level = LodLevel.LOD0,
            SimplificationRatio = 1.0,
            AggressiveSimplification = false
        };
        var quantization = QuantizationSettings.CreateDefault();

        var result = await wrapper.OptimizeAsync(
            "/nonexistent/input.glb",
            "output.glb",
            lodSettings,
            CompressionMode.Quantization,
            quantization: quantization
        );

        // Should fail because executable doesn't exist, but shouldn't throw
        Assert.False(result.Success);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OptimizeAsync_WithGenerateReportFlag_HandlesParameter(bool generateReport) {
        var wrapper = new GltfPackWrapper("nonexistent_executable.exe");
        var lodSettings = new LodSettings {
            Level = LodLevel.LOD0,
            SimplificationRatio = 1.0,
            AggressiveSimplification = false
        };

        var result = await wrapper.OptimizeAsync(
            "/nonexistent/input.glb",
            "output.glb",
            lodSettings,
            CompressionMode.None,
            generateReport: generateReport
        );

        // Should fail because executable doesn't exist, but shouldn't throw
        Assert.False(result.Success);
    }

    [Fact]
    public void GltfPackResult_SuccessWithMetrics_HasValidState() {
        var result = new GltfPackResult {
            Success = true,
            OutputFilePath = "optimized.glb",
            OutputFileSize = 1024 * 100, // 100 KB
            TriangleCount = 2000,
            VertexCount = 1200
        };

        Assert.True(result.Success);
        Assert.NotNull(result.OutputFilePath);
        Assert.True(result.OutputFileSize > 0);
        Assert.True(result.TriangleCount > 0);
        Assert.True(result.VertexCount > 0);
        Assert.Null(result.Error);
    }

    [Fact]
    public void GltfPackResult_FailureWithError_HasValidState() {
        var result = new GltfPackResult {
            Success = false,
            Error = "Optimization failed: invalid GLB format"
        };

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Null(result.OutputFilePath);
        Assert.Equal(0, result.OutputFileSize);
        Assert.Equal(0, result.TriangleCount);
        Assert.Equal(0, result.VertexCount);
    }

    [Fact]
    public void GltfPackWrapper_ExecutablePath_IsAccessible() {
        var customPath = "my_gltfpack.exe";
        var wrapper = new GltfPackWrapper(customPath);

        // ExecutablePath property should be publicly accessible
        var path = wrapper.ExecutablePath;

        Assert.Equal(customPath, path);
    }
}