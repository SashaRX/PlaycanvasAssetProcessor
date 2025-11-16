using AssetProcessor.TextureConversion.BasisU;
using AssetProcessor.TextureConversion.Core;
using Xunit;

namespace AssetProcessor.Tests.TextureConversion;

public class KtxCreateWrapperTests {
    [Fact]
    public void BuildArguments_ToksvigManualMipmaps_UsesLevelsFlag() {
        var wrapper = new KtxCreateWrapper("ktx");
        var compression = CompressionSettings.CreateETC1SDefault();
        compression.GenerateMipmaps = false;

        var mipmaps = new List<string> {
            "C:/tmp/mip0.png",
            "C:/tmp/mip1.png"
        };

        var args = wrapper.BuildKtxCreateArguments(mipmaps, "C:/tmp/output.ktx2", compression, null);

        Assert.Contains("--levels 2", args);
        Assert.DoesNotContain("--generate-mipmap", args);
        Assert.Contains("\"C:/tmp/mip0.png\"", args);
        Assert.Contains("\"C:/tmp/mip1.png\"", args);
    }

    [Fact]
    public void BuildArguments_AutomaticMipmaps_EnableGenerateFlag() {
        var wrapper = new KtxCreateWrapper("ktx");
        var compression = CompressionSettings.CreateETC1SDefault();
        compression.GenerateMipmaps = true;

        var mipmaps = new List<string> { "C:/tmp/mip0.png" };

        var args = wrapper.BuildKtxCreateArguments(mipmaps, "C:/tmp/output.ktx2", compression, null);

        Assert.Contains("--generate-mipmap", args);
        Assert.DoesNotContain("--levels", args);
    }
}
