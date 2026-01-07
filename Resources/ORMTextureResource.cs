using AssetProcessor.Services;
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

        // Metallic settings
        private AOProcessingMode metallicProcessingMode = AOProcessingMode.None;
        private float metallicBias = 0.5f;
        private float metallicPercentile = 10.0f;

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
        public string AOSourceName => AOSource?.Name ?? "[No texture]";
        public string GlossSourceName => GlossSource?.Name ?? "[No texture]";
        public string MetallicSourceName => MetallicSource?.Name ?? "[No texture]";
        public string HeightSourceName => HeightSource?.Name ?? "[No texture]";

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
        /// Режим обработки Metallic мипмапов
        /// </summary>
        public AOProcessingMode MetallicProcessingMode {
            get => metallicProcessingMode;
            set {
                metallicProcessingMode = value;
                OnPropertyChanged(nameof(MetallicProcessingMode));
            }
        }

        /// <summary>
        /// Bias для Metallic обработки (0.3-0.7)
        /// </summary>
        public float MetallicBias {
            get => metallicBias;
            set {
                metallicBias = value;
                OnPropertyChanged(nameof(MetallicBias));
            }
        }

        /// <summary>
        /// Percentile для Metallic обработки
        /// </summary>
        public float MetallicPercentile {
            get => metallicPercentile;
            set {
                metallicPercentile = value;
                OnPropertyChanged(nameof(MetallicPercentile));
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

        /// <summary>
        /// Проверяет, готова ли ORM текстура к упаковке (минимум 2 канала)
        /// </summary>
        public bool IsReadyToPack() {
            int channelCount = (AOSource != null ? 1 : 0) +
                              (GlossSource != null ? 1 : 0) +
                              (MetallicSource != null ? 1 : 0) +
                              (HeightSource != null ? 1 : 0);
            return channelCount >= 2;
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

        /// <summary>
        /// ID проекта для сохранения настроек
        /// </summary>
        public int ProjectId { get; set; }

        /// <summary>
        /// Уникальный ключ для хранения настроек (на основе материала или имени)
        /// </summary>
        public string SettingsKey { get; set; } = "";

        /// <summary>
        /// Загрузить настройки из хранилища
        /// </summary>
        public void LoadSettings() {
            if (ProjectId == 0 || string.IsNullOrEmpty(SettingsKey)) return;

            var settings = ResourceSettingsService.Instance.GetORMTextureSettings(ProjectId, SettingsKey);
            if (settings == null) return;

            // Packing mode
            if (Enum.TryParse<ChannelPackingMode>(settings.PackingMode, out var mode)) {
                packingMode = mode;
            }

            // AO settings
            if (Enum.TryParse<AOProcessingMode>(settings.AOProcessingMode, out var aoMode)) {
                aoProcessingMode = aoMode;
            }
            aoBias = settings.AOBias;
            aoPercentile = settings.AOPercentile;
            if (Enum.TryParse<FilterType>(settings.AOFilterType, out var aoFilter)) {
                aoFilterType = aoFilter;
            }

            // Gloss settings
            glossToksvigEnabled = settings.GlossToksvigEnabled;
            glossToksvigPower = settings.GlossToksvigPower;
            if (Enum.TryParse<ToksvigCalculationMode>(settings.GlossToksvigCalculationMode, out var toksvigMode)) {
                glossToksvigCalculationMode = toksvigMode;
            }
            glossToksvigMinMipLevel = settings.GlossToksvigMinMipLevel;
            glossToksvigEnergyPreserving = settings.GlossToksvigEnergyPreserving;
            glossToksvigSmoothVariance = settings.GlossToksvigSmoothVariance;
            if (Enum.TryParse<FilterType>(settings.GlossFilterType, out var glossFilter)) {
                glossFilterType = glossFilter;
            }

            // Metallic settings
            if (Enum.TryParse<AOProcessingMode>(settings.MetallicProcessingMode, out var metalMode)) {
                metallicProcessingMode = metalMode;
            }
            metallicBias = settings.MetallicBias;
            metallicPercentile = settings.MetallicPercentile;
            if (Enum.TryParse<FilterType>(settings.MetallicFilterType, out var metalFilter)) {
                metallicFilterType = metalFilter;
            }

            // Compression settings
            if (Enum.TryParse<CompressionFormat>(settings.CompressionFormat, out var format)) {
                compressionFormat = format;
            }
            compressLevel = settings.CompressLevel;
            qualityLevel = settings.QualityLevel;
            uastcQuality = settings.UASTCQuality;
            enableRDO = settings.EnableRDO;
            rdoLambda = settings.RDOLambda;
            perceptual = settings.Perceptual;
            enableSupercompression = settings.EnableSupercompression;
            supercompressionLevel = settings.SupercompressionLevel;

            // Status
            if (!string.IsNullOrEmpty(settings.Status)) {
                Status = settings.Status;
            }
            if (!string.IsNullOrEmpty(settings.OutputPath)) {
                Path = settings.OutputPath;
            }
        }

        /// <summary>
        /// Сохранить настройки в хранилище
        /// </summary>
        public void SaveSettings() {
            if (ProjectId == 0 || string.IsNullOrEmpty(SettingsKey)) return;

            var settings = new ORMTextureSettings {
                PackingMode = packingMode.ToString(),

                // Source IDs
                AOSourceId = aoSource?.ID,
                GlossSourceId = glossSource?.ID,
                MetallicSourceId = metallicSource?.ID,
                HeightSourceId = heightSource?.ID,

                // AO settings
                AOProcessingMode = aoProcessingMode.ToString(),
                AOBias = aoBias,
                AOPercentile = aoPercentile,
                AOFilterType = aoFilterType.ToString(),

                // Gloss settings
                GlossToksvigEnabled = glossToksvigEnabled,
                GlossToksvigPower = glossToksvigPower,
                GlossToksvigCalculationMode = glossToksvigCalculationMode.ToString(),
                GlossToksvigMinMipLevel = glossToksvigMinMipLevel,
                GlossToksvigEnergyPreserving = glossToksvigEnergyPreserving,
                GlossToksvigSmoothVariance = glossToksvigSmoothVariance,
                GlossFilterType = glossFilterType.ToString(),

                // Metallic settings
                MetallicProcessingMode = metallicProcessingMode.ToString(),
                MetallicBias = metallicBias,
                MetallicPercentile = metallicPercentile,
                MetallicFilterType = metallicFilterType.ToString(),

                // Compression settings
                CompressionFormat = compressionFormat.ToString(),
                CompressLevel = compressLevel,
                QualityLevel = qualityLevel,
                UASTCQuality = uastcQuality,
                EnableRDO = enableRDO,
                RDOLambda = rdoLambda,
                Perceptual = perceptual,
                EnableSupercompression = enableSupercompression,
                SupercompressionLevel = supercompressionLevel,

                // Status
                Status = Status,
                OutputPath = Path
            };

            ResourceSettingsService.Instance.SaveORMTextureSettings(ProjectId, SettingsKey, settings);
        }

        /// <summary>
        /// Восстановить источники текстур по сохраненным ID
        /// </summary>
        public void RestoreSources(List<TextureResource> availableTextures) {
            if (ProjectId == 0 || string.IsNullOrEmpty(SettingsKey)) return;

            var settings = ResourceSettingsService.Instance.GetORMTextureSettings(ProjectId, SettingsKey);
            if (settings == null) return;

            if (settings.AOSourceId.HasValue) {
                aoSource = availableTextures.Find(t => t.ID == settings.AOSourceId.Value);
            }
            if (settings.GlossSourceId.HasValue) {
                glossSource = availableTextures.Find(t => t.ID == settings.GlossSourceId.Value);
            }
            if (settings.MetallicSourceId.HasValue) {
                metallicSource = availableTextures.Find(t => t.ID == settings.MetallicSourceId.Value);
            }
            if (settings.HeightSourceId.HasValue) {
                heightSource = availableTextures.Find(t => t.ID == settings.HeightSourceId.Value);
            }
        }
    }
}
