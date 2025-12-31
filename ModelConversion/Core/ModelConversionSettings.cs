namespace AssetProcessor.ModelConversion.Core {
    /// <summary>
    /// Настройки конвертации модели
    /// </summary>
    public class ModelConversionSettings {
        /// <summary>
        /// Тип источника модели (FBX или GLB)
        /// </summary>
        public ModelSourceType SourceType { get; set; } = ModelSourceType.FBX;

        /// <summary>
        /// Генерировать LOD цепочку (LOD0-LOD3)
        /// </summary>
        public bool GenerateLods { get; set; } = true;

        /// <summary>
        /// Настройки для каждого уровня LOD
        /// </summary>
        public List<LodSettings> LodChain { get; set; } = LodSettings.CreateFullChain();

        /// <summary>
        /// Режим сжатия
        /// </summary>
        public CompressionMode CompressionMode { get; set; } = CompressionMode.Quantization;

        /// <summary>
        /// Настройки квантования (используется если CompressionMode = Quantization или MeshOpt*)
        /// </summary>
        public QuantizationSettings? Quantization { get; set; } = QuantizationSettings.CreateDefault();

        /// <summary>
        /// Расширенные настройки gltfpack
        /// </summary>
        public GltfPackSettings? AdvancedSettings { get; set; } = GltfPackSettings.CreateDefault();

        /// <summary>
        /// Гистерезис для переключения LOD (0.0-1.0)
        /// Предотвращает "мерцание" при переключении LOD
        /// </summary>
        public float LodHysteresis { get; set; } = 0.02f;

        /// <summary>
        /// Генерировать два трека сборки:
        /// 1. dist/glb - только квантование (fallback для редакторов)
        /// 2. dist/meshopt - с EXT_meshopt_compression (для продакшена)
        /// </summary>
        public bool GenerateBothTracks { get; set; } = false;

        /// <summary>
        /// Удалять промежуточные файлы после конвертации
        /// </summary>
        public bool CleanupIntermediateFiles { get; set; } = true;

        /// <summary>
        /// Исключить текстуры из GLB (экспортировать только геометрию, материалы, анимации)
        /// ВАЖНО: Текстуры должны обрабатываться отдельно через TextureConversion пайплайн!
        /// </summary>
        public bool ExcludeTextures { get; set; } = true;

        /// <summary>
        /// Создавать JSON манифест с метаданными LOD
        /// </summary>
        public bool GenerateManifest { get; set; } = true;

        /// <summary>
        /// Создавать QA отчет с метриками (треугольники, размер, bbox)
        /// </summary>
        public bool GenerateQAReport { get; set; } = true;

        /// <summary>
        /// Настройки по умолчанию
        /// Режим: Quantization only (совместимость с редакторами)
        /// </summary>
        public static ModelConversionSettings CreateDefault() {
            return new ModelConversionSettings {
                SourceType = ModelSourceType.FBX,
                GenerateLods = true,
                LodChain = LodSettings.CreateFullChain(),
                CompressionMode = CompressionMode.Quantization,
                Quantization = QuantizationSettings.CreateDefault(),
                AdvancedSettings = GltfPackSettings.CreateDefault(),
                LodHysteresis = 0.02f,
                GenerateBothTracks = false,
                CleanupIntermediateFiles = true,
                ExcludeTextures = true,
                GenerateManifest = true,
                GenerateQAReport = true
            };
        }

        /// <summary>
        /// Настройки для продакшена (EXT_meshopt_compression)
        /// </summary>
        public static ModelConversionSettings CreateProduction() {
            return new ModelConversionSettings {
                SourceType = ModelSourceType.FBX,
                GenerateLods = true,
                LodChain = LodSettings.CreateFullChain(),
                CompressionMode = CompressionMode.MeshOpt,
                Quantization = QuantizationSettings.CreateDefault(),
                AdvancedSettings = GltfPackSettings.CreateDefault(),
                LodHysteresis = 0.02f,
                GenerateBothTracks = true, // Генерируем оба трека
                CleanupIntermediateFiles = true,
                ExcludeTextures = true,
                GenerateManifest = true,
                GenerateQAReport = true
            };
        }

        /// <summary>
        /// Настройки для высокого качества
        /// </summary>
        public static ModelConversionSettings CreateHighQuality() {
            return new ModelConversionSettings {
                SourceType = ModelSourceType.FBX,
                GenerateLods = true,
                LodChain = LodSettings.CreateFullChain(),
                CompressionMode = CompressionMode.MeshOpt,
                Quantization = QuantizationSettings.CreateHighQuality(),
                AdvancedSettings = GltfPackSettings.CreateHighQuality(),
                LodHysteresis = 0.02f,
                GenerateBothTracks = true,
                CleanupIntermediateFiles = true,
                ExcludeTextures = true,
                GenerateManifest = true,
                GenerateQAReport = true
            };
        }

        /// <summary>
        /// Настройки для минимального размера
        /// </summary>
        public static ModelConversionSettings CreateMinSize() {
            return new ModelConversionSettings {
                SourceType = ModelSourceType.FBX,
                GenerateLods = true,
                LodChain = LodSettings.CreateFullChain(),
                CompressionMode = CompressionMode.MeshOpt,
                Quantization = QuantizationSettings.CreateMinSize(),
                AdvancedSettings = GltfPackSettings.CreateMinSize(),
                LodHysteresis = 0.02f,
                GenerateBothTracks = false,
                CleanupIntermediateFiles = true,
                ExcludeTextures = true,
                GenerateManifest = true,
                GenerateQAReport = true
            };
        }
    }
}
