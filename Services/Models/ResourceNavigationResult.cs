using AssetProcessor.Resources;

namespace AssetProcessor.Services.Models;

public sealed class ResourceNavigationResult {
    public TextureResource? Texture { get; init; }
    public TextureResource? OrmGroupTexture { get; init; }
    public ModelResource? Model { get; init; }
    public MaterialResource? Material { get; init; }
    public bool IsOrmFile { get; init; }

    public bool HasMatch => Texture != null || OrmGroupTexture != null || Model != null || Material != null;
}
