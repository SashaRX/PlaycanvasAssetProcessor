using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Assimp;
using HelixToolkit.Wpf;
using AssetProcessor.ModelConversion.Core;
using NLog;

namespace AssetProcessor.ModelConversion.Viewer {
    /// <summary>
    /// WPF контрол для отображения GLB моделей с поддержкой LOD
    /// </summary>
    public class GlbViewerControl : Border {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly HelixViewport3D _viewport;
        private readonly GlbLoader _glbLoader;
        private readonly ModelVisual3D _modelVisual;
        private readonly DirectionalLight _light1;
        private readonly DirectionalLight _light2;
        private readonly AmbientLight _ambientLight;

        private Dictionary<LodLevel, Scene> _lodScenes = new();
        private Dictionary<LodLevel, string> _lodFiles = new();
        private LodLevel _currentLod = LodLevel.LOD0;

        public GlbViewerControl(string? gltfPackPath = null) {
            _glbLoader = new GlbLoader(gltfPackPath);

            // Создаём HelixViewport3D
            _viewport = new HelixViewport3D {
                ShowCoordinateSystem = true,
                ShowViewCube = true,
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                Camera = new PerspectiveCamera {
                    Position = new Point3D(5, 5, 5),
                    LookDirection = new Vector3D(-5, -5, -5),
                    UpDirection = new Vector3D(0, 1, 0),
                    FieldOfView = 60
                }
            };

            // Освещение
            _light1 = new DirectionalLight {
                Color = Colors.White,
                Direction = new Vector3D(-1, -1, -1)
            };
            _viewport.Children.Add(new ModelVisual3D { Content = _light1 });

            _light2 = new DirectionalLight {
                Color = Colors.White,
                Direction = new Vector3D(1, -1, 1)
            };
            _viewport.Children.Add(new ModelVisual3D { Content = _light2 });

            _ambientLight = new AmbientLight {
                Color = Color.FromRgb(50, 50, 50)
            };
            _viewport.Children.Add(new ModelVisual3D { Content = _ambientLight });

            // ModelVisual3D для отображения модели
            _modelVisual = new ModelVisual3D();
            _viewport.Children.Add(_modelVisual);

            Child = _viewport;

            // Подписываемся на событие выгрузки
            Unloaded += OnUnloaded;
        }

        /// <summary>
        /// Загружает один GLB файл
        /// </summary>
        public async Task LoadGlbAsync(string glbPath) {
            try {
                Logger.Info($"Loading GLB into viewer: {glbPath}");

                var scene = await _glbLoader.LoadGlbAsync(glbPath);
                if (scene == null) {
                    Logger.Error("Failed to load GLB scene");
                    return;
                }

                // Конвертируем Assimp Scene в WPF Model3DGroup
                var model = ConvertAssimpSceneToWpf(scene);

                // Отображаем модель
                _modelVisual.Content = model;

                // Центрируем камеру на модель
                _viewport.ZoomExtents();

                Logger.Info($"GLB loaded successfully: {scene.MeshCount} meshes");
            } catch (Exception ex) {
                Logger.Error(ex, "Failed to load GLB in viewer");
            }
        }

        /// <summary>
        /// Загружает LOD цепочку из манифеста
        /// </summary>
        public async Task LoadLodChainAsync(Dictionary<LodLevel, string> lodFiles) {
            try {
                _lodFiles = lodFiles;
                _lodScenes.Clear();

                Logger.Info($"Loading LOD chain: {lodFiles.Count} levels");

                foreach (var kvp in lodFiles) {
                    var lodLevel = kvp.Key;
                    var glbPath = kvp.Value;

                    Logger.Info($"  Loading {lodLevel}: {glbPath}");

                    var scene = await _glbLoader.LoadGlbAsync(glbPath);
                    if (scene != null) {
                        _lodScenes[lodLevel] = scene;
                    }
                }

                // Отображаем LOD0
                if (_lodScenes.ContainsKey(LodLevel.LOD0)) {
                    SwitchLod(LodLevel.LOD0);
                } else if (_lodScenes.Count > 0) {
                    var firstLod = _lodScenes.Keys.First();
                    SwitchLod(firstLod);
                }

                Logger.Info($"LOD chain loaded: {_lodScenes.Count} levels");
            } catch (Exception ex) {
                Logger.Error(ex, "Failed to load LOD chain");
            }
        }

        /// <summary>
        /// Переключает текущий LOD уровень
        /// </summary>
        public void SwitchLod(LodLevel lodLevel) {
            try {
                if (!_lodScenes.ContainsKey(lodLevel)) {
                    Logger.Warn($"LOD level {lodLevel} not loaded");
                    return;
                }

                Logger.Info($"Switching to {lodLevel}");

                var scene = _lodScenes[lodLevel];
                var model = ConvertAssimpSceneToWpf(scene);

                _modelVisual.Content = model;
                _currentLod = lodLevel;

                Logger.Info($"Switched to {lodLevel}: {scene.MeshCount} meshes");
            } catch (Exception ex) {
                Logger.Error(ex, $"Failed to switch to {lodLevel}");
            }
        }

        /// <summary>
        /// Получает текущий LOD уровень
        /// </summary>
        public LodLevel CurrentLod => _currentLod;

        /// <summary>
        /// Получает список загруженных LOD уровней
        /// </summary>
        public List<LodLevel> AvailableLods => _lodScenes.Keys.OrderBy(l => l).ToList();

        /// <summary>
        /// Конвертирует Assimp Scene в WPF Model3DGroup
        /// </summary>
        private Model3DGroup ConvertAssimpSceneToWpf(Scene scene) {
            var modelGroup = new Model3DGroup();

            foreach (var mesh in scene.Meshes) {
                var geometry = new MeshGeometry3D();

                // Вершины
                foreach (var vertex in mesh.Vertices) {
                    geometry.Positions.Add(new Point3D(vertex.X, vertex.Y, vertex.Z));
                }

                // Нормали
                if (mesh.HasNormals) {
                    foreach (var normal in mesh.Normals) {
                        geometry.Normals.Add(new Vector3D(normal.X, normal.Y, normal.Z));
                    }
                }

                // UV координаты
                if (mesh.HasTextureCoords(0)) {
                    foreach (var texCoord in mesh.TextureCoordinateChannels[0]) {
                        geometry.TextureCoordinates.Add(new System.Windows.Point(texCoord.X, 1 - texCoord.Y)); // Инвертируем V
                    }
                }

                // Индексы
                foreach (var face in mesh.Faces) {
                    if (face.IndexCount == 3) {
                        geometry.TriangleIndices.Add(face.Indices[0]);
                        geometry.TriangleIndices.Add(face.Indices[1]);
                        geometry.TriangleIndices.Add(face.Indices[2]);
                    }
                }

                // Материал (простой серый материал по умолчанию)
                var material = new DiffuseMaterial(new SolidColorBrush(Colors.LightGray));

                // Создаём GeometryModel3D
                var geometryModel = new GeometryModel3D {
                    Geometry = geometry,
                    Material = material,
                    BackMaterial = material
                };

                modelGroup.Children.Add(geometryModel);
            }

            return modelGroup;
        }

        /// <summary>
        /// Очищает viewer
        /// </summary>
        public void Clear() {
            _modelVisual.Content = null;
            _lodScenes.Clear();
            _lodFiles.Clear();
            _glbLoader.ClearCache();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e) {
            _glbLoader.Dispose();
        }
    }
}
