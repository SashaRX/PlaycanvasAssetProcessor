using AssetProcessor.Resources;
using AssetProcessor.Services;
using AssetProcessor.TextureConversion.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AssetProcessor.ViewModels;

/// <summary>
/// ViewModel for ORM texture creation, management, and operations.
/// Handles business logic and raises events for UI-specific operations.
/// </summary>
public partial class ORMTextureViewModel : ObservableObject {
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();

    private readonly IORMTextureService ormTextureService;
    private readonly ILogService logService;

    #region Observable Properties

    [ObservableProperty]
    private bool isCreatingORM;

    [ObservableProperty]
    private int creationProgress;

    [ObservableProperty]
    private int creationTotal;

    [ObservableProperty]
    private string? creationStatus;

    #endregion

    #region Events

    /// <summary>
    /// Raised when an ORM texture is created successfully
    /// </summary>
    public event EventHandler<ORMCreatedEventArgs>? ORMCreated;

    /// <summary>
    /// Raised when an ORM texture is deleted
    /// </summary>
    public event EventHandler<ORMDeletedEventArgs>? ORMDeleted;

    /// <summary>
    /// Raised when user confirmation is needed (e.g., overwrite existing)
    /// </summary>
    public event EventHandler<ORMConfirmationRequestEventArgs>? ConfirmationRequested;

    /// <summary>
    /// Raised when an error occurs that should be shown to user
    /// </summary>
    public event EventHandler<ORMErrorEventArgs>? ErrorOccurred;

    /// <summary>
    /// Raised when batch creation completes
    /// </summary>
    public event EventHandler<ORMBatchCreationCompletedEventArgs>? BatchCreationCompleted;

    #endregion

    public ORMTextureViewModel(IORMTextureService ormTextureService, ILogService logService) {
        this.ormTextureService = ormTextureService ?? throw new ArgumentNullException(nameof(ormTextureService));
        this.logService = logService ?? throw new ArgumentNullException(nameof(logService));
    }

    /// <summary>
    /// Creates an empty ORM texture with a unique name.
    /// </summary>
    [RelayCommand]
    private void CreateEmptyORM(ObservableCollection<TextureResource> textures) {
        if (textures == null) return;

        try {
            var ormTexture = ormTextureService.CreateEmptyORM(textures);
            textures.Add(ormTexture);

            logService.LogInfo($"Created new ORM texture: {ormTexture.Name}");
            ORMCreated?.Invoke(this, new ORMCreatedEventArgs(ormTexture, isNew: true));
        } catch (Exception ex) {
            logService.LogError($"Error creating ORM texture: {ex.Message}");
            ErrorOccurred?.Invoke(this, new ORMErrorEventArgs("Failed to create ORM texture", ex.Message));
        }
    }

    /// <summary>
    /// Creates an ORM texture from a material's texture references.
    /// </summary>
    [RelayCommand]
    private void CreateORMFromMaterial(ORMFromMaterialRequest request) {
        if (request?.Material == null || request.Textures == null) {
            ErrorOccurred?.Invoke(this, new ORMErrorEventArgs("No Material Selected", "Please select a material first."));
            return;
        }

        var material = request.Material;
        var textures = request.Textures;

        try {
            // Use service to find textures and detect workflow
            var aoTexture = ormTextureService.FindTextureById(material.AOMapId, textures);
            var glossTexture = ormTextureService.FindTextureById(material.GlossMapId, textures);
            var workflowResult = ormTextureService.DetectWorkflow(material, textures);

            logService.LogInfo($"Material '{material.Name}': AO={aoTexture?.Name ?? "null"}, Gloss={glossTexture?.Name ?? "null"}, {workflowResult.WorkflowInfo}");

            // Auto-detect packing mode
            var mode = ormTextureService.DetectPackingMode(aoTexture, glossTexture, workflowResult.MetalnessOrSpecularTexture);

            if (mode == ChannelPackingMode.None) {
                // Build detailed error message
                var aoStatus = aoTexture != null ? $"Found: {aoTexture.Name}" : "Missing";
                var glossStatus = glossTexture != null ? $"Found: {glossTexture.Name}" : "Missing";
                var metallicStatus = workflowResult.MetalnessOrSpecularTexture != null
                    ? $"Found: {workflowResult.MetalnessOrSpecularTexture.Name}"
                    : "Missing";

                var message = $"Cannot create ORM texture - insufficient textures.\n\n" +
                              $"{workflowResult.WorkflowInfo}\n\n" +
                              $"AO: {aoStatus}\n" +
                              $"Gloss: {glossStatus}\n" +
                              $"{workflowResult.MapTypeLabel}: {metallicStatus}\n\n" +
                              $"Required combinations:\n" +
                              $"  - OGM: AO + Gloss + Metallic\n" +
                              $"  - OG: AO + Gloss";

                ErrorOccurred?.Invoke(this, new ORMErrorEventArgs("Insufficient Textures", message));
                return;
            }

            // Generate ORM name
            string baseMaterialName = ormTextureService.GetBaseMaterialName(material.Name);
            string ormTextureName = ormTextureService.GenerateORMName(baseMaterialName, mode);

            // Check for existing ORM
            var existingORM = textures.OfType<ORMTextureResource>()
                .FirstOrDefault(t => t.Name == ormTextureName);

            if (existingORM != null) {
                // Request confirmation to overwrite
                var confirmArgs = new ORMConfirmationRequestEventArgs(
                    $"ORM texture '{ormTextureName}' already exists.\n\nDo you want to update it?",
                    existingORM,
                    () => {
                        // Callback: remove existing and create new
                        textures.Remove(existingORM);
                        CreateAndAddORM(textures, material, ormTextureName, mode, aoTexture, glossTexture, workflowResult.MetalnessOrSpecularTexture);
                    });
                ConfirmationRequested?.Invoke(this, confirmArgs);
                return;
            }

            CreateAndAddORM(textures, material, ormTextureName, mode, aoTexture, glossTexture, workflowResult.MetalnessOrSpecularTexture);

        } catch (Exception ex) {
            logService.LogError($"Error creating ORM from material: {ex.Message}");
            ErrorOccurred?.Invoke(this, new ORMErrorEventArgs("Error", $"Failed to create ORM texture: {ex.Message}"));
        }
    }

    private void CreateAndAddORM(
        ObservableCollection<TextureResource> textures,
        MaterialResource material,
        string ormTextureName,
        ChannelPackingMode mode,
        TextureResource? aoTexture,
        TextureResource? glossTexture,
        TextureResource? metallicTexture) {

        var ormTexture = new ORMTextureResource {
            Name = ormTextureName,
            TextureType = "ORM (Virtual)",
            PackingMode = mode,
            AOSource = aoTexture,
            GlossSource = glossTexture,
            MetallicSource = metallicTexture,
            Status = "Ready to pack"
        };

        textures.Add(ormTexture);

        logService.LogInfo($"Created ORM texture '{ormTexture.Name}' with mode {mode}");
        ORMCreated?.Invoke(this, new ORMCreatedEventArgs(ormTexture, isNew: true, mode, aoTexture, glossTexture, metallicTexture));
    }

    /// <summary>
    /// Creates ORM textures for all materials that have sufficient textures.
    /// </summary>
    [RelayCommand]
    private async Task CreateAllORMsAsync(ORMBatchCreationRequest request, CancellationToken ct = default) {
        if (request?.Materials == null || request.Textures == null) return;

        var materials = request.Materials.ToList();
        var textures = request.Textures;

        CreationTotal = materials.Count;
        CreationProgress = 0;
        IsCreatingORM = true;
        CreationStatus = "Creating ORM textures...";

        int created = 0;
        int skipped = 0;
        var errors = new List<string>();

        try {
            foreach (var material in materials) {
                ct.ThrowIfCancellationRequested();

                try {
                    var result = TryCreateORMForMaterial(material, textures);
                    if (result.Created) {
                        created++;
                    } else {
                        skipped++;
                    }
                    if (result.Error != null) {
                        errors.Add($"{material.Name}: {result.Error}");
                    }
                } catch (Exception ex) {
                    errors.Add($"{material.Name}: {ex.Message}");
                }

                CreationProgress++;
                CreationStatus = $"Processing {CreationProgress}/{CreationTotal}...";

                // Allow UI to update
                await Task.Yield();
            }

            logService.LogInfo($"Batch ORM creation: {created} created, {skipped} skipped, {errors.Count} errors");
            BatchCreationCompleted?.Invoke(this, new ORMBatchCreationCompletedEventArgs(created, skipped, errors));

        } finally {
            IsCreatingORM = false;
            CreationStatus = null;
        }
    }

    private (bool Created, string? Error) TryCreateORMForMaterial(
        MaterialResource material,
        ObservableCollection<TextureResource> textures) {

        var aoTexture = ormTextureService.FindTextureById(material.AOMapId, textures);
        var glossTexture = ormTextureService.FindTextureById(material.GlossMapId, textures);
        var workflowResult = ormTextureService.DetectWorkflow(material, textures);

        var mode = ormTextureService.DetectPackingMode(aoTexture, glossTexture, workflowResult.MetalnessOrSpecularTexture);

        if (mode == ChannelPackingMode.None) {
            return (false, null); // Just skip, not an error
        }

        string baseMaterialName = ormTextureService.GetBaseMaterialName(material.Name);
        string ormTextureName = ormTextureService.GenerateORMName(baseMaterialName, mode);

        // Check if already exists
        var existingORM = textures.OfType<ORMTextureResource>()
            .FirstOrDefault(t => t.Name == ormTextureName);

        if (existingORM != null) {
            return (false, null); // Skip existing
        }

        var ormTexture = new ORMTextureResource {
            Name = ormTextureName,
            TextureType = "ORM (Virtual)",
            PackingMode = mode,
            AOSource = aoTexture,
            GlossSource = glossTexture,
            MetallicSource = workflowResult.MetalnessOrSpecularTexture,
            Status = "Ready to pack"
        };

        textures.Add(ormTexture);
        return (true, null);
    }

    /// <summary>
    /// Deletes an ORM texture associated with a material.
    /// </summary>
    [RelayCommand]
    private void DeleteORMForMaterial(ORMDeleteRequest request) {
        if (request?.Material == null || request.Textures == null) return;

        var material = request.Material;
        var textures = request.Textures;

        string baseMaterialName = ormTextureService.GetBaseMaterialName(material.Name);

        // Try all possible ORM suffixes
        var ormTexture = textures.OfType<ORMTextureResource>()
            .FirstOrDefault(t => t.Name == baseMaterialName + "_og" ||
                                 t.Name == baseMaterialName + "_ogm" ||
                                 t.Name == baseMaterialName + "_ogmh");

        if (ormTexture == null) {
            ErrorOccurred?.Invoke(this, new ORMErrorEventArgs(
                "Not Found",
                $"No ORM texture found for material '{material.Name}'.\n\nExpected: {baseMaterialName}_og, {baseMaterialName}_ogm, or {baseMaterialName}_ogmh"));
            return;
        }

        // Request confirmation
        var confirmArgs = new ORMConfirmationRequestEventArgs(
            $"Delete ORM texture '{ormTexture.Name}'?\n\nThis will only remove the virtual container, not the source textures.",
            ormTexture,
            () => {
                textures.Remove(ormTexture);
                logService.LogInfo($"Deleted ORM texture: {ormTexture.Name}");
                ORMDeleted?.Invoke(this, new ORMDeletedEventArgs(ormTexture));
            });
        ConfirmationRequested?.Invoke(this, confirmArgs);
    }

    /// <summary>
    /// Deletes an ORM texture directly from the texture list.
    /// </summary>
    [RelayCommand]
    private void DeleteORM(ORMDirectDeleteRequest request) {
        if (request?.ORMTexture == null || request.Textures == null) {
            ErrorOccurred?.Invoke(this, new ORMErrorEventArgs(
                "Not an ORM Texture",
                "Please select an ORM texture to delete.\n\nThis option only works for ORM textures (textureName_og, textureName_ogm, textureName_ogmh)."));
            return;
        }

        var ormTexture = request.ORMTexture;
        var textures = request.Textures;

        // Request confirmation
        var confirmArgs = new ORMConfirmationRequestEventArgs(
            $"Delete ORM texture '{ormTexture.Name}'?\n\nThis will only remove the virtual container, not the source textures.",
            ormTexture,
            () => {
                textures.Remove(ormTexture);
                logService.LogInfo($"Deleted ORM texture '{ormTexture.Name}' from texture list");
                ORMDeleted?.Invoke(this, new ORMDeletedEventArgs(ormTexture));
            });
        ConfirmationRequested?.Invoke(this, confirmArgs);
    }
}

#region Event Args and Request Types

/// <summary>
/// Request for creating ORM from a material
/// </summary>
public class ORMFromMaterialRequest {
    public MaterialResource? Material { get; init; }
    public ObservableCollection<TextureResource>? Textures { get; init; }
}

/// <summary>
/// Request for batch ORM creation
/// </summary>
public class ORMBatchCreationRequest {
    public IEnumerable<MaterialResource>? Materials { get; init; }
    public ObservableCollection<TextureResource>? Textures { get; init; }
}

/// <summary>
/// Request for deleting ORM by material
/// </summary>
public class ORMDeleteRequest {
    public MaterialResource? Material { get; init; }
    public ObservableCollection<TextureResource>? Textures { get; init; }
}

/// <summary>
/// Request for direct ORM deletion
/// </summary>
public class ORMDirectDeleteRequest {
    public ORMTextureResource? ORMTexture { get; init; }
    public ObservableCollection<TextureResource>? Textures { get; init; }
}

/// <summary>
/// Event args when ORM is created
/// </summary>
public class ORMCreatedEventArgs : EventArgs {
    public ORMTextureResource ORMTexture { get; }
    public bool IsNew { get; }
    public ChannelPackingMode? Mode { get; }
    public TextureResource? AOSource { get; }
    public TextureResource? GlossSource { get; }
    public TextureResource? MetallicSource { get; }

    public ORMCreatedEventArgs(ORMTextureResource ormTexture, bool isNew = false,
        ChannelPackingMode? mode = null, TextureResource? ao = null,
        TextureResource? gloss = null, TextureResource? metallic = null) {
        ORMTexture = ormTexture;
        IsNew = isNew;
        Mode = mode;
        AOSource = ao;
        GlossSource = gloss;
        MetallicSource = metallic;
    }
}

/// <summary>
/// Event args when ORM is deleted
/// </summary>
public class ORMDeletedEventArgs : EventArgs {
    public ORMTextureResource ORMTexture { get; }

    public ORMDeletedEventArgs(ORMTextureResource ormTexture) {
        ORMTexture = ormTexture;
    }
}

/// <summary>
/// Event args for confirmation requests
/// </summary>
public class ORMConfirmationRequestEventArgs : EventArgs {
    public string Message { get; }
    public ORMTextureResource ORMTexture { get; }
    public Action OnConfirmed { get; }

    public ORMConfirmationRequestEventArgs(string message, ORMTextureResource ormTexture, Action onConfirmed) {
        Message = message;
        ORMTexture = ormTexture;
        OnConfirmed = onConfirmed;
    }
}

/// <summary>
/// Event args for errors
/// </summary>
public class ORMErrorEventArgs : EventArgs {
    public string Title { get; }
    public string Message { get; }

    public ORMErrorEventArgs(string title, string message) {
        Title = title;
        Message = message;
    }
}

/// <summary>
/// Event args for batch creation completion
/// </summary>
public class ORMBatchCreationCompletedEventArgs : EventArgs {
    public int Created { get; }
    public int Skipped { get; }
    public IReadOnlyList<string> Errors { get; }

    public ORMBatchCreationCompletedEventArgs(int created, int skipped, IReadOnlyList<string> errors) {
        Created = created;
        Skipped = skipped;
        Errors = errors;
    }
}

#endregion
