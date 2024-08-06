using TexTool.Resources;

public class MaterialResource : BaseResource {
    public string? Shader { get; set; }
    public string? MaterialJsonUrl { get; set; }
    public string? MaterialJsonPath { get; set; }
    public string? CreatedAt { get; set; }
    public string? BlendType { get; set; }
    public string? Cull { get; set; }
    public bool UseLighting { get; set; }
    public bool TwoSidedLighting { get; set; }


    public float? Reflectivity { get; set; }
    public float? RefractionIndex { get; set; }

    public float? AlphaTest { get; set; }
    public int? OpacityMapId { get; set; }
    public float? Opacity { get; set; }

    public bool AOTint { get; set; }
    public List<int>? AOColor { get; set; }
    public int? AOMapId { get; set; }
    public bool AOVertexColor { get; set; }
    
    public bool DiffuseTint { get; set; }
    public List<int>? Diffuse { get; set; }
    public int? DiffuseMapId { get; set; }
    public bool DiffuseVertexColor { get; set; }

    public bool SpecularTint { get; set; }
    public List<int>? Specular { get; set; }
    public int? SpecularMapId { get; set; }
    public bool SpecularVertexColor { get; set; }
    public float? SpecularityFactor { get; set; }

    public float? Shininess { get; set; }
    public int? GlossMapId { get; set; }


    public bool UseMetalness { get; set; }
    public float? Metalness { get; set; }
    public int? MetalnessMapId { get; set; }


    public List<int>? Emissive { get; set; }
    public int? EmissiveMapId { get; set; }
    public float? EmissiveIntensity { get; set; }


    public int? NormalMapId { get; set; }
    public float? BumpMapFactor { get; set; }


    public int? DiffuseColorChannel { get; set; }
    public int? DiffuseUVChannel { get; set; }
    public int? SpecularColorChannel { get; set; }
    public int? SpecularUVChannel { get; set; }
    public int? NormalColorChannel { get; set; }
    public int? NormalUVChannel { get; set; }
    public int? BumpMapColorChannel { get; set; }
    public int? BumpMapUVChannel { get; set; }
}
