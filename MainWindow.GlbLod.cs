using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Assimp;
using HelixToolkit.Wpf;
using AssetProcessor.ModelConversion.Core;
using AssetProcessor.ModelConversion.Viewer;
using AssetProcessor.ModelConversion.Settings;
using AssetProcessor.Resources;
using NLog;

namespace AssetProcessor {
    /// <summary>
    /// MainWindow partial class для работы с GLB LOD просмотром
    /// </summary>
    public partial class MainWindow {
        private static readonly Logger LodLogger = LogManager.GetCurrentClassLogger();
        private GlbLoader? _glbLoader;
        private Dictionary<LodLevel, Scene> _lodScenes = new();
        private Dictionary<LodLevel, GlbLodHelper.LodInfo> _currentLodInfos = new();
        private Dictionary<LodLevel, GlbQuantizationAnalyzer.UVQuantizationInfo> _lodQuantizationInfos = new();
        private ObservableCollection<LodDisplayInfo> _lodDisplayItems = new();
        private bool _isGlbViewerActive = false;
        private LodLevel _currentLod = LodLevel.LOD0;
        private string? _currentFbxPath;  // Путь к FBX для переключения Source Type

        /// <summary>
        /// Инициализация GLB LOD компонентов (вызывается из конструктора MainWindow)
        /// </summary>
        private void InitializeGlbLodComponents() {
            // Инициализация выполнена - коллекция создана в поле
            LodLogger.Info("GLB LOD components initialized");
        }

        /// <summary>
        /// Очищает GLB viewer ресурсы при закрытии окна
        /// </summary>
        private void CleanupGlbViewer() {
            try {
                _lodScenes.Clear();
                _currentLodInfos.Clear();
                _lodQuantizationInfos.Clear();
                _lodDisplayItems.Clear();
                _isGlbViewerActive = false;
                _currentFbxPath = null;
                _glbLoader?.ClearCache();
                _glbLoader?.Dispose();
                LodLogger.Info("GLB viewer cleaned up");
            } catch (Exception ex) {
                LodLogger.Error(ex, "Failed to cleanup GLB viewer");
            }
        }

        /// <summary>
        /// Класс для отображения LOD информации в DataGrid
        /// </summary>
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

        /// <summary>
        /// Проверяет наличие GLB LOD файлов для выбранной модели и загружает их
        /// </summary>
        private async Task TryLoadGlbLodAsync(string fbxPath) {
            try {
                LodLogger.Info($"Checking for GLB LOD files: {fbxPath}");
                _currentFbxPath = fbxPath;  // Сохраняем для переключения Source Type

                // Ищем GLB LOD файлы
                _currentLodInfos = GlbLodHelper.FindGlbLodFiles(fbxPath);

                if (_currentLodInfos.Count == 0) {
                    LodLogger.Info("No GLB LOD files found, using FBX viewer");
                    HideGlbLodUI();
                    return;
                }

                LodLogger.Info($"Found {_currentLodInfos.Count} GLB LOD files");

                // Показываем LOD UI
                ShowGlbLodUI();

                // Заполняем DataGrid
                PopulateLodDataGrid();

                // Создаём GlbLoader если его еще нет
                if (_glbLoader == null) {
                    LodLogger.Info("Creating GlbLoader");

                    var modelConversionSettings = ModelConversionSettingsManager.LoadSettings();
                    var gltfPackPath = string.IsNullOrWhiteSpace(modelConversionSettings.GltfPackExecutablePath)
                        ? "gltfpack.exe"
                        : modelConversionSettings.GltfPackExecutablePath;

                    LodLogger.Info($"  gltfpack for meshopt decompression: {gltfPackPath}");
                    _glbLoader = new GlbLoader(gltfPackPath);
                }

                // Загружаем все LOD сцены и анализируем квантование
                _lodScenes.Clear();
                _lodQuantizationInfos.Clear();
                var lodFilePaths = GlbLodHelper.GetLodFilePaths(fbxPath);

                foreach (var kvp in lodFilePaths) {
                    var lodLevel = kvp.Key;
                    var glbPath = kvp.Value;

                    LodLogger.Info($"  Loading {lodLevel}: {glbPath}");

                    // Анализируем квантование ПЕРЕД загрузкой (из оригинального GLB)
                    var quantInfo = GlbQuantizationAnalyzer.AnalyzeQuantization(glbPath);
                    _lodQuantizationInfos[lodLevel] = quantInfo;

                    var scene = await _glbLoader.LoadGlbAsync(glbPath);
                    if (scene != null) {
                        _lodScenes[lodLevel] = scene;
                    }
                }

                LodLogger.Info($"Loaded {_lodScenes.Count} LOD scenes");

                // Отображаем LOD0 в существующем viewport
                // zoomToFit=true только при первой загрузке модели
                if (_lodScenes.ContainsKey(LodLevel.LOD0)) {
                    LoadGlbModelToViewport(LodLevel.LOD0, zoomToFit: true);
                } else if (_lodScenes.Count > 0) {
                    var firstLod = _lodScenes.Keys.First();
                    LoadGlbModelToViewport(firstLod, zoomToFit: true);
                }

                _isGlbViewerActive = true;

                // Выбираем LOD0 по умолчанию
                SelectLod(LodLevel.LOD0);

                LodLogger.Info("GLB LOD preview loaded successfully");

            } catch (Exception ex) {
                LodLogger.Error(ex, "Failed to load GLB LOD files");
                HideGlbLodUI();
            }
        }

        /// <summary>
        /// Загружает GLB модель в существующий viewPort3d
        /// </summary>
        /// <param name="zoomToFit">Выполнить ZoomExtents после загрузки (только при первой загрузке модели)</param>
        private void LoadGlbModelToViewport(LodLevel lodLevel, bool zoomToFit = false) {
            try {
                if (!_lodScenes.TryGetValue(lodLevel, out var scene)) {
                    LodLogger.Warn($"LOD {lodLevel} scene not found");
                    return;
                }

                LodLogger.Info($"Loading GLB LOD{(int)lodLevel} to viewport: {scene.MeshCount} meshes");

                // Очищаем только модели, оставляя освещение (аналогично LoadModel)
                var modelsToRemove = viewPort3d.Children.OfType<ModelVisual3D>().ToList();
                foreach (var model in modelsToRemove) {
                    viewPort3d.Children.Remove(model);
                }

                // Убеждаемся что есть освещение
                if (!viewPort3d.Children.OfType<DefaultLights>().Any()) {
                    viewPort3d.Children.Add(new DefaultLights());
                }

                // NOTE: UV scale correction is NOT needed!
                // GlbLoader uses gltfpack -noq to decode the GLB, which automatically
                // converts quantized UVs back to proper float values (0-1 range).
                // The quantization analyzer is kept for informational logging only.
                if (_lodQuantizationInfos.TryGetValue(lodLevel, out var quantInfo)) {
                    LodLogger.Info($"  GLB quantization info: IsQuantized={quantInfo.IsQuantized}, " +
                                   $"ComponentType={quantInfo.ComponentType}, Normalized={quantInfo.Normalized}");
                    if (quantInfo.Max != null && quantInfo.Max.Length >= 2) {
                        LodLogger.Info($"  Original UV max: ({quantInfo.Max[0]:F4}, {quantInfo.Max[1]:F4})");
                    }
                    LodLogger.Info($"  UV scale correction NOT applied (gltfpack -noq handles decoding)");
                }

                // Конвертируем Assimp Scene в WPF модель (без UV scale - gltfpack уже декодировал)
                var modelGroup = ConvertAssimpSceneToWpfModel(scene);

                // Добавляем в viewport
                var visual3d = new ModelVisual3D { Content = modelGroup };
                viewPort3d.Children.Add(visual3d);

                // Применяем настройки viewer (wireframe, up vector)
                ApplyViewerSettingsToModel();

                // Обновляем UV preview (берём первую mesh с UV координатами)
                var meshWithUV = scene.Meshes.FirstOrDefault(m => m.HasTextureCoords(0));
                if (meshWithUV != null) {
                    UpdateUVImage(meshWithUV);
                }

                // Центрируем камеру только при первой загрузке модели
                if (zoomToFit) {
                    viewPort3d.ZoomExtents();
                }

                _currentLod = lodLevel;
                LodLogger.Info($"Loaded GLB LOD{(int)lodLevel} successfully");

            } catch (Exception ex) {
                LodLogger.Error(ex, $"Failed to load GLB LOD {lodLevel} to viewport");
            }
        }

        /// <summary>
        /// Конвертирует Assimp Scene в WPF Model3DGroup (аналогично LoadModel)
        /// </summary>
        /// <param name="scene">Assimp Scene</param>
        private Model3DGroup ConvertAssimpSceneToWpfModel(Scene scene) {
            var modelGroup = new Model3DGroup();

            foreach (var mesh in scene.Meshes) {
                LodLogger.Info($"Processing mesh: {mesh.VertexCount} vertices, {mesh.FaceCount} faces, HasUVs={mesh.HasTextureCoords(0)}");
                LodLogger.Info($"  TextureCoordinateChannelCount={mesh.TextureCoordinateChannelCount}");

                // Логируем первые 5 вершин из Assimp для диагностики
                LodLogger.Info($"First 5 vertices from Assimp:");
                for (int i = 0; i < Math.Min(5, mesh.VertexCount); i++) {
                    var v = mesh.Vertices[i];
                    LodLogger.Info($"  Vertex {i}: ({v.X:F4}, {v.Y:F4}, {v.Z:F4})");
                }

                // Создаём геометрию напрямую, без MeshBuilder (он багованный)
                var geometry = new MeshGeometry3D();

                // Вершины и нормали
                for (int i = 0; i < mesh.VertexCount; i++) {
                    var vertex = mesh.Vertices[i];
                    var normal = mesh.Normals[i];
                    geometry.Positions.Add(new Point3D(vertex.X, vertex.Y, vertex.Z));
                    geometry.Normals.Add(new System.Windows.Media.Media3D.Vector3D(normal.X, normal.Y, normal.Z));
                }

                // UV координаты (если есть)
                if (mesh.TextureCoordinateChannelCount > 0 && mesh.HasTextureCoords(0)) {
                    LodLogger.Info($"  Found UV channel 0, copying {mesh.VertexCount} UV coordinates");

                    // Загружаем UV из Assimp (gltfpack -noq уже декодировал квантование)
                    for (int i = 0; i < mesh.VertexCount; i++) {
                        var uv = mesh.TextureCoordinateChannels[0][i];
                        geometry.TextureCoordinates.Add(new Point(uv.X, uv.Y));
                    }

                    // Логируем первые 5 UV для проверки
                    LodLogger.Info($"  First 5 UVs (from decoded GLB):");
                    for (int i = 0; i < Math.Min(5, geometry.TextureCoordinates.Count); i++) {
                        var uv = geometry.TextureCoordinates[i];
                        LodLogger.Info($"    UV {i}: ({uv.X:F4}, {uv.Y:F4})");
                    }
                } else {
                    LodLogger.Info($"  No UV coordinates found (ChannelCount={mesh.TextureCoordinateChannelCount})");
                }

                // Индексы
                for (int i = 0; i < mesh.FaceCount; i++) {
                    var face = mesh.Faces[i];
                    if (face.IndexCount == 3) {
                        geometry.TriangleIndices.Add(face.Indices[0]);
                        geometry.TriangleIndices.Add(face.Indices[1]);
                        geometry.TriangleIndices.Add(face.Indices[2]);
                    }
                }

                LodLogger.Info($"Geometry created: Positions={geometry.Positions.Count}, Normals={geometry.Normals.Count}, TexCoords={geometry.TextureCoordinates.Count}, Indices={geometry.TriangleIndices.Count}");

                // Логируем первые 5 вершин из geometry.Positions для проверки
                LodLogger.Info($"First 5 vertices in geometry.Positions:");
                for (int i = 0; i < Math.Min(5, geometry.Positions.Count); i++) {
                    var p = geometry.Positions[i];
                    LodLogger.Info($"  Position {i}: ({p.X:F4}, {p.Y:F4}, {p.Z:F4})");
                }

                // Create two-sided material with emissive properties for visibility
                var frontMaterial = new DiffuseMaterial(new SolidColorBrush(Colors.LightGray));
                var backMaterial = new DiffuseMaterial(new SolidColorBrush(Colors.Red)); // Red for debugging backfaces
                var emissiveMaterial = new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(40, 40, 40)));

                var materialGroup = new MaterialGroup();
                materialGroup.Children.Add(frontMaterial);
                materialGroup.Children.Add(emissiveMaterial);

                var model = new GeometryModel3D(geometry, materialGroup);
                model.BackMaterial = backMaterial; // Ensure backfaces are visible
                modelGroup.Children.Add(model);

                LodLogger.Info($"Mesh created successfully");
            }

            // Вычисление bounds вручную из всех вершин (modelGroup.Bounds не работает до добавления в viewport)
            var minX = double.MaxValue;
            var minY = double.MaxValue;
            var minZ = double.MaxValue;
            var maxX = double.MinValue;
            var maxY = double.MinValue;
            var maxZ = double.MinValue;

            foreach (var child in modelGroup.Children) {
                if (child is GeometryModel3D geoModel && geoModel.Geometry is MeshGeometry3D mesh) {
                    foreach (var pos in mesh.Positions) {
                        minX = Math.Min(minX, pos.X);
                        minY = Math.Min(minY, pos.Y);
                        minZ = Math.Min(minZ, pos.Z);
                        maxX = Math.Max(maxX, pos.X);
                        maxY = Math.Max(maxY, pos.Y);
                        maxZ = Math.Max(maxZ, pos.Z);
                    }
                }
            }

            var sizeX = maxX - minX;
            var sizeY = maxY - minY;
            var sizeZ = maxZ - minZ;
            var centerOffset = new System.Windows.Media.Media3D.Vector3D(
                -(minX + sizeX / 2),
                -(minY + sizeY / 2),
                -(minZ + sizeZ / 2)
            );

            LodLogger.Info($"Model bounds: min=({minX:F2}, {minY:F2}, {minZ:F2}), max=({maxX:F2}, {maxY:F2}, {maxZ:F2})");
            LodLogger.Info($"Model size: {sizeX:F2} x {sizeY:F2} x {sizeZ:F2}");
            LodLogger.Info($"Center offset: X={centerOffset.X:F2}, Y={centerOffset.Y:F2}, Z={centerOffset.Z:F2}");

            var transformGroup = new Transform3DGroup();
            transformGroup.Children.Add(new TranslateTransform3D(centerOffset));
            modelGroup.Transform = transformGroup;

            return modelGroup;
        }

        /// <summary>
        /// Показывает UI элементы для работы с LOD
        /// </summary>
        private void ShowGlbLodUI() {
            Dispatcher.Invoke(() => {
                LodControlsPanel.Visibility = Visibility.Visible;
                ModelCurrentLodTextBlock.Visibility = Visibility.Visible;
            });
        }

        /// <summary>
        /// Скрывает UI элементы для работы с LOD
        /// </summary>
        private void HideGlbLodUI() {
            Dispatcher.Invoke(() => {
                LodControlsPanel.Visibility = Visibility.Collapsed;
                ModelCurrentLodTextBlock.Visibility = Visibility.Collapsed;

                // Очищаем данные
                _currentLodInfos.Clear();
                _lodQuantizationInfos.Clear();
                _lodDisplayItems.Clear();
                _lodScenes.Clear();
                _isGlbViewerActive = false;
            });
        }

        /// <summary>
        /// Заполняет DataGrid информацией о LOD уровнях
        /// </summary>
        private void PopulateLodDataGrid() {
            Dispatcher.Invoke(() => {
                _lodDisplayItems.Clear();

                foreach (var kvp in _currentLodInfos.OrderBy(x => x.Key)) {
                    var lodInfo = kvp.Value;
                    _lodDisplayItems.Add(new LodDisplayInfo {
                        Level = lodInfo.Level,
                        TriangleCount = lodInfo.TriangleCount,
                        VertexCount = lodInfo.VertexCount,
                        FileSizeFormatted = lodInfo.FileSizeFormatted
                    });
                }

                LodInformationGrid.ItemsSource = _lodDisplayItems;
            });
        }


        /// <summary>
        /// Выбирает конкретный LOD уровень для просмотра
        /// </summary>
        private void SelectLod(LodLevel lodLevel) {
            try {
                LodLogger.Info($"Selecting LOD: {lodLevel}");

                // Загружаем модель LOD в viewport
                LoadGlbModelToViewport(lodLevel);

                // Обновляем UI
                Dispatcher.Invoke(() => {
                    // Обновляем текст Current LOD
                    ModelCurrentLodTextBlock.Text = $"Current LOD: {lodLevel} (GLB)";

                    // Обновляем информацию о модели
                    if (_currentLodInfos.TryGetValue(lodLevel, out var lodInfo)) {
                        ModelTrianglesTextBlock.Text = $"Triangles: {lodInfo.TriangleCount:N0}";
                        ModelVerticesTextBlock.Text = $"Vertices: {lodInfo.VertexCount:N0}";
                    }

                    // Обновляем кнопки (подсвечиваем активную)
                    UpdateLodButtonStates(lodLevel);

                    // Обновляем выделение в DataGrid
                    var selectedItem = _lodDisplayItems.FirstOrDefault(x => x.Level == lodLevel);
                    if (selectedItem != null) {
                        LodInformationGrid.SelectedItem = selectedItem;
                    }
                });

                LodLogger.Info($"LOD {lodLevel} selected successfully");

            } catch (Exception ex) {
                LodLogger.Error(ex, $"Failed to select LOD: {lodLevel}");
            }
        }

        /// <summary>
        /// Обновляет состояние кнопок LOD (подсвечивает активную)
        /// </summary>
        private void UpdateLodButtonStates(LodLevel currentLod) {
            // Обновляем стили кнопок
            LodButton0.FontWeight = currentLod == LodLevel.LOD0 ? FontWeights.Bold : FontWeights.Normal;
            LodButton1.FontWeight = currentLod == LodLevel.LOD1 ? FontWeights.Bold : FontWeights.Normal;
            LodButton2.FontWeight = currentLod == LodLevel.LOD2 ? FontWeights.Bold : FontWeights.Normal;
            LodButton3.FontWeight = currentLod == LodLevel.LOD3 ? FontWeights.Bold : FontWeights.Normal;

            // Включаем/выключаем кнопки в зависимости от доступности LOD
            LodButton0.IsEnabled = _currentLodInfos.ContainsKey(LodLevel.LOD0);
            LodButton1.IsEnabled = _currentLodInfos.ContainsKey(LodLevel.LOD1);
            LodButton2.IsEnabled = _currentLodInfos.ContainsKey(LodLevel.LOD2);
            LodButton3.IsEnabled = _currentLodInfos.ContainsKey(LodLevel.LOD3);
        }

        /// <summary>
        /// Обработчик клика по кнопкам LOD
        /// </summary>
        private void LodButton_Click(object sender, RoutedEventArgs e) {
            if (sender is Button button && button.Tag is string tagStr) {
                if (int.TryParse(tagStr, out int lodIndex)) {
                    var lodLevel = (LodLevel)lodIndex;
                    SelectLod(lodLevel);
                }
            }
        }

        /// <summary>
        /// Обработчик выбора строки в LOD Information DataGrid
        /// </summary>
        private void LodInformationGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (LodInformationGrid.SelectedItem is LodDisplayInfo selectedLod) {
                SelectLod(selectedLod.Level);
            }
        }

        /// <summary>
        /// Обработчик изменения размера вьюпорта модели через GridSplitter
        /// </summary>
        private void ModelViewportGridSplitter_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e) {
            if (ModelViewportRow == null) {
                return;
            }

            double desiredHeight = ModelViewportRow.ActualHeight + e.VerticalChange;

            // Ограничиваем высоту вьюпорта
            const double minHeight = 150;
            const double maxHeight = 800;

            desiredHeight = Math.Max(minHeight, Math.Min(maxHeight, desiredHeight));
            ModelViewportRow.Height = new GridLength(desiredHeight);

            e.Handled = true;
        }

        /// <summary>
        /// Текущий режим отображения: FBX или GLB
        /// </summary>
        private bool _isShowingFbx = false;

        /// <summary>
        /// Обработчик клика по кнопкам Source Type (FBX/GLB)
        /// </summary>
        private void SourceTypeButton_Click(object sender, RoutedEventArgs e) {
            if (sender is Button button && button.Tag is string tagStr) {
                bool showFbx = tagStr == "FBX";

                if (showFbx == _isShowingFbx) {
                    return; // Уже показываем этот тип
                }

                _isShowingFbx = showFbx;

                // Обновляем UI кнопок
                SourceFbxButton.FontWeight = showFbx ? FontWeights.Bold : FontWeights.Normal;
                SourceGlbButton.FontWeight = showFbx ? FontWeights.Normal : FontWeights.Bold;

                // Включаем/выключаем LOD кнопки
                LodButton0.IsEnabled = !showFbx;
                LodButton1.IsEnabled = !showFbx && _currentLodInfos.ContainsKey(LodLevel.LOD1);
                LodButton2.IsEnabled = !showFbx && _currentLodInfos.ContainsKey(LodLevel.LOD2);
                LodButton3.IsEnabled = !showFbx && _currentLodInfos.ContainsKey(LodLevel.LOD3);

                if (showFbx) {
                    // Показываем FBX модель
                    SwitchToFbxView();
                } else {
                    // Показываем GLB модель
                    SwitchToGlbView();
                }
            }
        }

        /// <summary>
        /// Переключает вьюпорт на FBX модель
        /// </summary>
        private void SwitchToFbxView() {
            try {
                if (string.IsNullOrEmpty(_currentFbxPath)) {
                    LodLogger.Warn("No FBX path available for switching");
                    return;
                }

                LodLogger.Info($"Switching to FBX view: {_currentFbxPath}");

                // Загружаем FBX модель (используем существующий метод LoadModel)
                // Это вызовет LoadModel который загрузит FBX и отобразит его
                Dispatcher.Invoke(() => {
                    ModelCurrentLodTextBlock.Text = "Current: FBX (Original)";
                });

                // Загружаем FBX напрямую через Assimp без конвертации
                LoadFbxModelDirectly(_currentFbxPath);

            } catch (Exception ex) {
                LodLogger.Error(ex, "Failed to switch to FBX view");
            }
        }

        /// <summary>
        /// Переключает вьюпорт на GLB модель
        /// </summary>
        private void SwitchToGlbView() {
            try {
                LodLogger.Info("Switching to GLB view");

                // Загружаем текущий LOD
                LoadGlbModelToViewport(_currentLod);

            } catch (Exception ex) {
                LodLogger.Error(ex, "Failed to switch to GLB view");
            }
        }

        /// <summary>
        /// Загружает FBX модель напрямую (без GLB конвертации)
        /// </summary>
        private void LoadFbxModelDirectly(string fbxPath) {
            try {
                using var context = new AssimpContext();
                var scene = context.ImportFile(fbxPath,
                    PostProcessSteps.Triangulate |
                    PostProcessSteps.GenerateNormals |
                    PostProcessSteps.FlipUVs);

                if (scene == null || !scene.HasMeshes) {
                    LodLogger.Error("Failed to load FBX: no meshes");
                    return;
                }

                LodLogger.Info($"Loaded FBX: {scene.MeshCount} meshes");

                // Очищаем viewport
                var modelsToRemove = viewPort3d.Children.OfType<ModelVisual3D>().ToList();
                foreach (var model in modelsToRemove) {
                    viewPort3d.Children.Remove(model);
                }

                if (!viewPort3d.Children.OfType<DefaultLights>().Any()) {
                    viewPort3d.Children.Add(new DefaultLights());
                }

                // Конвертируем в WPF модель
                var modelGroup = ConvertAssimpSceneToWpfModel(scene);

                // Добавляем в viewport
                var visual3d = new ModelVisual3D { Content = modelGroup };
                viewPort3d.Children.Add(visual3d);

                // Применяем настройки viewer
                ApplyViewerSettingsToModel();

                // Обновляем UV preview
                var meshWithUV = scene.Meshes.FirstOrDefault(m => m.HasTextureCoords(0));
                if (meshWithUV != null) {
                    UpdateUVImage(meshWithUV);
                }

                LodLogger.Info("FBX model loaded successfully");

            } catch (Exception ex) {
                LodLogger.Error(ex, $"Failed to load FBX model: {fbxPath}");
            }
        }
    }
}
