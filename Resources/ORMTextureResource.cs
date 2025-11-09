using AssetProcessor.TextureConversion.Core;
using System.Collections.Generic;

namespace AssetProcessor.Resources {
    /// <summary>
    /// Виртуальная ORM текстура для упаковки каналов
    /// </summary>
    public class ORMTextureResource : TextureResource {
        private ChannelPackingMode packingMode = ChannelPackingMode.OGM;
        private TextureResource? aoSource;
        private TextureResource? glossSource;
        private TextureResource? metallicSource;
        private TextureResource? heightSource;

        // AO settings
        private AOProcessingMode aoProcessingMode = AOProcessingMode.None;  // Changed from BiasedDarkening to None to avoid validation errors on Gloss channel
        private float aoBias = 0.5f;
        private float aoPercentile = 10.0f;

        // Gloss settings
        private bool glossToksvigEnabled = true;
        private float glossToksvigPower = 4.0f;

        // Default values for missing channels
        private float aoDefaultValue = 1.0f;
        private float glossDefaultValue = 0.5f;
        private float metallicDefaultValue = 0.0f;
        private float heightDefaultValue = 0.5f;

        /// <summary>
        /// Режим упаковки (OG, OGM, OGMH)
        /// </summary>
        public ChannelPackingMode PackingMode {
            get => packingMode;
            set {
                packingMode = value;
                OnPropertyChanged(nameof(PackingMode));
                OnPropertyChanged(nameof(PackingModeDescription));
                OnPropertyChanged(nameof(IsOGMode));
                OnPropertyChanged(nameof(IsOGMMode));
                OnPropertyChanged(nameof(IsOGMHMode));
            }
        }

        public string PackingModeDescription => PackingMode switch {
            ChannelPackingMode.OG => "OG (RGB=AO, A=Gloss)",
            ChannelPackingMode.OGM => "OGM (R=AO, G=Gloss, B=Metallic)",
            ChannelPackingMode.OGMH => "OGMH (R=AO, G=Gloss, B=Metallic, A=Height)",
            _ => "None"
        };

        public bool IsOGMode => PackingMode == ChannelPackingMode.OG;
        public bool IsOGMMode => PackingMode == ChannelPackingMode.OGM;
        public bool IsOGMHMode => PackingMode == ChannelPackingMode.OGMH;

        /// <summary>
        /// Источник для AO канала
        /// </summary>
        public TextureResource? AOSource {
            get => aoSource;
            set {
                aoSource = value;
                OnPropertyChanged(nameof(AOSource));
                OnPropertyChanged(nameof(AOSourceName));
            }
        }

        /// <summary>
        /// Источник для Gloss канала
        /// </summary>
        public TextureResource? GlossSource {
            get => glossSource;
            set {
                glossSource = value;
                OnPropertyChanged(nameof(GlossSource));
                OnPropertyChanged(nameof(GlossSourceName));
            }
        }

        /// <summary>
        /// Источник для Metallic канала
        /// </summary>
        public TextureResource? MetallicSource {
            get => metallicSource;
            set {
                metallicSource = value;
                OnPropertyChanged(nameof(MetallicSource));
                OnPropertyChanged(nameof(MetallicSourceName));
            }
        }

        /// <summary>
        /// Источник для Height канала
        /// </summary>
        public TextureResource? HeightSource {
            get => heightSource;
            set {
                heightSource = value;
                OnPropertyChanged(nameof(HeightSource));
                OnPropertyChanged(nameof(HeightSourceName));
            }
        }

        // Display names for UI
        public string AOSourceName => AOSource?.Name ?? $"[Constant: {AODefaultValue:F2}]";
        public string GlossSourceName => GlossSource?.Name ?? $"[Constant: {GlossDefaultValue:F2}]";
        public string MetallicSourceName => MetallicSource?.Name ?? $"[Constant: {MetallicDefaultValue:F2}]";
        public string HeightSourceName => HeightSource?.Name ?? $"[Constant: {HeightDefaultValue:F2}]";

        /// <summary>
        /// Режим обработки AO мипмапов
        /// </summary>
        public AOProcessingMode AOProcessingMode {
            get => aoProcessingMode;
            set {
                aoProcessingMode = value;
                OnPropertyChanged(nameof(AOProcessingMode));
            }
        }

        /// <summary>
        /// Bias для AO обработки (0.3-0.7)
        /// </summary>
        public float AOBias {
            get => aoBias;
            set {
                aoBias = value;
                OnPropertyChanged(nameof(AOBias));
            }
        }

        /// <summary>
        /// Percentile для AO обработки
        /// </summary>
        public float AOPercentile {
            get => aoPercentile;
            set {
                aoPercentile = value;
                OnPropertyChanged(nameof(AOPercentile));
            }
        }

        /// <summary>
        /// Включить Toksvig для Gloss
        /// </summary>
        public bool GlossToksvigEnabled {
            get => glossToksvigEnabled;
            set {
                glossToksvigEnabled = value;
                OnPropertyChanged(nameof(GlossToksvigEnabled));
            }
        }

        /// <summary>
        /// Composite Power для Toksvig
        /// </summary>
        public float GlossToksvigPower {
            get => glossToksvigPower;
            set {
                glossToksvigPower = value;
                OnPropertyChanged(nameof(GlossToksvigPower));
            }
        }

        // Default values
        public float AODefaultValue {
            get => aoDefaultValue;
            set {
                aoDefaultValue = value;
                OnPropertyChanged(nameof(AODefaultValue));
                OnPropertyChanged(nameof(AOSourceName));
            }
        }

        public float GlossDefaultValue {
            get => glossDefaultValue;
            set {
                glossDefaultValue = value;
                OnPropertyChanged(nameof(GlossDefaultValue));
                OnPropertyChanged(nameof(GlossSourceName));
            }
        }

        public float MetallicDefaultValue {
            get => metallicDefaultValue;
            set {
                metallicDefaultValue = value;
                OnPropertyChanged(nameof(MetallicDefaultValue));
                OnPropertyChanged(nameof(MetallicSourceName));
            }
        }

        public float HeightDefaultValue {
            get => heightDefaultValue;
            set {
                heightDefaultValue = value;
                OnPropertyChanged(nameof(HeightDefaultValue));
                OnPropertyChanged(nameof(HeightSourceName));
            }
        }

        /// <summary>
        /// Проверяет, готова ли ORM текстура к упаковке
        /// </summary>
        public bool IsReadyToPack() {
            return PackingMode switch {
                ChannelPackingMode.OG => AOSource != null && GlossSource != null,
                ChannelPackingMode.OGM => AOSource != null && GlossSource != null && MetallicSource != null,
                ChannelPackingMode.OGMH => AOSource != null && GlossSource != null && MetallicSource != null && HeightSource != null,
                _ => false
            };
        }

        /// <summary>
        /// Возвращает список недостающих каналов
        /// </summary>
        public List<string> GetMissingChannels() {
            var missing = new List<string>();

            if (PackingMode >= ChannelPackingMode.OG) {
                if (AOSource == null) missing.Add("AO");
                if (GlossSource == null) missing.Add("Gloss");
            }

            if (PackingMode >= ChannelPackingMode.OGM) {
                if (MetallicSource == null) missing.Add("Metallic");
            }

            if (PackingMode == ChannelPackingMode.OGMH) {
                if (HeightSource == null) missing.Add("Height");
            }

            return missing;
        }

        public ORMTextureResource() {
            // Виртуальная текстура имеет специальное имя
            Name = "[ORM Texture - Not Packed]";
            TextureType = "ORM (Virtual)";
        }
    }
}
