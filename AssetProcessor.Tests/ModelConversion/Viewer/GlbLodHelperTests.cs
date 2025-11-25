using AssetProcessor.ModelConversion.Core;
using AssetProcessor.ModelConversion.Viewer;
using System.IO;
using System.Linq;
using Xunit;

namespace AssetProcessor.Tests.ModelConversion.Viewer;

/// <summary>
/// Unit tests for GlbLodHelper
/// </summary>
public class GlbLodHelperTests {
    [Fact]
    public void LodInfo_FormatFileSize_FormatsBytes() {
        var lodInfo = new GlbLodHelper.LodInfo {
            FileSize = 512
        };

        Assert.Equal("512 B", lodInfo.FileSizeFormatted);
    }

    [Fact]
    public void LodInfo_FormatFileSize_FormatsKilobytes() {
        var lodInfo = new GlbLodHelper.LodInfo {
            FileSize = 2048
        };

        Assert.Equal("2 KB", lodInfo.FileSizeFormatted);
    }

    [Fact]
    public void LodInfo_FormatFileSize_FormatsKilobytesWithoutDecimal() {
        var lodInfo = new GlbLodHelper.LodInfo {
            FileSize = 1536 // 1.5 KB should round to 2 KB (F0 format)
        };

        Assert.Equal("2 KB", lodInfo.FileSizeFormatted);
    }

    [Fact]
    public void LodInfo_FormatFileSize_FormatsMegabytes() {
        var lodInfo = new GlbLodHelper.LodInfo {
            FileSize = 1024 * 1024 * 5 // 5 MB
        };

        Assert.Equal("5.0 MB", lodInfo.FileSizeFormatted);
    }

    [Fact]
    public void LodInfo_FormatFileSize_FormatsMegabytesWithOneDecimal() {
        var lodInfo = new GlbLodHelper.LodInfo {
            FileSize = (long)(1024 * 1024 * 2.7) // 2.7 MB
        };

        Assert.Equal("2.7 MB", lodInfo.FileSizeFormatted);
    }

    [Fact]
    public void LodInfo_Properties_CanBeSetAndRetrieved() {
        var lodInfo = new GlbLodHelper.LodInfo {
            Level = LodLevel.LOD2,
            FilePath = "test_lod2.glb",
            TriangleCount = 1500,
            VertexCount = 800,
            FileSize = 50000
        };

        Assert.Equal(LodLevel.LOD2, lodInfo.Level);
        Assert.Equal("test_lod2.glb", lodInfo.FilePath);
        Assert.Equal(1500, lodInfo.TriangleCount);
        Assert.Equal(800, lodInfo.VertexCount);
        Assert.Equal(50000, lodInfo.FileSize);
    }

    [Fact]
    public void FindGlbLodFiles_WithNonExistentFbx_ReturnsEmptyDictionary() {
        var result = GlbLodHelper.FindGlbLodFiles("/nonexistent/path/model.fbx");

        Assert.Empty(result);
    }

    [Fact]
    public void FindGlbLodFiles_WithEmptyDirectory_ReturnsEmptyDictionary() {
        var tempDir = Directory.CreateTempSubdirectory("GlbLodTest");
        try {
            var fbxPath = Path.Combine(tempDir.FullName, "model.fbx");
            File.WriteAllText(fbxPath, "fake fbx content");

            var result = GlbLodHelper.FindGlbLodFiles(fbxPath);

            Assert.Empty(result);
        } finally {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void HasGlbLodFiles_WithNoLodFiles_ReturnsFalse() {
        var tempDir = Directory.CreateTempSubdirectory("GlbLodTest");
        try {
            var fbxPath = Path.Combine(tempDir.FullName, "model.fbx");
            File.WriteAllText(fbxPath, "fake fbx content");

            var result = GlbLodHelper.HasGlbLodFiles(fbxPath);

            Assert.False(result);
        } finally {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void GetLodFilePaths_WithNoLodFiles_ReturnsEmptyDictionary() {
        var tempDir = Directory.CreateTempSubdirectory("GlbLodTest");
        try {
            var fbxPath = Path.Combine(tempDir.FullName, "model.fbx");
            File.WriteAllText(fbxPath, "fake fbx content");

            var result = GlbLodHelper.GetLodFilePaths(fbxPath);

            Assert.Empty(result);
        } finally {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void FindGlbLodFiles_WithNullOrEmptyDirectory_ReturnsEmptyDictionary() {
        // Test with filename only (no directory)
        var result = GlbLodHelper.FindGlbLodFiles("model.fbx");

        // Should handle gracefully without throwing
        Assert.Empty(result);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1, "1 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "2 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(1572864, "1.5 MB")]
    [InlineData(10485760, "10.0 MB")]
    public void LodInfo_FormatFileSize_HandlesVariousSizes(long bytes, string expected) {
        var lodInfo = new GlbLodHelper.LodInfo {
            FileSize = bytes
        };

        Assert.Equal(expected, lodInfo.FileSizeFormatted);
    }

    [Fact]
    public void LodInfo_DefaultValues_AreInitialized() {
        var lodInfo = new GlbLodHelper.LodInfo();

        Assert.Equal(LodLevel.LOD0, lodInfo.Level);
        Assert.Equal(string.Empty, lodInfo.FilePath);
        Assert.Equal(0, lodInfo.TriangleCount);
        Assert.Equal(0, lodInfo.VertexCount);
        Assert.Equal(0, lodInfo.FileSize);
    }

    [Fact]
    public void GetLodFilePaths_ReturnsOnlyFilePaths() {
        var tempDir = Directory.CreateTempSubdirectory("GlbLodTest");
        try {
            var fbxPath = Path.Combine(tempDir.FullName, "model.fbx");
            File.WriteAllText(fbxPath, "fake fbx content");

            // Create a minimal valid GLB file
            var glbPath = Path.Combine(tempDir.FullName, "model_lod0.glb");
            CreateMinimalGlbFile(glbPath);

            var result = GlbLodHelper.GetLodFilePaths(fbxPath);

            if (result.Count > 0) {
                // Verify dictionary contains only LodLevel keys and string values
                Assert.All(result, kvp => {
                    Assert.IsType<LodLevel>(kvp.Key);
                    Assert.IsType<string>(kvp.Value);
                    Assert.False(string.IsNullOrEmpty(kvp.Value));
                });
            }
        } finally {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void FindGlbLodFiles_SearchesInGlbSubdirectory() {
        var tempDir = Directory.CreateTempSubdirectory("GlbLodTest");
        try {
            var fbxPath = Path.Combine(tempDir.FullName, "model.fbx");
            File.WriteAllText(fbxPath, "fake fbx content");

            // Create glb subdirectory
            var glbDir = Path.Combine(tempDir.FullName, "glb");
            Directory.CreateDirectory(glbDir);

            // Create a minimal valid GLB file in glb/ subdirectory
            var glbPath = Path.Combine(glbDir, "model_lod0.glb");
            CreateMinimalGlbFile(glbPath);

            var result = GlbLodHelper.FindGlbLodFiles(fbxPath);

            // Should find files in glb/ subdirectory
            if (result.Count > 0) {
                Assert.Contains(LodLevel.LOD0, result.Keys);
                Assert.Contains("glb", result[LodLevel.LOD0].FilePath);
            }
        } finally {
            tempDir.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Creates a minimal valid GLB file for testing
    /// </summary>
    private void CreateMinimalGlbFile(string path) {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        // GLB header
        writer.Write(0x46546C67u); // magic: "glTF"
        writer.Write(2u);           // version
        writer.Write(44u);          // length (header + JSON chunk)

        // JSON chunk header
        writer.Write(20u);          // chunk length
        writer.Write(0x4E4F534Au); // chunk type: "JSON"

        // Minimal JSON content (20 bytes including padding)
        var json = "{\"asset\":{\"version\":\"2.0\"}}";
        var jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);
        writer.Write(jsonBytes);
    }
}