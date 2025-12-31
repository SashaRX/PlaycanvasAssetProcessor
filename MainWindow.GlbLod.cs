using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
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
using NLog;

namespace AssetProcessor {
    /// <summary>
    /// MainWindow partial class для работы с GLB LOD просмотром
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
        private string? _currentFbxPath;  // Путь к FBX для переключения Source Type
        private ImageBrush? _cachedAlbedoBrush;  // Кэшированная albedo текстура для preview
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
                _lodGlbData.Clear();
                _currentLodInfos.Clear();
                _lodQuantizationInfos.Clear();
                _lodDisplayItems.Clear();
                _isGlbViewerActive = false;
                _currentFbxPath = null;
                _cachedAlbedoBrush = null;
                _sharpGlbLoader?.Dispose();
                LodLogger.Info("GLB viewer cleaned up");
            } catch (Exception ex) {
                LodLogger.Error(ex, "Failed to cleanup GLB viewer");
            }
        }

        /// <summary>
        /// Ищет и загружает albedo текстуру для модели из таблицы материалов.
        /// Использует viewModel.Materials и viewModel.Textures для поиска по ID.
        /// </summary>
        private ImageBrush? FindAndLoadAlbedoTexture(string fbxPath) {
            try {
                var modelName = System.IO.Path.GetFileNameWithoutExtension(fbxPath);
                LodLogger.Info($"[Texture] Looking for albedo texture for model: {modelName}");

                // Ищем материал по имени модели (модель "chair" -> материал "chair_mat" или "chair")
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
                    if (material != null) {
                        LodLogger.Info($"[Texture] Found material: {material.Name} (ID: {material.ID})");
                        break;
                    }
                }

                if (material == null) {
                    LodLogger.Info($"[Texture] No material found for model: {modelName}");
                    return null;
                }

                // Получаем ID текстуры из материала
                var diffuseMapId = material.DiffuseMapId;
                if (!diffuseMapId.HasValue) {
                    LodLogger.Info($"[Texture] Material {material.Name} has no DiffuseMapId");
                    return null;
                }

                // Ищем текстуру по ID в таблице текстур
                var texture = viewModel.Textures.FirstOrDefault(t => t.ID == diffuseMapId.Value);
                if (texture == null) {
                    LodLogger.Info($"[Texture] Texture with ID {diffuseMapId.Value} not found in textures table");
                    return null;
                }

                // Загружаем текстуру по пути
                if (string.IsNullOrEmpty(texture.Path) || !System.IO.File.Exists(texture.Path)) {
                    LodLogger.Info($"[Texture] Texture file not found: {texture.Path}");
                    return null;
                }

                LodLogger.Info($"[Texture] Found albedo from material table: {texture.Name} (ID: {texture.ID}) -> {texture.Path}");
                return LoadTextureAsBrush(texture.Path);

            } catch (Exception ex) {
                LodLogger.Warn(ex, "Failed to find albedo texture from materials table");
                return null;
            }
        }

        /// <summary>
        /// Загружает текстуру как ImageBrush для WPF 3D
        /// </summary>
        private ImageBrush? LoadTextureAsBrush(string texturePath) {
            try {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(texturePath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze(); // Для thread-safety

                var brush = new ImageBrush(bitmap) {
                    ViewportUnits = BrushMappingMode.Absolute,
                    TileMode = TileMode.Tile,
                    Viewport = new Rect(0, 0, 1, 1),
                    ViewboxUnits = BrushMappingMode.RelativeToBoundingBox
                };

                LodLogger.Info($"[Texture] Loaded: {texturePath} ({bitmap.PixelWidth}x{bitmap.PixelHeight})");
                return brush;

            } catch (Exception ex) {
                LodLogger.Warn(ex, $"Failed to load texture: {texturePath}");
                return null;
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
                LodLogger.Info($"Loading GLB LOD files for: {fbxPath}");
                _currentFbxPath = fbxPath;

                // Загружаем albedo текстуру из таблицы материалов
                _cachedAlbedoBrush = FindAndLoadAlbedoTexture(fbxPath);

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
                PopulateLodDataGrid();

                // Создаём SharpGlbLoader если его еще нет
                if (_sharpGlbLoader == null) {
                    var modelConversionSettings = ModelConversion.Settings.ModelConversionSettingsManager.LoadSettings();
                    var gltfPackPath = string.IsNullOrWhiteSpace(modelConversionSettings.GltfPackExecutablePath)
                        ? "gltfpack.exe"
                        : modelConversionSettings.GltfPackExecutablePath;
                    _sharpGlbLoader = new SharpGlbLoader(gltfPackPath);
                } else {
                    _sharpGlbLoader.ClearCache();
                }

                // Загружаем все LOD данные через SharpGLTF
                _lodGlbData.Clear();
                _lodQuantizationInfos.Clear();
                var lodFilePaths = GlbLodHelper.GetLodFilePaths(fbxPath);

                // Обёртываем CPU-интенсивные операции в Task.Run
                await Task.Run(() => {
                    foreach (var kvp in lodFilePaths) {
                        var lodLevel = kvp.Key;
                        var glbPath = kvp.Value;

                        var quantInfo = GlbQuantizationAnalyzer.AnalyzeQuantization(glbPath);
                        _lodQuantizationInfos[lodLevel] = quantInfo;

                        var glbData = _sharpGlbLoader!.LoadGlb(glbPath);
                        if (glbData.Success) {
                            _lodGlbData[lodLevel] = glbData;
                            LodLogger.Info($"{lodLevel} loaded: {glbData.Meshes.Count} meshes");
                        } else {
                            LodLogger.Error($"Failed to load {lodLevel}: {glbData.Error}");
                        }
                    }
                });

                LodLogger.Info($"Loaded {_lodGlbData.Count} LOD meshes");

                // UI операции в Dispatcher
                await Dispatcher.InvokeAsync(() => {
                    if (_lodGlbData.Count == 0) {
                        LodLogger.Warn("All GLB failed to load, falling back to FBX");
                        HideGlbLodUI();
                        LoadFbxModelDirectly(fbxPath);
                        return;
                    }

                    if (_lodGlbData.ContainsKey(LodLevel.LOD0)) {
                        LoadGlbModelToViewport(LodLevel.LOD0, zoomToFit: true);
                    } else if (_lodGlbData.Count > 0) {
                        var firstLod = _lodGlbData.Keys.First();
                        LoadGlbModelToViewport(firstLod, zoomToFit: true);
                    }

                    _isGlbViewerActive = true;
                    SelectLod(LodLevel.LOD0);
                    LodLogger.Info("GLB LOD preview loaded successfully");
                });

            } catch (Exception ex) {
                LodLogger.Error(ex, "Failed to load GLB LOD files");
                Dispatcher.Invoke(() => {
                    HideGlbLodUI();
                });
            }
        }

        /// <summary>
        /// Загружает GLB модель в существующий viewPort3d (через SharpGLTF)
        /// </summary>
        /// <param name="zoomToFit">Выполнить ZoomExtents после загрузки (только при первой загрузке модели)</param>
        private void LoadGlbModelToViewport(LodLevel lodLevel, bool zoomToFit = false) {
            try {
                if (!_lodGlbData.TryGetValue(lodLevel, out var glbData)) {
                    LodLogger.Warn($"LOD {lodLevel} data not found");
                    return;
                }

                LodLogger.Info($"Loading GLB LOD{(int)lodLevel} to viewport: {glbData.Meshes.Count} meshes (SharpGLTF)");

                // Очищаем только модели, оставляя освещение (аналогично LoadModel)
                var modelsToRemove = viewPort3d.Children.OfType<ModelVisual3D>().ToList();
                foreach (var model in modelsToRemove) {
                    viewPort3d.Children.Remove(model);
                }

                // Убеждаемся что есть освещение
                if (!viewPort3d.Children.OfType<DefaultLights>().Any()) {
                    viewPort3d.Children.Add(new DefaultLights());
                }

                // Конвертируем SharpGLTF данные в WPF модель
                var modelGroup = ConvertSharpGlbToWpfModel(glbData);

                // Добавляем в viewport
                var visual3d = new ModelVisual3D { Content = modelGroup };
                viewPort3d.Children.Add(visual3d);

                // Применяем настройки viewer (wireframe, up vector)
                ApplyViewerSettingsToModel();

                // Обновляем UV preview (берём первую mesh с UV координатами)
                var meshWithUV = glbData.Meshes.FirstOrDefault(m => m.TextureCoordinates.Count > 0);
                if (meshWithUV != null) {
                    UpdateUVImageFromSharpGlb(meshWithUV, lodLevel);
                }

                // Центрируем камеру только при первой загрузке модели
                if (zoomToFit) {
                    viewPort3d.ZoomExtents();
                }

                _currentLod = lodLevel;
                LodLogger.Info($"Loaded GLB LOD{(int)lodLevel} successfully via SharpGLTF");

            } catch (Exception ex) {
                LodLogger.Error(ex, $"Failed to load GLB LOD {lodLevel} to viewport");
            }
        }

        /// <summary>
        /// Конвертирует Assimp Scene в WPF Model3DGroup
        /// UV координаты копируются напрямую без преобразований
        /// (GLB уже имеет top-left UV origin, как WPF)
        ///
        /// Координатные системы:
        /// - FBX может быть Z-up (3ds Max) или Y-up (Maya, Blender)
        /// - WPF использует Y-up правую систему координат
        /// - Определяем UpAxis из метаданных FBX и применяем соответствующее преобразование
        /// </summary>
        private Model3DGroup ConvertAssimpSceneToWpfModel(Scene scene) {
            var modelGroup = new Model3DGroup();

            // Определяем координатную систему FBX из метаданных
            // UpAxis: 0 = X, 1 = Y, 2 = Z (default = 2 для 3ds Max)
            bool isZUp = true; // По умолчанию Z-up (3ds Max)
            if (scene.Metadata != null && scene.Metadata.TryGetValue("UpAxis", out var upAxisEntry)) {
                if (upAxisEntry.Data is int upAxis) {
                    isZUp = upAxis == 2; // 2 = Z-up, 1 = Y-up
                    LodLogger.Info($"FBX UpAxis from metadata: {upAxis} (isZUp={isZUp})");
                }
            } else {
                LodLogger.Info("FBX UpAxis metadata not found, assuming Z-up (3ds Max default)");
            }

            foreach (var mesh in scene.Meshes) {
                LodLogger.Info($"Processing mesh: {mesh.VertexCount} vertices, {mesh.FaceCount} faces, HasUVs={mesh.HasTextureCoords(0)}");
                LodLogger.Info($"  TextureCoordinateChannelCount={mesh.TextureCoordinateChannelCount}");

                // Логируем первые 5 вершин из Assimp для диагностики (до преобразования)
                LodLogger.Info($"First 5 vertices from Assimp (before axis transform, isZUp={isZUp}):");
                for (int i = 0; i < Math.Min(5, mesh.VertexCount); i++) {
                    var v = mesh.Vertices[i];
                    LodLogger.Info($"  Vertex {i}: ({v.X:F4}, {v.Y:F4}, {v.Z:F4})");
                }

                // Создаём геометрию напрямую, без MeshBuilder (он багованный)
                var geometry = new MeshGeometry3D();

                // Вершины и нормали
                // FBX/glTF и WPF Viewport3D используют правую систему координат (X вправо, Y вверх, Z к камере).
                // Приводим только up-axis (Z-up → Y-up), без инверсии знаков, чтобы не переворачивать модель.
                for (int i = 0; i < mesh.VertexCount; i++) {
                    var vertex = mesh.Vertices[i];
                    var normal = mesh.Normals[i];

                    double x, y, z;
                    double nx, ny, nz;

                    if (isZUp) {
                        // Z-up → Y-up: swap Y↔Z
                        x = vertex.X;
                        y = vertex.Z;
                        z = vertex.Y;

                        nx = normal.X;
                        ny = normal.Z;
                        nz = normal.Y;
                    } else {
                        // Y-up → Y-up: просто копируем
                        x = vertex.X;
                        y = vertex.Y;
                        z = vertex.Z;

                        nx = normal.X;
                        ny = normal.Y;
                        nz = normal.Z;
                    }

                    geometry.Positions.Add(new Point3D(x, y, z));
                    geometry.Normals.Add(new System.Windows.Media.Media3D.Vector3D(nx, ny, nz));
                }

                // UV координаты (если есть)
                if (mesh.TextureCoordinateChannelCount > 0 && mesh.HasTextureCoords(0)) {
                    var uvChannel = mesh.TextureCoordinateChannels[0];
                    var uvCount = uvChannel.Count;
                    var vertexCount = mesh.VertexCount;
                    
                    // КРИТИЧНО: Проверяем, что количество UV координат соответствует количеству вершин
                    // Если не совпадает, используем минимальное значение для безопасного доступа
                    var safeCount = Math.Min(vertexCount, uvCount);
                    
                    if (uvCount != vertexCount) {
                        LodLogger.Warn($"  UV channel 0 count ({uvCount}) doesn't match vertex count ({vertexCount}), using safe count: {safeCount}");
                    } else {
                        LodLogger.Info($"  Found UV channel 0, copying {vertexCount} UV coordinates");
                    }

                    // Копируем UV координаты с безопасной границей
                    for (int i = 0; i < safeCount; i++) {
                        var uv = uvChannel[i];
                        geometry.TextureCoordinates.Add(new Point(uv.X, uv.Y));
                    }
                    
                    // Если UV координат меньше вершин, заполняем оставшиеся нулевыми координатами
                    if (safeCount < vertexCount) {
                        for (int i = safeCount; i < vertexCount; i++) {
                            geometry.TextureCoordinates.Add(new Point(0, 0));
                        }
                        LodLogger.Warn($"  Filled {vertexCount - safeCount} missing UV coordinates with (0, 0)");
                    }

                    // Логируем первые 5 UV для проверки
                    if (safeCount > 0) {
                        LodLogger.Info($"  First 5 UVs:");
                        for (int i = 0; i < Math.Min(5, safeCount); i++) {
                            var uv = uvChannel[i];
                            LodLogger.Info($"    UV {i}: ({uv.X:F4}, {uv.Y:F4})");
                        }
                    }
                } else {
                    LodLogger.Info($"  No UV coordinates found");
                }

                // Индексы копируем напрямую (WPF тоже использует правую СК, инверсия winding не требуется)
                for (int i = 0; i < mesh.FaceCount; i++) {
                    var face = mesh.Faces[i];
                    if (face.IndexCount == 3) {
                        geometry.TriangleIndices.Add(face.Indices[0]);
                        geometry.TriangleIndices.Add(face.Indices[1]);
                        geometry.TriangleIndices.Add(face.Indices[2]);
                    }
                }

                LodLogger.Info($"Geometry created: Positions={geometry.Positions.Count}, Normals={geometry.Normals.Count}, TexCoords={geometry.TextureCoordinates.Count}, Indices={geometry.TriangleIndices.Count}");

                // Логируем первые 5 вершин из geometry.Positions для проверки (после axis transform)
                LodLogger.Info($"First 5 vertices in geometry.Positions (after axis transform, isZUp={isZUp}):");
                for (int i = 0; i < Math.Min(5, geometry.Positions.Count); i++) {
                    var p = geometry.Positions[i];
                    LodLogger.Info($"  Position {i}: ({p.X:F4}, {p.Y:F4}, {p.Z:F4})");
                }

                // Create two-sided material - используем albedo текстуру если загружена
                DiffuseMaterial frontMaterial;
                if (_cachedAlbedoBrush != null && geometry.TextureCoordinates.Count > 0) {
                    frontMaterial = new DiffuseMaterial(_cachedAlbedoBrush);
                    LodLogger.Info($"Using albedo texture for material");
                } else {
                    frontMaterial = new DiffuseMaterial(new SolidColorBrush(Colors.LightGray));
                }

                var backMaterial = new DiffuseMaterial(new SolidColorBrush(Colors.DarkRed));
                var emissiveMaterial = new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(30, 30, 30)));

                var materialGroup = new MaterialGroup();
                materialGroup.Children.Add(frontMaterial);
                materialGroup.Children.Add(emissiveMaterial);

                var model = new GeometryModel3D(geometry, materialGroup);
                model.BackMaterial = backMaterial;
                modelGroup.Children.Add(model);

                LodLogger.Info($"Mesh created successfully");
            }

            // Вычисление bounds вручную из всех вершин (для логирования)
            var minX = double.MaxValue;
            var minY = double.MaxValue;
            var minZ = double.MaxValue;
            var maxX = double.MinValue;
            var maxY = double.MinValue;
            var maxZ = double.MinValue;

            var transform = modelGroup.Transform ?? Transform3D.Identity;

            foreach (var child in modelGroup.Children) {
                if (child is GeometryModel3D geoModel && geoModel.Geometry is MeshGeometry3D mesh) {
                    foreach (var pos in mesh.Positions) {
                        var transformed = transform.Transform(pos);
                        minX = Math.Min(minX, transformed.X);
                        minY = Math.Min(minY, transformed.Y);
                        minZ = Math.Min(minZ, transformed.Z);
                        maxX = Math.Max(maxX, transformed.X);
                        maxY = Math.Max(maxY, transformed.Y);
                        maxZ = Math.Max(maxZ, transformed.Z);
                    }
                }
            }

            var sizeX = maxX - minX;
            var sizeY = maxY - minY;
            var sizeZ = maxZ - minZ;

            LodLogger.Info($"Model bounds (after axis conversion): min=({minX:F2}, {minY:F2}, {minZ:F2}), max=({maxX:F2}, {maxY:F2}, {maxZ:F2})");
            LodLogger.Info($"Model size (Y=height): {sizeX:F2} x {sizeY:F2} x {sizeZ:F2}");
            LodLogger.Info($"Model pivot preserved (no auto-centering)");

            // Базовый разворот для согласованности с GLB: FBX/glTF смотрит вдоль +Z вперёд, камера HelixToolkit направлена по -Z.
            // Разворачиваем сцену на 180° вокруг Y, чтобы forward совпадал с кубом навигации и pivot.
            // Это обеспечивает согласованное отображение при переключении между FBX и GLB через Source Type кнопки.
            var baseTransform = new Transform3DGroup();
            baseTransform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new System.Windows.Media.Media3D.Vector3D(0, 1, 0), 180)));
            modelGroup.Transform = baseTransform;

            LodLogger.Info("[Assimp→WPF] Applied base Y-rotation 180° to align forward with HelixToolkit camera (consistent with GLB)");

            // НЕ центрируем модель - сохраняем оригинальный pivot из файла
            // Камера подстроится через ZoomExtents()

            return modelGroup;
        }

        /// <summary>
        /// Конвертирует SharpGLTF данные в WPF Model3DGroup
        /// SharpGLTF автоматически декодирует KHR_mesh_quantization
        ///
        /// Координатные системы:
        /// - GLB/glTF спецификация требует Y-up (FBX2glTF конвертирует из исходной FBX)
        /// - Viewport3D использует такую же правую систему координат (+Z к камере)
        /// - Достаточно прямого копирования вершин/индексов без разворотов, чтобы избежать переворота модели
        /// </summary>
        private Model3DGroup ConvertSharpGlbToWpfModel(SharpGlbLoader.GlbData glbData) {
            var modelGroup = new Model3DGroup();

            foreach (var meshData in glbData.Meshes) {
                LodLogger.Info($"[SharpGLTF→WPF] Processing mesh: {meshData.Positions.Count} vertices, {meshData.Indices.Count / 3} triangles, HasUVs={meshData.TextureCoordinates.Count > 0}");

                var geometry = new MeshGeometry3D();

                // Вершины и нормали
                // glTF и WPF Viewport3D имеют одинаковую правую СК, поэтому копируем напрямую.
                // КРИТИЧНО: В WPF MeshGeometry3D, если нормали добавляются, их должно быть ровно столько же, сколько позиций.
                // Если количество нормалей не совпадает с количеством позиций, не добавляем нормали вообще.
                bool hasValidNormals = meshData.Normals.Count == meshData.Positions.Count;
                if (meshData.Normals.Count > 0 && !hasValidNormals) {
                    LodLogger.Warn($"[SharpGLTF→WPF] Normals count ({meshData.Normals.Count}) doesn't match positions count ({meshData.Positions.Count}), skipping normals");
                }

                for (int i = 0; i < meshData.Positions.Count; i++) {
                    var pos = meshData.Positions[i];
                    geometry.Positions.Add(new Point3D(pos.X, pos.Y, pos.Z));

                    // Добавляем нормали только если их количество совпадает с количеством позиций
                    if (hasValidNormals) {
                        var normal = meshData.Normals[i];
                        geometry.Normals.Add(new System.Windows.Media.Media3D.Vector3D(normal.X, normal.Y, normal.Z));
                    }
                }

                // UV координаты (SharpGLTF уже декодировал quantization!)
                if (meshData.TextureCoordinates.Count > 0) {
                    LodLogger.Info($"[SharpGLTF→WPF]   Adding {meshData.TextureCoordinates.Count} UV coordinates");
                    foreach (var uv in meshData.TextureCoordinates) {
                        geometry.TextureCoordinates.Add(new Point(uv.X, uv.Y));
                    }

                    // Логируем первые 5 UV
                    LodLogger.Info($"[SharpGLTF→WPF]   First 5 UVs in WPF geometry:");
                    for (int i = 0; i < Math.Min(5, meshData.TextureCoordinates.Count); i++) {
                        var uv = meshData.TextureCoordinates[i];
                        LodLogger.Info($"[SharpGLTF→WPF]     [{i}]: ({uv.X:F6}, {uv.Y:F6})");
                    }
                }

                // Индексы копируем напрямую, т.к. handedness совпадает.
                for (int i = 0; i < meshData.Indices.Count; i += 3) {
                    if (i + 2 < meshData.Indices.Count) {
                        geometry.TriangleIndices.Add(meshData.Indices[i]);
                        geometry.TriangleIndices.Add(meshData.Indices[i + 1]);
                        geometry.TriangleIndices.Add(meshData.Indices[i + 2]);
                    }
                }

                LodLogger.Info($"[SharpGLTF→WPF]   Geometry: {geometry.Positions.Count} positions, {geometry.TriangleIndices.Count / 3} triangles");

                // Материал - используем albedo текстуру если она загружена
                DiffuseMaterial frontMaterial;
                if (_cachedAlbedoBrush != null && geometry.TextureCoordinates.Count > 0) {
                    frontMaterial = new DiffuseMaterial(_cachedAlbedoBrush);
                    LodLogger.Info($"[SharpGLTF→WPF]   Using albedo texture for material");
                } else {
                    frontMaterial = new DiffuseMaterial(new SolidColorBrush(Colors.LightGray));
                }

                var backMaterial = new DiffuseMaterial(new SolidColorBrush(Colors.DarkRed));
                var emissiveMaterial = new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(30, 30, 30)));

                var materialGroup = new MaterialGroup();
                materialGroup.Children.Add(frontMaterial);
                materialGroup.Children.Add(emissiveMaterial);

                var model = new GeometryModel3D(geometry, materialGroup);
                model.BackMaterial = backMaterial;
                modelGroup.Children.Add(model);
            }

            // Базовый разворот glTF -> WPF: glTF смотрит вдоль +Z вперёд, камера HelixToolkit направлена по -Z.
            // Разворачиваем сцену на 180° вокруг Y, чтобы forward совпадал с кубом навигации и pivot.
            var baseTransform = new Transform3DGroup();
            baseTransform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new System.Windows.Media.Media3D.Vector3D(0, 1, 0), 180)));
            modelGroup.Transform = baseTransform;

            LodLogger.Info("[SharpGLTF→WPF] Applied base Y-rotation 180° to align forward with HelixToolkit camera");

            // Логируем bounds
            var minX = double.MaxValue;
            var minY = double.MaxValue;
            var minZ = double.MaxValue;
            var maxX = double.MinValue;
            var maxY = double.MinValue;
            var maxZ = double.MinValue;

            var transform = modelGroup.Transform ?? Transform3D.Identity;

            foreach (var child in modelGroup.Children) {
                if (child is GeometryModel3D geoModel && geoModel.Geometry is MeshGeometry3D mesh) {
                    foreach (var pos in mesh.Positions) {
                        var transformed = transform.Transform(pos);
                        minX = Math.Min(minX, transformed.X);
                        minY = Math.Min(minY, transformed.Y);
                        minZ = Math.Min(minZ, transformed.Z);
                        maxX = Math.Max(maxX, transformed.X);
                        maxY = Math.Max(maxY, transformed.Y);
                        maxZ = Math.Max(maxZ, transformed.Z);
                    }
                }
            }

            var sizeX = maxX - minX;
            var sizeY = maxY - minY;
            var sizeZ = maxZ - minZ;

            LodLogger.Info($"[SharpGLTF→WPF] Model bounds: min=({minX:F2}, {minY:F2}, {minZ:F2}), max=({maxX:F2}, {maxY:F2}, {maxZ:F2})");
            LodLogger.Info($"[SharpGLTF→WPF] Model size (Y=height): {sizeX:F2} x {sizeY:F2} x {sizeZ:F2}");

            return modelGroup;
        }

        /// <summary>
        /// Обновляет UV preview из SharpGLTF mesh данных
        /// </summary>
        /// <param name="meshData">Данные меша из SharpGLTF</param>
        /// <param name="lodLevel">Уровень LOD для получения информации о квантовании</param>
        private void UpdateUVImageFromSharpGlb(SharpGlbLoader.MeshData meshData, LodLevel lodLevel) {
            try {
                if (meshData.TextureCoordinates.Count == 0) {
                    LodLogger.Warn("[UV Preview] No UV coordinates in mesh");
                    return;
                }

                const int width = 512;
                const int height = 512;

                // КРИТИЧНО: Проверяем совместимость UV0 с количеством вершин
                // Индексы относятся к вершинам, поэтому UV0 должно иметь столько же координат, сколько вершин
                if (meshData.TextureCoordinates.Count != meshData.Positions.Count) {
                    LodLogger.Warn($"[UV Preview] UV0 count ({meshData.TextureCoordinates.Count}) doesn't match vertex count ({meshData.Positions.Count}), cannot render UV preview");
                    return;
                }

                // Получаем информацию о квантовании для текущего LOD
                float uvScaleU = 1.0f;
                float uvScaleV = 1.0f;
                if (_lodQuantizationInfos.TryGetValue(lodLevel, out var quantInfo)) {
                    // Применяем масштабирование если UV были квантованы
                    // SharpGLTF декодирует квантование, но если информация о масштабе есть,
                    // возможно нужно применить дополнительную коррекцию для правильного отображения
                    if (GlbQuantizationAnalyzer.NeedsUVScaling(quantInfo)) {
                        uvScaleU = quantInfo.UVScaleU;
                        uvScaleV = quantInfo.UVScaleV;
                        LodLogger.Info($"[UV Preview] Applying UV scale correction: U={uvScaleU:F3}x, V={uvScaleV:F3}x (LOD {lodLevel})");
                    }
                }

                // Создаём bitmap для UV preview
                var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);

                // Рисуем UV wireframe
                var points = new Point[meshData.TextureCoordinates.Count];
                for (int i = 0; i < meshData.TextureCoordinates.Count; i++) {
                    var uv = meshData.TextureCoordinates[i];
                    // Применяем масштабирование UV если нужно (для коррекции квантования)
                    float scaledU = uv.X * uvScaleU;
                    float scaledV = uv.Y * uvScaleV;
                    points[i] = new Point(scaledU * width, scaledV * height);
                }

                // Рисуем треугольники
                bitmap.Lock();
                try {
                    var pixels = new byte[width * height * 4];

                    // Заполняем фон тёмно-серым
                    for (int i = 0; i < pixels.Length; i += 4) {
                        pixels[i] = 40;     // B
                        pixels[i + 1] = 40; // G
                        pixels[i + 2] = 40; // R
                        pixels[i + 3] = 255; // A
                    }

                    // Рисуем UV треугольники
                    for (int i = 0; i < meshData.Indices.Count; i += 3) {
                        if (i + 2 >= meshData.Indices.Count) break;

                        var i0 = meshData.Indices[i];
                        var i1 = meshData.Indices[i + 1];
                        var i2 = meshData.Indices[i + 2];

                        // Проверяем границы: индексы должны быть неотрицательны и меньше длины массива
                        // Это защищает от повреждённых GLB данных с отрицательными индексами
                        if (i0 < 0 || i0 >= points.Length || i1 < 0 || i1 >= points.Length || i2 < 0 || i2 >= points.Length) continue;

                        DrawLine(pixels, width, height, points[i0], points[i1], 0, 255, 0); // Green
                        DrawLine(pixels, width, height, points[i1], points[i2], 0, 255, 0);
                        DrawLine(pixels, width, height, points[i2], points[i0], 0, 255, 0);
                    }

                    bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
                } finally {
                    bitmap.Unlock();
                }

                // Создаём вторичный UV preview (UV1/lightmap) если доступен
                WriteableBitmap? bitmap2 = null;
                if (meshData.TextureCoordinates2.Count > 0) {
                    // КРИТИЧНО: Проверяем совместимость UV1 с количеством вершин
                    // Индексы относятся к вершинам, поэтому UV1 должно иметь столько же координат, сколько вершин
                    if (meshData.TextureCoordinates2.Count != meshData.Positions.Count) {
                        LodLogger.Warn($"[UV Preview] UV1 (lightmap) count ({meshData.TextureCoordinates2.Count}) doesn't match vertex count ({meshData.Positions.Count}), skipping UV1 preview");
                    } else {
                        bitmap2 = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                        var points2 = new Point[meshData.TextureCoordinates2.Count];
                        for (int i = 0; i < meshData.TextureCoordinates2.Count; i++) {
                            var uv = meshData.TextureCoordinates2[i];
                            // Применяем масштабирование UV если нужно (для коррекции квантования)
                            float scaledU = uv.X * uvScaleU;
                            float scaledV = uv.Y * uvScaleV;
                            points2[i] = new Point(scaledU * width, scaledV * height);
                        }

                        // Дополнительная проверка: находим максимальный индекс перед циклом
                        // Это защищает от повреждённых данных, где индексы могут превышать количество UV координат
                        int maxIndex = -1;
                        if (meshData.Indices.Count > 0) {
                            maxIndex = meshData.Indices.Max();
                        }
                        if (maxIndex >= points2.Length) {
                            LodLogger.Warn($"[UV Preview] UV1 indices contain out-of-range value: max index={maxIndex}, UV1 count={points2.Length}, skipping UV1 preview");
                            // КРИТИЧНО: Устанавливаем bitmap2 = null при ошибке валидации, чтобы не отображать неинициализированный bitmap
                            bitmap2 = null;
                        } else {
                            bitmap2.Lock();
                            try {
                                var pixels2 = new byte[width * height * 4];
                                for (int i = 0; i < pixels2.Length; i += 4) {
                                    pixels2[i] = 40;     // B
                                    pixels2[i + 1] = 40; // G
                                    pixels2[i + 2] = 40; // R
                                    pixels2[i + 3] = 255; // A
                                }

                                // Рисуем UV треугольники для вторичного канала
                                // Используем те же индексы, что и для UV0, так как они относятся к вершинам
                                // (UV1 должно иметь столько же координат, сколько вершин - проверено выше)
                                // (максимальный индекс также проверен выше)
                                for (int i = 0; i < meshData.Indices.Count; i += 3) {
                                    if (i + 2 >= meshData.Indices.Count) break;

                                    var i0 = meshData.Indices[i];
                                    var i1 = meshData.Indices[i + 1];
                                    var i2 = meshData.Indices[i + 2];

                                    // Проверяем границы: индексы должны быть неотрицательны и меньше длины массива
                                    // Это защищает от повреждённых GLB данных с отрицательными индексами
                                    if (i0 < 0 || i0 >= points2.Length || i1 < 0 || i1 >= points2.Length || i2 < 0 || i2 >= points2.Length) continue;

                                    DrawLine(pixels2, width, height, points2[i0], points2[i1], 0, 255, 0); // Green
                                    DrawLine(pixels2, width, height, points2[i1], points2[i2], 0, 255, 0);
                                    DrawLine(pixels2, width, height, points2[i2], points2[i0], 0, 255, 0);
                                }

                                bitmap2.WritePixels(new Int32Rect(0, 0, width, height), pixels2, width * 4, 0);
                            } finally {
                                bitmap2.Unlock();
                            }
                        }
                    }
                }

                // Обновляем UI
                Dispatcher.Invoke(() => {
                    UVImage.Source = bitmap;
                    // Обновляем вторичный UV preview если доступен, иначе очищаем
                    UVImage2.Source = bitmap2;
                });

                LodLogger.Info($"[UV Preview] Updated UV0 with {meshData.TextureCoordinates.Count} coordinates");
                if (meshData.TextureCoordinates2.Count > 0) {
                    LodLogger.Info($"[UV Preview] Updated UV1 (lightmap) with {meshData.TextureCoordinates2.Count} coordinates");
                } else {
                    LodLogger.Info("[UV Preview] No UV1 (lightmap) coordinates, cleared UVImage2");
                }

            } catch (Exception ex) {
                LodLogger.Error(ex, "[UV Preview] Failed to update UV image");
            }
        }

        /// <summary>
        /// Рисует линию на bitmap (Bresenham's algorithm)
        /// </summary>
        private void DrawLine(byte[] pixels, int width, int height, Point p0, Point p1, byte r, byte g, byte b) {
            int x0 = (int)Math.Clamp(p0.X, 0, width - 1);
            int y0 = (int)Math.Clamp(p0.Y, 0, height - 1);
            int x1 = (int)Math.Clamp(p1.X, 0, width - 1);
            int y1 = (int)Math.Clamp(p1.Y, 0, height - 1);

            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true) {
                int idx = (y0 * width + x0) * 4;
                if (idx >= 0 && idx + 3 < pixels.Length) {
                    pixels[idx] = b;
                    pixels[idx + 1] = g;
                    pixels[idx + 2] = r;
                    pixels[idx + 3] = 255;
                }

                if (x0 == x1 && y0 == y1) break;

                int e2 = 2 * err;
                if (e2 > -dy) {
                    err -= dy;
                    x0 += sx;
                }
                if (e2 < dx) {
                    err += dx;
                    y0 += sy;
                }
            }
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
                _lodGlbData.Clear();
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
                // Загружаем текстуру если ещё не загружена
                if (_cachedAlbedoBrush == null) {
                    _cachedAlbedoBrush = FindAndLoadAlbedoTexture(fbxPath);
                }

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
                    // FBX загружается с PostProcessSteps.FlipUVs, поэтому нужно flipV: true
                    // чтобы отменить переворот для корректного отображения оригинальной развёртки
                    UpdateUVImage(meshWithUV, flipV: true);
                }

                LodLogger.Info("FBX model loaded successfully");

            } catch (Exception ex) {
                LodLogger.Error(ex, $"Failed to load FBX model: {fbxPath}");
            }
        }
    }
}
