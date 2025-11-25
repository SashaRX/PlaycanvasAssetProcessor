using AssetProcessor.ModelConversion.Core;
using AssetProcessor.ModelConversion.Pipeline;
using Xunit;

namespace AssetProcessor.Tests.ModelConversion.Pipeline;

/// <summary>
/// Unit tests for ModelConversionPipeline and related classes
/// </summary>
public class ModelConversionPipelineTests {
    [Fact]
    public void ModelConversionResult_DefaultConstructor_InitializesCollections() {
        var result = new ModelConversionResult();

        Assert.False(result.Success);
        Assert.Equal(string.Empty, result.InputPath);
        Assert.Equal(string.Empty, result.OutputDirectory);
        Assert.NotNull(result.LodFiles);
        Assert.Empty(result.LodFiles);
        Assert.NotNull(result.LodMetrics);
        Assert.Empty(result.LodMetrics);
        Assert.NotNull(result.Warnings);
        Assert.Empty(result.Warnings);
        Assert.NotNull(result.Errors);
        Assert.Empty(result.Errors);
        Assert.Equal(TimeSpan.Zero, result.Duration);
        Assert.Null(result.BaseGlbPath);
        Assert.Null(result.ManifestPath);
        Assert.Null(result.QAReportPath);
        Assert.Null(result.QAReport);
    }

    [Fact]
    public void ModelConversionResult_Properties_CanBeSetAndRetrieved() {
        var result = new ModelConversionResult {
            Success = true,
            InputPath = "input.fbx",
            OutputDirectory = "output",
            BaseGlbPath = "base.glb",
            ManifestPath = "manifest.json",
            QAReportPath = "qa_report.json",
            Duration = TimeSpan.FromSeconds(30)
        };

        Assert.True(result.Success);
        Assert.Equal("input.fbx", result.InputPath);
        Assert.Equal("output", result.OutputDirectory);
        Assert.Equal("base.glb", result.BaseGlbPath);
        Assert.Equal("manifest.json", result.ManifestPath);
        Assert.Equal("qa_report.json", result.QAReportPath);
        Assert.Equal(TimeSpan.FromSeconds(30), result.Duration);
    }

    [Fact]
    public void ModelConversionResult_CanAddLodFiles() {
        var result = new ModelConversionResult();

        result.LodFiles[LodLevel.LOD0] = "lod0.glb";
        result.LodFiles[LodLevel.LOD1] = "lod1.glb";

        Assert.Equal(2, result.LodFiles.Count);
        Assert.Equal("lod0.glb", result.LodFiles[LodLevel.LOD0]);
        Assert.Equal("lod1.glb", result.LodFiles[LodLevel.LOD1]);
    }

    [Fact]
    public void ModelConversionResult_CanAddWarningsAndErrors() {
        var result = new ModelConversionResult();

        result.Warnings.Add("Warning 1");
        result.Warnings.Add("Warning 2");
        result.Errors.Add("Error 1");

        Assert.Equal(2, result.Warnings.Count);
        Assert.Single(result.Errors);
        Assert.Contains("Warning 1", result.Warnings);
        Assert.Contains("Error 1", result.Errors);
    }

    [Fact]
    public void ToolsAvailability_DefaultConstructor_InitializesFalse() {
        var availability = new ToolsAvailability();

        Assert.False(availability.FBX2glTFAvailable);
        Assert.False(availability.GltfPackAvailable);
        Assert.False(availability.AllAvailable);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public void ToolsAvailability_AllAvailable_ReturnsCorrectValue(bool fbx2glTF, bool gltfPack, bool expectedAll) {
        var availability = new ToolsAvailability {
            FBX2glTFAvailable = fbx2glTF,
            GltfPackAvailable = gltfPack
        };

        Assert.Equal(expectedAll, availability.AllAvailable);
    }

    [Fact]
    public void ToolsAvailability_Properties_CanBeSet() {
        var availability = new ToolsAvailability {
            FBX2glTFAvailable = true,
            GltfPackAvailable = true
        };

        Assert.True(availability.FBX2glTFAvailable);
        Assert.True(availability.GltfPackAvailable);
        Assert.True(availability.AllAvailable);
    }

    [Fact]
    public void ModelConversionPipeline_Constructor_AcceptsNullPaths() {
        // Should not throw when paths are null
        var pipeline = new ModelConversionPipeline(null, null);

        Assert.NotNull(pipeline);
    }

    [Fact]
    public void ModelConversionPipeline_Constructor_AcceptsCustomPaths() {
        var pipeline = new ModelConversionPipeline("custom_fbx2gltf.exe", "custom_gltfpack.exe");

        Assert.NotNull(pipeline);
    }

    [Fact]
    public async Task CheckToolsAsync_ReturnsToolsAvailability() {
        var pipeline = new ModelConversionPipeline();
        
        var availability = await pipeline.CheckToolsAsync();

        Assert.NotNull(availability);
        // Properties should be set (either true or false)
        Assert.IsType<bool>(availability.FBX2glTFAvailable);
        Assert.IsType<bool>(availability.GltfPackAvailable);
    }

    [Fact]
    public void ModelConversionResult_LodMetrics_CanStoreMultipleLods() {
        var result = new ModelConversionResult();

        result.LodMetrics["LOD0"] = new MeshMetrics {
            TriangleCount = 5000,
            VertexCount = 3000,
            FileSize = 150000,
            SimplificationRatio = 1.0,
            CompressionMode = "None"
        };

        result.LodMetrics["LOD1"] = new MeshMetrics {
            TriangleCount = 2500,
            VertexCount = 1500,
            FileSize = 80000,
            SimplificationRatio = 0.5,
            CompressionMode = "Quantization"
        };

        Assert.Equal(2, result.LodMetrics.Count);
        Assert.Equal(5000, result.LodMetrics["LOD0"].TriangleCount);
        Assert.Equal(2500, result.LodMetrics["LOD1"].TriangleCount);
    }

    [Fact]
    public void ModelConversionResult_Duration_CanBeMeasured() {
        var result = new ModelConversionResult();
        var startTime = DateTime.Now;

        // Simulate some work
        System.Threading.Thread.Sleep(10);

        result.Duration = DateTime.Now - startTime;

        Assert.True(result.Duration.TotalMilliseconds >= 10);
        Assert.True(result.Duration.TotalMilliseconds < 1000);
    }

    [Fact]
    public void ModelConversionResult_Success_DependsOnErrors() {
        var result = new ModelConversionResult {
            Success = true
        };

        Assert.True(result.Success);
        Assert.Empty(result.Errors);

        // Add error - typically Success would be set to false
        result.Errors.Add("Some error");
        result.Success = result.Errors.Count == 0;

        Assert.False(result.Success);
    }

    [Fact]
    public void ModelConversionResult_CanHaveWarningsWithoutErrors() {
        var result = new ModelConversionResult {
            Success = true
        };

        result.Warnings.Add("Non-critical warning");

        // Warnings shouldn't affect success if there are no errors
        Assert.True(result.Success);
        Assert.Single(result.Warnings);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ModelConversionResult_LodFiles_SupportsAllLodLevels() {
        var result = new ModelConversionResult();

        result.LodFiles[LodLevel.LOD0] = "lod0.glb";
        result.LodFiles[LodLevel.LOD1] = "lod1.glb";
        result.LodFiles[LodLevel.LOD2] = "lod2.glb";
        result.LodFiles[LodLevel.LOD3] = "lod3.glb";

        Assert.Equal(4, result.LodFiles.Count);
        Assert.True(result.LodFiles.ContainsKey(LodLevel.LOD0));
        Assert.True(result.LodFiles.ContainsKey(LodLevel.LOD1));
        Assert.True(result.LodFiles.ContainsKey(LodLevel.LOD2));
        Assert.True(result.LodFiles.ContainsKey(LodLevel.LOD3));
    }
}