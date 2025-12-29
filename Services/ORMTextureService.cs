using AssetProcessor.Resources;
using AssetProcessor.TextureConversion.Core;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetProcessor.Services;

/// <summary>
/// Реализация сервиса ORM текстур.
/// </summary>
public class ORMTextureService : IORMTextureService {
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();
    private readonly ILogService _logService;

    public ORMTextureService(ILogService logService) {
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
    }

    public ChannelPackingMode DetectPackingMode(TextureResource? ao, TextureResource? gloss, TextureResource? metallic) {
        int count = 0;
        if (ao != null) count++;
        if (gloss != null) count++;
        if (metallic != null) count++;

        if (count < 2) return ChannelPackingMode.None;

        if (ao != null && gloss != null && metallic != null) {
            return ChannelPackingMode.OGM;
        } else if (ao != null && gloss != null) {
            return ChannelPackingMode.OG;
        } else {
            return ChannelPackingMode.OGM;
        }
    }

    public TextureResource? FindTextureById(int? mapId, IEnumerable<TextureResource> textures) {
        if (mapId == null) return null;

        var found = textures.FirstOrDefault(t => t.ID == mapId.Value);
        if (found == null) {
            logger.Debug($"Texture with ID {mapId.Value} not found");
        }
        return found;
    }

    public WorkflowResult DetectWorkflow(MaterialResource material, IEnumerable<TextureResource> textures) {
        var textureList = textures.ToList();

        var aoTexture = FindTextureById(material.AOMapId, textureList);
        var glossTexture = FindTextureById(material.GlossMapId, textureList);
        var metalnessCandidate = FindTextureById(material.MetalnessMapId, textureList);
        var specularCandidate = FindTextureById(material.SpecularMapId, textureList);

        TextureResource? metalnessOrSpecular;
        bool isMetalness;
        string workflowInfo;
        string mapType;

        if (metalnessCandidate != null) {
            metalnessOrSpecular = metalnessCandidate;
            isMetalness = true;
            workflowInfo = "Workflow: Metalness (PBR)";
            mapType = "Metallic";
            _logService.LogInfo($"Metalness workflow detected: Metallic={metalnessCandidate.Name}");
        } else if (specularCandidate != null) {
            metalnessOrSpecular = specularCandidate;
            isMetalness = false;
            workflowInfo = "Workflow: Specular (Legacy)\nNote: Specular map will be used as Metallic";
            mapType = "Specular";
            _logService.LogInfo($"Specular workflow detected: Specular={specularCandidate.Name}");
        } else {
            metalnessOrSpecular = null;
            isMetalness = material.UseMetalness;
            workflowInfo = material.UseMetalness ? "Workflow: Metalness (PBR)" : "Workflow: Specular (Legacy)";
            mapType = material.UseMetalness ? "Metallic" : "Specular";
            _logService.LogWarn($"No metallic or specular texture found for material '{material.Name}'");
        }

        return new WorkflowResult {
            IsMetalnessWorkflow = isMetalness,
            WorkflowInfo = workflowInfo,
            MapTypeLabel = mapType,
            MetalnessOrSpecularTexture = metalnessOrSpecular,
            AOTexture = aoTexture,
            GlossTexture = glossTexture
        };
    }

    public ORMCreationResult CreateORMFromMaterial(MaterialResource material, IEnumerable<TextureResource> textures) {
        var textureList = textures.ToList();
        var workflow = DetectWorkflow(material, textureList);

        var mode = DetectPackingMode(workflow.AOTexture, workflow.GlossTexture, workflow.MetalnessOrSpecularTexture);

        if (mode == ChannelPackingMode.None) {
            return new ORMCreationResult {
                Success = false,
                PackingMode = mode,
                ErrorMessage = $"Insufficient textures for ORM packing.\n\n" +
                              $"{workflow.WorkflowInfo}\n\n" +
                              $"AO: {(workflow.AOTexture != null ? "Found" : "Missing")}\n" +
                              $"Gloss: {(workflow.GlossTexture != null ? "Found" : "Missing")}\n" +
                              $"{workflow.MapTypeLabel}: {(workflow.MetalnessOrSpecularTexture != null ? "Found" : "Missing")}\n\n" +
                              $"At least 2 textures are required."
            };
        }

        string ormName = GenerateORMName(material.Name, mode);

        // Check for existing
        var existing = textureList.OfType<ORMTextureResource>().FirstOrDefault(t => t.Name == ormName);
        if (existing != null) {
            return new ORMCreationResult {
                Success = false,
                AlreadyExists = true,
                ExistingName = ormName,
                PackingMode = mode
            };
        }

        var ormTexture = new ORMTextureResource {
            Name = ormName,
            TextureType = "ORM (Virtual)",
            PackingMode = mode,
            AOSource = workflow.AOTexture,
            GlossSource = workflow.GlossTexture,
            MetallicSource = workflow.MetalnessOrSpecularTexture,
            Status = "Ready to pack"
        };

        _logService.LogInfo($"Created ORM texture '{ormName}' with mode {mode}");

        return new ORMCreationResult {
            Success = true,
            ORMTexture = ormTexture,
            PackingMode = mode
        };
    }

    public ORMTextureResource CreateEmptyORM(IEnumerable<TextureResource> existingTextures) {
        int ormCount = existingTextures.Count(t => t is ORMTextureResource) + 1;

        return new ORMTextureResource {
            Name = $"[ORM Texture {ormCount}]",
            TextureType = "ORM (Virtual)",
            PackingMode = ChannelPackingMode.OGM,
            Status = "Ready to configure"
        };
    }

    public string GenerateORMName(string? materialName, ChannelPackingMode mode) {
        string baseName = GetBaseMaterialName(materialName);
        string suffix = mode switch {
            ChannelPackingMode.OG => "_og",
            ChannelPackingMode.OGM => "_ogm",
            ChannelPackingMode.OGMH => "_ogmh",
            _ => "_ogm"
        };
        return baseName + suffix;
    }

    public string GetBaseMaterialName(string? materialName) {
        if (string.IsNullOrEmpty(materialName)) {
            return "unknown";
        }

        if (materialName.EndsWith("_mat", StringComparison.OrdinalIgnoreCase)) {
            return materialName.Substring(0, materialName.Length - 4);
        }

        return materialName;
    }
}
