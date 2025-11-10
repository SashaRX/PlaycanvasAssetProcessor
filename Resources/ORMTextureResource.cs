using AssetProcessor.TextureConversion.Core;
using System.Collections.Generic;

namespace AssetProcessor.Resources {
    /// <summary>
    /// Виртуальная ORM текстура для упаковки каналов
    /// </summary>
    public class ORMTextureResource : TextureResource {
        /// <summary>
        /// Флаг для определения ORM текстуры в UI (переопределяет базовый класс)
        /// </summary>
        public override bool IsORMTexture => true;

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
        private ToksvigCalculationMode glossToksvigCalculationMode = ToksvigCalculationMode.Classic;
        private int glossToksvigMinMipLevel = 0;
        private bool glossToksvigEnergyPreserving = true;
        private bool glossToksvigSmoothVariance = true;

        // Compression settings
        private CompressionFormat compressionFormat = CompressionFormat.ETC1S;
        private int compressLevel = 1;   // ETC1S compress level (0-5)
        private int qualityLevel = 128;  // ETC1S quality (1-255)
        private int uastcQuality = 2;    // UASTC quality (0-4)

        // UASTC RDO settings
        private bool enableRDO = false;
        private float rdoLambda = 1.0f;  // 0.001-10.0

        // Perceptual settings
        private bool perceptual = false;

        // Supercompression (Zstd) - ONLY for UASTC!
        private bool enableSupercompression = false;
        private int supercompressionLevel = 3;  // 1-22

        // Mipmap settings
        private int mipmapCount = 0;     // 0 = auto
        private FilterType filterType = FilterType.Kaiser;

        // Per-channel filter settings
        private FilterType aoFilterType = FilterType.Kaiser;
        private FilterType glossFilterType = FilterType.Kaiser;
        private FilterType metallicFilterType = FilterType.Box;  // Box for binary metallic values

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

        /// <summary>
        /// Режим расчёта Toksvig
        /// </summary>
        public ToksvigCalculationMode GlossToksvigCalculationMode {
            get => glossToksvigCalculationMode;
            set {
                glossToksvigCalculationMode = value;
                OnPropertyChanged(nameof(GlossToksvigCalculationMode));
            }
        }

        /// <summary>
        /// Минимальный уровень мипмапа для Toksvig
        /// </summary>
        public int GlossToksvigMinMipLevel {
            get => glossToksvigMinMipLevel;
            set {
                glossToksvigMinMipLevel = value;
                OnPropertyChanged(nameof(GlossToksvigMinMipLevel));
            }
        }

        /// <summary>
        /// Использовать Energy-Preserving фильтрацию
        /// </summary>
        public bool GlossToksvigEnergyPreserving {
            get => glossToksvigEnergyPreserving;
            set {
                glossToksvigEnergyPreserving = value;
                OnPropertyChanged(nameof(GlossToksvigEnergyPreserving));
            }
        }

        /// <summary>
        /// Сглаживать вариацию при Toksvig расчете
        /// </summary>
        public bool GlossToksvigSmoothVariance {
            get => glossToksvigSmoothVariance;
            set {
                glossToksvigSmoothVariance = value;
                OnPropertyChanged(nameof(GlossToksvigSmoothVariance));
            }
        }

        /// <summary>
        /// Формат сжатия
        /// </summary>
        public new CompressionFormat CompressionFormat {
            get => compressionFormat;
            set {
                compressionFormat = value;
                OnPropertyChanged(nameof(CompressionFormat));
            }
        }

        /// <summary>
        /// Уровень сжатия для ETC1S (0-5)
        /// </summary>
        public int CompressLevel {
            get => compressLevel;
            set {
                compressLevel = value;
                OnPropertyChanged(nameof(CompressLevel));
            }
        }

        /// <summary>
        /// Качество для ETC1S (1-255)
        /// </summary>
        public int QualityLevel {
            get => qualityLevel;
            set {
                qualityLevel = value;
                OnPropertyChanged(nameof(QualityLevel));
            }
        }

        /// <summary>
        /// Качество для UASTC (0-4)
        /// </summary>
        public int UASTCQuality {
            get => uastcQuality;
            set {
                uastcQuality = value;
                OnPropertyChanged(nameof(UASTCQuality));
            }
        }

        /// <summary>
        /// Включить RDO для UASTC
        /// </summary>
        public bool EnableRDO {
            get => enableRDO;
            set {
                enableRDO = value;
                OnPropertyChanged(nameof(EnableRDO));
            }
        }

        /// <summary>
        /// RDO Lambda (0.001-10.0)
        /// </summary>
        public float RDOLambda {
            get => rdoLambda;
            set {
                rdoLambda = value;
                OnPropertyChanged(nameof(RDOLambda));
            }
        }

        /// <summary>
        /// Perceptual mode
        /// </summary>
        public bool Perceptual {
            get => perceptual;
            set {
                perceptual = value;
                OnPropertyChanged(nameof(Perceptual));
            }
        }

        /// <summary>
        /// Включить Supercompression (Zstd) - только для UASTC!
        /// </summary>
        public bool EnableSupercompression {
            get => enableSupercompression;
            set {
                enableSupercompression = value;
                OnPropertyChanged(nameof(EnableSupercompression));
            }
        }

        /// <summary>
        /// Уровень Supercompression (1-22)
        /// </summary>
        public int SupercompressionLevel {
            get => supercompressionLevel;
            set {
                supercompressionLevel = value;
                OnPropertyChanged(nameof(SupercompressionLevel));
            }
        }

        /// <summary>
        /// Количество мипмапов (0 = auto)
        /// </summary>
        public new int MipmapCount {
            get => mipmapCount;
            set {
                mipmapCount = value;
                OnPropertyChanged(nameof(MipmapCount));
            }
        }

        /// <summary>
        /// Тип фильтра для мипмапов
        /// </summary>
        public FilterType FilterType {
            get => filterType;
            set {
                filterType = value;
                OnPropertyChanged(nameof(FilterType));
            }
        }

        /// <summary>
        /// Тип фильтра для AO канала
        /// </summary>
        public FilterType AOFilterType {
            get => aoFilterType;
            set {
                aoFilterType = value;
                OnPropertyChanged(nameof(AOFilterType));
            }
        }

        /// <summary>
        /// Тип фильтра для Gloss канала
        /// </summary>
        public FilterType GlossFilterType {
            get => glossFilterType;
            set {
                glossFilterType = value;
                OnPropertyChanged(nameof(GlossFilterType));
            }
        }

        /// <summary>
        /// Тип фильтра для Metallic канала
        /// </summary>
        public FilterType MetallicFilterType {
            get => metallicFilterType;
            set {
                metallicFilterType = value;
                OnPropertyChanged(nameof(MetallicFilterType));
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
