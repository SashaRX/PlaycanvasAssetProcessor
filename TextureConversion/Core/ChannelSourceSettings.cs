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
        /// Путь к исходной текстуре для этого канала
        /// Null = использовать значение по умолчанию (белый для AO, черный для остальных)
        /// </summary>
        public string? SourcePath { get; set; }

        /// <summary>
        /// Значение по умолчанию если SourcePath = null (0.0-1.0)
        /// </summary>
        public float DefaultValue { get; set; } = 1.0f;

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
                    DefaultValue = 1.0f, // Белый = без затенения
                    AOProcessingMode = AOProcessingMode.BiasedDarkening,
                    AOBias = 0.5f,
                    MipProfile = MipGenerationProfile.CreateDefault(TextureType.AmbientOcclusion)
                },

                ChannelType.Gloss => new ChannelSourceSettings {
                    ChannelType = ChannelType.Gloss,
                    DefaultValue = 0.5f, // Средний глянец
                    ApplyToksvig = true,
                    ToksvigSettings = ToksvigSettings.CreateDefault(),
                    MipProfile = MipGenerationProfile.CreateDefault(TextureType.Gloss)
                },

                ChannelType.Metallic => new ChannelSourceSettings {
                    ChannelType = ChannelType.Metallic,
                    DefaultValue = 0.0f, // Не металл по умолчанию
                    MipProfile = MipGenerationProfile.CreateDefault(TextureType.Metallic)
                },

                ChannelType.Height => new ChannelSourceSettings {
                    ChannelType = ChannelType.Height,
                    DefaultValue = 0.5f, // Средняя высота
                    MipProfile = MipGenerationProfile.CreateDefault(TextureType.Height)
                },

                _ => throw new ArgumentException($"Unknown channel type: {channelType}")
            };
        }

        /// <summary>
        /// Валидация настроек
        /// </summary>
        public bool Validate(out string? error) {
            if (ApplyToksvig && ChannelType != ChannelType.Gloss) {
                error = "Toksvig correction can only be applied to Gloss channel";
                return false;
            }

            if (AOProcessingMode != AOProcessingMode.None && ChannelType != ChannelType.AmbientOcclusion) {
                error = "AO processing can only be applied to AmbientOcclusion channel";
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

            if (DefaultValue < 0.0f || DefaultValue > 1.0f) {
                error = $"DefaultValue must be in range [0.0, 1.0], got {DefaultValue}";
                return false;
            }

            error = null;
            return true;
        }
    }
}
