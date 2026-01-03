namespace AssetProcessor.TextureConversion.Core {
    /// <summary>
    /// Настройки для источника одного канала в упакованной текстуре
    /// </summary>
    public class ChannelSourceSettings {
        /// <summary>
        /// Тип канала
        /// </summary>
        public ChannelType ChannelType { get; set; }

        /// <summary>
        /// Путь к исходной текстуре для этого канала (обязателен)
        /// </summary>
        public string? SourcePath { get; set; }

        /// <summary>
        /// Тип фильтра для генерации мипмапов
        /// </summary>
        public FilterType FilterType { get; set; } = FilterType.Kaiser;

        /// <summary>
        /// Применять ли Toksvig коррекцию (только для Gloss)
        /// </summary>
        public bool ApplyToksvig { get; set; } = false;

        /// <summary>
        /// Настройки Toksvig (если ApplyToksvig = true)
        /// </summary>
        public ToksvigSettings? ToksvigSettings { get; set; }

        /// <summary>
        /// Режим обработки AO мипмапов (только для AO)
        /// </summary>
        public AOProcessingMode AOProcessingMode { get; set; } = AOProcessingMode.BiasedDarkening;

        /// <summary>
        /// Bias для AO обработки (0.0-1.0, обычно 0.3-0.7)
        /// При 0.3: lerp(mean, min, 0.3) = более темные мипы
        /// При 0.7: lerp(mean, min, 0.7) = более светлые мипы
        /// </summary>
        public float AOBias { get; set; } = 0.5f;

        /// <summary>
        /// Percentile для AO (если AOProcessingMode = Percentile)
        /// Например, 10.0 = 10-й перцентиль
        /// </summary>
        public float AOPercentile { get; set; } = 10.0f;

        /// <summary>
        /// Профиль генерации мипмапов (если null - используется автоматический)
        /// </summary>
        public MipGenerationProfile? MipProfile { get; set; }

        /// <summary>
        /// Создает настройки по умолчанию для типа канала
        /// </summary>
        public static ChannelSourceSettings CreateDefault(ChannelType channelType) {
            return channelType switch {
                ChannelType.AmbientOcclusion => new ChannelSourceSettings {
                    ChannelType = ChannelType.AmbientOcclusion,
                    AOProcessingMode = AOProcessingMode.BiasedDarkening,
                    AOBias = 0.5f,
                    MipProfile = MipGenerationProfile.CreateDefault(TextureType.AmbientOcclusion)
                },

                ChannelType.Gloss => new ChannelSourceSettings {
                    ChannelType = ChannelType.Gloss,
                    ApplyToksvig = true,
                    ToksvigSettings = ToksvigSettings.CreateDefault(),
                    MipProfile = MipGenerationProfile.CreateDefault(TextureType.Gloss)
                },

                ChannelType.Metallic => new ChannelSourceSettings {
                    ChannelType = ChannelType.Metallic,
                    MipProfile = MipGenerationProfile.CreateDefault(TextureType.Metallic)
                },

                ChannelType.Height => new ChannelSourceSettings {
                    ChannelType = ChannelType.Height,
                    MipProfile = MipGenerationProfile.CreateDefault(TextureType.Height)
                },

                _ => throw new ArgumentException($"Unknown channel type: {channelType}")
            };
        }

        /// <summary>
        /// Валидация настроек
        /// </summary>
        public bool Validate(out string? error) {
            // КРИТИЧНО: SourcePath обязателен - нет текстуры = нет канала
            if (string.IsNullOrEmpty(SourcePath)) {
                error = $"SourcePath is required for {ChannelType} channel - texture file must be specified";
                return false;
            }

            if (!System.IO.File.Exists(SourcePath)) {
                error = $"Texture file not found for {ChannelType} channel: {SourcePath}";
                return false;
            }

            if (ApplyToksvig && ChannelType != ChannelType.Gloss) {
                error = "Toksvig correction can only be applied to Gloss channel";
                return false;
            }

            if (AOProcessingMode != AOProcessingMode.None &&
                ChannelType != ChannelType.AmbientOcclusion &&
                ChannelType != ChannelType.Metallic) {
                error = "AO processing can only be applied to AmbientOcclusion or Metallic channels";
                return false;
            }

            if (AOBias < 0.0f || AOBias > 1.0f) {
                error = $"AOBias must be in range [0.0, 1.0], got {AOBias}";
                return false;
            }

            if (AOPercentile < 0.0f || AOPercentile > 100.0f) {
                error = $"AOPercentile must be in range [0.0, 100.0], got {AOPercentile}";
                return false;
            }

            // SourcePath обязателен
            if (string.IsNullOrEmpty(SourcePath)) {
                error = "SourcePath is required";
                return false;
            }

            error = null;
            return true;
        }
    }
}
