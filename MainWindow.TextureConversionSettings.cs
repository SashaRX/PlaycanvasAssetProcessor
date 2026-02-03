using AssetProcessor.Resources;
using AssetProcessor.Services;
using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureConversion.Settings;
using AssetProcessor.ViewModels;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace AssetProcessor {
    /// <summary>
    /// Partial class containing texture conversion settings handlers:
    /// - Conversion settings panel event handlers
    /// - Settings loading and saving
    /// - Texture type detection and mapping
    /// </summary>
    public partial class MainWindow {

        #region Texture Conversion Settings Handlers

        private void ConversionSettingsExpander_Expanded(object sender, RoutedEventArgs e) {
            // Settings expanded - could save state if needed
        }

        private void ConversionSettingsExpander_Collapsed(object sender, RoutedEventArgs e) {
            // Settings collapsed - could save state if needed
        }

        private void ConversionSettingsPanel_SettingsChanged(object? sender, EventArgs e) {
            logService.LogInfo($"[ConversionSettingsPanel_SettingsChanged] Event triggered");

            if (TexturesDataGrid.SelectedItem is TextureResource selectedTexture) {
                logService.LogInfo($"[ConversionSettingsPanel_SettingsChanged] Updating settings for texture: {selectedTexture.Name}");
                UpdateTextureConversionSettings(selectedTexture);
                logService.LogInfo($"[ConversionSettingsPanel_SettingsChanged] Settings updated for texture: {selectedTexture.Name}");
            } else {
                logService.LogInfo($"[ConversionSettingsPanel_SettingsChanged] No texture selected, skipping update");
            }

            logService.LogInfo($"[ConversionSettingsPanel_SettingsChanged] Event handler completed");
        }

        private void UpdateTextureConversionSettings(TextureResource texture) {
            try {
                // Get project ID
                int projectId = 0;
                if (!string.IsNullOrEmpty(viewModel.SelectedProjectId)) {
                    int.TryParse(viewModel.SelectedProjectId, out projectId);
                }
                if (projectId <= 0) return;

                // Build TextureSettings from UI panel
                var compression = ConversionSettingsPanel.GetCompressionSettings();
                var mipProfile = ConversionSettingsPanel.GetMipProfileSettings();
                var histogramSettings = ConversionSettingsPanel.GetHistogramSettings();

                // Don't update CompressionFormat here - it shows actual compression from KTX2 file,
                // not the intended settings from the UI panel
                texture.PresetName = ConversionSettingsPanel.PresetName ?? "(Custom)";

                var settings = new TextureSettings {
                    PresetName = ConversionSettingsPanel.PresetName,

                    // Compression
                    CompressionFormat = compression.CompressionFormat.ToString(),
                    ColorSpace = compression.ColorSpace.ToString(),
                    CompressionLevel = compression.CompressionLevel,
                    QualityLevel = compression.QualityLevel,
                    UASTCQuality = compression.UASTCQuality,
                    UseUASTCRDO = compression.UseUASTCRDO,
                    UASTCRDOQuality = compression.UASTCRDOQuality,
                    UseETC1SRDO = compression.UseETC1SRDO,
                    ETC1SRDOLambda = 1.0f,
                    KTX2Supercompression = compression.KTX2Supercompression.ToString(),
                    KTX2ZstdLevel = compression.KTX2ZstdLevel,

                    // Mipmaps
                    GenerateMipmaps = compression.GenerateMipmaps,
                    UseCustomMipmaps = compression.UseCustomMipmaps,
                    FilterType = mipProfile?.Filter.ToString() ?? "Kaiser",
                    ApplyGammaCorrection = mipProfile?.ApplyGammaCorrection ?? true,
                    Gamma = mipProfile?.Gamma ?? 2.2f,
                    NormalizeNormals = mipProfile?.NormalizeNormals ?? false,

                    // Normal map
                    ConvertToNormalMap = compression.ConvertToNormalMap,
                    NormalizeVectors = compression.NormalizeVectors,

                    // Advanced
                    PerceptualMode = compression.PerceptualMode,
                    SeparateAlpha = compression.SeparateAlpha,
                    ForceAlphaChannel = compression.ForceAlphaChannel,
                    RemoveAlphaChannel = compression.RemoveAlphaChannel,
                    WrapMode = compression.WrapMode.ToString(),

                    // Histogram
                    HistogramEnabled = histogramSettings != null && histogramSettings.Mode != HistogramMode.Off,
                    HistogramMode = histogramSettings?.Mode.ToString() ?? "Off",
                    HistogramQuality = histogramSettings?.Quality.ToString() ?? "HighQuality",
                    HistogramChannelMode = histogramSettings?.ChannelMode.ToString() ?? "PerChannel",
                    HistogramPercentileLow = histogramSettings?.PercentileLow ?? 5.0f,
                    HistogramPercentileHigh = histogramSettings?.PercentileHigh ?? 95.0f,
                    HistogramKneeWidth = histogramSettings?.KneeWidth ?? 0.02f
                };

                // Delegate to ViewModel for saving
                viewModel.ConversionSettings.SaveSettingsCommand.Execute(new SettingsSaveRequest {
                    Texture = texture,
                    Settings = settings,
                    ProjectId = projectId
                });

                logService.LogInfo($"Updated conversion settings for {texture.Name}");
            } catch (Exception ex) {
                logService.LogError($"Error updating conversion settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Загружает сохранённые настройки текстуры в UI панель
        /// </summary>
        private void LoadSavedSettingsToUI(TextureSettings saved) {
            try {
                // Устанавливаем флаг загрузки чтобы не триггерить SettingsChanged
                ConversionSettingsPanel.BeginLoadingSettings();

                // Preset
                if (!string.IsNullOrEmpty(saved.PresetName)) {
                    ConversionSettingsPanel.SetPresetSilently(saved.PresetName);
                } else {
                    ConversionSettingsPanel.SetPresetSilently("Custom");
                }

                // Compression format
                if (Enum.TryParse<CompressionFormat>(saved.CompressionFormat, true, out var format)) {
                    ConversionSettingsPanel.CompressionFormatComboBox.SelectedItem = format;
                }

                // Color space
                if (Enum.TryParse<ColorSpace>(saved.ColorSpace, true, out var colorSpace)) {
                    ConversionSettingsPanel.ColorSpaceComboBox.SelectedItem = colorSpace;
                }

                // ETC1S settings
                ConversionSettingsPanel.CompressionLevelSlider.Value = saved.CompressionLevel;
                ConversionSettingsPanel.ETC1SQualitySlider.Value = saved.QualityLevel;
                ConversionSettingsPanel.UseETC1SRDOCheckBox.IsChecked = saved.UseETC1SRDO;

                // UASTC settings
                ConversionSettingsPanel.UASTCQualitySlider.Value = saved.UASTCQuality;
                ConversionSettingsPanel.UseUASTCRDOCheckBox.IsChecked = saved.UseUASTCRDO;
                ConversionSettingsPanel.UASTCRDOLambdaSlider.Value = saved.UASTCRDOQuality;

                // Supercompression
                if (Enum.TryParse<KTX2SupercompressionType>(saved.KTX2Supercompression, true, out var supercomp)) {
                    ConversionSettingsPanel.KTX2SupercompressionComboBox.SelectedItem = supercomp;
                }
                ConversionSettingsPanel.ZstdLevelSlider.Value = saved.KTX2ZstdLevel;

                // Mipmaps
                ConversionSettingsPanel.GenerateMipmapsCheckBox.IsChecked = saved.GenerateMipmaps;
                ConversionSettingsPanel.CustomMipmapsCheckBox.IsChecked = saved.UseCustomMipmaps;

                if (Enum.TryParse<FilterType>(saved.FilterType, true, out var filter)) {
                    ConversionSettingsPanel.MipFilterComboBox.SelectedItem = filter;
                }

                ConversionSettingsPanel.ApplyGammaCorrectionCheckBox.IsChecked = saved.ApplyGammaCorrection;
                ConversionSettingsPanel.NormalizeNormalsCheckBox.IsChecked = saved.NormalizeNormals;

                // Normal map
                ConversionSettingsPanel.ConvertToNormalMapCheckBox.IsChecked = saved.ConvertToNormalMap;
                ConversionSettingsPanel.NormalizeVectorsCheckBox.IsChecked = saved.NormalizeVectors;

                // Advanced
                ConversionSettingsPanel.PerceptualModeCheckBox.IsChecked = saved.PerceptualMode;
                ConversionSettingsPanel.ForceAlphaCheckBox.IsChecked = saved.ForceAlphaChannel;
                ConversionSettingsPanel.RemoveAlphaCheckBox.IsChecked = saved.RemoveAlphaChannel;

                if (Enum.TryParse<WrapMode>(saved.WrapMode, true, out var wrapMode)) {
                    ConversionSettingsPanel.WrapModeComboBox.SelectedItem = wrapMode;
                }

                // Histogram
                ConversionSettingsPanel.EnableHistogramCheckBox.IsChecked = saved.HistogramEnabled;
                if (saved.HistogramEnabled) {
                    if (Enum.TryParse<HistogramQuality>(saved.HistogramQuality, true, out var hquality)) {
                        ConversionSettingsPanel.HistogramQualityComboBox.SelectedItem = hquality;
                    }
                    if (Enum.TryParse<HistogramChannelMode>(saved.HistogramChannelMode, true, out var hchannel)) {
                        ConversionSettingsPanel.HistogramChannelModeComboBox.SelectedItem = hchannel;
                    }
                    ConversionSettingsPanel.HistogramPercentileLowSlider.Value = saved.HistogramPercentileLow;
                    ConversionSettingsPanel.HistogramPercentileHighSlider.Value = saved.HistogramPercentileHigh;
                }

                logService.LogInfo($"Loaded saved settings to UI: Format={saved.CompressionFormat}, Quality={saved.QualityLevel}");
            } catch (Exception ex) {
                logService.LogError($"Error loading saved settings to UI: {ex.Message}");
            } finally {
                ConversionSettingsPanel.EndLoadingSettings();
            }
        }

        private void LoadTextureConversionSettings(TextureResource texture) {
            // Delegate to ViewModel - it will raise SettingsLoaded event
            // which is handled by OnConversionSettingsLoaded
            int projectId = 0;
            if (!string.IsNullOrEmpty(viewModel.SelectedProjectId)) {
                int.TryParse(viewModel.SelectedProjectId, out projectId);
            }

            viewModel.ConversionSettings.LoadSettingsForTextureCommand.Execute(new SettingsLoadRequest {
                Texture = texture,
                ProjectId = projectId
            });
        }

        // Initialize compression format and preset for texture without updating UI panel
        private void InitializeTextureConversionSettings(TextureResource texture) {
            // Mark as initialized first to prevent re-entry on scroll
            texture.IsConversionSettingsInitialized = true;

            // Базовая инициализация для текстуры - без тяжелых операций чтения
            var textureType = TextureResource.DetermineTextureType(texture.Name ?? "");
            var profile = MipGenerationProfile.CreateDefault(
                MapTextureTypeToCore(textureType));
            var compression = CompressionSettings.CreateETC1SDefault();

            // CompressionFormat is only set when texture is actually compressed
            // (from KTX2 metadata or after compression process)

            // Auto-detect preset by filename if not already set
            if (string.IsNullOrEmpty(texture.PresetName)) {
                var matchedPreset = cachedPresetManager.FindPresetByFileName(texture.Name ?? "");
                texture.PresetName = matchedPreset?.Name ?? "";
            }

            // Проверка наличие сжатого файла - запускаем асинхронно чтобы не блокировать UI
            if (!string.IsNullOrEmpty(texture.Path) && texture.CompressedSize == 0) {
                // Используем TryAdd для предотвращения повторной проверки одной текстуры
                var lockObject = new object();
                if (texturesBeingChecked.TryAdd(texture.Path, lockObject)) {
                    // Запускаем проверку только если CompressedSize еще не установлен
                    Task.Run(() => {
                        try {
                            if (File.Exists(texture.Path)) {
                                var sourceDir = Path.GetDirectoryName(texture.Path);
                                var sourceFileName = Path.GetFileNameWithoutExtension(texture.Path);

                                if (!string.IsNullOrEmpty(sourceDir) && !string.IsNullOrEmpty(sourceFileName)) {
                                    // Check for .ktx2 file first
                                    var ktx2Path = Path.Combine(sourceDir, sourceFileName + ".ktx2");
                                    if (File.Exists(ktx2Path)) {
                                        var fileInfo = new FileInfo(ktx2Path);
                                        // Read KTX2 header to get mip levels and compression format
                                        int mipLevels = 0;
                                        string? compressionFormat = null;
                                        try {
                                            using var stream = File.OpenRead(ktx2Path);
                                            using var reader = new BinaryReader(stream);
                                            // KTX2 header structure:
                                            // Bytes 12-15: vkFormat (uint32) - 0 means Basis Universal
                                            // Bytes 40-43: levelCount (uint32)
                                            // Bytes 44-47: supercompressionScheme (uint32)
                                            reader.BaseStream.Seek(12, SeekOrigin.Begin);
                                            uint vkFormat = reader.ReadUInt32();

                                            reader.BaseStream.Seek(40, SeekOrigin.Begin);
                                            mipLevels = (int)reader.ReadUInt32();
                                            uint supercompression = reader.ReadUInt32();

                                            // Only set compression format for Basis Universal textures (vkFormat = 0)
                                            if (vkFormat == 0) {
                                                // supercompressionScheme: 1=BasisLZ(ETC1S), 0/2=UASTC(None/Zstd)
                                                compressionFormat = supercompression == 1 ? "ETC1S" : "UASTC";
                                            }
                                            // vkFormat != 0 means raw texture format, no Basis compression
                                        } catch {
                                            // Ignore header read errors
                                        }
                                        Dispatcher.InvokeAsync(() => {
                                            texture.CompressedSize = fileInfo.Length;
                                            if (mipLevels > 0) {
                                                texture.MipmapCount = mipLevels;
                                            }
                                            if (compressionFormat != null) {
                                                texture.CompressionFormat = compressionFormat;
                                            }
                                        });
                                    } else {
                                        // Check for .basis file as fallback
                                        var basisPath = Path.Combine(sourceDir, sourceFileName + ".basis");
                                        if (File.Exists(basisPath)) {
                                            var fileInfo = new FileInfo(basisPath);
                                            Dispatcher.InvokeAsync(() => {
                                                texture.CompressedSize = fileInfo.Length;
                                            });
                                        }
                                    }
                                }
                            }
                        } catch {
                            // Игнорируем ошибки при проверке файлов - это не критично для функционала
                        } finally {
                            // Удаляем текстуру из трекинга после завершения проверки
                            texturesBeingChecked.TryRemove(texture.Path, out _);
                        }
                    });
                }
            }
        }

        private TextureType MapTextureTypeToCore(string textureType) {
            return textureType.ToLower() switch {
                "albedo" => TextureType.Albedo,
                "normal" => TextureType.Normal,
                "roughness" => TextureType.Roughness,
                "metallic" => TextureType.Metallic,
                "ao" => TextureType.AmbientOcclusion,
                "emissive" => TextureType.Emissive,
                "gloss" => TextureType.Gloss,
                "height" => TextureType.Height,
                _ => TextureType.Generic
            };
        }

        #endregion
    }
}
