using AssetProcessor.ModelConversion.Core;
using Xunit;

namespace AssetProcessor.Tests.ModelConversion.Core;

/// <summary>
/// Edge case and boundary tests for CompressionMode and QuantizationSettings
/// </summary>
public class CompressionModeEdgeCaseTests {
    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    [InlineData(32)]
    public void QuantizationSettings_CanAcceptOutOfRangeBits(int bits) {
        // System should accept any int value, validation would be done by gltfpack
        var settings = new QuantizationSettings {
            PositionBits = bits,
            TexCoordBits = bits,
            NormalBits = bits,
            ColorBits = bits
        };

        Assert.Equal(bits, settings.PositionBits);
        Assert.Equal(bits, settings.TexCoordBits);
    }

    [Fact]
    public void QuantizationSettings_NegativeValues_CanBeSet() {
        // Should accept negative values (though they'd be rejected by gltfpack)
        var settings = new QuantizationSettings {
            PositionBits = -1,
            TexCoordBits = -5
        };

        Assert.Equal(-1, settings.PositionBits);
        Assert.Equal(-5, settings.TexCoordBits);
    }

    [Fact]
    public void QuantizationSettings_MaxIntValue_CanBeSet() {
        var settings = new QuantizationSettings {
            PositionBits = int.MaxValue,
            TexCoordBits = int.MaxValue,
            NormalBits = int.MaxValue,
            ColorBits = int.MaxValue
        };

        Assert.Equal(int.MaxValue, settings.PositionBits);
        Assert.Equal(int.MaxValue, settings.ColorBits);
    }

    [Fact]
    public void QuantizationSettings_MinIntValue_CanBeSet() {
        var settings = new QuantizationSettings {
            PositionBits = int.MinValue,
            TexCoordBits = int.MinValue
        };

        Assert.Equal(int.MinValue, settings.PositionBits);
        Assert.Equal(int.MinValue, settings.TexCoordBits);
    }

    [Fact]
    public void QuantizationSettings_MultipleModifications_MaintainIndependence() {
        var settings = QuantizationSettings.CreateDefault();
        var originalPositionBits = settings.PositionBits;

        settings.PositionBits = 10;
        settings.PositionBits = 12;
        settings.PositionBits = 16;

        Assert.Equal(16, settings.PositionBits);
        Assert.NotEqual(originalPositionBits, settings.PositionBits);
    }

    [Theory]
    [InlineData(14, 16, 8, 8)]
    [InlineData(16, 16, 10, 10)]
    [InlineData(12, 16, 8, 8)]
    public void QuantizationSettings_Presets_HaveConsistentTexCoordBits(
        int expectedPosition, int expectedTexCoord, int expectedNormal, int expectedColor) {
        
        // All presets should have TexCoordBits = 16 to avoid denormalization bug
        var settings = expectedPosition switch {
            14 => QuantizationSettings.CreateDefault(),
            16 => QuantizationSettings.CreateHighQuality(),
            12 => QuantizationSettings.CreateMinSize(),
            _ => throw new ArgumentException()
        };

        Assert.Equal(expectedTexCoord, settings.TexCoordBits);
    }

    [Fact]
    public void CompressionMode_CanBeCastToInt() {
        int noneValue = (int)CompressionMode.None;
        int quantizationValue = (int)CompressionMode.Quantization;
        int meshOptValue = (int)CompressionMode.MeshOpt;
        int meshOptAggressiveValue = (int)CompressionMode.MeshOptAggressive;

        Assert.Equal(0, noneValue);
        Assert.Equal(1, quantizationValue);
        Assert.Equal(2, meshOptValue);
        Assert.Equal(3, meshOptAggressiveValue);

        // Verify sequential ordering
        Assert.True(quantizationValue > noneValue);
        Assert.True(meshOptValue > quantizationValue);
        Assert.True(meshOptAggressiveValue > meshOptValue);
    }

    [Fact]
    public void CompressionMode_CanBeCastFromInt() {
        CompressionMode mode0 = (CompressionMode)0;
        CompressionMode mode1 = (CompressionMode)1;
        CompressionMode mode2 = (CompressionMode)2;
        CompressionMode mode3 = (CompressionMode)3;

        Assert.Equal(CompressionMode.None, mode0);
        Assert.Equal(CompressionMode.Quantization, mode1);
        Assert.Equal(CompressionMode.MeshOpt, mode2);
        Assert.Equal(CompressionMode.MeshOptAggressive, mode3);
    }

    [Fact]
    public void CompressionMode_InvalidIntValue_DoesNotThrow() {
        // C# allows casting invalid enum values
        CompressionMode invalid = (CompressionMode)999;

        // Should not throw, but won't match any defined value
        Assert.NotEqual(CompressionMode.None, invalid);
        Assert.NotEqual(CompressionMode.Quantization, invalid);
        Assert.NotEqual(CompressionMode.MeshOpt, invalid);
        Assert.NotEqual(CompressionMode.MeshOptAggressive, invalid);
    }

    [Fact]
    public void QuantizationSettings_AllPresetsAreDifferent() {
        var defaultSettings = QuantizationSettings.CreateDefault();
        var highQualitySettings = QuantizationSettings.CreateHighQuality();
        var minSizeSettings = QuantizationSettings.CreateMinSize();

        // Default vs High Quality
        Assert.NotEqual(defaultSettings.PositionBits, highQualitySettings.PositionBits);
        Assert.NotEqual(defaultSettings.NormalBits, highQualitySettings.NormalBits);

        // Default vs Min Size
        Assert.NotEqual(defaultSettings.PositionBits, minSizeSettings.PositionBits);

        // High Quality vs Min Size
        Assert.NotEqual(highQualitySettings.PositionBits, minSizeSettings.PositionBits);
    }

    [Fact]
    public void QuantizationSettings_ZeroBits_CanBeSet() {
        var settings = new QuantizationSettings {
            PositionBits = 0,
            TexCoordBits = 0,
            NormalBits = 0,
            ColorBits = 0
        };

        Assert.Equal(0, settings.PositionBits);
        Assert.Equal(0, settings.TexCoordBits);
        Assert.Equal(0, settings.NormalBits);
        Assert.Equal(0, settings.ColorBits);
    }

    [Theory]
    [InlineData(1, 1, 1, 1)]
    [InlineData(2, 4, 8, 16)]
    [InlineData(16, 16, 16, 16)]
    public void QuantizationSettings_VariousBitCombinations_Work(
        int positionBits, int texCoordBits, int normalBits, int colorBits) {
        
        var settings = new QuantizationSettings {
            PositionBits = positionBits,
            TexCoordBits = texCoordBits,
            NormalBits = normalBits,
            ColorBits = colorBits
        };

        Assert.Equal(positionBits, settings.PositionBits);
        Assert.Equal(texCoordBits, settings.TexCoordBits);
        Assert.Equal(normalBits, settings.NormalBits);
        Assert.Equal(colorBits, settings.ColorBits);
    }
}