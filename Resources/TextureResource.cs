namespace AssetProcessor.Resources {
    public class TextureResource : BaseResource {
        private int[] resolution = new int[2];
        private int[] resizeResolution = new int[2];
        private string? groupName;

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

        public int? ResolutionArea => Resolution[0] * Resolution[1];
        public int? ResizeResolutionArea => ResizeResolution[0] * ResizeResolution[1];

        public static string DetermineTextureType(string textureName) {
            string lowerName = textureName.ToLower();

            if (lowerName.Contains("albedo") || lowerName.Contains("diffuse") || lowerName.Contains("color") || lowerName.Contains("base"))
                return "Albedo";
            if (lowerName.Contains("normal") || lowerName.Contains("bump"))
                return "Normal";
            if (lowerName.Contains("metallic") || lowerName.Contains("metalness"))
                return "Metallic";
            if (lowerName.Contains("roughness") || lowerName.Contains("gloss"))
                return "Roughness/Gloss";
            if (lowerName.Contains("ao") || lowerName.Contains("ambient") || lowerName.Contains("occlusion"))
                return "AO";
            if (lowerName.Contains("emissive") || lowerName.Contains("emission"))
                return "Emissive";
            if (lowerName.Contains("opacity") || lowerName.Contains("alpha"))
                return "Opacity";
            if (lowerName.Contains("height") || lowerName.Contains("displacement"))
                return "Height";

            return "Other";
        }
    }
}
