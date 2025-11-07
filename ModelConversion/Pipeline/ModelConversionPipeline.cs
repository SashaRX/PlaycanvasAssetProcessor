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
            _fbx2glTFWrapper = new FBX2glTFWrapper(fbx2glTFPath);
            _gltfPackWrapper = new GltfPackWrapper(gltfPackPath);
            _manifestGenerator = new LodManifestGenerator();
        }

        /// <summary>
        /// Конвертирует FBX в оптимизированные GLB с LOD цепочкой
        /// </summary>
        /// <param name="inputFbxPath">Путь к FBX файлу</param>
        /// <param name="outputDirectory">Директория для выходных файлов</param>
        /// <param name="settings">Настройки конвертации</param>
        /// <returns>Результат конвертации</returns>
        public async Task<ModelConversionResult> ConvertAsync(
            string inputFbxPath,
            string outputDirectory,
            ModelConversionSettings settings) {

            var result = new ModelConversionResult {
                InputPath = inputFbxPath,
                OutputDirectory = outputDirectory
            };

            var startTime = DateTime.Now;

            try {
                Logger.Info($"=== MODEL CONVERSION PIPELINE START ===");
                Logger.Info($"Input: {inputFbxPath}");
                Logger.Info($"Output: {outputDirectory}");
                Logger.Info($"Settings: Generate LODs={settings.GenerateLods}, Compression={settings.CompressionMode}");

                // Проверяем доступность инструментов
                if (!await _fbx2glTFWrapper.IsAvailableAsync()) {
                    throw new Exception("FBX2glTF not available. Please install it or specify path.");
                }

                if (!await _gltfPackWrapper.IsAvailableAsync()) {
                    throw new Exception("gltfpack not available. Please install meshoptimizer or specify path.");
                }

                // Создаём директории
                Directory.CreateDirectory(outputDirectory);
                var buildDir = Path.Combine(outputDirectory, "build");
                Directory.CreateDirectory(buildDir);

                var modelName = Path.GetFileNameWithoutExtension(inputFbxPath);

                // ШАГ A: FBX → базовый GLB (без сжатия)
                Logger.Info("=== STEP A: FBX → BASE GLB ===");
                var baseGlbPath = Path.Combine(buildDir, modelName);
                var fbxResult = await _fbx2glTFWrapper.ConvertToGlbAsync(inputFbxPath, baseGlbPath);

                if (!fbxResult.Success) {
                    throw new Exception($"FBX2glTF conversion failed: {fbxResult.Error}");
                }

                Logger.Info($"Base GLB created: {fbxResult.OutputFilePath} ({fbxResult.OutputFileSize} bytes)");
                result.BaseGlbPath = fbxResult.OutputFilePath!;

                // ШАГ B & C: Генерация LOD цепочки
                if (settings.GenerateLods) {
                    Logger.Info("=== STEP B & C: LOD GENERATION ===");

                    // Генерируем два трека если нужно:
                    // 1. dist/glb - только квантование (fallback для редакторов)
                    // 2. dist/meshopt - с EXT_meshopt_compression (для продакшена)
                    var tracks = settings.GenerateBothTracks
                        ? new[] { ("glb", CompressionMode.Quantization), ("meshopt", settings.CompressionMode) }
                        : new[] { (settings.CompressionMode == CompressionMode.MeshOpt || settings.CompressionMode == CompressionMode.MeshOptAggressive ? "meshopt" : "glb", settings.CompressionMode) };

                    foreach (var (trackName, compressionMode) in tracks) {
                        Logger.Info($"Generating track: {trackName} (compression: {compressionMode})");

                        var trackDir = Path.Combine(outputDirectory, "dist", trackName);
                        Directory.CreateDirectory(trackDir);

                        var lodFiles = new Dictionary<LodLevel, string>();
                        var lodMetrics = new Dictionary<string, MeshMetrics>();

                        foreach (var lodSettings in settings.LodChain) {
                            var lodName = $"LOD{(int)lodSettings.Level}";
                            var lodFileName = $"{modelName}_lod{(int)lodSettings.Level}.glb";
                            var lodOutputPath = Path.Combine(trackDir, lodFileName);

                            Logger.Info($"  Generating {lodName}: simplification={lodSettings.SimplificationRatio:F2}, aggressive={lodSettings.AggressiveSimplification}");

                            var gltfResult = await _gltfPackWrapper.OptimizeAsync(
                                fbxResult.OutputFilePath!,
                                lodOutputPath,
                                lodSettings,
                                compressionMode,
                                settings.Quantization,
                                generateReport: settings.GenerateQAReport
                            );

                            if (!gltfResult.Success) {
                                Logger.Error($"Failed to generate {lodName}: {gltfResult.Error}");
                                result.Errors.Add($"{lodName} generation failed: {gltfResult.Error}");
                                continue;
                            }

                            Logger.Info($"  {lodName} created: {gltfResult.OutputFileSize} bytes, {gltfResult.TriangleCount} tris, {gltfResult.VertexCount} verts");

                            lodFiles[lodSettings.Level] = lodOutputPath;

                            // Собираем метрики
                            lodMetrics[lodName] = new MeshMetrics {
                                TriangleCount = gltfResult.TriangleCount,
                                VertexCount = gltfResult.VertexCount,
                                FileSize = gltfResult.OutputFileSize,
                                SimplificationRatio = lodSettings.SimplificationRatio,
                                CompressionMode = compressionMode.ToString()
                            };
                        }

                        // Сохраняем LOD файлы и метрики в результат
                        if (trackName == "meshopt" || !settings.GenerateBothTracks) {
                            result.LodFiles = lodFiles;
                            result.LodMetrics = lodMetrics;
                        }

                        // ШАГ D: Генерация манифеста
                        if (settings.GenerateManifest && lodFiles.Count > 0) {
                            Logger.Info($"=== STEP D: MANIFEST GENERATION ({trackName}) ===");

                            var manifestDir = Path.Combine(outputDirectory, "dist", "manifest");
                            Directory.CreateDirectory(manifestDir);

                            var manifestPath = _manifestGenerator.GenerateManifest(
                                modelName,
                                lodFiles,
                                settings,
                                manifestDir
                            );

                            Logger.Info($"Manifest created: {manifestPath}");

                            if (trackName == "meshopt" || !settings.GenerateBothTracks) {
                                result.ManifestPath = manifestPath;
                            }
                        }

                        // ШАГ E: QA отчёт
                        if (settings.GenerateQAReport && lodMetrics.Count > 0) {
                            Logger.Info($"=== STEP E: QA REPORT ({trackName}) ===");

                            var qaReport = new QualityReport {
                                ModelName = modelName,
                                LodMetrics = lodMetrics
                            };

                            // Оцениваем критерии приёмки
                            qaReport.EvaluateAcceptanceCriteria(settings);

                            // Сохраняем отчёт
                            var reportsDir = Path.Combine(outputDirectory, "reports");
                            Directory.CreateDirectory(reportsDir);

                            var reportPath = Path.Combine(reportsDir, $"{modelName}_{trackName}_report.json");
                            qaReport.SaveToFile(reportPath);

                            Logger.Info($"QA Report saved: {reportPath}");
                            Logger.Info(qaReport.ToTextReport());

                            if (trackName == "meshopt" || !settings.GenerateBothTracks) {
                                result.QAReport = qaReport;
                                result.QAReportPath = reportPath;
                            }

                            // Добавляем warnings/errors из отчёта
                            result.Warnings.AddRange(qaReport.Warnings);
                            result.Errors.AddRange(qaReport.Errors);
                        }
                    }
                }

                // Cleanup промежуточных файлов
                if (settings.CleanupIntermediateFiles) {
                    Logger.Info("=== CLEANUP INTERMEDIATE FILES ===");
                    try {
                        if (Directory.Exists(buildDir)) {
                            Directory.Delete(buildDir, recursive: true);
                            Logger.Info($"Deleted build directory: {buildDir}");
                        }
                    } catch (Exception ex) {
                        Logger.Warn($"Failed to cleanup build directory: {ex.Message}");
                    }
                }

                result.Success = result.Errors.Count == 0;
                result.Duration = DateTime.Now - startTime;

                Logger.Info($"=== MODEL CONVERSION COMPLETE ===");
                Logger.Info($"Success: {result.Success}");
                Logger.Info($"Duration: {result.Duration.TotalSeconds:F2}s");
                Logger.Info($"LOD files: {result.LodFiles.Count}");

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
