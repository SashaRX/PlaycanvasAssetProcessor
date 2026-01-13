namespace AssetProcessor.MasterMaterials.Models;

/// <summary>
/// Defines the standard shader chunk schema for PlayCanvas materials.
/// Each slot represents a customizable part of the shader pipeline.
/// </summary>
public static class ShaderChunkSchema
{
    /// <summary>
    /// All available chunk slots in rendering order
    /// </summary>
    public static IReadOnlyList<ChunkSlot> AllSlots { get; } = new List<ChunkSlot>
    {
        // ===== VERTEX SHADER SLOTS =====
        new ChunkSlot
        {
            Id = "transform",
            DisplayName = "Transform",
            Category = "Vertex",
            ShaderType = "vertex",
            DefaultChunkId = "transformVS",
            Description = "Vertex position transformation",
            Order = 10,
            IsRequired = true
        },
        new ChunkSlot
        {
            Id = "normal_vertex",
            DisplayName = "Normal (Vertex)",
            Category = "Vertex",
            ShaderType = "vertex",
            DefaultChunkId = "normalVS",
            Description = "Vertex normal transformation",
            Order = 20,
            IsRequired = true
        },

        // ===== SURFACE SLOTS (Fragment) =====
        new ChunkSlot
        {
            Id = "diffuse",
            DisplayName = "Diffuse/Albedo",
            Category = "Surface",
            ShaderType = "fragment",
            DefaultChunkId = "diffusePS",
            Description = "Base color/albedo sampling",
            Order = 100,
            IsRequired = true
        },
        new ChunkSlot
        {
            Id = "normal",
            DisplayName = "Normal Map",
            Category = "Surface",
            ShaderType = "fragment",
            DefaultChunkId = "normalMapPS",
            Description = "Normal map sampling and transformation",
            Order = 110,
            IsEnabledByDefault = true
        },
        new ChunkSlot
        {
            Id = "specular",
            DisplayName = "Specular",
            Category = "Surface",
            ShaderType = "fragment",
            DefaultChunkId = "specularPS",
            Description = "Specular color sampling",
            Order = 120,
            IsEnabledByDefault = true
        },
        new ChunkSlot
        {
            Id = "gloss",
            DisplayName = "Gloss/Roughness",
            Category = "Surface",
            ShaderType = "fragment",
            DefaultChunkId = "glossPS",
            Description = "Glossiness/roughness sampling",
            Order = 130,
            IsEnabledByDefault = true
        },
        new ChunkSlot
        {
            Id = "metalness",
            DisplayName = "Metalness",
            Category = "Surface",
            ShaderType = "fragment",
            DefaultChunkId = "metalnessPS",
            Description = "Metalness value sampling",
            Order = 140,
            IsEnabledByDefault = true
        },
        new ChunkSlot
        {
            Id = "ao",
            DisplayName = "Ambient Occlusion",
            Category = "Surface",
            ShaderType = "fragment",
            DefaultChunkId = "aoPS",
            Description = "Ambient occlusion sampling",
            Order = 150,
            IsEnabledByDefault = true
        },
        new ChunkSlot
        {
            Id = "emissive",
            DisplayName = "Emissive",
            Category = "Surface",
            ShaderType = "fragment",
            DefaultChunkId = "emissivePS",
            Description = "Emissive color/texture sampling",
            Order = 160,
            IsEnabledByDefault = false
        },
        new ChunkSlot
        {
            Id = "opacity",
            DisplayName = "Opacity",
            Category = "Surface",
            ShaderType = "fragment",
            DefaultChunkId = "opacityPS",
            Description = "Opacity/alpha sampling",
            Order = 170,
            IsEnabledByDefault = false
        },
        new ChunkSlot
        {
            Id = "alphaTest",
            DisplayName = "Alpha Test",
            Category = "Surface",
            ShaderType = "fragment",
            DefaultChunkId = "alphaTestPS",
            Description = "Alpha test (cutout) functionality",
            Order = 175,
            IsEnabledByDefault = false
        },

        // ===== CLEAR COAT SLOTS =====
        new ChunkSlot
        {
            Id = "clearCoat",
            DisplayName = "Clear Coat",
            Category = "Clear Coat",
            ShaderType = "fragment",
            DefaultChunkId = "clearCoatPS",
            Description = "Clear coat layer intensity",
            Order = 200,
            IsEnabledByDefault = false
        },
        new ChunkSlot
        {
            Id = "clearCoatGloss",
            DisplayName = "Clear Coat Gloss",
            Category = "Clear Coat",
            ShaderType = "fragment",
            DefaultChunkId = "clearCoatGlossPS",
            Description = "Clear coat glossiness",
            Order = 210,
            IsEnabledByDefault = false
        },
        new ChunkSlot
        {
            Id = "clearCoatNormal",
            DisplayName = "Clear Coat Normal",
            Category = "Clear Coat",
            ShaderType = "fragment",
            DefaultChunkId = "clearCoatNormalPS",
            Description = "Clear coat normal map",
            Order = 220,
            IsEnabledByDefault = false
        },

        // ===== LIGHTING SLOTS =====
        new ChunkSlot
        {
            Id = "lightDiffuse",
            DisplayName = "Diffuse Lighting",
            Category = "Lighting",
            ShaderType = "fragment",
            DefaultChunkId = "lightDiffuseLambertPS",
            Description = "Diffuse lighting model",
            Order = 300,
            IsRequired = true
        },
        new ChunkSlot
        {
            Id = "lightSpecular",
            DisplayName = "Specular Lighting",
            Category = "Lighting",
            ShaderType = "fragment",
            DefaultChunkId = "lightSpecularBlinnPS",
            Description = "Specular lighting model",
            Order = 310,
            IsEnabledByDefault = true
        },
        new ChunkSlot
        {
            Id = "fresnel",
            DisplayName = "Fresnel",
            Category = "Lighting",
            ShaderType = "fragment",
            DefaultChunkId = "fresnelSchlickPS",
            Description = "Fresnel effect calculation",
            Order = 320,
            IsEnabledByDefault = true
        },
        new ChunkSlot
        {
            Id = "aoDiffuseOcc",
            DisplayName = "AO Diffuse Occlusion",
            Category = "Lighting",
            ShaderType = "fragment",
            DefaultChunkId = "aoDiffuseOccPS",
            Description = "Apply AO to diffuse lighting",
            Order = 330,
            IsEnabledByDefault = true
        },
        new ChunkSlot
        {
            Id = "aoSpecOcc",
            DisplayName = "AO Specular Occlusion",
            Category = "Lighting",
            ShaderType = "fragment",
            DefaultChunkId = "aoSpecOccPS",
            Description = "Apply AO to specular lighting",
            Order = 340,
            IsEnabledByDefault = true
        },

        // ===== ENVIRONMENT SLOTS =====
        new ChunkSlot
        {
            Id = "reflection",
            DisplayName = "Reflection",
            Category = "Environment",
            ShaderType = "fragment",
            DefaultChunkId = "reflectionEnvPS",
            Description = "Environment reflections",
            Order = 400,
            IsEnabledByDefault = true
        },
        new ChunkSlot
        {
            Id = "lightmap",
            DisplayName = "Lightmap",
            Category = "Environment",
            ShaderType = "fragment",
            DefaultChunkId = "lightmapSinglePS",
            Description = "Lightmap sampling",
            Order = 410,
            IsEnabledByDefault = false
        },
        new ChunkSlot
        {
            Id = "fog",
            DisplayName = "Fog",
            Category = "Environment",
            ShaderType = "fragment",
            DefaultChunkId = "fogPS",
            Description = "Fog calculations",
            Order = 420,
            IsEnabledByDefault = false
        },

        // ===== ADVANCED SLOTS =====
        new ChunkSlot
        {
            Id = "parallax",
            DisplayName = "Parallax",
            Category = "Advanced",
            ShaderType = "fragment",
            DefaultChunkId = "parallaxPS",
            Description = "Parallax occlusion mapping",
            Order = 500,
            IsEnabledByDefault = false
        },
        new ChunkSlot
        {
            Id = "refraction",
            DisplayName = "Refraction",
            Category = "Advanced",
            ShaderType = "fragment",
            DefaultChunkId = "refractionPS",
            Description = "Refraction for transparent materials",
            Order = 510,
            IsEnabledByDefault = false
        },

        // ===== OUTPUT SLOTS =====
        new ChunkSlot
        {
            Id = "combine",
            DisplayName = "Combine",
            Category = "Output",
            ShaderType = "fragment",
            DefaultChunkId = "combinePS",
            Description = "Combine diffuse and specular",
            Order = 900,
            IsRequired = true
        },
        new ChunkSlot
        {
            Id = "tonemapping",
            DisplayName = "Tonemapping",
            Category = "Output",
            ShaderType = "fragment",
            DefaultChunkId = "tonemappingAcesPS",
            Description = "HDR tonemapping",
            Order = 910,
            IsEnabledByDefault = true
        },
        new ChunkSlot
        {
            Id = "gamma",
            DisplayName = "Gamma",
            Category = "Output",
            ShaderType = "fragment",
            DefaultChunkId = "gammaPS",
            Description = "Gamma correction",
            Order = 920,
            IsRequired = true
        },
        new ChunkSlot
        {
            Id = "outputAlpha",
            DisplayName = "Output Alpha",
            Category = "Output",
            ShaderType = "fragment",
            DefaultChunkId = "outputAlphaPS",
            Description = "Final alpha output based on blend type",
            Order = 930,
            IsEnabledByDefault = true
        }
    };

    /// <summary>
    /// Get slots by category
    /// </summary>
    public static IEnumerable<ChunkSlot> GetSlotsByCategory(string category)
        => AllSlots.Where(s => s.Category == category).OrderBy(s => s.Order);

    /// <summary>
    /// Get all unique categories
    /// </summary>
    public static IEnumerable<string> GetCategories()
        => AllSlots.Select(s => s.Category).Distinct();

    /// <summary>
    /// Get slot by ID
    /// </summary>
    public static ChunkSlot? GetSlot(string slotId)
        => AllSlots.FirstOrDefault(s => s.Id == slotId);

    /// <summary>
    /// Get compatible chunks for a slot (chunks that can replace the default)
    /// </summary>
    public static IEnumerable<ShaderChunk> GetCompatibleChunks(string slotId, IEnumerable<ShaderChunk> allChunks)
    {
        var slot = GetSlot(slotId);
        if (slot == null) return Enumerable.Empty<ShaderChunk>();

        // Filter by shader type
        var typeFiltered = allChunks.Where(c => c.Type == slot.ShaderType);

        // For now, return all chunks of matching type
        // In the future, could add more sophisticated matching based on function signatures
        return typeFiltered;
    }

    /// <summary>
    /// Create default slot assignments for a new master material
    /// </summary>
    public static List<ChunkSlotAssignment> CreateDefaultAssignments()
    {
        return AllSlots
            .Where(s => s.IsEnabledByDefault || s.IsRequired)
            .Select(s => new ChunkSlotAssignment
            {
                SlotId = s.Id,
                ChunkId = null, // null means use default
                Enabled = true
            })
            .ToList();
    }
}
