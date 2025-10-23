namespace AssetProcessor.Resources {
    public class TextureResource : BaseResource {
        private int[] resolution = new int[2];
        private int[] resizeResolution = new int[2];
        private string? groupName;
        private string? textureType;

        public int[] Resolution {
            get => resolution;
            set {
                resolution = value;
                OnPropertyChanged(nameof(Resolution));
                OnPropertyChanged(nameof(ResolutionArea));
            }
        }

        public int[] ResizeResolution {
            get => resizeResolution;
            set {
                resizeResolution = value;
                OnPropertyChanged(nameof(ResizeResolution));
                OnPropertyChanged(nameof(ResizeResolutionArea));
            }
        }

        public string? GroupName {
            get => groupName;
            set {
                groupName = value;
                OnPropertyChanged(nameof(GroupName));
            }
        }

        public string? TextureType {
            get => textureType;
            set {
                textureType = value;
                OnPropertyChanged(nameof(TextureType));
            }
        }

        public int? ResolutionArea => Resolution[0] * Resolution[1];
        public int? ResizeResolutionArea => ResizeResolution[0] * ResizeResolution[1];

        public static string DetermineTextureType(string textureName) {
            if (string.IsNullOrEmpty(textureName))
                return "Other";

            string lowerName = textureName.ToLower();

            if (lowerName.Contains("albedo") || lowerName.Contains("diffuse") || lowerName.Contains("color") || lowerName.Contains("basecolor") || lowerName.Contains("base"))
                return "Albedo";
            if (lowerName.Contains("normal") || lowerName.Contains("normalmap") || lowerName.Contains("norm") || lowerName.Contains("bump") || lowerName.Contains("bumpmap"))
                return "Normal";
            if (lowerName.Contains("gloss") || lowerName.Contains("glossiness"))
                return "Gloss";
            if (lowerName.Contains("ao") || lowerName.Contains("ambientocclusion") || lowerName.Contains("ambient") || lowerName.Contains("occlusion"))
                return "AO";
            if (lowerName.Contains("metallic") || lowerName.Contains("metalness") || lowerName.Contains("metal") || lowerName.Contains("met"))
                return "Metallic";
            if (lowerName.Contains("roughness") || lowerName.Contains("rough"))
                return "Roughness";
            if (lowerName.Contains("emissive") || lowerName.Contains("emission") || lowerName.Contains("emit"))
                return "Emissive";
            if (lowerName.Contains("opacity") || lowerName.Contains("alpha") || lowerName.Contains("transparency"))
                return "Opacity";
            if (lowerName.Contains("height") || lowerName.Contains("displacement") || lowerName.Contains("disp"))
                return "Height";
            if (lowerName.Contains("specular") || lowerName.Contains("spec"))
                return "Specular";

            return "Other";
        }

        public static string ExtractBaseTextureName(string textureName) {
            if (string.IsNullOrEmpty(textureName))
                return textureName;

            string lowerName = textureName.ToLower();

            // Список суффиксов типов текстур для удаления
            string[] suffixes = new[] {
                "_albedo", "_diffuse", "_color", "_basecolor", "_base",
                "_normal", "_normalmap", "_norm", "_bump", "_bumpmap",
                "_metallic", "_metalness", "_metal", "_met",
                "_roughness", "_rough", "_gloss", "_glossiness",
                "_ao", "_ambientocclusion", "_ambient", "_occlusion",
                "_emissive", "_emission", "_emit",
                "_opacity", "_alpha", "_transparency",
                "_height", "_displacement", "_disp",
                "_specular", "_spec"
            };

            // Находим и удаляем суффикс (игнорируя регистр)
            foreach (string suffix in suffixes) {
                int index = lowerName.LastIndexOf(suffix, System.StringComparison.OrdinalIgnoreCase);
                if (index > 0 && index == lowerName.Length - suffix.Length) {
                    // Возвращаем оригинальное имя без суффикса (сохраняя регистр)
                    return textureName.Substring(0, index);
                }
            }

            // Если суффикс не найден, возвращаем исходное имя
            return textureName;
        }
    }
}
