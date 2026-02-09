using System.IO;
using AssetProcessor.ModelConversion.Analysis;
using AssetProcessor.ModelConversion.Core;
using AssetProcessor.ModelConversion.Wrappers;
using NLog;

namespace AssetProcessor.ModelConversion.Pipeline {
    /// <summary>
    /// Главный пайплайн конвертации моделей FBX → GLB + LOD
    /// Объединяет FBX2glTF и gltfpack для генерации оптимизированных моделей с LOD цепочкой
    /// </summary>
    public class ModelConversionPipeline {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly FBX2glTFWrapper _fbx2glTFWrapper;
        private readonly GltfPackWrapper _gltfPackWrapper;
        private readonly LodManifestGenerator _manifestGenerator;

        public ModelConversionPipeline(
            string? fbx2glTFPath = null,
            string? gltfPackPath = null) {
            Logger.Info($"Initializing ModelConversionPipeline with FBX2glTF: {fbx2glTFPath ?? "default"}, gltfpack: {gltfPackPath ?? "default"}");
            _fbx2glTFWrapper = new FBX2glTFWrapper(fbx2glTFPath);
            _gltfPackWrapper = new GltfPackWrapper(gltfPackPath);
            _manifestGenerator = new LodManifestGenerator();
        }

        /// <summary>
        /// Конвертирует FBX/glTF/GLB в оптимизированные GLB с LOD цепочкой
        /// </summary>
        /// <param name="inputPath">Путь к FBX, glTF или GLB файлу</param>
        /// <param name="outputDirectory">Директория для выходных файлов</param>
        /// <param name="settings">Настройки конвертации</param>
        /// <returns>Результат конвертации</returns>
        public async Task<ModelConversionResult> ConvertAsync(
            string inputPath,
            string outputDirectory,
            ModelConversionSettings settings) {

            var result = new ModelConversionResult {
                InputPath = inputPath,
                OutputDirectory = outputDirectory
            };

            var startTime = DateTime.Now;

            try {
                Logger.Info($"Model conversion: {inputPath} → {outputDirectory}, LODs={settings.GenerateLods}, Compression={settings.CompressionMode}");

                // Определяем формат входного файла
                var inputExtension = Path.GetExtension(inputPath).ToLowerInvariant();
                var isGltfInput = inputExtension is ".gltf" or ".glb";

                // Проверяем доступность инструментов
                if (!isGltfInput && !await _fbx2glTFWrapper.IsAvailableAsync()) {
                    throw new Exception("FBX2glTF not available. Please install it or specify path.");
                }

                if (!await _gltfPackWrapper.IsAvailableAsync()) {
                    throw new Exception("gltfpack not available. Please install meshoptimizer or specify path.");
                }

                // Создаём директории
                Directory.CreateDirectory(outputDirectory);
                var buildDir = Path.Combine(outputDirectory, "build");
                Directory.CreateDirectory(buildDir);

                var modelName = Path.GetFileNameWithoutExtension(inputPath);

                string baseGltfPath;

                if (isGltfInput) {
                    Logger.Info($"Direct {inputExtension.ToUpper()} input (skipping FBX2glTF)");
                    baseGltfPath = inputPath;
                    result.BaseGlbPath = inputPath;
                } else {
                    Logger.Info($"Converting FBX → GLB (excludeTextures={settings.ExcludeTextures})");
                    var baseGlbPathNoExt = Path.Combine(buildDir, modelName);
                    var fbxResult = await _fbx2glTFWrapper.ConvertToGlbAsync(inputPath, baseGlbPathNoExt, settings.ExcludeTextures);

                    if (!fbxResult.Success) {
                        throw new Exception($"FBX2glTF conversion failed: {fbxResult.Error}");
                    }

                    Logger.Info($"Base GLB created: {fbxResult.OutputFilePath} ({fbxResult.OutputFileSize} bytes)");
                    baseGltfPath = fbxResult.OutputFilePath!;
                    result.BaseGlbPath = baseGltfPath;
                }

                // ШАГ B & C: Генерация LOD цепочки
                if (settings.GenerateLods) {
                    var lodFiles = new Dictionary<LodLevel, string>();
                    var lodMetrics = new Dictionary<string, MeshMetrics>();

                    foreach (var lodSettings in settings.LodChain) {
                        var lodName = $"LOD{(int)lodSettings.Level}";
                        var lodFileName = $"{modelName}_lod{(int)lodSettings.Level}.glb";
                        var lodOutputPath = Path.Combine(outputDirectory, lodFileName);

                        Logger.Info($"Generating {lodName}: simplification={lodSettings.SimplificationRatio:F2}");

                        // Используем gltfpack для симплификации
                        var gltfResult = await _gltfPackWrapper.OptimizeAsync(
                            baseGltfPath,
                            lodOutputPath,
                            lodSettings,
                            settings.CompressionMode,
                            settings.Quantization,
                            settings.AdvancedSettings,
                            generateReport: settings.GenerateQAReport,
                            excludeTextures: settings.ExcludeTextures
                        );

                        if (!gltfResult.Success) {
                            Logger.Error($"Failed to generate {lodName}: {gltfResult.Error}");
                            result.Errors.Add($"{lodName} generation failed: {gltfResult.Error}");
                            continue;
                        }

                        Logger.Info($"{lodName} created: {gltfResult.OutputFileSize} bytes, {gltfResult.TriangleCount} tris, {gltfResult.VertexCount} verts");

                        lodFiles[lodSettings.Level] = lodOutputPath;

                        // Собираем метрики
                        lodMetrics[lodName] = new MeshMetrics {
                            TriangleCount = gltfResult.TriangleCount,
                            VertexCount = gltfResult.VertexCount,
                            FileSize = gltfResult.OutputFileSize,
                            SimplificationRatio = lodSettings.SimplificationRatio,
                            CompressionMode = settings.CompressionMode.ToString()
                        };
                    }

                    // Сохраняем LOD файлы и метрики в результат
                    result.LodFiles = lodFiles;
                    result.LodMetrics = lodMetrics;

                    if (settings.GenerateManifest && lodFiles.Count > 0) {
                        var manifestPath = _manifestGenerator.GenerateManifest(
                            modelName,
                            lodFiles,
                            settings,
                            outputDirectory
                        );

                        Logger.Info($"Manifest created: {manifestPath}");
                        result.ManifestPath = manifestPath;
                    }

                    if (settings.GenerateQAReport && lodMetrics.Count > 0) {
                        var qaReport = new QualityReport {
                            ModelName = modelName,
                            LodMetrics = lodMetrics
                        };

                        qaReport.EvaluateAcceptanceCriteria(settings);

                        var reportPath = Path.Combine(outputDirectory, $"{modelName}_qa_report.json");
                        qaReport.SaveToFile(reportPath);

                        Logger.Info($"QA Report saved: {reportPath}");

                        result.QAReport = qaReport;
                        result.QAReportPath = reportPath;

                        result.Warnings.AddRange(qaReport.Warnings);
                        result.Errors.AddRange(qaReport.Errors);
                    }
                }

                // Cleanup промежуточных файлов
                if (settings.CleanupIntermediateFiles) {
                    try {
                        // КРИТИЧНО: Удаляем только buildDir, который содержит все промежуточные файлы
                        // НЕ удаляем файлы из input directory - это может удалить пользовательские текстуры!
                        if (Directory.Exists(buildDir)) {
                            Directory.Delete(buildDir, recursive: true);
                        }
                    } catch (Exception ex) {
                        Logger.Warn($"Failed to cleanup build directory: {ex.Message}");
                    }
                }

                result.Success = result.Errors.Count == 0;
                result.Duration = DateTime.Now - startTime;

                Logger.Info($"Model conversion complete: Success={result.Success}, Duration={result.Duration.TotalSeconds:F2}s");

            } catch (Exception ex) {
                Logger.Error(ex, "Model conversion failed");
                result.Success = false;
                result.Errors.Add(ex.Message);
                result.Duration = DateTime.Now - startTime;
            }

            return result;
        }

        /// <summary>
        /// Проверяет доступность всех необходимых инструментов
        /// </summary>
        public async Task<ToolsAvailability> CheckToolsAsync() {
            var availability = new ToolsAvailability();

            try {
                availability.FBX2glTFAvailable = await _fbx2glTFWrapper.IsAvailableAsync();
                availability.GltfPackAvailable = await _gltfPackWrapper.IsAvailableAsync();
            } catch (Exception ex) {
                Logger.Error(ex, "Failed to check tools availability");
            }

            return availability;
        }
    }

    /// <summary>
    /// Результат конвертации модели
    /// </summary>
    public class ModelConversionResult {
        public bool Success { get; set; }
        public string InputPath { get; set; } = string.Empty;
        public string OutputDirectory { get; set; } = string.Empty;
        public string? BaseGlbPath { get; set; }
        public Dictionary<LodLevel, string> LodFiles { get; set; } = new();
        public Dictionary<string, MeshMetrics> LodMetrics { get; set; } = new();
        public string? ManifestPath { get; set; }
        public string? QAReportPath { get; set; }
        public QualityReport? QAReport { get; set; }
        public List<string> Warnings { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// Информация о доступности инструментов
    /// </summary>
    public class ToolsAvailability {
        public bool FBX2glTFAvailable { get; set; }
        public bool GltfPackAvailable { get; set; }
        public bool AllAvailable => FBX2glTFAvailable && GltfPackAvailable;
    }
}
