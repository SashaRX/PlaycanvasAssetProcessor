using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AssetProcessor.ModelConversion.Core;
using AssetProcessor.ModelConversion.Pipeline;
using AssetProcessor.ModelConversion.Settings;
using NLog;
using Microsoft.Win32;
using Ookii.Dialogs.Wpf;

namespace AssetProcessor.ViewModels {
    /// <summary>
    /// ViewModel для окна конвертации моделей
    /// </summary>
    public partial class ModelConversionViewModel : ObservableObject {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        [ObservableProperty]
        private GlobalModelConversionSettings _globalSettings;

        [ObservableProperty]
        private ObservableCollection<ModelItemViewModel> _models = new();

        [ObservableProperty]
        private ModelItemViewModel? _selectedModel;

        [ObservableProperty]
        private string _outputDirectory = "output_models";

        [ObservableProperty]
        private bool _isProcessing;

        [ObservableProperty]
        private string _processingStatus = "";

        [ObservableProperty]
        private int _processingProgress;

        [ObservableProperty]
        private string _fbx2glTFPath = "FBX2glTF-windows-x64.exe";

        [ObservableProperty]
        private string _gltfPackPath = "gltfpack.exe";

        public ModelConversionViewModel() {
            _globalSettings = ModelConversionSettingsManager.LoadSettings();
            _outputDirectory = _globalSettings.DefaultOutputDirectory;
            _fbx2glTFPath = _globalSettings.FBX2glTFExecutablePath;
            _gltfPackPath = _globalSettings.GltfPackExecutablePath;

            // Загружаем сохраненные модели
            LoadSavedModels();
        }

        /// <summary>
        /// Загружает сохраненные настройки моделей
        /// </summary>
        private void LoadSavedModels() {
            Models.Clear();
            foreach (var settings in GlobalSettings.ModelSettings) {
                if (File.Exists(settings.ModelPath)) {
                    Models.Add(new ModelItemViewModel(settings));
                }
            }
        }

        /// <summary>
        /// Добавить модели
        /// </summary>
        [RelayCommand]
        private void AddModels() {
            var dialog = new OpenFileDialog {
                Title = "Select FBX Models",
                Filter = "FBX Files|*.fbx|All Files|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true) {
                foreach (var filePath in dialog.FileNames) {
                    // Проверяем, не добавлена ли уже эта модель
                    if (Models.Any(m => m.ModelPath == filePath)) {
                        continue;
                    }

                    var settings = CreateDefaultSettings(filePath);
                    var viewModel = new ModelItemViewModel(settings);
                    Models.Add(viewModel);
                }

                SaveSettings();
            }
        }

        /// <summary>
        /// Удалить выбранную модель
        /// </summary>
        [RelayCommand]
        private void RemoveModel() {
            if (SelectedModel != null) {
                Models.Remove(SelectedModel);
                SaveSettings();
            }
        }

        /// <summary>
        /// Обработать все включенные модели
        /// </summary>
        [RelayCommand]
        private async Task ProcessAllModels() {
            var enabledModels = Models.Where(m => m.IsEnabled).ToList();
            if (enabledModels.Count == 0) {
                MessageBox.Show("No models enabled for processing.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsProcessing = true;
            ProcessingProgress = 0;

            try {
                var pipeline = new ModelConversionPipeline(Fbx2glTFPath, GltfPackPath);

                Directory.CreateDirectory(OutputDirectory);

                var totalModels = enabledModels.Count;
                var processed = 0;

                foreach (var model in enabledModels) {
                    ProcessingStatus = $"Processing {Path.GetFileName(model.ModelPath)}...";

                    var modelName = Path.GetFileNameWithoutExtension(model.ModelPath);
                    var modelOutputDir = Path.Combine(OutputDirectory, modelName);

                    var conversionSettings = model.ConversionSettings.ToModelConversionSettings();

                    var result = await pipeline.ConvertAsync(
                        model.ModelPath,
                        modelOutputDir,
                        conversionSettings
                    );

                    if (result.Success) {
                        Logger.Info($"Successfully processed: {model.ModelPath}");
                        Logger.Info($"  LOD files: {result.LodFiles.Count}");
                        Logger.Info($"  Manifest: {result.ManifestPath}");
                        Logger.Info($"  QA Report: {result.QAReportPath}");
                    } else {
                        Logger.Error($"Failed to process {model.ModelPath}");
                        foreach (var error in result.Errors) {
                            Logger.Error($"  {error}");
                        }
                    }

                    processed++;
                    ProcessingProgress = (int)((processed / (double)totalModels) * 100);
                }

                ProcessingStatus = $"Completed! Processed {processed} models.";
                MessageBox.Show($"Successfully processed {processed} models!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            } catch (Exception ex) {
                Logger.Error(ex, "Error during batch processing");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            } finally {
                IsProcessing = false;
                ProcessingProgress = 0;
            }
        }

        /// <summary>
        /// Обработать выбранную модель
        /// </summary>
        [RelayCommand]
        private async Task ProcessSelectedModel() {
            if (SelectedModel == null) {
                MessageBox.Show("Please select a model to process.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsProcessing = true;
            ProcessingStatus = $"Processing {Path.GetFileName(SelectedModel.ModelPath)}...";

            try {
                var pipeline = new ModelConversionPipeline(Fbx2glTFPath, GltfPackPath);
                Directory.CreateDirectory(OutputDirectory);

                var modelName = Path.GetFileNameWithoutExtension(SelectedModel.ModelPath);
                var modelOutputDir = Path.Combine(OutputDirectory, modelName);

                var conversionSettings = SelectedModel.ConversionSettings.ToModelConversionSettings();

                var result = await pipeline.ConvertAsync(
                    SelectedModel.ModelPath,
                    modelOutputDir,
                    conversionSettings
                );

                if (result.Success) {
                    ProcessingStatus = "Processing completed!";
                    var message = $"Successfully processed model!\n\nOutput: {modelOutputDir}\nLOD files: {result.LodFiles.Count}\nDuration: {result.Duration.TotalSeconds:F2}s";

                    if (!string.IsNullOrEmpty(result.ManifestPath)) {
                        message += $"\n\nManifest: {result.ManifestPath}";
                    }

                    if (!string.IsNullOrEmpty(result.QAReportPath)) {
                        message += $"\nQA Report: {result.QAReportPath}";
                    }

                    MessageBox.Show(message, "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                } else {
                    ProcessingStatus = "Processing failed.";
                    var errorMessage = string.Join("\n", result.Errors);
                    MessageBox.Show($"Failed to process model:\n\n{errorMessage}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            } catch (Exception ex) {
                Logger.Error(ex, "Error processing model");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            } finally {
                IsProcessing = false;
            }
        }

        /// <summary>
        /// Выбрать выходную директорию
        /// </summary>
        [RelayCommand]
        private void SelectOutputDirectory() {
            var dialog = new VistaFolderBrowserDialog {
                Description = "Select Output Directory",
                UseDescriptionForTitle = true
            };

            if (dialog.ShowDialog() == true) {
                OutputDirectory = dialog.SelectedPath;
                GlobalSettings.DefaultOutputDirectory = OutputDirectory;
                SaveSettings();
            }
        }

        /// <summary>
        /// Выбрать путь к FBX2glTF executable
        /// </summary>
        [RelayCommand]
        private void BrowseFbx2glTF() {
            var dialog = new OpenFileDialog {
                Title = "Select FBX2glTF Executable",
                Filter = "Executable Files|*.exe|All Files|*.*",
                CheckFileExists = true
            };

            // Установить начальную директорию на текущий путь, если файл существует
            if (!string.IsNullOrEmpty(Fbx2glTFPath) && File.Exists(Fbx2glTFPath)) {
                dialog.InitialDirectory = Path.GetDirectoryName(Fbx2glTFPath);
                dialog.FileName = Path.GetFileName(Fbx2glTFPath);
            }

            if (dialog.ShowDialog() == true) {
                Fbx2glTFPath = dialog.FileName;
                GlobalSettings.FBX2glTFExecutablePath = Fbx2glTFPath;
                SaveSettings();
                Logger.Info($"FBX2glTF path set to: {Fbx2glTFPath}");
            }
        }

        /// <summary>
        /// Выбрать путь к gltfpack executable
        /// </summary>
        [RelayCommand]
        private void BrowseGltfPack() {
            var dialog = new OpenFileDialog {
                Title = "Select gltfpack Executable",
                Filter = "Executable Files|*.exe|All Files|*.*",
                CheckFileExists = true
            };

            // Установить начальную директорию на текущий путь, если файл существует
            if (!string.IsNullOrEmpty(GltfPackPath) && File.Exists(GltfPackPath)) {
                dialog.InitialDirectory = Path.GetDirectoryName(GltfPackPath);
                dialog.FileName = Path.GetFileName(GltfPackPath);
            }

            if (dialog.ShowDialog() == true) {
                GltfPackPath = dialog.FileName;
                GlobalSettings.GltfPackExecutablePath = GltfPackPath;
                SaveSettings();
                Logger.Info($"gltfpack path set to: {GltfPackPath}");
            }
        }

        /// <summary>
        /// Применить пресет ко всем моделям
        /// </summary>
        [RelayCommand]
        private void ApplyPresetToAll(string presetName) {
            foreach (var model in Models) {
                ApplyPreset(model, presetName);
            }
            SaveSettings();
        }

        /// <summary>
        /// Применить пресет к выбранной модели
        /// </summary>
        [RelayCommand]
        private void ApplyPresetToSelected(string presetName) {
            if (SelectedModel != null) {
                ApplyPreset(SelectedModel, presetName);
                SaveSettings();
            }
        }

        /// <summary>
        /// Применяет пресет к модели
        /// </summary>
        private void ApplyPreset(ModelItemViewModel model, string presetName) {
            switch (presetName) {
                case "Default":
                    var defaultSettings = ModelConversionSettings.CreateDefault();
                    model.ConversionSettings = ModelConversionSettingsData.FromModelConversionSettings(defaultSettings);
                    break;

                case "Production":
                    var prodSettings = ModelConversionSettings.CreateProduction();
                    model.ConversionSettings = ModelConversionSettingsData.FromModelConversionSettings(prodSettings);
                    break;

                case "HighQuality":
                    var hqSettings = ModelConversionSettings.CreateHighQuality();
                    model.ConversionSettings = ModelConversionSettingsData.FromModelConversionSettings(hqSettings);
                    break;

                case "MinSize":
                    var minSizeSettings = ModelConversionSettings.CreateMinSize();
                    model.ConversionSettings = ModelConversionSettingsData.FromModelConversionSettings(minSizeSettings);
                    break;
            }
        }

        /// <summary>
        /// Сохранить настройки
        /// </summary>
        [RelayCommand]
        private void SaveSettings() {
            GlobalSettings.ModelSettings = Models.Select(m => m.ToSettings()).ToList();
            GlobalSettings.DefaultOutputDirectory = OutputDirectory;
            GlobalSettings.FBX2glTFExecutablePath = Fbx2glTFPath;
            GlobalSettings.GltfPackExecutablePath = GltfPackPath;
            ModelConversionSettingsManager.SaveSettings(GlobalSettings);
        }

        /// <summary>
        /// Создает настройки по умолчанию для модели
        /// </summary>
        private ModelConversionSettingsData CreateDefaultSettings(string filePath) {
            return new ModelConversionSettingsData {
                ModelPath = filePath,
                IsEnabled = true,
                GenerateLods = true,
                CompressionMode = CompressionMode.Quantization,
                Quantization = QuantizationSettingsData.CreateDefault(),
                LodChain = LodSettingsData.CreateDefaultChain(),
                GenerateBothTracks = false,
                CleanupIntermediateFiles = true,
                ExcludeTextures = true,
                GenerateManifest = true,
                GenerateQAReport = true
            };
        }
    }

    /// <summary>
    /// ViewModel для отдельной модели в списке
    /// </summary>
    public partial class ModelItemViewModel : ObservableObject {
        [ObservableProperty]
        private string _modelPath = string.Empty;

        [ObservableProperty]
        private ModelConversionSettingsData _conversionSettings = new();

        [ObservableProperty]
        private bool _isEnabled = true;

        public string ModelName => Path.GetFileName(ModelPath);

        public ModelItemViewModel() { }

        public ModelItemViewModel(ModelConversionSettingsData settings) {
            ModelPath = settings.ModelPath;
            ConversionSettings = settings;
            IsEnabled = settings.IsEnabled;
        }

        public ModelConversionSettingsData ToSettings() {
            ConversionSettings.ModelPath = ModelPath;
            ConversionSettings.IsEnabled = IsEnabled;
            return ConversionSettings;
        }
    }
}
