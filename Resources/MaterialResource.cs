namespace TexTool.Resources {
    public class MaterialResource : BaseResource {
        private string? shader;
        private string? materialJsonUrl;
        private string? materialJsonPath;
        private string? createdAt;
        private string? blendType;
        private string? cull;
        private string? useLighting;
        private string? twoSidedLighting;
        private string? useMetalness;
        private string? metalness;
        private string? shininess;
        private string? opacity;
        private string? bumpMapFactor;
        private string? reflectivity;
        private string? alphaTest;
        private bool diffuseTint;
        private List<double>? diffuse;
        private List<double>? emissive;
        private List<double>? specular;
        private int? diffuseMapId;
        private int? metalnessMapId;
        private int? normalMapId;
        private int? glossMapId;
        private int? aoMapId;
        private int? specularMapId;
        private int? emissiveMapId;
        private float? specularityFactor;
        private float? refractionIndex;
        private float? emissiveIntensity;
        private int? opacityMapId;

        public string? Shader {
            get => shader;
            set {
                shader = value;
                OnPropertyChanged(nameof(Shader));
            }
        }

        public string? MaterialJsonUrl {
            get => materialJsonUrl;
            set {
                materialJsonUrl = value;
                OnPropertyChanged(nameof(MaterialJsonUrl));
            }
        }

        public string? MaterialJsonPath {
            get => materialJsonPath;
            set {
                materialJsonPath = value;
                OnPropertyChanged(nameof(MaterialJsonPath));
            }
        }

        public string? CreatedAt {
            get => createdAt;
            set {
                createdAt = value;
                OnPropertyChanged(nameof(CreatedAt));
            }
        }

        public string? BlendType {
            get => blendType;
            set {
                blendType = value;
                OnPropertyChanged(nameof(BlendType));
            }
        }

        public string? Cull {
            get => cull;
            set {
                cull = value;
                OnPropertyChanged(nameof(Cull));
            }
        }

        public string? UseLighting {
            get => useLighting;
            set {
                useLighting = value;
                OnPropertyChanged(nameof(UseLighting));
            }
        }

        public string? TwoSidedLighting {
            get => twoSidedLighting;
            set {
                twoSidedLighting = value;
                OnPropertyChanged(nameof(TwoSidedLighting));
            }
        }

        public string? UseMetalness {
            get => useMetalness;
            set {
                useMetalness = value;
                OnPropertyChanged(nameof(UseMetalness));
            }
        }

        public string? Metalness {
            get => metalness;
            set {
                metalness = value;
                OnPropertyChanged(nameof(Metalness));
            }
        }

        public string? Shininess {
            get => shininess;
            set {
                shininess = value;
                OnPropertyChanged(nameof(Shininess));
            }
        }

        public string? Opacity {
            get => opacity;
            set {
                opacity = value;
                OnPropertyChanged(nameof(Opacity));
            }
        }

        public string? BumpMapFactor {
            get => bumpMapFactor;
            set {
                bumpMapFactor = value;
                OnPropertyChanged(nameof(BumpMapFactor));
            }
        }

        public string? Reflectivity {
            get => reflectivity;
            set {
                reflectivity = value;
                OnPropertyChanged(nameof(Reflectivity));
            }
        }

        public string? AlphaTest {
            get => alphaTest;
            set {
                alphaTest = value;
                OnPropertyChanged(nameof(AlphaTest));
            }
        }

        public bool DiffuseTint {
            get => diffuseTint;
            set {
                diffuseTint = value;
                OnPropertyChanged(nameof(DiffuseTint));
            }
        }

        public List<double>? Diffuse {
            get => diffuse;
            set {
                diffuse = value;
                OnPropertyChanged(nameof(Diffuse));
            }
        }

        public int? DiffuseMapId {
            get => diffuseMapId;
            set {
                diffuseMapId = value;
                OnPropertyChanged(nameof(DiffuseMapId));
            }
        }

        public int? MetalnessMapId {
            get => metalnessMapId;
            set {
                metalnessMapId = value;
                OnPropertyChanged(nameof(MetalnessMapId));
            }
        }

        public int? NormalMapId {
            get => normalMapId;
            set {
                normalMapId = value;
                OnPropertyChanged(nameof(NormalMapId));
            }
        }

        public int? GlossMapId {
            get => glossMapId;
            set {
                glossMapId = value;
                OnPropertyChanged(nameof(GlossMapId));
            }
        }

        public int? AOMapId {
            get => aoMapId;
            set {
                aoMapId = value;
                OnPropertyChanged(nameof(AOMapId));
            }
        }

        public int? SpecularMapId {
            get => specularMapId;
            set {
                specularMapId = value;
                OnPropertyChanged(nameof(SpecularMapId));
            }
        }

        public int? EmissiveMapId {
            get => emissiveMapId;
            set {
                emissiveMapId = value;
                OnPropertyChanged(nameof(EmissiveMapId));
            }
        }

        public int? OpacityMapId {
            get => opacityMapId;
            set {
                opacityMapId = value;
                OnPropertyChanged(nameof(OpacityMapId));
            }
        }

        public float? SpecularityFactor {
            get => specularityFactor;
            set {
                specularityFactor = value;
                OnPropertyChanged(nameof(SpecularityFactor));
            }
        }

        public float? RefractionIndex {
            get => refractionIndex;
            set {
                refractionIndex = value;
                OnPropertyChanged(nameof(RefractionIndex));
            }
        }

        public float? EmissiveIntensity {
            get => emissiveIntensity;
            set {
                emissiveIntensity = value;
                OnPropertyChanged(nameof(EmissiveIntensity));
            }
        }

        private List<int> textureIds = [];

        public List<int> TextureIds {
            get => textureIds;
            set {
                textureIds = value;
                OnPropertyChanged(nameof(TextureIds));
            }
        }

        public List<double>? Emissive {
            get => emissive;
            set {
                emissive = value;
                OnPropertyChanged(nameof(Emissive));
            }
        }

        public List<double>? Specular {
            get => specular;
            set {
                specular = value;
                OnPropertyChanged(nameof(Specular));
            }
        }
    }
}
