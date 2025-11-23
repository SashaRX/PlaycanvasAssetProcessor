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
        private ObservableCollection<LodDisplayInfo> _lodDisplayItems = new();
        private bool _isGlbViewerActive = false;
        private LodLevel _currentLod = LodLevel.LOD0;

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
                _lodDisplayItems.Clear();
                _isGlbViewerActive = false;
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
                    _glbLoader = new GlbLoader();
                }

                // Загружаем все LOD сцены
                _lodScenes.Clear();
                var lodFilePaths = GlbLodHelper.GetLodFilePaths(fbxPath);

                foreach (var kvp in lodFilePaths) {
                    var lodLevel = kvp.Key;
                    var glbPath = kvp.Value;

                    LodLogger.Info($"  Loading {lodLevel}: {glbPath}");
                    var scene = await _glbLoader.LoadGlbAsync(glbPath);
                    if (scene != null) {
                        _lodScenes[lodLevel] = scene;
                    }
                }

                LodLogger.Info($"Loaded {_lodScenes.Count} LOD scenes");

                // Отображаем LOD0 в существующем viewport
                if (_lodScenes.ContainsKey(LodLevel.LOD0)) {
                    LoadGlbModelToViewport(LodLevel.LOD0);
                } else if (_lodScenes.Count > 0) {
                    var firstLod = _lodScenes.Keys.First();
                    LoadGlbModelToViewport(firstLod);
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
        private void LoadGlbModelToViewport(LodLevel lodLevel) {
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

                // Конвертируем Assimp Scene в WPF модель
                var modelGroup = ConvertAssimpSceneToWpfModel(scene);

                // Добавляем в viewport
                var visual3d = new ModelVisual3D { Content = modelGroup };
                viewPort3d.Children.Add(visual3d);

                // Центрируем камеру
                viewPort3d.ZoomExtents();

                _currentLod = lodLevel;
                LodLogger.Info($"Loaded GLB LOD{(int)lodLevel} successfully");

            } catch (Exception ex) {
                LodLogger.Error(ex, $"Failed to load GLB LOD {lodLevel} to viewport");
            }
        }

        /// <summary>
        /// Конвертирует Assimp Scene в WPF Model3DGroup (аналогично LoadModel)
        /// </summary>
        private Model3DGroup ConvertAssimpSceneToWpfModel(Scene scene) {
            var modelGroup = new Model3DGroup();

            foreach (var mesh in scene.Meshes) {
                var builder = new MeshBuilder();

                // Вершины и нормали
                for (int i = 0; i < mesh.VertexCount; i++) {
                    var vertex = mesh.Vertices[i];
                    var normal = mesh.Normals[i];
                    builder.Positions.Add(new Point3D(vertex.X, vertex.Y, vertex.Z));
                    builder.Normals.Add(new System.Windows.Media.Media3D.Vector3D(normal.X, normal.Y, normal.Z));

                    // Текстурные координаты
                    if (mesh.TextureCoordinateChannels.Length > 0 &&
                        mesh.TextureCoordinateChannels[0] != null &&
                        i < mesh.TextureCoordinateChannels[0].Count) {
                        builder.TextureCoordinates.Add(new System.Windows.Point(
                            mesh.TextureCoordinateChannels[0][i].X,
                            mesh.TextureCoordinateChannels[0][i].Y));
                    }
                }

                // Индексы
                for (int i = 0; i < mesh.FaceCount; i++) {
                    var face = mesh.Faces[i];
                    if (face.IndexCount == 3) {
                        builder.TriangleIndices.Add(face.Indices[0]);
                        builder.TriangleIndices.Add(face.Indices[1]);
                        builder.TriangleIndices.Add(face.Indices[2]);
                    }
                }

                var geometry = builder.ToMesh(true);
                var material = new DiffuseMaterial(new SolidColorBrush(Colors.Gray));
                var model = new GeometryModel3D(geometry, material);
                modelGroup.Children.Add(model);
            }

            // Центрирование модели (аналогично LoadModel)
            var bounds = modelGroup.Bounds;
            var centerOffset = new System.Windows.Media.Media3D.Vector3D(
                -bounds.X - bounds.SizeX / 2,
                -bounds.Y - bounds.SizeY / 2,
                -bounds.Z - bounds.SizeZ / 2
            );

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
                LodQuickSwitchPanel.Visibility = Visibility.Visible;
                LodInformationPanel.Visibility = Visibility.Visible;
                ModelCurrentLodTextBlock.Visibility = Visibility.Visible;
            });
        }

        /// <summary>
        /// Скрывает UI элементы для работы с LOD
        /// </summary>
        private void HideGlbLodUI() {
            Dispatcher.Invoke(() => {
                LodQuickSwitchPanel.Visibility = Visibility.Collapsed;
                LodInformationPanel.Visibility = Visibility.Collapsed;
                ModelCurrentLodTextBlock.Visibility = Visibility.Collapsed;

                // Очищаем данные
                _currentLodInfos.Clear();
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
    }
}
