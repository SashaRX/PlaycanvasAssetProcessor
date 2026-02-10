using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Media.Imaging;
using Assimp;
using HelixToolkit.Wpf;
using AssetProcessor.ModelConversion.Core;
using AssetProcessor.ModelConversion.Viewer;
using AssetProcessor.ModelConversion.Settings;
using AssetProcessor.Resources;
using AssetProcessor.Helpers;
using NLog;

namespace AssetProcessor {
    /// <summary>
    /// Partial class for GLB LOD viewer:
    /// - Fields, init/cleanup
    /// - TryLoadGlbLodAsync (main entry point)
    /// - LoadGlbModelToViewport
    /// - LOD UI management (slider, show/hide, source type switching)
    /// - Texture loading helpers
    /// Rendering methods are in MainWindow.GlbLod.Rendering.cs
    /// </summary>
    public partial class MainWindow {
        private static readonly Logger LodLogger = LogManager.GetCurrentClassLogger();
        private SharpGlbLoader? _sharpGlbLoader;
        private Dictionary<LodLevel, SharpGlbLoader.GlbData> _lodGlbData = new();
        private Dictionary<LodLevel, GlbLodHelper.LodInfo> _currentLodInfos = new();
        private Dictionary<LodLevel, GlbQuantizationAnalyzer.UVQuantizationInfo> _lodQuantizationInfos = new();
        private ObservableCollection<LodDisplayInfo> _lodDisplayItems = new();
        private bool _isGlbViewerActive = false;
        private LodLevel _currentLod = LodLevel.LOD0;
        private string? _currentFbxPath;
        private ImageBrush? _cachedAlbedoBrush;

        private void InitializeGlbLodComponents() {
            LodLogger.Info("GLB LOD components initialized");
        }

        private void CleanupGlbViewer() {
            try {
                _lodGlbData.Clear();
                _currentLodInfos.Clear();
                _lodQuantizationInfos.Clear();
                _lodDisplayItems.Clear();
                _isGlbViewerActive = false;
                _currentFbxPath = null;
                _cachedAlbedoBrush = null;
                _sharpGlbLoader?.Dispose();
            } catch (Exception ex) {
                LodLogger.Error(ex, "Failed to cleanup GLB viewer");
            }
        }

        #region Texture Loading Helpers

        private ImageBrush? FindAndLoadAlbedoTexture(string fbxPath) {
            try {
                var modelName = Path.GetFileNameWithoutExtension(fbxPath);

                var materialNames = new[] {
                    modelName + "_mat",
                    modelName,
                    modelName.ToLowerInvariant() + "_mat",
                    modelName.ToLowerInvariant()
                };

                MaterialResource? material = null;
                foreach (var matName in materialNames) {
                    material = viewModel.Materials.FirstOrDefault(m =>
                        string.Equals(m.Name, matName, StringComparison.OrdinalIgnoreCase));
                    if (material != null) break;
                }

                if (material == null) return null;

                var diffuseMapId = material.DiffuseMapId;
                if (!diffuseMapId.HasValue) return null;

                var texture = viewModel.Textures.FirstOrDefault(t => t.ID == diffuseMapId.Value);
                if (texture == null || string.IsNullOrEmpty(texture.Path) || !File.Exists(texture.Path))
                    return null;

                return LoadTextureAsBrush(texture.Path);
            } catch (Exception) {
                return null;
            }
        }

        private ImageBrush? LoadTextureAsBrush(string texturePath) {
            try {
                var imageBytes = File.ReadAllBytes(texturePath);
                return CreateBrushFromBytes(imageBytes);
            } catch (Exception) {
                return null;
            }
        }

        private ImageBrush? CreateBrushFromBytes(byte[] imageBytes) {
            try {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = new MemoryStream(imageBytes);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                var brush = new ImageBrush(bitmap) {
                    ViewportUnits = BrushMappingMode.Absolute,
                    TileMode = TileMode.Tile,
                    Viewport = new Rect(0, 0, 1, 1),
                    ViewboxUnits = BrushMappingMode.RelativeToBoundingBox
                };

                return brush;
            } catch (Exception) {
                return null;
            }
        }

        private byte[]? LoadAlbedoTextureBytes(string fbxPath) {
            try {
                var modelName = Path.GetFileNameWithoutExtension(fbxPath);

                var materialNames = new[] {
                    modelName + "_mat",
                    modelName,
                    modelName.ToLowerInvariant() + "_mat",
                    modelName.ToLowerInvariant()
                };

                MaterialResource? material = null;
                foreach (var matName in materialNames) {
                    material = viewModel.Materials.FirstOrDefault(m =>
                        string.Equals(m.Name, matName, StringComparison.OrdinalIgnoreCase));
                    if (material != null) break;
                }

                if (material == null) return null;

                var diffuseMapId = material.DiffuseMapId;
                if (!diffuseMapId.HasValue) return null;

                var texture = viewModel.Textures.FirstOrDefault(t => t.ID == diffuseMapId.Value);
                if (texture == null || string.IsNullOrEmpty(texture.Path) || !File.Exists(texture.Path))
                    return null;

                return File.ReadAllBytes(texture.Path);
            } catch (Exception) {
                return null;
            }
        }

        #endregion

        public class LodDisplayInfo : INotifyPropertyChanged {
            private bool _isSelected;

            public LodLevel Level { get; set; }
            public string LodName => $"LOD{(int)Level}";
            public int TriangleCount { get; set; }
            public int VertexCount { get; set; }
            public string FileSizeFormatted { get; set; } = string.Empty;

            public bool IsSelected {
                get => _isSelected;
                set {
                    if (_isSelected != value) {
                        _isSelected = value;
                        OnPropertyChanged(nameof(IsSelected));
                    }
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            protected void OnPropertyChanged(string propertyName) {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        #region LOD Loading

        private async Task TryLoadGlbLodAsync(string fbxPath) {
            try {
                _currentFbxPath = fbxPath;

                var initResult = await Task.Run(() => {
                    var lodInfos = GlbLodHelper.FindGlbLodFiles(fbxPath);

                    if (lodInfos.Count == 0) {
                        return (LodInfos: lodInfos, LodFilePaths: (Dictionary<LodLevel, string>?)null, GltfPackPath: (string?)null);
                    }

                    var modelConversionSettings = ModelConversionSettingsManager.LoadSettings();
                    var gltfPackPath = string.IsNullOrWhiteSpace(modelConversionSettings.GltfPackExecutablePath)
                        ? "gltfpack.exe"
                        : modelConversionSettings.GltfPackExecutablePath;

                    var lodFilePaths = GlbLodHelper.GetLodFilePaths(fbxPath);

                    return (LodInfos: lodInfos, LodFilePaths: lodFilePaths, GltfPackPath: gltfPackPath);
                });

                _currentLodInfos = initResult.LodInfos;

                if (_currentLodInfos.Count == 0) {
                    HideGlbLodUI();
                    return;
                }

                ShowGlbLodUI();
                UpdateLodSliderLimits();

                if (_sharpGlbLoader == null) {
                    _sharpGlbLoader = new SharpGlbLoader(initResult.GltfPackPath!);
                } else {
                    _sharpGlbLoader.ClearCache();
                }

                _lodGlbData.Clear();
                _lodQuantizationInfos.Clear();

                var loadResult = await Task.Run(() => {
                    var lodData = new Dictionary<LodLevel, SharpGlbLoader.GlbData>();
                    var quantInfos = new Dictionary<LodLevel, GlbQuantizationAnalyzer.UVQuantizationInfo>();

                    foreach (var kvp in initResult.LodFilePaths!) {
                        var lodLevel = kvp.Key;
                        var glbPath = kvp.Value;

                        var quantInfo = GlbQuantizationAnalyzer.AnalyzeQuantization(glbPath);
                        quantInfos[lodLevel] = quantInfo;

                        var glbData = _sharpGlbLoader!.LoadGlb(glbPath);
                        if (glbData.Success) {
                            lodData[lodLevel] = glbData;
                            LodLogger.Info($"{lodLevel} loaded: {glbData.Meshes.Count} meshes");
                        } else {
                            LodLogger.Error($"Failed to load {lodLevel}: {glbData.Error}");
                        }
                    }

                    var textureBytes = LoadAlbedoTextureBytes(fbxPath);
                    return (LodData: lodData, QuantInfos: quantInfos, TextureBytes: textureBytes);
                });

                _lodGlbData = loadResult.LodData;
                _lodQuantizationInfos = loadResult.QuantInfos;

                if (_lodGlbData.Count == 0) {
                    HideGlbLodUI();
                    return;
                }

                _cachedAlbedoBrush = loadResult.TextureBytes != null
                    ? CreateBrushFromBytes(loadResult.TextureBytes)
                    : null;

                if (_lodGlbData.ContainsKey(LodLevel.LOD0)) {
                    LoadGlbModelToViewport(LodLevel.LOD0, zoomToFit: true);
                } else if (_lodGlbData.Count > 0) {
                    var firstLod = _lodGlbData.Keys.First();
                    LoadGlbModelToViewport(firstLod, zoomToFit: true);
                }

                _isGlbViewerActive = true;
                SelectLod(LodLevel.LOD0);

            } catch (Exception ex) {
                LodLogger.Error(ex, "Failed to load GLB LOD files");
                Dispatcher.Invoke(() => { HideGlbLodUI(); });
            }
        }

        private void LoadGlbModelToViewport(LodLevel lodLevel, bool zoomToFit = false) {
            try {
                if (!_lodGlbData.TryGetValue(lodLevel, out var glbData)) {
                    LodLogger.Warn($"LOD {lodLevel} data not found");
                    return;
                }

                var modelsToRemove = viewPort3d.Children.OfType<ModelVisual3D>().ToList();
                foreach (var model in modelsToRemove) {
                    viewPort3d.Children.Remove(model);
                }

                if (!viewPort3d.Children.OfType<DefaultLights>().Any()) {
                    viewPort3d.Children.Add(new DefaultLights());
                }

                var modelGroup = ConvertSharpGlbToWpfModel(glbData);

                var visual3d = new ModelVisual3D { Content = modelGroup };
                viewPort3d.Children.Add(visual3d);

                ApplyViewerSettingsToModel();

                var meshWithUV = glbData.Meshes.FirstOrDefault(m => m.TextureCoordinates.Count > 0);
                if (meshWithUV != null) {
                    UpdateUVImageFromSharpGlb(meshWithUV, lodLevel);
                }

                if (zoomToFit) {
                    viewPort3d.ZoomExtents();
                }

                _currentLod = lodLevel;

            } catch (Exception ex) {
                LodLogger.Error(ex, $"Failed to load GLB LOD {lodLevel} to viewport");
            }
        }

        #endregion

        #region LOD UI Management

        private void ShowGlbLodUI() {
            Dispatcher.Invoke(() => {
                LodControlsPanel.Visibility = Visibility.Visible;
                ModelCurrentLodTextBlock.Visibility = Visibility.Visible;
            });
        }

        private void HideGlbLodUI() {
            Dispatcher.Invoke(() => {
                LodControlsPanel.Visibility = Visibility.Collapsed;
                ModelCurrentLodTextBlock.Visibility = Visibility.Collapsed;

                _currentLodInfos.Clear();
                _lodQuantizationInfos.Clear();
                _lodDisplayItems.Clear();
                _lodGlbData.Clear();
                _isGlbViewerActive = false;
            });
        }

        private void UpdateLodSliderLimits() {
            Dispatcher.Invoke(() => {
                int maxLod = 0;
                for (int i = 3; i >= 0; i--) {
                    if (_currentLodInfos.ContainsKey((LodLevel)i)) {
                        maxLod = i;
                        break;
                    }
                }

                LodSlider.Maximum = maxLod;
                LodSlider.IsEnabled = maxLod > 0;
            });
        }

        private void SelectLod(LodLevel lodLevel) {
            try {
                LoadGlbModelToViewport(lodLevel);

                ModelCurrentLodTextBlock.Text = $"Current LOD: {lodLevel} (GLB)";

                if (_currentLodInfos.TryGetValue(lodLevel, out var lodInfo)) {
                    ModelTrianglesTextBlock.Text = $"Triangles: {lodInfo.TriangleCount:N0}";
                    ModelVerticesTextBlock.Text = $"Vertices: {lodInfo.VertexCount:N0}";
                }

                UpdateLodSliderState(lodLevel);

            } catch (Exception ex) {
                LodLogger.Error(ex, $"Failed to select LOD: {lodLevel}");
            }
        }

        private void UpdateLodSliderState(LodLevel currentLod) {
            int maxLod = 0;
            for (int i = 3; i >= 0; i--) {
                if (_currentLodInfos.ContainsKey((LodLevel)i)) {
                    maxLod = i;
                    break;
                }
            }

            LodSlider.Maximum = maxLod;
            LodSlider.Value = (int)currentLod;
            LodSliderValueText.Text = $"LOD{(int)currentLod}";

            if (_currentLodInfos.TryGetValue(currentLod, out var lodInfo)) {
                LodInfoText.Text = $"△ {lodInfo.TriangleCount:N0}  |  {lodInfo.FileSize / 1024.0:F0} KB";
            } else {
                LodInfoText.Text = "";
            }
        }

        private void LodSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            if (LodSlider == null || LodSliderValueText == null) return;

            int lodIndex = (int)e.NewValue;
            var lodLevel = (LodLevel)lodIndex;

            LodSliderValueText.Text = $"LOD{lodIndex}";

            if (_currentLodInfos != null && _currentLodInfos.TryGetValue(lodLevel, out var lodInfo)) {
                LodInfoText.Text = $"△ {lodInfo.TriangleCount:N0}  |  {lodInfo.FileSize / 1024.0:F0} KB";
            }

            SelectLod(lodLevel);
        }

        private void ModelViewportGridSplitter_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e) {
            if (ModelViewportRow == null) return;

            double oldHeight = ModelViewportRow.ActualHeight;
            double desiredHeight = oldHeight + e.VerticalChange;

            const double minHeight = 150;
            const double maxHeight = 800;

            desiredHeight = Math.Max(minHeight, Math.Min(maxHeight, desiredHeight));
            ModelViewportRow.Height = new GridLength(desiredHeight);

            // Синхронизируем внешнюю строку (Expander)
            double delta = desiredHeight - oldHeight;
            if (ModelPreviewRow != null && delta != 0) {
                double outerHeight = ModelPreviewRow.ActualHeight + delta;
                outerHeight = Math.Max(200, Math.Min(1200, outerHeight));
                ModelPreviewRow.Height = new GridLength(outerHeight);
            }

            e.Handled = true;
        }

        /// <summary>
        /// Внешний Thumb между Model Preview и нижней секцией (UV Maps).
        /// Ресайзит ModelPreviewRow и синхронно ModelViewportRow.
        /// </summary>
        private void ModelPreviewGridSplitter_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e) {
            if (ModelPreviewRow == null || ModelViewportRow == null) return;

            double desiredOuter = ModelPreviewRow.ActualHeight + e.VerticalChange;
            desiredOuter = Math.Max(200, Math.Min(1200, desiredOuter));
            ModelPreviewRow.Height = new GridLength(desiredOuter);

            // Синхронно уменьшаем/увеличиваем вьюпорт
            double desiredViewport = ModelViewportRow.ActualHeight + e.VerticalChange;
            desiredViewport = Math.Max(100, Math.Min(800, desiredViewport));
            ModelViewportRow.Height = new GridLength(desiredViewport);

            e.Handled = true;
        }

        #endregion

        #region Source Type Switching (FBX/GLB)

        private bool _isShowingFbx = false;

        private async void SourceTypeButton_Click(object sender, RoutedEventArgs e) {
            if (sender is Button button && button.Tag is string tagStr) {
                bool showFbx = tagStr == "FBX";

                if (showFbx == _isShowingFbx) return;

                _isShowingFbx = showFbx;

                SourceFbxButton.FontWeight = showFbx ? FontWeights.Bold : FontWeights.Normal;
                SourceGlbButton.FontWeight = showFbx ? FontWeights.Normal : FontWeights.Bold;

                LodSlider.IsEnabled = !showFbx;

                if (showFbx) {
                    await UiAsyncHelper.ExecuteAsync(
                        () => SwitchToFbxViewAsync(),
                        nameof(SourceTypeButton_Click));
                } else {
                    SwitchToGlbView();
                }
            }
        }

        private async Task SwitchToFbxViewAsync() {
            if (string.IsNullOrEmpty(_currentFbxPath)) {
                LodLogger.Warn("No FBX path available for switching");
                return;
            }

            ModelCurrentLodTextBlock.Text = "Current: FBX (Original)";
            await LoadFbxModelDirectlyAsync(_currentFbxPath);
        }

        private void SwitchToGlbView() {
            try {
                LoadGlbModelToViewport(_currentLod);
            } catch (Exception ex) {
                LodLogger.Error(ex, "Failed to switch to GLB view");
            }
        }

        private async Task LoadFbxModelDirectlyAsync(string fbxPath) {
            try {
                var loadResult = await Task.Run(() => {
                    byte[]? textureBytes = null;
                    if (_cachedAlbedoBrush == null) {
                        textureBytes = LoadAlbedoTextureBytes(fbxPath);
                    }

                    using var context = new AssimpContext();
                    var scene = context.ImportFile(fbxPath,
                        PostProcessSteps.Triangulate |
                        PostProcessSteps.GenerateNormals |
                        PostProcessSteps.FlipUVs);

                    if (scene == null || !scene.HasMeshes) {
                        return (Scene: (Scene?)null, TextureBytes: textureBytes, Error: "Failed to load FBX: no meshes");
                    }

                    return (Scene: scene, TextureBytes: textureBytes, Error: (string?)null);
                });

                if (loadResult.Scene == null) {
                    LodLogger.Error(loadResult.Error ?? "Unknown error loading FBX");
                    return;
                }

                if (_cachedAlbedoBrush == null && loadResult.TextureBytes != null) {
                    _cachedAlbedoBrush = CreateBrushFromBytes(loadResult.TextureBytes);
                }

                var scene = loadResult.Scene;

                var modelsToRemove = viewPort3d.Children.OfType<ModelVisual3D>().ToList();
                foreach (var model in modelsToRemove) {
                    viewPort3d.Children.Remove(model);
                }

                if (!viewPort3d.Children.OfType<DefaultLights>().Any()) {
                    viewPort3d.Children.Add(new DefaultLights());
                }

                var modelGroup = ConvertAssimpSceneToWpfModel(scene);

                var visual3d = new ModelVisual3D { Content = modelGroup };
                viewPort3d.Children.Add(visual3d);

                ApplyViewerSettingsToModel();

                var meshWithUV = scene.Meshes.FirstOrDefault(m => m.HasTextureCoords(0));
                if (meshWithUV != null) {
                    UpdateUVImage(meshWithUV, flipV: true);
                }

            } catch (Exception ex) {
                LodLogger.Error(ex, $"Failed to load FBX model: {fbxPath}");
            }
        }

        #endregion
    }
}
