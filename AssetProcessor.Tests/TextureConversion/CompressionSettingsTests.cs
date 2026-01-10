using AssetProcessor.TextureConversion.Core;
using Xunit;

namespace AssetProcessor.Tests.TextureConversion;

public class CompressionSettingsTests {
    #region Factory Method Tests

    [Fact]
    public void CreateETC1SDefault_ReturnsCorrectSettings() {
        var settings = CompressionSettings.CreateETC1SDefault();

        Assert.Equal(CompressionFormat.ETC1S, settings.CompressionFormat);
        Assert.Equal(OutputFormat.KTX2, settings.OutputFormat);
        Assert.Equal(1, settings.CompressionLevel);
        Assert.Equal(128, settings.QualityLevel);
        Assert.True(settings.GenerateMipmaps);
        Assert.True(settings.UseMultithreading);
        Assert.True(settings.PerceptualMode);
        Assert.Equal(KTX2SupercompressionType.Zstandard, settings.KTX2Supercompression);
        Assert.Equal(3, settings.KTX2ZstdLevel);
        Assert.True(settings.UseETC1SRDO);
        Assert.Equal(ColorSpace.Auto, settings.ColorSpace);
    }

    [Fact]
    public void CreateUASTCDefault_ReturnsCorrectSettings() {
        var settings = CompressionSettings.CreateUASTCDefault();

        Assert.Equal(CompressionFormat.UASTC, settings.CompressionFormat);
        Assert.Equal(OutputFormat.KTX2, settings.OutputFormat);
        Assert.Equal(2, settings.UASTCQuality);
        Assert.True(settings.UseUASTCRDO);
        Assert.Equal(1.0f, settings.UASTCRDOQuality);
        Assert.True(settings.GenerateMipmaps);
        Assert.True(settings.UseMultithreading);
        Assert.Equal(KTX2SupercompressionType.Zstandard, settings.KTX2Supercompression);
    }

    [Fact]
    public void CreateHighQuality_ReturnsUASTCWithMaxQuality() {
        var settings = CompressionSettings.CreateHighQuality();

        Assert.Equal(CompressionFormat.UASTC, settings.CompressionFormat);
        Assert.Equal(4, settings.UASTCQuality);
        Assert.True(settings.UseUASTCRDO);
        Assert.Equal(0.5f, settings.UASTCRDOQuality);
        Assert.True(settings.PerceptualMode);
    }

    [Fact]
    public void CreateMinSize_ReturnsETC1SWithLowQuality() {
        var settings = CompressionSettings.CreateMinSize();

        Assert.Equal(CompressionFormat.ETC1S, settings.CompressionFormat);
        Assert.Equal(64, settings.QualityLevel);
        Assert.False(settings.PerceptualMode);
    }

    #endregion

    #region Default Values Tests

    [Fact]
    public void DefaultValues_AreCorrect() {
        var settings = new CompressionSettings();

        Assert.Equal(CompressionFormat.ETC1S, settings.CompressionFormat);
        Assert.Equal(OutputFormat.KTX2, settings.OutputFormat);
        Assert.Equal(1, settings.CompressionLevel);
        Assert.Equal(128, settings.QualityLevel);
        Assert.Equal(2, settings.UASTCQuality);
        Assert.True(settings.UseUASTCRDO);
        Assert.Equal(1.0f, settings.UASTCRDOQuality);
        Assert.True(settings.UseETC1SRDO);
        Assert.Equal(1.0f, settings.ETC1SRDOLambda);
        Assert.Equal(1.0f, settings.MipScale);
        Assert.Equal(1, settings.MipSmallestDimension);
        Assert.True(settings.GenerateMipmaps);
        Assert.False(settings.UseCustomMipmaps);
        Assert.True(settings.UseMultithreading);
        Assert.Equal(0, settings.ThreadCount);
        Assert.True(settings.PerceptualMode);
        Assert.False(settings.SeparateAlpha);
        Assert.False(settings.ForceAlphaChannel);
        Assert.False(settings.RemoveAlphaChannel);
        Assert.False(settings.ClampMipmaps);
        Assert.Equal(ColorSpace.Auto, settings.ColorSpace);
        Assert.Equal(ToktxFilterType.Kaiser, settings.ToktxMipFilter);
        Assert.Equal(WrapMode.Clamp, settings.WrapMode);
        Assert.False(settings.UseLinearMipFiltering);
        Assert.True(settings.UseSSE41);
        Assert.Equal(KTX2SupercompressionType.Zstandard, settings.KTX2Supercompression);
        Assert.Equal(3, settings.KTX2ZstdLevel);
        Assert.False(settings.ConvertToNormalMap);
        Assert.False(settings.NormalizeVectors);
        Assert.False(settings.KeepRGBLayout);
        Assert.True(settings.RemoveTemporaryMipmaps);
        Assert.Null(settings.HistogramAnalysis);
    }

    #endregion

    #region Property Range Tests

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void CompressionLevel_AcceptsValidValues(int level) {
        var settings = new CompressionSettings { CompressionLevel = level };
        Assert.Equal(level, settings.CompressionLevel);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(192)]
    [InlineData(255)]
    public void QualityLevel_AcceptsValidValues(int quality) {
        var settings = new CompressionSettings { QualityLevel = quality };
        Assert.Equal(quality, settings.QualityLevel);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void UASTCQuality_AcceptsValidValues(int quality) {
        var settings = new CompressionSettings { UASTCQuality = quality };
        Assert.Equal(quality, settings.UASTCQuality);
    }

    [Theory]
    [InlineData(0.001f)]
    [InlineData(0.5f)]
    [InlineData(1.0f)]
    [InlineData(5.0f)]
    [InlineData(10.0f)]
    public void UASTCRDOQuality_AcceptsValidValues(float quality) {
        var settings = new CompressionSettings { UASTCRDOQuality = quality };
        Assert.Equal(quality, settings.UASTCRDOQuality);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    [InlineData(22)]
    public void KTX2ZstdLevel_AcceptsValidValues(int level) {
        var settings = new CompressionSettings { KTX2ZstdLevel = level };
        Assert.Equal(level, settings.KTX2ZstdLevel);
    }

    #endregion

    #region ColorSpace Tests

    [Fact]
    public void ColorSpace_Auto_IsDefault() {
        var settings = new CompressionSettings();
        Assert.Equal(ColorSpace.Auto, settings.ColorSpace);
    }

    [Theory]
    [InlineData(ColorSpace.Auto)]
    [InlineData(ColorSpace.Linear)]
    [InlineData(ColorSpace.SRGB)]
    public void ColorSpace_CanBeSet(ColorSpace colorSpace) {
        var settings = new CompressionSettings { ColorSpace = colorSpace };
        Assert.Equal(colorSpace, settings.ColorSpace);
    }

    #endregion

    #region Histogram Settings Tests

    [Fact]
    public void HistogramAnalysis_DefaultIsNull() {
        var settings = new CompressionSettings();
        Assert.Null(settings.HistogramAnalysis);
    }

    [Fact]
    public void HistogramAnalysis_CanBeSet() {
        var histogramSettings = HistogramSettings.CreateHighQuality();
        var settings = new CompressionSettings { HistogramAnalysis = histogramSettings };

        Assert.NotNull(settings.HistogramAnalysis);
        Assert.Equal(HistogramMode.Percentile, settings.HistogramAnalysis.Mode);
    }

    #endregion

    #region Normal Map Settings Tests

    [Fact]
    public void NormalMapSettings_DefaultsAreDisabled() {
        var settings = new CompressionSettings();

        Assert.False(settings.ConvertToNormalMap);
        Assert.False(settings.NormalizeVectors);
    }

    [Fact]
    public void NormalMapSettings_CanBeEnabled() {
        var settings = new CompressionSettings {
            ConvertToNormalMap = true,
            NormalizeVectors = true
        };

        Assert.True(settings.ConvertToNormalMap);
        Assert.True(settings.NormalizeVectors);
    }

    #endregion

    #region Mipmap Settings Tests

    [Fact]
    public void UseCustomMipmaps_DefaultIsFalse() {
        var settings = new CompressionSettings();
        Assert.False(settings.UseCustomMipmaps);
    }

    [Fact]
    public void UseCustomMipmaps_CanBeEnabled() {
        var settings = new CompressionSettings { UseCustomMipmaps = true };
        Assert.True(settings.UseCustomMipmaps);
    }

    [Theory]
    [InlineData(ToktxFilterType.Box)]
    [InlineData(ToktxFilterType.Tent)]
    [InlineData(ToktxFilterType.Kaiser)]
    [InlineData(ToktxFilterType.CatmullRom)]
    [InlineData(ToktxFilterType.Mitchell)]
    public void ToktxMipFilter_AcceptsValidValues(ToktxFilterType filter) {
        var settings = new CompressionSettings { ToktxMipFilter = filter };
        Assert.Equal(filter, settings.ToktxMipFilter);
    }

    #endregion

    #region Wrap Mode Tests

    [Theory]
    [InlineData(WrapMode.Clamp)]
    [InlineData(WrapMode.Wrap)]
    public void WrapMode_AcceptsValidValues(WrapMode mode) {
        var settings = new CompressionSettings { WrapMode = mode };
        Assert.Equal(mode, settings.WrapMode);
    }

    #endregion

    #region Supercompression Tests

    [Theory]
    [InlineData(KTX2SupercompressionType.None)]
    [InlineData(KTX2SupercompressionType.Zstandard)]
    public void KTX2Supercompression_AcceptsValidValues(KTX2SupercompressionType type) {
        var settings = new CompressionSettings { KTX2Supercompression = type };
        Assert.Equal(type, settings.KTX2Supercompression);
    }

    #endregion

    #region Compression Format Tests

    [Theory]
    [InlineData(CompressionFormat.ETC1S)]
    [InlineData(CompressionFormat.UASTC)]
    public void CompressionFormat_AcceptsValidValues(CompressionFormat format) {
        var settings = new CompressionSettings { CompressionFormat = format };
        Assert.Equal(format, settings.CompressionFormat);
    }

    #endregion

    #region Output Format Tests

    [Theory]
    [InlineData(OutputFormat.KTX2)]
    [InlineData(OutputFormat.Basis)]
    public void OutputFormat_AcceptsValidValues(OutputFormat format) {
        var settings = new CompressionSettings { OutputFormat = format };
        Assert.Equal(format, settings.OutputFormat);
    }

    #endregion

    #region Threading Tests

    [Fact]
    public void ThreadCount_ZeroMeansAuto() {
        var settings = new CompressionSettings { ThreadCount = 0 };
        Assert.Equal(0, settings.ThreadCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    public void ThreadCount_AcceptsPositiveValues(int count) {
        var settings = new CompressionSettings { ThreadCount = count };
        Assert.Equal(count, settings.ThreadCount);
    }

    #endregion
}
