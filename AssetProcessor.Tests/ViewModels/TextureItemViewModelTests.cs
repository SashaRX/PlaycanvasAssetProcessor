using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureConversion.Settings;
using AssetProcessor.ViewModels;
using Xunit;

namespace AssetProcessor.Tests.ViewModels;

public class TextureItemViewModelTests {
    [Fact]
    public void BuildToksvigSettings_UsesViewModelValues() {
        var sourceSettings = new TextureConversionSettings {
            TexturePath = "textures/rough.png",
            TextureType = TextureType.Roughness,
            MipProfile = new MipProfileSettings(),
            Compression = new CompressionSettingsData(),
            ToksvigSettings = ToksvigSettings.CreateDefault()
        };

        var viewModel = new TextureItemViewModel(sourceSettings) {
            ToksvigEnabled = true,
            ToksvigCalculationMode = ToksvigCalculationMode.Simplified,
            ToksvigCompositePower = 2.5f,
            ToksvigMinMipLevel = 1,
            ToksvigSmoothVariance = false,
            ToksvigUseEnergyPreserving = false,
            ToksvigVarianceThreshold = 0.004f,
            ToksvigNormalMapPath = "textures/rough_normal.png"
        };

        var toksvig = viewModel.BuildToksvigSettings();

        Assert.True(toksvig.Enabled);
        Assert.Equal(ToksvigCalculationMode.Simplified, toksvig.CalculationMode);
        Assert.Equal(2.5f, toksvig.CompositePower);
        Assert.Equal(1, toksvig.MinToksvigMipLevel);
        Assert.False(toksvig.SmoothVariance);
        Assert.False(toksvig.UseEnergyPreserving);
        Assert.Equal(0.004f, toksvig.VarianceThreshold);
        Assert.Equal("textures/rough_normal.png", toksvig.NormalMapPath);
    }

    [Fact]
    public void ToSettings_PersistsToksvigConfiguration() {
        var viewModel = new TextureItemViewModel {
            TexturePath = "textures/gloss.png",
            TextureType = TextureType.Gloss,
            MipProfile = new MipProfileSettings(),
            Compression = new CompressionSettingsData(),
            IsEnabled = true,
            SaveSeparateMipmaps = false,
            ToksvigEnabled = true,
            ToksvigCalculationMode = ToksvigCalculationMode.Classic,
            ToksvigCompositePower = 1.2f,
            ToksvigMinMipLevel = 2,
            ToksvigSmoothVariance = true,
            ToksvigUseEnergyPreserving = true,
            ToksvigVarianceThreshold = 0.0015f,
            ToksvigNormalMapPath = "textures/gloss_normal.png"
        };

        var serialized = viewModel.ToSettings();

        Assert.NotNull(serialized.ToksvigSettings);
        Assert.True(serialized.ToksvigSettings.Enabled);
        Assert.Equal(ToksvigCalculationMode.Classic, serialized.ToksvigSettings.CalculationMode);
        Assert.Equal(1.2f, serialized.ToksvigSettings.CompositePower);
        Assert.Equal(2, serialized.ToksvigSettings.MinToksvigMipLevel);
        Assert.True(serialized.ToksvigSettings.SmoothVariance);
        Assert.True(serialized.ToksvigSettings.UseEnergyPreserving);
        Assert.Equal(0.0015f, serialized.ToksvigSettings.VarianceThreshold);
        Assert.Equal("textures/gloss_normal.png", serialized.ToksvigSettings.NormalMapPath);
    }
}
