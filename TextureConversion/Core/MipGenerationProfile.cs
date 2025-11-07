namespace AssetProcessor.TextureConversion.Core {
    /// <summary>
    /// Профиль для генерации мипмапов
    /// Определяет настройки генерации для конкретного типа текстуры
    /// </summary>
    public class MipGenerationProfile {
        /// <summary>
        /// Тип текстуры
        /// </summary>
        public TextureType TextureType { get; set; }

        /// <summary>
        /// Тип фильтра для ресэмплинга
        /// </summary>
        public FilterType Filter { get; set; }

        /// <summary>
        /// Применять ли гамма-коррекцию (для sRGB текстур)
        /// </summary>
        public bool ApplyGammaCorrection { get; set; }

        /// <summary>
        /// Значение гаммы (обычно 2.2)
        /// </summary>
        public float Gamma { get; set; } = 2.2f;

        /// <summary>
        /// Дополнительный blur radius (0.0 = нет дополнительного блюра)
        /// </summary>
        public float BlurRadius { get; set; } = 0.0f;

        /// <summary>
        /// Включать ли последний уровень мипмапа (1x1)
        /// </summary>
        public bool IncludeLastLevel { get; set; } = true;

        /// <summary>
        /// Минимальный размер мипмапа (по умолчанию 1x1)
        /// </summary>
        public int MinMipSize { get; set; } = 1;

        /// <summary>
        /// Нормализовать ли нормали (только для normal maps)
        /// </summary>
        public bool NormalizeNormals { get; set; } = false;

        /// <summary>
        /// Модификаторы для постобработки мипмапов
        /// </summary>
        public List<IMipModifier> Modifiers { get; set; } = new();

        /// <summary>
        /// Использовать ли energy-preserving фильтрацию для roughness/gloss
        /// При включении усредняется alpha² вместо прямого усреднения roughness
        /// Применимо только для Roughness и Gloss типов
        /// </summary>
        public bool UseEnergyPreserving { get; set; } = false;

        /// <summary>
        /// Это gloss карта (true) или roughness (false)
        /// Используется для energy-preserving фильтрации
        /// </summary>
        public bool IsGloss { get; set; } = false;

        /// <summary>
        /// Создает профиль по умолчанию для типа текстуры
        /// </summary>
        public static MipGenerationProfile CreateDefault(TextureType textureType) {
            return textureType switch {
                TextureType.Albedo => new MipGenerationProfile {
                    TextureType = TextureType.Albedo,
                    Filter = FilterType.Kaiser,
                    ApplyGammaCorrection = true,
                    Gamma = 2.2f,
                    BlurRadius = 0.0f,
                    IncludeLastLevel = true
                },

                TextureType.Normal => new MipGenerationProfile {
                    TextureType = TextureType.Normal,
                    Filter = FilterType.Kaiser,
                    ApplyGammaCorrection = false, // Normal maps в линейном пространстве
                    NormalizeNormals = true,
                    BlurRadius = 0.0f,
                    IncludeLastLevel = true
                },

                TextureType.Roughness => new MipGenerationProfile {
                    TextureType = TextureType.Roughness,
                    Filter = FilterType.Kaiser,
                    ApplyGammaCorrection = false, // Линейное пространство
                    BlurRadius = 0.0f,
                    IncludeLastLevel = true
                },

                TextureType.Metallic => new MipGenerationProfile {
                    TextureType = TextureType.Metallic,
                    Filter = FilterType.Box, // Metallic обычно бинарный
                    ApplyGammaCorrection = false,
                    BlurRadius = 0.0f,
                    IncludeLastLevel = true
                },

                TextureType.AmbientOcclusion => new MipGenerationProfile {
                    TextureType = TextureType.AmbientOcclusion,
                    Filter = FilterType.Kaiser,
                    ApplyGammaCorrection = false,
                    BlurRadius = 0.0f,
                    IncludeLastLevel = true
                },

                TextureType.Emissive => new MipGenerationProfile {
                    TextureType = TextureType.Emissive,
                    Filter = FilterType.Kaiser,
                    ApplyGammaCorrection = true,
                    Gamma = 2.2f,
                    BlurRadius = 0.0f,
                    IncludeLastLevel = true
                },

                TextureType.Gloss => new MipGenerationProfile {
                    TextureType = TextureType.Gloss,
                    Filter = FilterType.Kaiser,
                    ApplyGammaCorrection = false,
                    BlurRadius = 0.0f,
                    IncludeLastLevel = true
                },

                _ => new MipGenerationProfile {
                    TextureType = TextureType.Generic,
                    Filter = FilterType.Kaiser,
                    ApplyGammaCorrection = false,
                    BlurRadius = 0.0f,
                    IncludeLastLevel = true
                }
            };
        }

        /// <summary>
        /// Клонирует профиль
        /// </summary>
        public MipGenerationProfile Clone() {
            return new MipGenerationProfile {
                TextureType = TextureType,
                Filter = Filter,
                ApplyGammaCorrection = ApplyGammaCorrection,
                Gamma = Gamma,
                BlurRadius = BlurRadius,
                IncludeLastLevel = IncludeLastLevel,
                MinMipSize = MinMipSize,
                NormalizeNormals = NormalizeNormals,
                Modifiers = new List<IMipModifier>(Modifiers),
                UseEnergyPreserving = UseEnergyPreserving,
                IsGloss = IsGloss
            };
        }
    }
}
