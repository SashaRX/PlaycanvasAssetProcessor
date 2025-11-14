using AssetProcessor.Resources;
using AssetProcessor.Services;
using AssetProcessor.Services.Models;
using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureConversion.Settings;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AssetProcessor.Tests.Services;

public class TextureProcessingServiceTests {
    [Fact]
    public async Task ProcessTexturesAsync_SetsSuccessAndPreview() {
        var tempDir = Directory.CreateTempSubdirectory();
        try {
            string sourcePath = Path.Combine(tempDir.FullName, "test.png");
            await File.WriteAllBytesAsync(sourcePath, new byte[] { 0x1, 0x2, 0x3 });

            var texture = new TextureResource { Name = "test_diffuse.png", Path = sourcePath };
            var textures = new List<TextureResource> { texture };

            var pipeline = new FakePipeline((input, output) => {
                File.WriteAllBytes(output, new byte[] { 0x5, 0x6, 0x7, 0x8 });
                return new TextureConversion.Pipeline.ConversionResult {
                    Success = true,
                    OutputPath = output,
                    MipLevels = 4,
                    ToksvigApplied = false
                };
            });

            var service = new TextureProcessingService(new FakePipelineFactory(pipeline));

            var result = await service.ProcessTexturesAsync(new TextureProcessingRequest {
                Textures = textures,
                SettingsProvider = new FakeSettingsProvider(),
                SelectedTexture = texture
            }, CancellationToken.None);

            Assert.Equal(1, result.SuccessCount);
            Assert.Equal(0, result.ErrorCount);
            Assert.Equal(texture, result.PreviewTexture);
            Assert.NotNull(result.PreviewTexturePath);
            Assert.Equal("Converted", texture.Status);
            Assert.Equal(4, texture.MipmapCount);
            Assert.True(texture.CompressedSize > 0);
            Assert.Equal("TestPreset", texture.PresetName);
        } finally {
            tempDir.Delete(true);
        }
    }

    private sealed class FakePipelineFactory : ITextureConversionPipelineFactory {
        private readonly ITextureConversionPipeline pipeline;

        public FakePipelineFactory(ITextureConversionPipeline pipeline) {
            this.pipeline = pipeline;
        }

        public ITextureConversionPipeline Create(string ktxExecutablePath) => pipeline;
    }

    private sealed class FakePipeline : ITextureConversionPipeline {
        private readonly Func<string, string, TextureConversion.Pipeline.ConversionResult> converter;

        public FakePipeline(Func<string, string, TextureConversion.Pipeline.ConversionResult> converter) {
            this.converter = converter;
        }

        public Task<TextureConversion.Pipeline.ConversionResult> ConvertTextureAsync(
            string sourcePath,
            string outputPath,
            MipGenerationProfile mipProfile,
            CompressionSettings compressionSettings,
            ToksvigSettings toksvigSettings,
            bool saveSeparateMipmaps,
            string? mipmapOutputDirectory) =>
            Task.FromResult(converter(sourcePath, outputPath));
    }

    private sealed class FakeSettingsProvider : ITextureConversionSettingsProvider {
        public CompressionSettingsData GetCompressionSettings() => new CompressionSettingsData {
            CompressionFormat = CompressionFormat.ETC1S,
            OutputFormat = OutputFormat.KTX2,
            CompressionLevel = 1,
            QualityLevel = 128,
            UASTCQuality = 2,
            UseUASTCRDO = true,
            UASTCRDOQuality = 1.0f,
            PerceptualMode = true,
            KTX2Supercompression = KTX2SupercompressionType.Zstandard,
            KTX2ZstdLevel = 1,
            UseETC1SRDO = true,
            ForceAlphaChannel = false,
            RemoveAlphaChannel = false,
            ToktxMipFilter = ToktxFilterType.Kaiser,
            WrapMode = WrapMode.Clamp,
            GenerateMipmaps = true,
            UseCustomMipmaps = false,
            RemoveTemporaryMipmaps = true
        };

        public HistogramSettings? GetHistogramSettings() => null;

        public bool SaveSeparateMipmaps => false;

        public ToksvigSettings GetToksvigSettings(string texturePath) => new ToksvigSettings { Enabled = false };

        public string? PresetName => "TestPreset";
    }
}
