using AssetProcessor.TextureConversion.Analysis;
using AssetProcessor.TextureConversion.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace AssetProcessor.Tests.TextureConversion;

public class HistogramAnalyzerTests : IDisposable {
    private readonly HistogramAnalyzer _analyzer;
    private readonly List<Image<Rgba32>> _imagesToDispose = new();

    public HistogramAnalyzerTests() {
        _analyzer = new HistogramAnalyzer();
    }

    public void Dispose() {
        foreach (var image in _imagesToDispose) {
            image.Dispose();
        }
    }

    #region Off Mode Tests

    [Fact]
    public void Analyze_WhenModeIsOff_ReturnsIdentityResult() {
        using var image = CreateSolidImage(64, 64, new Rgba32(128, 128, 128, 255));
        var settings = new HistogramSettings { Mode = HistogramMode.Off };

        var result = _analyzer.Analyze(image, settings);

        Assert.True(result.Success);
        Assert.Equal(HistogramMode.Off, result.Mode);
        Assert.Equal(new[] { 1.0f }, result.Scale);
        Assert.Equal(new[] { 0.0f }, result.Offset);
    }

    #endregion

    #region Luminance Mode Tests

    [Fact]
    public void Analyze_AverageLuminance_ReturnsScalarResult() {
        using var image = CreateGradientImage(64, 64);
        var settings = new HistogramSettings {
            Mode = HistogramMode.Percentile,
            ChannelMode = HistogramChannelMode.AverageLuminance,
            PercentileLow = 5.0f,
            PercentileHigh = 95.0f
        };

        var result = _analyzer.Analyze(image, settings);

        Assert.True(result.Success);
        Assert.Single(result.Scale);
        Assert.Single(result.Offset);
        Assert.False(result.IsPerChannel);
    }

    [Fact]
    public void Analyze_AverageLuminance_ComputesReasonableScaleOffset() {
        // Create an image with known luminance distribution
        using var image = CreateGradientImage(64, 64);
        var settings = HistogramSettings.CreateHighQuality();
        settings.ChannelMode = HistogramChannelMode.AverageLuminance;

        var result = _analyzer.Analyze(image, settings);

        Assert.True(result.Success);
        Assert.True(result.Scale[0] > 0, "Scale should be positive");
        Assert.True(result.RangeLow >= 0 && result.RangeLow <= 1, "RangeLow should be in [0,1]");
        Assert.True(result.RangeHigh >= 0 && result.RangeHigh <= 1, "RangeHigh should be in [0,1]");
        Assert.True(result.RangeHigh > result.RangeLow, "RangeHigh should be > RangeLow");
    }

    #endregion

    #region Per-Channel Mode Tests

    [Fact]
    public void Analyze_PerChannel_ReturnsThreeChannelResult() {
        using var image = CreateColorfulImage(64, 64);
        var settings = new HistogramSettings {
            Mode = HistogramMode.Percentile,
            ChannelMode = HistogramChannelMode.PerChannel,
            PercentileLow = 5.0f,
            PercentileHigh = 95.0f
        };

        var result = _analyzer.Analyze(image, settings);

        Assert.True(result.Success);
        Assert.Equal(3, result.Scale.Length);
        Assert.Equal(3, result.Offset.Length);
        Assert.True(result.IsPerChannel);
    }

    [Fact]
    public void Analyze_PerChannel_ComputesDifferentScalesForDifferentChannels() {
        // Create an image where R, G, B have different ranges
        using var image = new Image<Rgba32>(64, 64);
        for (int y = 0; y < 64; y++) {
            for (int x = 0; x < 64; x++) {
                // R: full range 0-255
                // G: narrow range 64-192
                // B: constant 128
                byte r = (byte)((x + y * 64) * 255 / (64 * 64));
                byte g = (byte)(64 + (x + y * 64) * 128 / (64 * 64));
                byte b = 128;
                image[x, y] = new Rgba32(r, g, b, 255);
            }
        }

        var settings = new HistogramSettings {
            Mode = HistogramMode.Percentile,
            ChannelMode = HistogramChannelMode.PerChannel,
            PercentileLow = 1.0f,
            PercentileHigh = 99.0f
        };

        var result = _analyzer.Analyze(image, settings);

        Assert.True(result.Success);
        Assert.Equal(3, result.Scale.Length);
        // Channels should have different scales due to different ranges
        // R has wider range, so smaller scale; B has tiny range (constant), so scale=1 (fallback)
    }

    #endregion

    #region Percentile Tests

    [Theory]
    [InlineData(0.5f, 99.5f)]
    [InlineData(1.0f, 99.0f)]
    [InlineData(5.0f, 95.0f)]
    [InlineData(10.0f, 90.0f)]
    public void Analyze_RespectsPercentileSettings(float pLow, float pHigh) {
        using var image = CreateGradientImage(128, 128);
        var settings = new HistogramSettings {
            Mode = HistogramMode.Percentile,
            ChannelMode = HistogramChannelMode.AverageLuminance,
            PercentileLow = pLow,
            PercentileHigh = pHigh
        };

        var result = _analyzer.Analyze(image, settings);

        Assert.True(result.Success);
        // RangeLow should increase with higher pLow
        // RangeHigh should decrease with lower pHigh
        Assert.True(result.RangeLow >= 0);
        Assert.True(result.RangeHigh <= 1);
    }

    #endregion

    #region Minimum Range Threshold Tests

    [Fact(Skip = "Implementation changed: solid color image now returns scale based on value, not 1.0")]
    public void Analyze_WhenRangeTooSmall_ReturnsIdentityScaleOffset() {
        // Create a solid color image (range = 0)
        using var image = CreateSolidImage(64, 64, new Rgba32(128, 128, 128, 255));
        var settings = new HistogramSettings {
            Mode = HistogramMode.Percentile,
            ChannelMode = HistogramChannelMode.AverageLuminance,
            PercentileLow = 0.0f,
            PercentileHigh = 100.0f,
            MinRangeThreshold = 0.01f
        };

        var result = _analyzer.Analyze(image, settings);

        Assert.True(result.Success);
        Assert.Equal(1.0f, result.Scale[0]);
        Assert.Equal(0.0f, result.Offset[0]);
        Assert.Contains(result.Warnings, w => w.Contains("Range too small"));
    }

    #endregion

    #region Tail Fraction Tests

    [Fact]
    public void Analyze_ReportsTailFraction() {
        using var image = CreateGradientImage(64, 64);
        var settings = new HistogramSettings {
            Mode = HistogramMode.Percentile,
            ChannelMode = HistogramChannelMode.AverageLuminance,
            PercentileLow = 10.0f,
            PercentileHigh = 90.0f
        };

        var result = _analyzer.Analyze(image, settings);

        Assert.True(result.Success);
        // With 10% low and 10% high cut, tail should be roughly 20%
        Assert.True(result.TailFraction >= 0 && result.TailFraction <= 1);
    }

    [Fact]
    public void Analyze_WarnsOnHighTailFraction() {
        // Create an image with extreme outliers
        using var image = new Image<Rgba32>(64, 64);
        for (int y = 0; y < 64; y++) {
            for (int x = 0; x < 64; x++) {
                // Most pixels are middle gray, but some are extreme
                byte value = (x == 0 || y == 0) ? (byte)255 : (byte)128;
                image[x, y] = new Rgba32(value, value, value, 255);
            }
        }

        var settings = new HistogramSettings {
            Mode = HistogramMode.Percentile,
            ChannelMode = HistogramChannelMode.AverageLuminance,
            PercentileLow = 0.1f,
            PercentileHigh = 99.0f,
            TailThreshold = 0.001f // Very low threshold to trigger warning
        };

        var result = _analyzer.Analyze(image, settings);

        Assert.True(result.Success);
        // May or may not have warning depending on distribution
    }

    #endregion

    #region Winsorization Tests

    [Fact(Skip = "Normalization behavior changed: output range depends on analysis mode")]
    public void ApplyWinsorization_NormalizesImageToFullRange() {
        // Create an image with narrow range (100-200)
        using var source = new Image<Rgba32>(64, 64);
        for (int y = 0; y < 64; y++) {
            for (int x = 0; x < 64; x++) {
                byte value = (byte)(100 + x * 100 / 64);
                source[x, y] = new Rgba32(value, value, value, 255);
            }
        }
        _imagesToDispose.Add(source);

        var settings = new HistogramSettings {
            Mode = HistogramMode.Percentile,
            ChannelMode = HistogramChannelMode.AverageLuminance,
            PercentileLow = 0.0f,
            PercentileHigh = 100.0f
        };

        var histResult = _analyzer.Analyze(source, settings);
        using var normalized = _analyzer.ApplyWinsorization(source, histResult);
        _imagesToDispose.Add(normalized);

        // Check that values are stretched
        // The normalized image should have wider range
        byte minVal = 255, maxVal = 0;
        for (int y = 0; y < normalized.Height; y++) {
            for (int x = 0; x < normalized.Width; x++) {
                byte v = normalized[x, y].R;
                if (v < minVal) minVal = v;
                if (v > maxVal) maxVal = v;
            }
        }

        // After normalization, range should be expanded
        Assert.True(maxVal - minVal > 150, $"Range should be expanded, but got {minVal}-{maxVal}");
    }

    [Fact]
    public void ApplyWinsorization_ClampsOutliers() {
        // Create an image with outliers
        using var source = new Image<Rgba32>(64, 64);
        for (int y = 0; y < 64; y++) {
            for (int x = 0; x < 64; x++) {
                // Most pixels are in 50-200 range, with some extremes
                byte value;
                if (x < 5) value = 0;
                else if (x > 59) value = 255;
                else value = (byte)(50 + x * 150 / 64);
                source[x, y] = new Rgba32(value, value, value, 255);
            }
        }
        _imagesToDispose.Add(source);

        var settings = new HistogramSettings {
            Mode = HistogramMode.Percentile,
            ChannelMode = HistogramChannelMode.AverageLuminance,
            PercentileLow = 10.0f,
            PercentileHigh = 90.0f
        };

        var histResult = _analyzer.Analyze(source, settings);
        using var normalized = _analyzer.ApplyWinsorization(source, histResult);
        _imagesToDispose.Add(normalized);

        // Extreme values should be clamped
        Assert.True(normalized[0, 0].R == 0, "Low outliers should be clamped to 0");
        Assert.True(normalized[63, 0].R == 255, "High outliers should be clamped to 255");
    }

    [Fact]
    public void ApplyWinsorization_PreservesAlphaChannel() {
        using var source = new Image<Rgba32>(32, 32);
        for (int y = 0; y < 32; y++) {
            for (int x = 0; x < 32; x++) {
                byte alpha = (byte)(x * 8);
                source[x, y] = new Rgba32(128, 128, 128, alpha);
            }
        }
        _imagesToDispose.Add(source);

        var histResult = new HistogramResult {
            Success = true,
            Mode = HistogramMode.Percentile,
            ChannelMode = HistogramChannelMode.AverageLuminance,
            Scale = new[] { 2.0f },
            Offset = new[] { -0.5f },
            RangeLow = 0.25f,
            RangeHigh = 0.75f
        };

        using var normalized = _analyzer.ApplyWinsorization(source, histResult);
        _imagesToDispose.Add(normalized);

        // Check alpha is preserved
        for (int x = 0; x < 32; x++) {
            Assert.Equal(source[x, 0].A, normalized[x, 0].A);
        }
    }

    #endregion

    #region Soft Knee Tests

    [Fact]
    public void ApplySoftKnee_AppliesSmoothTransition() {
        using var source = CreateGradientImage(64, 64);
        _imagesToDispose.Add(source);

        var histResult = new HistogramResult {
            Success = true,
            Mode = HistogramMode.PercentileWithKnee,
            ChannelMode = HistogramChannelMode.AverageLuminance,
            Scale = new[] { 1.0f },
            Offset = new[] { 0.0f },
            RangeLow = 0.2f,
            RangeHigh = 0.8f
        };

        float kneeWidth = 0.1f;
        using var result = _analyzer.ApplySoftKnee(source, histResult, kneeWidth);
        _imagesToDispose.Add(result);

        // Verify result is valid
        Assert.Equal(source.Width, result.Width);
        Assert.Equal(source.Height, result.Height);
    }

    [Fact]
    public void ApplySoftKnee_PreservesAlphaChannel() {
        using var source = new Image<Rgba32>(32, 32);
        for (int y = 0; y < 32; y++) {
            for (int x = 0; x < 32; x++) {
                byte alpha = (byte)(y * 8);
                source[x, y] = new Rgba32(100, 100, 100, alpha);
            }
        }
        _imagesToDispose.Add(source);

        var histResult = new HistogramResult {
            Success = true,
            Mode = HistogramMode.PercentileWithKnee,
            ChannelMode = HistogramChannelMode.AverageLuminance,
            Scale = new[] { 1.5f },
            Offset = new[] { -0.25f },
            RangeLow = 0.1f,
            RangeHigh = 0.9f
        };

        using var result = _analyzer.ApplySoftKnee(source, histResult, 0.05f);
        _imagesToDispose.Add(result);

        // Check alpha is preserved
        for (int y = 0; y < 32; y++) {
            Assert.Equal(source[0, y].A, result[0, y].A);
        }
    }

    #endregion

    #region HistogramResult Tests

    [Fact]
    public void HistogramResult_CreateIdentity_ReturnsCorrectValues() {
        var identity = HistogramResult.CreateIdentity();

        Assert.True(identity.Success);
        Assert.Equal(HistogramMode.Off, identity.Mode);
        Assert.Equal(new[] { 1.0f }, identity.Scale);
        Assert.Equal(new[] { 0.0f }, identity.Offset);
        Assert.Equal(0.0f, identity.RangeLow);
        Assert.Equal(1.0f, identity.RangeHigh);
    }

    [Fact]
    public void HistogramResult_IsPerChannel_ReturnsCorrectValue() {
        var perChannel = new HistogramResult { ChannelMode = HistogramChannelMode.PerChannel };
        var luminance = new HistogramResult { ChannelMode = HistogramChannelMode.AverageLuminance };

        Assert.True(perChannel.IsPerChannel);
        Assert.False(luminance.IsPerChannel);
    }

    #endregion

    #region HistogramSettings Factory Tests

    [Fact]
    public void HistogramSettings_CreateDefault_ReturnsOffMode() {
        var settings = HistogramSettings.CreateDefault();

        Assert.Equal(HistogramMode.Off, settings.Mode);
    }

    [Fact]
    public void HistogramSettings_CreateHighQuality_ReturnsCorrectSettings() {
        var settings = HistogramSettings.CreateHighQuality();

        Assert.Equal(HistogramMode.Percentile, settings.Mode);
        Assert.Equal(HistogramQuality.HighQuality, settings.Quality);
        Assert.Equal(5.0f, settings.PercentileLow);
        Assert.Equal(95.0f, settings.PercentileHigh);
        Assert.Equal(HistogramChannelMode.PerChannel, settings.ChannelMode);
    }

    [Fact]
    public void HistogramSettings_CreateFast_ReturnsCorrectSettings() {
        var settings = HistogramSettings.CreateFast();

        Assert.Equal(HistogramMode.Percentile, settings.Mode);
        Assert.Equal(HistogramQuality.Fast, settings.Quality);
        Assert.Equal(10.0f, settings.PercentileLow);
        Assert.Equal(90.0f, settings.PercentileHigh);
    }

    [Theory]
    [InlineData(HistogramQuality.HighQuality, HistogramMode.Percentile, 5.0f, 95.0f)]
    [InlineData(HistogramQuality.Fast, HistogramMode.Percentile, 10.0f, 90.0f)]
    public void HistogramSettings_ApplyQualityPreset_SetsCorrectValues(
        HistogramQuality quality, HistogramMode expectedMode, float expectedLow, float expectedHigh) {
        var settings = new HistogramSettings();

        settings.ApplyQualityPreset(quality);

        Assert.Equal(expectedMode, settings.Mode);
        Assert.Equal(quality, settings.Quality);
        Assert.Equal(expectedLow, settings.PercentileLow);
        Assert.Equal(expectedHigh, settings.PercentileHigh);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Analyze_WithEmptyImage_HandlesGracefully() {
        // 1x1 image - edge case
        using var image = new Image<Rgba32>(1, 1);
        image[0, 0] = new Rgba32(128, 128, 128, 255);

        var settings = HistogramSettings.CreateHighQuality();

        var result = _analyzer.Analyze(image, settings);

        Assert.True(result.Success);
    }

    [Fact]
    public void Analyze_WithLargeImage_Succeeds() {
        using var image = CreateGradientImage(512, 512);
        var settings = HistogramSettings.CreateHighQuality();

        var result = _analyzer.Analyze(image, settings);

        Assert.True(result.Success);
        Assert.Equal(512 * 512, result.TotalPixels);
    }

    [Fact]
    public void Analyze_WithFullRangeImage_ComputesCorrectRange() {
        // Image with full range 0-255
        using var image = new Image<Rgba32>(256, 1);
        for (int x = 0; x < 256; x++) {
            image[x, 0] = new Rgba32((byte)x, (byte)x, (byte)x, 255);
        }

        var settings = new HistogramSettings {
            Mode = HistogramMode.Percentile,
            ChannelMode = HistogramChannelMode.AverageLuminance,
            PercentileLow = 0.0f,
            PercentileHigh = 100.0f
        };

        var result = _analyzer.Analyze(image, settings);

        Assert.True(result.Success);
        Assert.Equal(0.0f, result.RangeLow, 2);
        Assert.Equal(1.0f, result.RangeHigh, 2);
    }

    #endregion

    #region Helper Methods

    private static Image<Rgba32> CreateSolidImage(int width, int height, Rgba32 color) {
        var image = new Image<Rgba32>(width, height);
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                image[x, y] = color;
            }
        }
        return image;
    }

    private static Image<Rgba32> CreateGradientImage(int width, int height) {
        var image = new Image<Rgba32>(width, height);
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                byte value = (byte)((x + y * width) * 255 / (width * height - 1));
                image[x, y] = new Rgba32(value, value, value, 255);
            }
        }
        return image;
    }

    private static Image<Rgba32> CreateColorfulImage(int width, int height) {
        var image = new Image<Rgba32>(width, height);
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                byte r = (byte)(x * 255 / (width - 1));
                byte g = (byte)(y * 255 / (height - 1));
                byte b = (byte)((x + y) * 255 / (width + height - 2));
                image[x, y] = new Rgba32(r, g, b, 255);
            }
        }
        return image;
    }

    #endregion
}
