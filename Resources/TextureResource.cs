using System.Windows.Media;
using AssetProcessor.Helpers;

namespace AssetProcessor.Resources {
    public class TextureResource : BaseResource {
        /// <summary>
        /// Флаг для определения ORM текстуры в UI (переопределяется в ORMTextureResource)
        /// </summary>
        public virtual bool IsORMTexture => false;

        /// <summary>
        /// Computed row background color based on texture type (theme-aware, cached)
        /// </summary>
        public Brush RowBackground {
            get {
                if (IsORMTexture) return ThemeBrushCache.GetThemeBrush("ThemeMaterialORM", Brushes.Pink);
                return TextureType switch {
                    "Normal" => ThemeBrushCache.GetThemeBrush("ThemeMaterialNormal", Brushes.LightBlue),
                    "Albedo" => ThemeBrushCache.GetThemeBrush("ThemeTextureAlbedo", Brushes.Khaki),
                    "Gloss" => ThemeBrushCache.GetThemeBrush("ThemeTextureGloss", Brushes.LightGray),
                    "AO" => ThemeBrushCache.GetThemeBrush("ThemeTextureAO", Brushes.Gray),
                    _ => ThemeBrushCache.GetThemeBrush("ThemeDataGridRowBackground", Brushes.Transparent)
                };
            }
        }

        private int[] resolution = new int[2];
        private int[] resizeResolution = new int[2];
        private int resolutionArea = 0; // Кэшированное значение для быстрой сортировки
        private int resizeResolutionArea = 0; // Кэшированное значение для быстрой сортировки
        private string? groupName;
        private string? subGroupName;  // Для вложенных групп (ORM подгруппы)
        private string? textureType;
        private string? compressionFormat;
        private int mipmapCount;
        private string? presetName;
        private long compressedSize;
        private bool toksvigEnabled;
        private float toksvigCompositePower = 1.0f;
        private int toksvigMinMipLevel = 1;
        private bool toksvigSmoothVariance = true;
        private string? normalMapPath;

        public int[] Resolution {
            get => resolution;
            set {
                resolution = value;
                // Cache computed value for fast sorting
                resolutionArea = (value != null && value.Length >= 2) ? value[0] * value[1] : 0;
                OnPropertyChanged(nameof(Resolution));
                OnPropertyChanged(nameof(ResolutionArea)); // Notify for sorting
            }
        }

        public int[] ResizeResolution {
            get => resizeResolution;
            set {
                resizeResolution = value;
                // Cache computed value for fast sorting
                resizeResolutionArea = (value != null && value.Length >= 2) ? value[0] * value[1] : 0;
                OnPropertyChanged(nameof(ResizeResolution));
                OnPropertyChanged(nameof(ResizeResolutionArea)); // Notify for sorting
            }
        }

        public string? GroupName {
            get => groupName;
            set {
                groupName = value;
                OnPropertyChanged(nameof(GroupName));
            }
        }

        /// <summary>
        /// Вторичная группа для вложенных элементов (ORM подгруппы)
        /// </summary>
        public string? SubGroupName {
            get => subGroupName;
            set {
                subGroupName = value;
                OnPropertyChanged(nameof(SubGroupName));
            }
        }

        /// <summary>
        /// Ссылка на родительскую ORM текстуру (для ao/gloss/metallic/height компонентов)
        /// </summary>
        public ORMTextureResource? ParentORMTexture { get; set; }

        public string? TextureType {
            get => textureType;
            set {
                if (textureType != value) {
                    textureType = value;
                    OnPropertyChanged(nameof(TextureType));
                    OnPropertyChanged(nameof(RowBackground)); // Update row color
                }
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

        /// <summary>
        /// Включить Toksvig mipmap generation (для gloss/roughness текстур)
        /// </summary>
        public bool ToksvigEnabled {
            get => toksvigEnabled;
            set {
                toksvigEnabled = value;
                OnPropertyChanged(nameof(ToksvigEnabled));
            }
        }

        /// <summary>
        /// Composite Power для Toksvig (вес влияния дисперсии, 0.5-2.0)
        /// </summary>
        public float ToksvigCompositePower {
            get => toksvigCompositePower;
            set {
                toksvigCompositePower = value;
                OnPropertyChanged(nameof(ToksvigCompositePower));
            }
        }

        /// <summary>
        /// Минимальный уровень мипмапа для применения Toksvig
        /// </summary>
        public int ToksvigMinMipLevel {
            get => toksvigMinMipLevel;
            set {
                toksvigMinMipLevel = value;
                OnPropertyChanged(nameof(ToksvigMinMipLevel));
            }
        }

        /// <summary>
        /// Применять ли сглаживание дисперсии
        /// </summary>
        public bool ToksvigSmoothVariance {
            get => toksvigSmoothVariance;
            set {
                toksvigSmoothVariance = value;
                OnPropertyChanged(nameof(ToksvigSmoothVariance));
            }
        }

        /// <summary>
        /// Путь к соответствующей normal map (null = автоматический поиск)
        /// </summary>
        public string? NormalMapPath {
            get => normalMapPath;
            set {
                normalMapPath = value;
                OnPropertyChanged(nameof(NormalMapPath));
            }
        }

        /// <summary>
        /// Кэшированное значение площади разрешения для быстрой сортировки
        /// </summary>
        public int ResolutionArea => resolutionArea;

        /// <summary>
        /// Кэшированное значение площади разрешения после изменения размера для быстрой сортировки
        /// </summary>
        public int ResizeResolutionArea => resizeResolutionArea;

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
