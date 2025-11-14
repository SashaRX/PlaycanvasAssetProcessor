using AssetProcessor.Resources;
using AssetProcessor.Services.Models;

namespace AssetProcessor.Services;

public interface ITextureProcessingService {
    Task<TextureProcessingResult> ProcessTexturesAsync(TextureProcessingRequest request, CancellationToken cancellationToken);

    Task<TexturePreviewResult?> LoadKtxPreviewAsync(TextureResource texture, CancellationToken cancellationToken);

    TextureAutoDetectResult AutoDetectPresets(IEnumerable<TextureResource> textures, ITextureConversionSettingsProvider settingsProvider);
}
