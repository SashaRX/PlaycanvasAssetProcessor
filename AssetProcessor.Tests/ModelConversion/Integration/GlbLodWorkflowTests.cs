using AssetProcessor.ModelConversion.Core;
using AssetProcessor.ModelConversion.Viewer;
using System.IO;
using Xunit;

namespace AssetProcessor.Tests.ModelConversion.Integration;

/// <summary>
/// Integration tests for GLB LOD workflow scenarios
/// </summary>
public class GlbLodWorkflowTests {
    [Fact]
    public void CompleteWorkflow_FindLodFiles_WithMultipleLods() {
        var tempDir = Directory.CreateTempSubdirectory("GlbLodWorkflow");
        try {
            var fbxPath = Path.Combine(tempDir.FullName, "model.fbx");
            File.WriteAllText(fbxPath, "fake fbx content");

            // Create multiple LOD files
            var glbDir = Path.Combine(tempDir.FullName, "glb");
            Directory.CreateDirectory(glbDir);

            CreateMinimalGlbFile(Path.Combine(glbDir, "model_lod0.glb"));
            CreateMinimalGlbFile(Path.Combine(glbDir, "model_lod1.glb"));
            CreateMinimalGlbFile(Path.Combine(glbDir, "model_lod2.glb"));

            // Find LOD files
            var lodInfos = GlbLodHelper.FindGlbLodFiles(fbxPath);
            var lodPaths = GlbLodHelper.GetLodFilePaths(fbxPath);
            var hasLods = GlbLodHelper.HasGlbLodFiles(fbxPath);

            // Verify workflow
            Assert.True(hasLods);
            Assert.NotEmpty(lodInfos);
            Assert.NotEmpty(lodPaths);
            Assert.Equal(lodInfos.Count, lodPaths.Count);
        } finally {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void CompleteWorkflow_NoLodFiles_ReturnsEmptyResults() {
        var tempDir = Directory.CreateTempSubdirectory("GlbLodWorkflow");
        try {
            var fbxPath = Path.Combine(tempDir.FullName, "model.fbx");
            File.WriteAllText(fbxPath, "fake fbx content");

            var lodInfos = GlbLodHelper.FindGlbLodFiles(fbxPath);
            var lodPaths = GlbLodHelper.GetLodFilePaths(fbxPath);
            var hasLods = GlbLodHelper.HasGlbLodFiles(fbxPath);

            Assert.False(hasLods);
            Assert.Empty(lodInfos);
            Assert.Empty(lodPaths);
        } finally {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void LodInfo_FormattingConsistency_AcrossMultipleSizes() {
        var sizes = new long[] { 0, 512, 1024, 1536, 1048576, 10485760 };
        var lodInfos = new List<GlbLodHelper.LodInfo>();

        foreach (var size in sizes) {
            lodInfos.Add(new GlbLodHelper.LodInfo { FileSize = size });
        }

        // Verify all formats are valid and non-null
        foreach (var info in lodInfos) {
            Assert.NotNull(info.FileSizeFormatted);
            Assert.NotEmpty(info.FileSizeFormatted);
            Assert.True(
                info.FileSizeFormatted.EndsWith(" B") ||
                info.FileSizeFormatted.EndsWith(" KB") ||
                info.FileSizeFormatted.EndsWith(" MB")
            );
        }
    }

    [Fact]
    public void QuantizationSettings_AllPresets_CanBeUsedInSequence() {
        var presets = new[] {
            QuantizationSettings.CreateDefault(),
            QuantizationSettings.CreateHighQuality(),
            QuantizationSettings.CreateMinSize()
        };

        // Verify all presets are valid and have expected TexCoord bits
        foreach (var preset in presets) {
            Assert.NotNull(preset);
            Assert.Equal(16, preset.TexCoordBits);
            Assert.True(preset.PositionBits >= 1);
            Assert.True(preset.NormalBits >= 1);
            Assert.True(preset.ColorBits >= 1);
        }

        // Verify they're all different instances
        for (int i = 0; i < presets.Length; i++) {
            for (int j = i + 1; j < presets.Length; j++) {
                Assert.NotSame(presets[i], presets[j]);
            }
        }
    }

    [Fact]
    public void CompressionMode_AllModes_CanBeIteratedAndUsed() {
        var modes = Enum.GetValues<CompressionMode>();

        foreach (var mode in modes) {
            // Verify each mode can be used in a string context
            var modeString = mode.ToString();
            Assert.NotNull(modeString);
            Assert.NotEmpty(modeString);

            // Verify can be parsed back
            var parsed = Enum.Parse<CompressionMode>(modeString);
            Assert.Equal(mode, parsed);

            // Verify has integer value
            int intValue = (int)mode;
            Assert.True(intValue >= 0);
            Assert.True(intValue < 10);
        }
    }

    private void CreateMinimalGlbFile(string path) {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        // GLB header
        writer.Write(0x46546C67u); // magic: "glTF"
        writer.Write(2u);           // version
        writer.Write(44u);          // length

        // JSON chunk header
        writer.Write(20u);          // chunk length
        writer.Write(0x4E4F534Au); // chunk type: "JSON"

        // Minimal JSON
        var json = "{\"asset\":{\"version\":\"2.0\"}}";
        var jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);
        writer.Write(jsonBytes);
    }
}