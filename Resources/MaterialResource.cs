using AssetProcessor.Infrastructure.Enums;

namespace AssetProcessor.Resources {

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
        public string? FresnelModel { get; set; }
        public float? Glossiness { get; set; }
        public bool? OpacityFresnel { get; set; }
        public float? CavityMapIntensity { get; set; }
        public bool? UseSkybox { get; set; }
        public bool? UseFog { get; set; }
        public bool? UseGammaTonemap { get; set; }

        public float? AlphaTest { get; set; }
        public int? OpacityMapId { get; set; }
        public float? Opacity { get; set; }

        public bool AOTint { get; set; }
        public List<float>? AOColor { get; set; }
        public int? AOMapId { get; set; }
        public bool AOVertexColor { get; set; }

        public bool DiffuseTint { get; set; }
        public List<float>? Diffuse { get; set; }
        public int? DiffuseMapId { get; set; }
        public bool DiffuseVertexColor { get; set; }

        public bool SpecularTint { get; set; }
        public List<float>? Specular { get; set; }
        public int? SpecularMapId { get; set; }
        public bool SpecularVertexColor { get; set; }
        public float? SpecularityFactor { get; set; }

        public float? Shininess { get; set; }
        public int? GlossMapId { get; set; }


        public bool UseMetalness { get; set; }
        public float? Metalness { get; set; }
        public int? MetalnessMapId { get; set; }


        public List<float>? Emissive { get; set; }
        public int? EmissiveMapId { get; set; }
        public float? EmissiveIntensity { get; set; }


        public int? NormalMapId { get; set; }
        public float? BumpMapFactor { get; set; }

        public int? DiffuseUVChannel { get; set; }
        public int? SpecularUVChannel { get; set; }
        public int? NormalUVChannel { get; set; }

        public ColorChannel? DiffuseColorChannel { get; set; }
        public ColorChannel? SpecularColorChannel { get; set; }
        public ColorChannel? MetalnessColorChannel { get; set; }
        public ColorChannel? GlossinessColorChannel { get; set; }
        public ColorChannel? AOChannel { get; set; }

        // Master Material assignment
        private string? masterMaterialName;

        /// <summary>
        /// Name of the master material this instance derives from
        /// </summary>
        public string? MasterMaterialName {
            get => masterMaterialName;
            set {
                if (masterMaterialName != value) {
                    masterMaterialName = value;
                    OnPropertyChanged(nameof(MasterMaterialName));
                }
            }
        }
    }
}
