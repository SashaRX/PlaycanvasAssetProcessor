using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureConversion.Pipeline;
using AssetProcessor.TextureConversion.Settings;

namespace AssetProcessor.Services;

public interface ITextureConversionPipeline {
    Task<ConversionResult> ConvertTextureAsync(
        string sourcePath,
        string outputPath,
        MipGenerationProfile mipProfile,
        CompressionSettings compressionSettings,
        ToksvigSettings toksvigSettings,
        bool saveSeparateMipmaps,
        string? mipmapOutputDirectory);
}

public interface ITextureConversionPipelineFactory {
    ITextureConversionPipeline Create(string ktxExecutablePath);
}

internal sealed class TextureConversionPipelineFactory : ITextureConversionPipelineFactory {
    public ITextureConversionPipeline Create(string ktxExecutablePath) =>
        new TextureConversionPipelineAdapter(new TextureConversionPipeline(ktxExecutablePath));
}

internal sealed class TextureConversionPipelineAdapter : ITextureConversionPipeline {
    private readonly TextureConversionPipeline pipeline;

    public TextureConversionPipelineAdapter(TextureConversionPipeline pipeline) {
        this.pipeline = pipeline;
    }

    public Task<ConversionResult> ConvertTextureAsync(
        string sourcePath,
        string outputPath,
        MipGenerationProfile mipProfile,
        CompressionSettings compressionSettings,
        ToksvigSettings toksvigSettings,
        bool saveSeparateMipmaps,
        string? mipmapOutputDirectory) =>
        pipeline.ConvertTextureAsync(
            sourcePath,
            outputPath,
            mipProfile,
            compressionSettings,
            toksvigSettings,
            saveSeparateMipmaps,
            mipmapOutputDirectory);
}
