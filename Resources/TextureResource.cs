namespace AssetProcessor.Resources {
    public class TextureResource : BaseResource {
        private int[] resolution = new int[2];
        private int[] resizeResolution = new int[2];
        private string? groupName;
        private string? textureType;
        private string? compressionFormat;
        private int mipmapCount;
        private string? presetName;
        private long compressedSize;

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

        public string? CompressionFormat {
            get => compressionFormat;
            set {
                compressionFormat = value;
                OnPropertyChanged(nameof(CompressionFormat));
            }
        }

        public int MipmapCount {
            get => mipmapCount;
            set {
                mipmapCount = value;
                OnPropertyChanged(nameof(MipmapCount));
            }
        }

        public string? PresetName {
            get => presetName;
            set {
                presetName = value;
                OnPropertyChanged(nameof(PresetName));
            }
        }

        public long CompressedSize {
            get => compressedSize;
            set {
                compressedSize = value;
                OnPropertyChanged(nameof(CompressedSize));
            }
        }

        public int? ResolutionArea => Resolution[0] * Resolution[1];
        public int? ResizeResolutionArea => ResizeResolution[0] * ResizeResolution[1];

        public static string DetermineTextureType(string textureName) {
            if (string.IsNullOrEmpty(textureName))
                return "Other";

            string lowerName = textureName.ToLower();

            // Проверяем суффиксы в конце имени (более специфичные сначала)
            if (EndsWith(lowerName, "_albedo", "_diffuse", "_color", "_basecolor"))
                return "Albedo";
            if (EndsWith(lowerName, "_normal", "_normalmap", "_norm", "_bump", "_bumpmap"))
                return "Normal";
            if (EndsWith(lowerName, "_gloss", "_glossiness"))
                return "Gloss";
            if (EndsWith(lowerName, "_ao", "_ambientocclusion", "_ambient", "_occlusion"))
                return "AO";
            if (EndsWith(lowerName, "_metallic", "_metalness", "_metal", "_met"))
                return "Metallic";
            if (EndsWith(lowerName, "_roughness", "_rough"))
                return "Roughness";
            if (EndsWith(lowerName, "_emissive", "_emission", "_emit"))
                return "Emissive";
            if (EndsWith(lowerName, "_opacity", "_alpha", "_transparency"))
                return "Opacity";
            if (EndsWith(lowerName, "_height", "_displacement", "_disp"))
                return "Height";
            if (EndsWith(lowerName, "_specular", "_spec"))
                return "Specular";

            return "Other";
        }

        private static bool EndsWith(string text, params string[] suffixes) {
            foreach (string suffix in suffixes) {
                if (text.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
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
