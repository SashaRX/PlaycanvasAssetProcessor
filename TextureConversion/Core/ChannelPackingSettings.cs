namespace AssetProcessor.TextureConversion.Core {
    /// <summary>
    /// Настройки упаковки каналов для ORM текстур
    /// </summary>
    public class ChannelPackingSettings {
        /// <summary>
        /// Режим упаковки каналов
        /// </summary>
        public ChannelPackingMode Mode { get; set; } = ChannelPackingMode.None;

        /// <summary>
        /// Настройки для R канала (или RGB в режиме OG)
        /// </summary>
        public ChannelSourceSettings? RedChannel { get; set; }

        /// <summary>
        /// Настройки для G канала
        /// </summary>
        public ChannelSourceSettings? GreenChannel { get; set; }

        /// <summary>
        /// Настройки для B канала
        /// </summary>
        public ChannelSourceSettings? BlueChannel { get; set; }

        /// <summary>
        /// Настройки для A канала
        /// </summary>
        public ChannelSourceSettings? AlphaChannel { get; set; }

        /// <summary>
        /// Базовый путь для автоматического поиска текстур
        /// Например, "/path/to/material_albedo.png" -> будет искать material_ao.png, material_gloss.png и т.д.
        /// </summary>
        public string? AutoDetectBasePath { get; set; }

        /// <summary>
        /// Количество мипмап уровней (-1 = авто, 0 = только базовый уровень, >0 = конкретное количество)
        /// </summary>
        public int MipmapCount { get; set; } = -1;

        /// <summary>
        /// Профиль генерации мипмапов
        /// </summary>
        public MipGenerationProfile? MipGenerationProfile { get; set; }

        /// <summary>
        /// Создает настройки по умолчанию для режима упаковки
        /// </summary>
        public static ChannelPackingSettings CreateDefault(ChannelPackingMode mode) {
            var settings = new ChannelPackingSettings {
                Mode = mode
            };

            switch (mode) {
                case ChannelPackingMode.OG:
                    // RGB = AO, A = Gloss
                    settings.RedChannel = ChannelSourceSettings.CreateDefault(ChannelType.AmbientOcclusion);
                    settings.AlphaChannel = ChannelSourceSettings.CreateDefault(ChannelType.Gloss);
                    break;

                case ChannelPackingMode.OGM:
                    // R = AO, G = Gloss, B = Metallic
                    settings.RedChannel = ChannelSourceSettings.CreateDefault(ChannelType.AmbientOcclusion);
                    settings.GreenChannel = ChannelSourceSettings.CreateDefault(ChannelType.Gloss);
                    settings.BlueChannel = ChannelSourceSettings.CreateDefault(ChannelType.Metallic);
                    break;

                case ChannelPackingMode.OGMH:
                    // R = AO, G = Gloss, B = Metallic, A = Height
                    settings.RedChannel = ChannelSourceSettings.CreateDefault(ChannelType.AmbientOcclusion);
                    settings.GreenChannel = ChannelSourceSettings.CreateDefault(ChannelType.Gloss);
                    settings.BlueChannel = ChannelSourceSettings.CreateDefault(ChannelType.Metallic);
                    settings.AlphaChannel = ChannelSourceSettings.CreateDefault(ChannelType.Height);
                    break;

                case ChannelPackingMode.None:
                    // Без упаковки
                    break;
            }

            return settings;
        }

        /// <summary>
        /// Получает описание режима упаковки
        /// </summary>
        public string GetModeDescription() {
            return Mode switch {
                ChannelPackingMode.OG => "OG (RGB=AO, A=Gloss) - for non-metallic materials",
                ChannelPackingMode.OGM => "OGM (R=AO, G=Gloss, B=Metallic)",
                ChannelPackingMode.OGMH => "OGMH (R=AO, G=Gloss, B=Metallic, A=Height)",
                ChannelPackingMode.None => "None - single channel texture",
                _ => "Unknown mode"
            };
        }

        /// <summary>
        /// Получает список активных каналов
        /// </summary>
        public List<ChannelSourceSettings> GetActiveChannels() {
            var channels = new List<ChannelSourceSettings>();

            if (RedChannel != null) channels.Add(RedChannel);
            if (GreenChannel != null) channels.Add(GreenChannel);
            if (BlueChannel != null) channels.Add(BlueChannel);
            if (AlphaChannel != null) channels.Add(AlphaChannel);

            return channels;
        }

        /// <summary>
        /// Валидация настроек
        /// </summary>
        public bool Validate(out string? error) {
            if (Mode == ChannelPackingMode.None) {
                error = "Channel packing mode is None - no packing will be performed";
                return true;
            }

            var channels = GetActiveChannels();
            if (channels.Count == 0) {
                error = $"No channels configured for mode {Mode}";
                return false;
            }

            // Валидация каждого канала
            foreach (var channel in channels) {
                if (!channel.Validate(out var channelError)) {
                    error = $"{channel.ChannelType}: {channelError}";
                    return false;
                }
            }

            // Проверка соответствия режима и каналов
            switch (Mode) {
                case ChannelPackingMode.OG:
                    if (RedChannel == null || RedChannel.ChannelType != ChannelType.AmbientOcclusion) {
                        error = "OG mode requires RedChannel to be AmbientOcclusion";
                        return false;
                    }
                    if (AlphaChannel == null || AlphaChannel.ChannelType != ChannelType.Gloss) {
                        error = "OG mode requires AlphaChannel to be Gloss";
                        return false;
                    }
                    break;

                case ChannelPackingMode.OGM:
                    if (RedChannel == null || RedChannel.ChannelType != ChannelType.AmbientOcclusion) {
                        error = "OGM mode requires RedChannel to be AmbientOcclusion";
                        return false;
                    }
                    if (GreenChannel == null || GreenChannel.ChannelType != ChannelType.Gloss) {
                        error = "OGM mode requires GreenChannel to be Gloss";
                        return false;
                    }
                    if (BlueChannel == null || BlueChannel.ChannelType != ChannelType.Metallic) {
                        error = "OGM mode requires BlueChannel to be Metallic";
                        return false;
                    }
                    break;

                case ChannelPackingMode.OGMH:
                    if (RedChannel == null || RedChannel.ChannelType != ChannelType.AmbientOcclusion) {
                        error = "OGMH mode requires RedChannel to be AmbientOcclusion";
                        return false;
                    }
                    if (GreenChannel == null || GreenChannel.ChannelType != ChannelType.Gloss) {
                        error = "OGMH mode requires GreenChannel to be Gloss";
                        return false;
                    }
                    if (BlueChannel == null || BlueChannel.ChannelType != ChannelType.Metallic) {
                        error = "OGMH mode requires BlueChannel to be Metallic";
                        return false;
                    }
                    if (AlphaChannel == null || AlphaChannel.ChannelType != ChannelType.Height) {
                        error = "OGMH mode requires AlphaChannel to be Height";
                        return false;
                    }
                    break;
            }

            error = null;
            return true;
        }
    }
}
