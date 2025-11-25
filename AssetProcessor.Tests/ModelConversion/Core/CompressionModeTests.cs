using AssetProcessor.ModelConversion.Core;
using Xunit;

namespace AssetProcessor.Tests.ModelConversion.Core;

/// <summary>
/// Unit tests for CompressionMode enum and QuantizationSettings
/// </summary>
public class CompressionModeTests {
    [Fact]
    public void CompressionMode_EnumValues_HaveExpectedCount() {
        // Verify all compression modes are defined
        var modes = Enum.GetValues<CompressionMode>();
        Assert.Equal(4, modes.Length);
    }

    [Theory]
    [InlineData(CompressionMode.None, 0)]
    [InlineData(CompressionMode.Quantization, 1)]
    [InlineData(CompressionMode.MeshOpt, 2)]
    [InlineData(CompressionMode.MeshOptAggressive, 3)]
    public void CompressionMode_EnumValues_HaveCorrectIntegerValues(CompressionMode mode, int expectedValue) {
        Assert.Equal(expectedValue, (int)mode);
    }

    [Fact]
    public void CreateDefault_ReturnsSettingsWithExpectedDefaultValues() {
        var settings = QuantizationSettings.CreateDefault();

        Assert.Equal(14, settings.PositionBits);
        Assert.Equal(16, settings.TexCoordBits); // Important: 16 bits to avoid denormalization bug
        Assert.Equal(8, settings.NormalBits);
        Assert.Equal(8, settings.ColorBits);
    }

    [Fact]
    public void CreateHighQuality_ReturnsSettingsWithHigherBitDepth() {
        var settings = QuantizationSettings.CreateHighQuality();

        Assert.Equal(16, settings.PositionBits);
        Assert.Equal(16, settings.TexCoordBits);
        Assert.Equal(10, settings.NormalBits);
        Assert.Equal(10, settings.ColorBits);

        // Verify high quality has higher or equal bits than default
        var defaultSettings = QuantizationSettings.CreateDefault();
        Assert.True(settings.PositionBits >= defaultSettings.PositionBits);
        Assert.True(settings.NormalBits >= defaultSettings.NormalBits);
        Assert.True(settings.ColorBits >= defaultSettings.ColorBits);
    }

    [Fact]
    public void CreateMinSize_ReturnsSettingsWithLowerBitDepthExceptTexCoord() {
        var settings = QuantizationSettings.CreateMinSize();

        Assert.Equal(12, settings.PositionBits);
        Assert.Equal(16, settings.TexCoordBits); // Always 16 to avoid denormalization bug
        Assert.Equal(8, settings.NormalBits);
        Assert.Equal(8, settings.ColorBits);

        // Verify min size has lower position bits than default
        var defaultSettings = QuantizationSettings.CreateDefault();
        Assert.True(settings.PositionBits < defaultSettings.PositionBits);
        // But TexCoord should still be 16 bits
        Assert.Equal(16, settings.TexCoordBits);
    }

    [Fact]
    public void QuantizationSettings_CanBeConstructedWithCustomValues() {
        var settings = new QuantizationSettings {
            PositionBits = 15,
            TexCoordBits = 14,
            NormalBits = 9,
            ColorBits = 10
        };

        Assert.Equal(15, settings.PositionBits);
        Assert.Equal(14, settings.TexCoordBits);
        Assert.Equal(9, settings.NormalBits);
        Assert.Equal(10, settings.ColorBits);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(14)]
    [InlineData(16)]
    public void QuantizationSettings_AcceptsValidBitRanges(int bits) {
        var settings = new QuantizationSettings {
            PositionBits = bits,
            TexCoordBits = bits,
            NormalBits = bits,
            ColorBits = bits
        };

        Assert.Equal(bits, settings.PositionBits);
        Assert.Equal(bits, settings.TexCoordBits);
        Assert.Equal(bits, settings.NormalBits);
        Assert.Equal(bits, settings.ColorBits);
    }

    [Fact]
    public void AllPresets_UseTexCoord16Bits_ToAvoidDenormalizationBug() {
        // Critical test: All presets must use 16 bits for TexCoord
        // to avoid the denormalization bug mentioned in comments
        var defaultSettings = QuantizationSettings.CreateDefault();
        var highQualitySettings = QuantizationSettings.CreateHighQuality();
        var minSizeSettings = QuantizationSettings.CreateMinSize();

        Assert.Equal(16, defaultSettings.TexCoordBits);
        Assert.Equal(16, highQualitySettings.TexCoordBits);
        Assert.Equal(16, minSizeSettings.TexCoordBits);
    }

    [Fact]
    public void QuantizationSettings_DefaultConstructor_InitializesWithDefaultValues() {
        var settings = new QuantizationSettings();

        // Verify default property initializers work
        Assert.Equal(14, settings.PositionBits);
        Assert.Equal(16, settings.TexCoordBits);
        Assert.Equal(8, settings.NormalBits);
        Assert.Equal(8, settings.ColorBits);
    }

    [Fact]
    public void QuantizationSettings_AllPresets_AreDifferentInstances() {
        var default1 = QuantizationSettings.CreateDefault();
        var default2 = QuantizationSettings.CreateDefault();
        var highQuality = QuantizationSettings.CreateHighQuality();
        var minSize = QuantizationSettings.CreateMinSize();

        // Verify factory methods create new instances
        Assert.NotSame(default1, default2);
        Assert.NotSame(default1, highQuality);
        Assert.NotSame(default1, minSize);
        Assert.NotSame(highQuality, minSize);
    }

    [Fact]
    public void QuantizationSettings_Presets_CanBeModifiedIndependently() {
        var settings1 = QuantizationSettings.CreateDefault();
        var settings2 = QuantizationSettings.CreateDefault();

        settings1.PositionBits = 10;

        // Verify modification doesn't affect other instances
        Assert.Equal(10, settings1.PositionBits);
        Assert.Equal(14, settings2.PositionBits);
    }

    [Theory]
    [InlineData(CompressionMode.None)]
    [InlineData(CompressionMode.Quantization)]
    [InlineData(CompressionMode.MeshOpt)]
    [InlineData(CompressionMode.MeshOptAggressive)]
    public void CompressionMode_AllValues_CanBeParsedFromString(CompressionMode mode) {
        var modeString = mode.ToString();
        var parsed = Enum.Parse<CompressionMode>(modeString);

        Assert.Equal(mode, parsed);
    }
}