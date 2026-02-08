using System.Reflection;
using AssetProcessor.ModelConversion.Core;
using AssetProcessor.ModelConversion.Wrappers;
using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureConversion.Pipeline;
using Xunit;

namespace AssetProcessor.Tests.ModelConversion;

public class GltfPackWrapperTests {
    private static string InvokeBuildArguments(
        GltfPackWrapper wrapper,
        GltfPackSettings settings,
        bool generateReport = false,
        bool excludeTextures = true) {

        var method = typeof(GltfPackWrapper).GetMethod(
            "BuildArguments",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var lod = LodSettings.CreateDefault(LodLevel.LOD1);
        var quantization = QuantizationSettings.CreateDefault();

        try {
            var result = method!.Invoke(wrapper, [
                "input.glb",
                "output.glb",
                lod,
                CompressionMode.Quantization,
                quantization,
                settings,
                generateReport,
                excludeTextures
            ]);

            return Assert.IsType<string>(result);
        } catch (TargetInvocationException ex) {
            throw ex.InnerException ?? ex;
        }
    }

    [Fact]
    public void BuildArguments_FlipUvsEnabled_ThrowsNotSupportedException() {
        var wrapper = new GltfPackWrapper("gltfpack.exe");
        var settings = GltfPackSettings.CreateDefault();
        settings.FlipUVs = true;

        var ex = Assert.Throws<NotSupportedException>(() => InvokeBuildArguments(wrapper, settings));

        Assert.Contains("FlipUVs=true не поддерживается", ex.Message);
    }

    [Fact]
    public void BuildArguments_FlipUvsDisabled_BuildsCommandLine() {
        var wrapper = new GltfPackWrapper("gltfpack.exe");
        var settings = GltfPackSettings.CreateDefault();
        settings.FlipUVs = false;

        var args = InvokeBuildArguments(wrapper, settings, generateReport: true, excludeTextures: true);

        Assert.Contains("-i \"input.glb\"", args);
        Assert.Contains("-o \"output.glb\"", args);
        Assert.Contains("-tr", args);
        Assert.Contains("-r \"output.report.json\"", args);
    }

    [Fact]
    public void BatchProcessor_ProcessDirectoryAsync_ContainsToksvigParameter() {
        var method = typeof(BatchProcessor).GetMethod(nameof(BatchProcessor.ProcessDirectoryAsync));

        Assert.NotNull(method);
        var parameters = method!.GetParameters();

        var toksvig = Assert.Single(parameters, p => p.Name == "toksvigSettings");
        Assert.Equal(typeof(ToksvigSettings), Nullable.GetUnderlyingType(toksvig.ParameterType));
        Assert.True(toksvig.HasDefaultValue);
        Assert.Null(toksvig.DefaultValue);
    }
}
