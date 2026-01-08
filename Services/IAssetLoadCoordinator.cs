using AssetProcessor.Resources;
using AssetProcessor.Services.Models;

namespace AssetProcessor.Services;

/// <summary>
/// Coordinates asset loading from local JSON cache.
/// Returns processed assets; UI updates are handled by the caller.
/// </summary>
public interface IAssetLoadCoordinator {
    /// <summary>
    /// Loads and processes assets from local JSON cache.
    /// </summary>
    /// <param name="projectFolderPath">Path to the project folder</param>
    /// <param name="projectName">Name of the project</param>
    /// <param name="projectsRoot">Root folder for all projects</param>
    /// <param name="progress">Optional progress reporter</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result containing processed assets or error</returns>
    Task<AssetLoadResult> LoadAssetsFromJsonAsync(
        string projectFolderPath,
        string projectName,
        string projectsRoot,
        IProgress<AssetLoadProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Generates virtual ORM textures for texture groups containing AO/Gloss/Metallic/Height.
    /// These virtual textures can be processed to create packed ORM textures.
    /// </summary>
    /// <param name="textures">Collection of textures to analyze</param>
    /// <param name="projectId">Project ID for settings persistence</param>
    /// <returns>List of generated virtual ORM textures</returns>
    IReadOnlyList<ORMTextureResource> GenerateVirtualORMTextures(
        IEnumerable<TextureResource> textures,
        int projectId);

    /// <summary>
    /// Detects and loads existing ORM textures from the file system.
    /// These are pre-packed KTX2 files with _og/_ogm/_ogmh suffixes.
    /// </summary>
    /// <param name="projectFolderPath">Path to the project folder</param>
    /// <param name="existingTextures">Collection of existing textures for source matching</param>
    /// <param name="projectId">Project ID for settings persistence</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of detected ORM textures</returns>
    Task<IReadOnlyList<ORMTextureResource>> DetectExistingORMTexturesAsync(
        string projectFolderPath,
        IEnumerable<TextureResource> existingTextures,
        int projectId,
        CancellationToken cancellationToken);
}
