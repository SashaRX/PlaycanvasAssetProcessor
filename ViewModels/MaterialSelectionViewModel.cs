using AssetProcessor.Resources;
using AssetProcessor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AssetProcessor.ViewModels;

/// <summary>
/// ViewModel for material selection and texture navigation.
/// Handles material loading from files and navigation requests.
/// </summary>
public partial class MaterialSelectionViewModel : ObservableObject {
    private readonly IAssetResourceService assetResourceService;
    private readonly ILogService logService;

    [ObservableProperty]
    private MaterialResource? selectedMaterial;

    [ObservableProperty]
    private MaterialResource? loadedMaterialParameters;

    [ObservableProperty]
    private bool isLoadingMaterial;

    /// <summary>
    /// Raised when material parameters have been loaded from file.
    /// </summary>
    public event EventHandler<MaterialParametersLoadedEventArgs>? MaterialParametersLoaded;

    /// <summary>
    /// Raised when navigation to a texture is requested.
    /// </summary>
    public event EventHandler<NavigateToTextureEventArgs>? NavigateToTextureRequested;

    /// <summary>
    /// Raised when an error occurs during material operations.
    /// </summary>
    public event EventHandler<MaterialErrorEventArgs>? ErrorOccurred;

    public MaterialSelectionViewModel(IAssetResourceService assetResourceService, ILogService logService) {
        this.assetResourceService = assetResourceService;
        this.logService = logService;
    }

    /// <summary>
    /// Selects a material and loads its parameters from file if available.
    /// </summary>
    [RelayCommand]
    private async Task SelectMaterialAsync(MaterialResource? material, CancellationToken ct = default) {
        if (material == null) {
            SelectedMaterial = null;
            LoadedMaterialParameters = null;
            return;
        }

        SelectedMaterial = material;
        IsLoadingMaterial = true;

        try {
            if (!string.IsNullOrEmpty(material.Path) && File.Exists(material.Path)) {
                var loadedParameters = await assetResourceService.LoadMaterialFromFileAsync(material.Path, ct);
                if (loadedParameters != null) {
                    LoadedMaterialParameters = loadedParameters;
                    MaterialParametersLoaded?.Invoke(this, new MaterialParametersLoadedEventArgs(loadedParameters));
                } else {
                    LoadedMaterialParameters = material;
                    MaterialParametersLoaded?.Invoke(this, new MaterialParametersLoadedEventArgs(material));
                }
            } else {
                LoadedMaterialParameters = material;
                MaterialParametersLoaded?.Invoke(this, new MaterialParametersLoadedEventArgs(material));
            }
        } catch (Exception ex) {
            logService.LogError($"Error loading material parameters: {ex.Message}");
            ErrorOccurred?.Invoke(this, new MaterialErrorEventArgs($"Failed to load material: {ex.Message}", ex));
            LoadedMaterialParameters = material;
            MaterialParametersLoaded?.Invoke(this, new MaterialParametersLoadedEventArgs(material));
        } finally {
            IsLoadingMaterial = false;
        }
    }

    /// <summary>
    /// Navigates to a texture by its ID.
    /// </summary>
    [RelayCommand]
    private void NavigateToTexture(NavigateToTextureRequest request) {
        if (!request.TextureId.HasValue) {
            logService.LogWarn($"Cannot navigate: texture ID is null for {request.MapType}");
            return;
        }

        logService.LogInfo($"Navigation to texture {request.TextureId} ({request.MapType}) requested from material {request.MaterialName}");
        NavigateToTextureRequested?.Invoke(this, new NavigateToTextureEventArgs(
            request.TextureId.Value,
            request.MapType,
            request.MaterialName));
    }

    /// <summary>
    /// Gets texture IDs associated with the currently selected material.
    /// </summary>
    public IEnumerable<MaterialTextureReference> GetMaterialTextureReferences() {
        var material = LoadedMaterialParameters ?? SelectedMaterial;
        if (material == null) yield break;

        if (material.DiffuseMapId.HasValue)
            yield return new MaterialTextureReference("Diffuse", material.DiffuseMapId.Value);
        if (material.NormalMapId.HasValue)
            yield return new MaterialTextureReference("Normal", material.NormalMapId.Value);
        if (material.SpecularMapId.HasValue)
            yield return new MaterialTextureReference("Specular", material.SpecularMapId.Value);
        if (material.MetalnessMapId.HasValue)
            yield return new MaterialTextureReference("Metalness", material.MetalnessMapId.Value);
        if (material.GlossMapId.HasValue)
            yield return new MaterialTextureReference("Gloss", material.GlossMapId.Value);
        if (material.AOMapId.HasValue)
            yield return new MaterialTextureReference("AO", material.AOMapId.Value);
        if (material.EmissiveMapId.HasValue)
            yield return new MaterialTextureReference("Emissive", material.EmissiveMapId.Value);
        if (material.OpacityMapId.HasValue)
            yield return new MaterialTextureReference("Opacity", material.OpacityMapId.Value);
    }

    /// <summary>
    /// Finds a texture by ID from provided collection.
    /// </summary>
    public TextureResource? FindTextureById(int textureId, IEnumerable<TextureResource> textures) {
        return textures.FirstOrDefault(t => t.ID == textureId);
    }
}

#region Event Args

public sealed class MaterialParametersLoadedEventArgs : EventArgs {
    public MaterialResource Material { get; }

    public MaterialParametersLoadedEventArgs(MaterialResource material) {
        Material = material;
    }
}

public sealed class NavigateToTextureEventArgs : EventArgs {
    public int TextureId { get; }
    public string MapType { get; }
    public string? MaterialName { get; }

    public NavigateToTextureEventArgs(int textureId, string mapType, string? materialName = null) {
        TextureId = textureId;
        MapType = mapType;
        MaterialName = materialName;
    }
}

public sealed class MaterialErrorEventArgs : EventArgs {
    public string Message { get; }
    public Exception? Exception { get; }

    public MaterialErrorEventArgs(string message, Exception? exception = null) {
        Message = message;
        Exception = exception;
    }
}

#endregion

#region Request Types

public sealed class NavigateToTextureRequest {
    public int? TextureId { get; init; }
    public string MapType { get; init; } = string.Empty;
    public string? MaterialName { get; init; }
}

public sealed record MaterialTextureReference(string MapType, int TextureId);

#endregion
