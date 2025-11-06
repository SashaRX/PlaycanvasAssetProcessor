using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureConversion.Pipeline;
using AssetProcessor.TextureConversion.Settings;
using NLog;
using Microsoft.Win32;
using Ookii.Dialogs.Wpf;

namespace AssetProcessor.ViewModels {
    /// <summary>
    /// ViewModel для окна конвертации текстур
    /// </summary>
    public partial class TextureConversionViewModel : ObservableObject {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        [ObservableProperty]
        private GlobalTextureConversionSettings _globalSettings;

        [ObservableProperty]
        private ObservableCollection<TextureItemViewModel> _textures = new();

        [ObservableProperty]
        private TextureItemViewModel? _selectedTexture;

        [ObservableProperty]
        private string _outputDirectory = "output_textures";

        [ObservableProperty]
        private bool _isProcessing;

        [ObservableProperty]
        private string _processingStatus = "";

        [ObservableProperty]
        private int _processingProgress;

        [ObservableProperty]
        private string _toktxPath = "toktx";

        public TextureConversionViewModel() {
            _globalSettings = TextureConversionSettingsManager.LoadSettings();
            _outputDirectory = _globalSettings.DefaultOutputDirectory;
            _toktxPath = string.IsNullOrWhiteSpace(_globalSettings.ToktxExecutablePath)
                ? "toktx"
                : _globalSettings.ToktxExecutablePath;

            // Загружаем сохраненные текстуры
            LoadSavedTextures();
        }

        /// <summary>
        /// Загружает сохраненные настройки текстур
        /// </summary>
        private void LoadSavedTextures() {
            Textures.Clear();
            foreach (var settings in GlobalSettings.TextureSettings) {
                if (File.Exists(settings.TexturePath)) {
                    Textures.Add(new TextureItemViewModel(settings));
                }
            }
        }

        /// <summary>
        /// Добавить текстуры
        /// </summary>
        [RelayCommand]
        private void AddTextures() {
            var dialog = new OpenFileDialog {
                Title = "Select Textures",
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.tga;*.bmp|All Files|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true) {
                foreach (var filePath in dialog.FileNames) {
                    // Проверяем, не добавлена ли уже эта текстура
                    if (Textures.Any(t => t.TexturePath == filePath)) {
                        continue;
                    }

                    var textureType = DetectTextureType(filePath);
                    var settings = CreateDefaultSettings(filePath, textureType);
                    var viewModel = new TextureItemViewModel(settings);
                    Textures.Add(viewModel);
                }

                SaveSettings();
            }
        }

        /// <summary>
        /// Удалить выбранную текстуру
        /// </summary>
        [RelayCommand]
        private void RemoveTexture() {
            if (SelectedTexture != null) {
                Textures.Remove(SelectedTexture);
                SaveSettings();
            }
        }

        /// <summary>
        /// Обработать все включенные текстуры
        /// </summary>
        [RelayCommand]
        private async Task ProcessAllTextures() {
            var enabledTextures = Textures.Where(t => t.IsEnabled).ToList();
            if (enabledTextures.Count == 0) {
                MessageBox.Show("No textures enabled for processing.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsProcessing = true;
            ProcessingProgress = 0;

            try {
                // Извлекаем директорию из пути к toktx.exe для загрузки ktx.dll
                string? ktxDllDirectory = null;
                if (!string.IsNullOrWhiteSpace(ToktxPath) && Path.IsPathRooted(ToktxPath)) {
                    ktxDllDirectory = Path.GetDirectoryName(ToktxPath);
                }

                var pipeline = new TextureConversionPipeline(ktxDllDirectory);

                Directory.CreateDirectory(OutputDirectory);

                var totalTextures = enabledTextures.Count;
                var processed = 0;

                foreach (var texture in enabledTextures) {
                    ProcessingStatus = $"Processing {Path.GetFileName(texture.TexturePath)}...";

                    var mipProfile = texture.MipProfile.ToMipGenerationProfile(texture.TextureType);
                    var compressionSettings = texture.Compression.ToCompressionSettings(GlobalSettings);

                    var outputFileName = Path.GetFileNameWithoutExtension(texture.TexturePath);
                    var extension = compressionSettings.OutputFormat == OutputFormat.KTX2 ? ".ktx2" : ".basis";
                    var outputPath = Path.Combine(OutputDirectory, outputFileName + extension);

                    string? mipmapDir = null;
                    if (texture.SaveSeparateMipmaps) {
                        mipmapDir = Path.Combine(OutputDirectory, "mipmaps", outputFileName);
                    }

                    // TODO: Add Toksvig support for batch processing
                    // For now, Toksvig is only available through the main window UI panel
                    ToksvigSettings? toksvigSettings = null;

                    var result = await pipeline.ConvertTextureAsync(
                        texture.TexturePath,
                        outputPath,
                        mipProfile,
                        compressionSettings,
                        toksvigSettings,
                        texture.SaveSeparateMipmaps,
                        mipmapDir
                    );

                    if (result.Success) {
                        Logger.Info($"Successfully processed: {texture.TexturePath}");
                        if (!string.IsNullOrEmpty(result.MipmapsSavedPath)) {
                            Logger.Info($"Separate mipmaps saved to: {result.MipmapsSavedPath}");
                        }
                    } else {
                        Logger.Error($"Failed to process {texture.TexturePath}: {result.Error}");
                    }

                    processed++;
                    ProcessingProgress = (int)((processed / (double)totalTextures) * 100);
                }

                ProcessingStatus = $"Completed! Processed {processed} textures.";
                MessageBox.Show($"Successfully processed {processed} textures!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            } catch (Exception ex) {
                Logger.Error(ex, "Error during batch processing");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            } finally {
                IsProcessing = false;
                ProcessingProgress = 0;
            }
        }

        /// <summary>
        /// Обработать выбранную текстуру
        /// </summary>
        [RelayCommand]
        private async Task ProcessSelectedTexture() {
            if (SelectedTexture == null) {
                MessageBox.Show("Please select a texture to process.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsProcessing = true;
            ProcessingStatus = $"Processing {Path.GetFileName(SelectedTexture.TexturePath)}...";

            try {
                // Извлекаем директорию из пути к toktx.exe для загрузки ktx.dll
                string? ktxDllDirectory = null;
                if (!string.IsNullOrWhiteSpace(ToktxPath) && Path.IsPathRooted(ToktxPath)) {
                    ktxDllDirectory = Path.GetDirectoryName(ToktxPath);
                }

                var pipeline = new TextureConversionPipeline(ktxDllDirectory);
                Directory.CreateDirectory(OutputDirectory);

                var mipProfile = SelectedTexture.MipProfile.ToMipGenerationProfile(SelectedTexture.TextureType);
                var compressionSettings = SelectedTexture.Compression.ToCompressionSettings(GlobalSettings);

                var outputFileName = Path.GetFileNameWithoutExtension(SelectedTexture.TexturePath);
                var extension = compressionSettings.OutputFormat == OutputFormat.KTX2 ? ".ktx2" : ".basis";
                var outputPath = Path.Combine(OutputDirectory, outputFileName + extension);

                string? mipmapDir = null;
                if (SelectedTexture.SaveSeparateMipmaps) {
                    mipmapDir = Path.Combine(OutputDirectory, "mipmaps", outputFileName);
                }

                // TODO: Add Toksvig support for single texture conversion
                // For now, Toksvig is only available through the main window UI panel
                ToksvigSettings? toksvigSettings = null;

                var result = await pipeline.ConvertTextureAsync(
                    SelectedTexture.TexturePath,
                    outputPath,
                    mipProfile,
                    compressionSettings,
                    toksvigSettings,
                    SelectedTexture.SaveSeparateMipmaps,
                    mipmapDir
                );

                if (result.Success) {
                    ProcessingStatus = "Processing completed!";
                    var message = $"Successfully processed texture!\n\nOutput: {outputPath}\nMip levels: {result.MipLevels}\nDuration: {result.Duration.TotalSeconds:F2}s";

                    if (!string.IsNullOrEmpty(result.MipmapsSavedPath)) {
                        message += $"\n\nSeparate mipmaps saved to:\n{result.MipmapsSavedPath}";
                    }

                    MessageBox.Show(message, "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                } else {
                    ProcessingStatus = "Processing failed.";
                    MessageBox.Show($"Failed to process texture:\n\n{result.Error}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            } catch (Exception ex) {
                Logger.Error(ex, "Error processing texture");
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
        /// Применить пресет ко всем текстурам
        /// </summary>
        [RelayCommand]
        private void ApplyPresetToAll(string presetName) {
            foreach (var texture in Textures) {
                ApplyPreset(texture, presetName);
            }
            SaveSettings();
        }

        /// <summary>
        /// Применить пресет к выбранной текстуре
        /// </summary>
        [RelayCommand]
        private void ApplyPresetToSelected(string presetName) {
            if (SelectedTexture != null) {
                ApplyPreset(SelectedTexture, presetName);
                SaveSettings();
            }
        }

        /// <summary>
        /// Применяет пресет к текстуре
        /// </summary>
        private void ApplyPreset(TextureItemViewModel texture, string presetName) {
            switch (presetName) {
                case "HighQuality":
                    var highQuality = CompressionSettings.CreateHighQuality();
                    texture.Compression = CompressionSettingsData.FromCompressionSettings(highQuality);
                    break;

                case "Balanced":
                    var balanced = CompressionSettings.CreateETC1SDefault();
                    texture.Compression = CompressionSettingsData.FromCompressionSettings(balanced);
                    break;

                case "SmallSize":
                    var smallSize = CompressionSettings.CreateMinSize();
                    texture.Compression = CompressionSettingsData.FromCompressionSettings(smallSize);
                    break;
            }
        }

        /// <summary>
        /// Сохранить настройки
        /// </summary>
        [RelayCommand]
        private void SaveSettings() {
            GlobalSettings.TextureSettings = Textures.Select(t => t.ToSettings()).ToList();
            GlobalSettings.DefaultOutputDirectory = OutputDirectory;
            GlobalSettings.ToktxExecutablePath = ToktxPath;
            TextureConversionSettingsManager.SaveSettings(GlobalSettings);
        }

        /// <summary>
        /// Определяет тип текстуры по имени файла
        /// </summary>
        private TextureType DetectTextureType(string filePath) {
            var fileName = Path.GetFileNameWithoutExtension(filePath).ToLower();

            if (fileName.Contains("albedo") || fileName.Contains("diffuse") || fileName.Contains("basecolor")) {
                return TextureType.Albedo;
            } else if (fileName.Contains("normal") || fileName.Contains("norm")) {
                return TextureType.Normal;
            } else if (fileName.Contains("rough")) {
                return TextureType.Roughness;
            } else if (fileName.Contains("metal")) {
                return TextureType.Metallic;
            } else if (fileName.Contains("ao") || fileName.Contains("occlusion")) {
                return TextureType.AmbientOcclusion;
            } else if (fileName.Contains("emissive") || fileName.Contains("emit")) {
                return TextureType.Emissive;
            } else if (fileName.Contains("gloss")) {
                return TextureType.Gloss;
            } else if (fileName.Contains("height") || fileName.Contains("disp")) {
                return TextureType.Height;
            }

            return TextureType.Generic;
        }

        /// <summary>
        /// Создает настройки по умолчанию для текстуры
        /// </summary>
        private TextureConversionSettings CreateDefaultSettings(string filePath, TextureType textureType) {
            var profile = MipGenerationProfile.CreateDefault(textureType);
            var compression = CompressionSettings.CreateETC1SDefault();

            return new TextureConversionSettings {
                TexturePath = filePath,
                TextureType = textureType,
                MipProfile = MipProfileSettings.FromMipGenerationProfile(profile),
                Compression = CompressionSettingsData.FromCompressionSettings(compression),
                IsEnabled = true,
                SaveSeparateMipmaps = false
            };
        }
    }

    /// <summary>
    /// ViewModel для отдельной текстуры в списке
    /// </summary>
    public partial class TextureItemViewModel : ObservableObject {
        [ObservableProperty]
        private string _texturePath = string.Empty;

        [ObservableProperty]
        private TextureType _textureType;

        [ObservableProperty]
        private MipProfileSettings _mipProfile = new();

        [ObservableProperty]
        private CompressionSettingsData _compression = new();

        [ObservableProperty]
        private bool _isEnabled = true;

        [ObservableProperty]
        private bool _saveSeparateMipmaps;

        public string TextureName => Path.GetFileName(TexturePath);

        public TextureItemViewModel() { }

        public TextureItemViewModel(TextureConversionSettings settings) {
            TexturePath = settings.TexturePath;
            TextureType = settings.TextureType;
            MipProfile = settings.MipProfile;
            Compression = settings.Compression;
            IsEnabled = settings.IsEnabled;
            SaveSeparateMipmaps = settings.SaveSeparateMipmaps;
        }

        public TextureConversionSettings ToSettings() {
            return new TextureConversionSettings {
                TexturePath = TexturePath,
                TextureType = TextureType,
                MipProfile = MipProfile,
                Compression = Compression,
                IsEnabled = IsEnabled,
                SaveSeparateMipmaps = SaveSeparateMipmaps
            };
        }
    }
}
