using AssetProcessor.ModelConversion.Core;
using AssetProcessor.ModelConversion.Pipeline;
using AssetProcessor.Resources;
using AssetProcessor.Services.Models;
using AssetProcessor.Settings;
using NLog;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AssetProcessor.Services {
    public class ModelConversionService : IModelConversionService {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly ILogService logService;

        public ModelConversionService(ILogService logService) {
            this.logService = logService;
        }

        public async Task<ModelConversionServiceResult> ConvertAsync(
            ModelResource model,
            ModelConversionSettings settings,
            CancellationToken cancellationToken = default) {
            if (string.IsNullOrWhiteSpace(model.Path)) {
                return new ModelConversionServiceResult(false, "Model file path is empty.", null);
            }

            var outputDir = BuildOutputDirectory(model.Path);

            try {
                File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [CONVERT] ProcessSelectedModel START\n");
                File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [CONVERT] Model: {model.Name}\n");

                logService.LogInfo($"Processing model: {model.Name}");
                logService.LogInfo($"  Source: {model.Path}");
                logService.LogInfo($"  Output: {outputDir}");

                var modelConversionSettings = ModelConversionSettingsManager.LoadSettings();
                var fbx2glTFPath = string.IsNullOrWhiteSpace(modelConversionSettings.FBX2glTFExecutablePath)
                    ? "FBX2glTF-windows-x86_64.exe"
                    : modelConversionSettings.FBX2glTFExecutablePath;
                var gltfPackPath = string.IsNullOrWhiteSpace(modelConversionSettings.GltfPackExecutablePath)
                    ? "gltfpack.exe"
                    : modelConversionSettings.GltfPackExecutablePath;

                logService.LogInfo($"  FBX2glTF: {fbx2glTFPath}");
                logService.LogInfo($"  gltfpack: {gltfPackPath}");

                var pipeline = new ModelConversionPipeline(fbx2glTFPath, gltfPackPath);

                File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [CONVERT] Calling ConvertAsync\n");
                var result = await pipeline.ConvertAsync(model.Path, outputDir, settings)
                    .ConfigureAwait(false);
                File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [CONVERT] ConvertAsync returned, Success={result.Success}\n");

                if (result.Success) {
                    logService.LogInfo("Model processed successfully");
                    logService.LogInfo($"  LOD files: {result.LodFiles.Count}");
                    logService.LogInfo($"  Manifest: {result.ManifestPath}");
                    return new ModelConversionServiceResult(true, "Model processed successfully.", result);
                }

                var errors = string.Join("\n", result.Errors);
                logService.LogError($"Model processing failed:\n{errors}");
                return new ModelConversionServiceResult(false, errors, result);
            } catch (Exception ex) {
                logService.LogError($"Error processing model: {ex.Message}");
                Logger.Error(ex, "Unhandled error during model conversion");
                return new ModelConversionServiceResult(false, ex.Message, null);
            }
        }

        private static string BuildOutputDirectory(string modelPath) {
            var sourceDir = Path.GetDirectoryName(modelPath) ?? Environment.CurrentDirectory;
            var outputDir = Path.Combine(sourceDir, "glb");
            Directory.CreateDirectory(outputDir);
            return outputDir;
        }
    }
}
