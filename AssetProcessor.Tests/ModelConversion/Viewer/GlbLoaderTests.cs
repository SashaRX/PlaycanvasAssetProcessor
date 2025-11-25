using AssetProcessor.ModelConversion.Viewer;
using Xunit;

namespace AssetProcessor.Tests.ModelConversion.Viewer;

/// <summary>
/// Unit tests for GlbLoader
/// </summary>
public class GlbLoaderTests {
    [Fact]
    public void GlbLoader_Constructor_WithNullPath_DoesNotThrow() {
        // Should create loader without gltfpack wrapper
        var loader = new GlbLoader(null);

        Assert.NotNull(loader);
    }

    [Fact]
    public void GlbLoader_Constructor_WithCustomPath_DoesNotThrow() {
        var loader = new GlbLoader("custom_gltfpack.exe");

        Assert.NotNull(loader);
    }

    [Fact]
    public async Task LoadGlbAsync_WithNonExistentFile_ReturnsNull() {
        var loader = new GlbLoader();

        var scene = await loader.LoadGlbAsync("/nonexistent/file.glb");

        Assert.Null(scene);
    }

    [Fact]
    public async Task LoadGlbAsync_WithInvalidFile_ReturnsNull() {
        var tempFile = Path.GetTempFileName();
        try {
            File.WriteAllText(tempFile, "This is not a valid GLB file");

            var loader = new GlbLoader();
            var scene = await loader.LoadGlbAsync(tempFile);

            // Should handle invalid files gracefully
            Assert.Null(scene);
        } finally {
            if (File.Exists(tempFile)) {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void GlbLoader_ClearCache_DoesNotThrow() {
        var loader = new GlbLoader();

        // Should not throw even if cache is empty
        loader.ClearCache();
    }

    [Fact]
    public void GlbLoader_Dispose_DoesNotThrow() {
        var loader = new GlbLoader();

        // Should dispose cleanly
        loader.Dispose();
    }

    [Fact]
    public void GlbLoader_CanBeDisposedMultipleTimes() {
        var loader = new GlbLoader();

        loader.Dispose();
        loader.Dispose(); // Should not throw on second dispose
    }

    [Fact]
    public void GlbLoader_MultipleInstances_AreIndependent() {
        var loader1 = new GlbLoader("path1");
        var loader2 = new GlbLoader("path2");

        Assert.NotSame(loader1, loader2);
    }
}