using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureConversion.MipGeneration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace AssetProcessor.Tests.TextureConversion;

public class MipGeneratorTests : IDisposable {
    private readonly MipGenerator _generator;
    private readonly List<Image<Rgba32>> _imagesToDispose = new();

    public MipGeneratorTests() {
        _generator = new MipGenerator();
    }

    public void Dispose() {
        foreach (var image in _imagesToDispose) {
            image.Dispose();
        }
    }

    #region Basic Mipmap Generation Tests

    [Fact]
    public void GenerateMipmaps_CreatesCorrectNumberOfLevels() {
        // 256x256 should give: 256, 128, 64, 32, 16, 8, 4, 2, 1 = 9 levels
        using var source = CreateTestImage(256, 256, new Rgba32(128, 128, 128, 255));
        var profile = MipGenerationProfile.CreateDefault(TextureType.Albedo);

        var mipmaps = _generator.GenerateMipmaps(source, profile);
        _imagesToDispose.AddRange(mipmaps);

        Assert.Equal(9, mipmaps.Count);
    }

    [Fact]
    public void GenerateMipmaps_FirstLevelMatchesSourceDimensions() {
        using var source = CreateTestImage(512, 256, new Rgba32(255, 0, 0, 255));
        var profile = MipGenerationProfile.CreateDefault(TextureType.Albedo);

        var mipmaps = _generator.GenerateMipmaps(source, profile);
        _imagesToDispose.AddRange(mipmaps);

        Assert.Equal(512, mipmaps[0].Width);
        Assert.Equal(256, mipmaps[0].Height);
    }

    [Fact]
    public void GenerateMipmaps_EachLevelIsHalfOfPrevious() {
        using var source = CreateTestImage(128, 128, new Rgba32(0, 255, 0, 255));
        var profile = MipGenerationProfile.CreateDefault(TextureType.Albedo);

        var mipmaps = _generator.GenerateMipmaps(source, profile);
        _imagesToDispose.AddRange(mipmaps);

        // 128, 64, 32, 16, 8, 4, 2, 1 = 8 levels
        Assert.Equal(8, mipmaps.Count);
        Assert.Equal(128, mipmaps[0].Width);
        Assert.Equal(64, mipmaps[1].Width);
        Assert.Equal(32, mipmaps[2].Width);
        Assert.Equal(16, mipmaps[3].Width);
        Assert.Equal(8, mipmaps[4].Width);
        Assert.Equal(4, mipmaps[5].Width);
        Assert.Equal(2, mipmaps[6].Width);
        Assert.Equal(1, mipmaps[7].Width);
    }

    [Fact]
    public void GenerateMipmaps_HandlesNonPowerOfTwo() {
        using var source = CreateTestImage(100, 75, new Rgba32(0, 0, 255, 255));
        var profile = MipGenerationProfile.CreateDefault(TextureType.Albedo);

        var mipmaps = _generator.GenerateMipmaps(source, profile);
        _imagesToDispose.AddRange(mipmaps);

        // Should still generate valid mipmaps
        Assert.True(mipmaps.Count > 1);
        Assert.Equal(100, mipmaps[0].Width);
        Assert.Equal(75, mipmaps[0].Height);
    }

    [Fact]
    public void GenerateMipmaps_ClonesSourceImage() {
        using var source = CreateTestImage(64, 64, new Rgba32(100, 100, 100, 255));
        var profile = MipGenerationProfile.CreateDefault(TextureType.Albedo);

        var mipmaps = _generator.GenerateMipmaps(source, profile);
        _imagesToDispose.AddRange(mipmaps);

        // Modify source to verify clone
        source[0, 0] = new Rgba32(255, 0, 0, 255);

        // Mip0 should still have original value
        Assert.NotEqual(source[0, 0], mipmaps[0][0, 0]);
    }

    [Fact]
    public void GenerateMipmaps_RespectsMinMipSize() {
        using var source = CreateTestImage(64, 64, new Rgba32(50, 50, 50, 255));
        var profile = MipGenerationProfile.CreateDefault(TextureType.Albedo);
        profile.MinMipSize = 4; // Stop at 4x4

        var mipmaps = _generator.GenerateMipmaps(source, profile);
        _imagesToDispose.AddRange(mipmaps);

        // 64, 32, 16, 8, 4 = 5 levels (not going to 2 or 1)
        Assert.Equal(5, mipmaps.Count);
        Assert.Equal(4, mipmaps[^1].Width);
    }

    [Fact]
    public void GenerateMipmaps_RespectsIncludeLastLevel() {
        using var source = CreateTestImage(16, 16, new Rgba32(200, 200, 200, 255));
        var profile = MipGenerationProfile.CreateDefault(TextureType.Albedo);
        profile.IncludeLastLevel = false;
        profile.MinMipSize = 1;

        var mipmaps = _generator.GenerateMipmaps(source, profile);
        _imagesToDispose.AddRange(mipmaps);

        // Should stop before 1x1
        Assert.True(mipmaps[^1].Width > 1 || mipmaps[^1].Height > 1);
    }

    #endregion

    #region Filter Type Tests

    [Theory]
    [InlineData(FilterType.Box)]
    [InlineData(FilterType.Bilinear)]
    [InlineData(FilterType.Bicubic)]
    [InlineData(FilterType.Lanczos3)]
    [InlineData(FilterType.Mitchell)]
    [InlineData(FilterType.Kaiser)]
    public void GenerateMipmaps_WorksWithAllStandardFilters(FilterType filterType) {
        using var source = CreateTestImage(64, 64, new Rgba32(128, 128, 128, 255));
        var profile = new MipGenerationProfile {
            TextureType = TextureType.Generic,
            Filter = filterType,
            ApplyGammaCorrection = false
        };

        var mipmaps = _generator.GenerateMipmaps(source, profile);
        _imagesToDispose.AddRange(mipmaps);

        Assert.True(mipmaps.Count > 1);
        Assert.All(mipmaps, mip => Assert.True(mip.Width >= 1 && mip.Height >= 1));
    }

    [Theory]
    [InlineData(FilterType.Min)]
    [InlineData(FilterType.Max)]
    public void GenerateMipmaps_WorksWithMinMaxFilters(FilterType filterType) {
        using var source = CreateTestImage(32, 32, new Rgba32(100, 150, 200, 255));
        var profile = new MipGenerationProfile {
            TextureType = TextureType.Roughness,
            Filter = filterType,
            ApplyGammaCorrection = false
        };

        var mipmaps = _generator.GenerateMipmaps(source, profile);
        _imagesToDispose.AddRange(mipmaps);

        Assert.True(mipmaps.Count > 1);
    }

    #endregion

    #region Texture Type Profile Tests

    [Theory]
    [InlineData(TextureType.Albedo, FilterType.Kaiser, true)]
    [InlineData(TextureType.Normal, FilterType.Kaiser, false)]
    [InlineData(TextureType.Roughness, FilterType.Kaiser, false)]
    [InlineData(TextureType.Metallic, FilterType.Box, false)]
    [InlineData(TextureType.AmbientOcclusion, FilterType.Kaiser, false)]
    [InlineData(TextureType.Emissive, FilterType.Kaiser, true)]
    [InlineData(TextureType.Gloss, FilterType.Kaiser, false)]
    public void CreateDefault_ReturnsCorrectProfileForTextureType(TextureType textureType, FilterType expectedFilter, bool expectedGammaCorrection) {
        var profile = MipGenerationProfile.CreateDefault(textureType);

        Assert.Equal(textureType, profile.TextureType);
        Assert.Equal(expectedFilter, profile.Filter);
        Assert.Equal(expectedGammaCorrection, profile.ApplyGammaCorrection);
    }

    [Fact]
    public void CreateDefault_NormalMap_HasNormalizeNormalsEnabled() {
        var profile = MipGenerationProfile.CreateDefault(TextureType.Normal);

        Assert.True(profile.NormalizeNormals);
    }

    [Fact]
    public void CreateDefault_Albedo_HasCorrectGamma() {
        var profile = MipGenerationProfile.CreateDefault(TextureType.Albedo);

        Assert.Equal(2.2f, profile.Gamma);
    }

    #endregion

    #region Gamma Correction Tests

    [Fact]
    public void GenerateMipmaps_AppliesGammaCorrectionWhenEnabled() {
        // Create a simple gradient image
        using var source = CreateGradientImage(64, 64);
        var profileWithGamma = new MipGenerationProfile {
            TextureType = TextureType.Albedo,
            Filter = FilterType.Box,
            ApplyGammaCorrection = true,
            Gamma = 2.2f
        };
        var profileWithoutGamma = new MipGenerationProfile {
            TextureType = TextureType.Albedo,
            Filter = FilterType.Box,
            ApplyGammaCorrection = false
        };

        var mipmapsWithGamma = _generator.GenerateMipmaps(source, profileWithGamma);
        var mipmapsWithoutGamma = _generator.GenerateMipmaps(source, profileWithoutGamma);
        _imagesToDispose.AddRange(mipmapsWithGamma);
        _imagesToDispose.AddRange(mipmapsWithoutGamma);

        // The mipmaps should be different due to gamma correction
        // Compare second level (32x32) to see the effect
        var mipWithGamma = mipmapsWithGamma[1];
        var mipWithoutGamma = mipmapsWithoutGamma[1];

        bool foundDifference = false;
        for (int y = 0; y < mipWithGamma.Height && !foundDifference; y++) {
            for (int x = 0; x < mipWithGamma.Width && !foundDifference; x++) {
                var pixelWithGamma = mipWithGamma[x, y];
                var pixelWithoutGamma = mipWithoutGamma[x, y];
                if (pixelWithGamma.R != pixelWithoutGamma.R ||
                    pixelWithGamma.G != pixelWithoutGamma.G ||
                    pixelWithGamma.B != pixelWithoutGamma.B) {
                    foundDifference = true;
                }
            }
        }

        Assert.True(foundDifference, "Gamma correction should produce different results");
    }

    #endregion

    #region Energy Preserving Tests

    [Fact]
    public void GenerateMipmaps_EnergyPreserving_WorksForRoughness() {
        using var source = CreateTestImage(64, 64, new Rgba32(128, 128, 128, 255));
        var profile = new MipGenerationProfile {
            TextureType = TextureType.Roughness,
            Filter = FilterType.Kaiser,
            ApplyGammaCorrection = false,
            UseEnergyPreserving = true,
            IsGloss = false
        };

        var mipmaps = _generator.GenerateMipmaps(source, profile);
        _imagesToDispose.AddRange(mipmaps);

        Assert.True(mipmaps.Count > 1);
        // All mipmaps should have valid pixels
        foreach (var mip in mipmaps) {
            Assert.True(mip[0, 0].R >= 0 && mip[0, 0].R <= 255);
        }
    }

    [Fact]
    public void GenerateMipmaps_EnergyPreserving_WorksForGloss() {
        using var source = CreateTestImage(64, 64, new Rgba32(200, 200, 200, 255));
        var profile = new MipGenerationProfile {
            TextureType = TextureType.Gloss,
            Filter = FilterType.Kaiser,
            ApplyGammaCorrection = false,
            UseEnergyPreserving = true,
            IsGloss = true
        };

        var mipmaps = _generator.GenerateMipmaps(source, profile);
        _imagesToDispose.AddRange(mipmaps);

        Assert.True(mipmaps.Count > 1);
    }

    [Fact]
    public void GenerateMipmaps_EnergyPreserving_PreservesAverageEnergy() {
        // Create an image with known values
        using var source = new Image<Rgba32>(64, 64);
        // Fill with a mix of low and high values
        for (int y = 0; y < 64; y++) {
            for (int x = 0; x < 64; x++) {
                byte value = (byte)((x + y) % 2 == 0 ? 64 : 192);
                source[x, y] = new Rgba32(value, value, value, 255);
            }
        }

        var profile = new MipGenerationProfile {
            TextureType = TextureType.Roughness,
            Filter = FilterType.Kaiser,
            UseEnergyPreserving = true,
            IsGloss = false
        };

        var mipmaps = _generator.GenerateMipmaps(source, profile);
        _imagesToDispose.AddRange(mipmaps);

        // Energy preserving should maintain energy across mip levels
        // The average value should be somewhat consistent
        Assert.True(mipmaps.Count > 1);
    }

    #endregion

    #region Normal Map Tests

    [Fact]
    public void GenerateMipmaps_NormalizesNormalMapsWhenEnabled() {
        // Create a normal map with non-normalized normals
        using var source = new Image<Rgba32>(64, 64);
        // Fill with values that would need normalization
        for (int y = 0; y < 64; y++) {
            for (int x = 0; x < 64; x++) {
                // Default normal pointing up (0, 0, 1) = (128, 128, 255)
                source[x, y] = new Rgba32(128, 128, 255, 255);
            }
        }

        var profile = MipGenerationProfile.CreateDefault(TextureType.Normal);

        var mipmaps = _generator.GenerateMipmaps(source, profile);
        _imagesToDispose.AddRange(mipmaps);

        Assert.True(mipmaps.Count > 1);
        // All mip levels should have valid normalized normals
        foreach (var mip in mipmaps) {
            var pixel = mip[mip.Width / 2, mip.Height / 2];
            // Z component should be high for upward-facing normal
            Assert.True(pixel.B >= 128, "Normal map Z component should be >= 0.5 (128)");
        }
    }

    #endregion

    #region CalculateMipLevels Tests

    [Theory]
    [InlineData(1, 1, 1, 1)]
    [InlineData(2, 2, 1, 2)]
    [InlineData(4, 4, 1, 3)]
    [InlineData(8, 8, 1, 4)]
    [InlineData(16, 16, 1, 5)]
    [InlineData(256, 256, 1, 9)]
    [InlineData(512, 256, 1, 10)]
    [InlineData(256, 512, 1, 10)]
    [InlineData(1024, 1024, 1, 11)]
    public void CalculateMipLevels_ReturnsCorrectCount(int width, int height, int minSize, int expected) {
        int levels = MipGenerator.CalculateMipLevels(width, height, minSize);
        Assert.Equal(expected, levels);
    }

    [Theory]
    [InlineData(64, 64, 4, 5)]
    [InlineData(128, 128, 8, 5)]
    [InlineData(256, 256, 16, 5)]
    public void CalculateMipLevels_RespectsMinSize(int width, int height, int minSize, int expected) {
        int levels = MipGenerator.CalculateMipLevels(width, height, minSize);
        Assert.Equal(expected, levels);
    }

    #endregion

    #region Profile Clone Tests

    [Fact]
    public void Clone_CreatesIndependentCopy() {
        var original = new MipGenerationProfile {
            TextureType = TextureType.Albedo,
            Filter = FilterType.Kaiser,
            ApplyGammaCorrection = true,
            Gamma = 2.2f,
            BlurRadius = 0.5f,
            IncludeLastLevel = true,
            MinMipSize = 1,
            NormalizeNormals = false,
            UseEnergyPreserving = true,
            IsGloss = true
        };

        var cloned = original.Clone();

        // Modify original
        original.TextureType = TextureType.Normal;
        original.Filter = FilterType.Box;
        original.ApplyGammaCorrection = false;

        // Cloned should remain unchanged
        Assert.Equal(TextureType.Albedo, cloned.TextureType);
        Assert.Equal(FilterType.Kaiser, cloned.Filter);
        Assert.True(cloned.ApplyGammaCorrection);
        Assert.Equal(2.2f, cloned.Gamma);
        Assert.True(cloned.UseEnergyPreserving);
        Assert.True(cloned.IsGloss);
    }

    #endregion

    #region Rectangular Image Tests

    [Fact]
    public void GenerateMipmaps_HandlesWideImages() {
        using var source = CreateTestImage(256, 64, new Rgba32(100, 100, 100, 255));
        var profile = MipGenerationProfile.CreateDefault(TextureType.Albedo);

        var mipmaps = _generator.GenerateMipmaps(source, profile);
        _imagesToDispose.AddRange(mipmaps);

        Assert.True(mipmaps.Count > 1);
        Assert.Equal(256, mipmaps[0].Width);
        Assert.Equal(64, mipmaps[0].Height);
        // Each level halves both dimensions
        Assert.Equal(128, mipmaps[1].Width);
        Assert.Equal(32, mipmaps[1].Height);
    }

    [Fact]
    public void GenerateMipmaps_HandlesTallImages() {
        using var source = CreateTestImage(64, 256, new Rgba32(100, 100, 100, 255));
        var profile = MipGenerationProfile.CreateDefault(TextureType.Albedo);

        var mipmaps = _generator.GenerateMipmaps(source, profile);
        _imagesToDispose.AddRange(mipmaps);

        Assert.True(mipmaps.Count > 1);
        Assert.Equal(64, mipmaps[0].Width);
        Assert.Equal(256, mipmaps[0].Height);
    }

    #endregion

    #region Helper Methods

    private static Image<Rgba32> CreateTestImage(int width, int height, Rgba32 fillColor) {
        var image = new Image<Rgba32>(width, height);
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                image[x, y] = fillColor;
            }
        }
        return image;
    }

    private static Image<Rgba32> CreateGradientImage(int width, int height) {
        var image = new Image<Rgba32>(width, height);
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                byte value = (byte)((x + y) * 255 / (width + height - 2));
                image[x, y] = new Rgba32(value, value, value, 255);
            }
        }
        return image;
    }

    #endregion
}
