using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace AssetProcessor {
    /// <summary>
    /// Логика для управления 3D viewer (pivot, wireframe, up vector, human silhouette)
    /// </summary>
    public partial class MainWindow {
        private CoordinateSystemVisual3D? _pivotVisual;
        private ModelVisual3D? _humanSilhouette; // Силуэт человека для масштаба
        private bool _isWireframeMode = false;
        private bool _isZUp = false; // Y-up по умолчанию
        private readonly List<LinesVisual3D> _wireframeLines = new(); // Линии для wireframe
        private readonly Dictionary<GeometryModel3D, Material> _originalMaterials = new(); // Оригинальные материалы

        /// <summary>
        /// Обработчик изменения чекбокса "Show Pivot"
        /// </summary>
        private void ShowPivotCheckBox_Changed(object sender, RoutedEventArgs e) {
            UpdatePivotVisibility();
        }

        /// <summary>
        /// Обновляет видимость и размер pivot visualization
        /// </summary>
        private void UpdatePivotVisibility() {
            bool showPivot = ShowPivotCheckBox.IsChecked == true;

            // Убираем старый pivot
            if (_pivotVisual != null && viewPort3d.Children.Contains(_pivotVisual)) {
                viewPort3d.Children.Remove(_pivotVisual);
            }

            if (showPivot) {
                // Вычисляем адаптивный размер на основе модели
                double pivotSize = CalculateOptimalPivotSize();

                // Создаём новый pivot с правильным размером
                _pivotVisual = new CoordinateSystemVisual3D {
                    ArrowLengths = pivotSize
                };

                // Применяем трансформацию up vector если нужно
                if (_isZUp) {
                    var rotation = new AxisAngleRotation3D(new Vector3D(1, 0, 0), -90);
                    _pivotVisual.Transform = new RotateTransform3D(rotation);
                }

                viewPort3d.Children.Add(_pivotVisual);
            }
        }

        /// <summary>
        /// Обработчик изменения чекбокса "Wireframe"
        /// </summary>
        private void ShowWireframeCheckBox_Changed(object sender, RoutedEventArgs e) {
            _isWireframeMode = ShowWireframeCheckBox.IsChecked == true;
            UpdateModelWireframe();
        }

        /// <summary>
        /// Обработчик изменения чекбокса "Show Human Silhouette"
        /// </summary>
        private void ShowHumanCheckBox_Changed(object sender, RoutedEventArgs e) {
            bool showHuman = ShowHumanCheckBox.IsChecked == true;

            if (showHuman) {
                UpdateHumanSilhouette();
            } else {
                // Убираем силуэт
                if (_humanSilhouette != null && viewPort3d.Children.Contains(_humanSilhouette)) {
                    viewPort3d.Children.Remove(_humanSilhouette);
                }
            }
        }

        /// <summary>
        /// Обновляет wireframe режим для всех моделей в viewport
        /// </summary>
        private void UpdateModelWireframe() {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Удаляем все старые wireframe линии
            foreach (var wireframe in _wireframeLines) {
                viewPort3d.Children.Remove(wireframe);
            }
            _wireframeLines.Clear();

            if (_isWireframeMode) {
                int totalEdges = 0;
                // Создаём wireframe для всех моделей
                // ВАЖНО: ToList() создаёт копию коллекции, чтобы избежать InvalidOperationException
                // при добавлении wireframe линий в viewPort3d.Children во время итерации
                foreach (var visual in viewPort3d.Children.ToList()) {
                    if (visual is ModelVisual3D modelVisual && modelVisual.Content is Model3DGroup modelGroup) {
                        int edges = CreateWireframeForModelGroup(modelGroup);
                        totalEdges += edges;
                    }
                }
                sw.Stop();
                LodLogger.Info($"Wireframe created: {totalEdges} edges in {sw.ElapsedMilliseconds}ms");
            } else {
                // Восстанавливаем оригинальные материалы
                foreach (var kvp in _originalMaterials) {
                    kvp.Key.Material = kvp.Value;
                    // Восстанавливаем стандартный backMaterial
                    kvp.Key.BackMaterial = new DiffuseMaterial(new SolidColorBrush(Colors.Red));
                }
                _originalMaterials.Clear();
            }
        }

        /// <summary>
        /// Создаёт wireframe линии для Model3DGroup рекурсивно
        /// </summary>
        /// <returns>Количество созданных рёбер</returns>
        private int CreateWireframeForModelGroup(Model3DGroup modelGroup) {
            // ОПТИМИЗАЦИЯ: Сначала собираем все точки в List, потом создаём LinesVisual3D
            // Это быстрее чем добавлять в wireframe.Points напрямую
            var allPoints = new List<Point3D>();
            AddWireframeForGroup(modelGroup, allPoints);

            if (allPoints.Count > 0) {
                // КРИТИЧЕСКАЯ ОПТИМИЗАЦИЯ: Создаём Point3DCollection отдельно и замораживаем
                // Freeze() делает коллекцию read-only но ЗНАЧИТЕЛЬНО ускоряет рендеринг
                var points = new Point3DCollection(allPoints.Count);
                foreach (var pt in allPoints) {
                    points.Add(pt);
                }
                points.Freeze(); // Замораживаем для максимальной производительности

                // Создаём LinesVisual3D и назначаем замороженную коллекцию
                var wireframe = new LinesVisual3D {
                    Color = Colors.White,
                    Thickness = 1,
                    Points = points // Назначаем готовую замороженную коллекцию
                };
                wireframe.Transform = modelGroup.Transform;

                _wireframeLines.Add(wireframe);
                viewPort3d.Children.Add(wireframe);
                return allPoints.Count / 2; // Каждое ребро = 2 точки
            }
            return 0;
        }

        /// <summary>
        /// Рекурсивно добавляет wireframe линии для Model3DGroup
        /// </summary>
        private void AddWireframeForGroup(Model3DGroup modelGroup, List<Point3D> wireframePoints) {
            foreach (var child in modelGroup.Children) {
                if (child is GeometryModel3D geoModel && geoModel.Geometry is MeshGeometry3D mesh) {
                    // Сохраняем оригинальный материал
                    if (!_originalMaterials.ContainsKey(geoModel)) {
                        _originalMaterials[geoModel] = geoModel.Material;
                    }

                    // Делаем модель полупрозрачной/тёмной
                    var darkMaterial = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(50, 20, 20, 20)));
                    geoModel.Material = darkMaterial;
                    // BackMaterial = null для правильного culling (задние грани не рендерятся)
                    geoModel.BackMaterial = null;

                    // Оптимизация: используем Dictionary вместо HashSet для лучшей производительности
                    // Capacity = примерно triangles * 1.5 (каждое ребро разделяется двумя треугольниками)
                    var triangleCount = mesh.TriangleIndices.Count / 3;
                    var edges = new Dictionary<long, (int, int)>(capacity: triangleCount * 3 / 2);

                    // Собираем уникальные рёбра
                    for (int i = 0; i < mesh.TriangleIndices.Count; i += 3) {
                        if (i + 2 >= mesh.TriangleIndices.Count) break;

                        var i0 = mesh.TriangleIndices[i];
                        var i1 = mesh.TriangleIndices[i + 1];
                        var i2 = mesh.TriangleIndices[i + 2];

                        if (i0 >= mesh.Positions.Count || i1 >= mesh.Positions.Count || i2 >= mesh.Positions.Count)
                            continue;

                        // Добавляем три ребра треугольника
                        TryAddEdge(edges, i0, i1);
                        TryAddEdge(edges, i1, i2);
                        TryAddEdge(edges, i2, i0);
                    }

                    // Добавляем уникальные рёбра в wireframePoints (List - это быстро)
                    foreach (var edge in edges.Values) {
                        wireframePoints.Add(mesh.Positions[edge.Item1]);
                        wireframePoints.Add(mesh.Positions[edge.Item2]);
                    }

                } else if (child is Model3DGroup childGroup) {
                    AddWireframeForGroup(childGroup, wireframePoints);
                }
            }
        }

        /// <summary>
        /// Добавляет ребро в Dictionary (упорядоченная пара для избежания дублирования)
        /// Использует long key для быстрого поиска вместо tuple
        /// </summary>
        private void TryAddEdge(Dictionary<long, (int, int)> edges, int i0, int i1) {
            // Упорядочиваем индексы
            if (i0 > i1) {
                (i0, i1) = (i1, i0);
            }
            // Упаковываем два int в один long для быстрого ключа
            long key = ((long)i0 << 32) | (uint)i1;
            edges.TryAdd(key, (i0, i1));
        }

        /// <summary>
        /// Обработчик изменения Up Vector
        /// </summary>
        private void UpVectorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (UpVectorComboBox.SelectedIndex == -1) return;

            bool newIsZUp = UpVectorComboBox.SelectedIndex == 1; // 0=Y, 1=Z

            if (newIsZUp != _isZUp) {
                _isZUp = newIsZUp;
                ApplyUpVectorTransform();
            }
        }

        /// <summary>
        /// Применяет трансформацию для изменения up vector
        /// </summary>
        private void ApplyUpVectorTransform() {
            // ToList() для безопасности (хотя мы не добавляем/удаляем элементы)
            foreach (var visual in viewPort3d.Children.ToList()) {
                if (visual is ModelVisual3D modelVisual && modelVisual.Content is Model3DGroup modelGroup) {
                    Transform3DGroup currentTransform;

                    // ИСПРАВЛЕНИЕ: НЕ перезаписываем существующий Transform (содержит Y-flip для GLB)!
                    if (modelGroup.Transform is Transform3DGroup existingGroup) {
                        currentTransform = existingGroup;
                    } else if (modelGroup.Transform != null && modelGroup.Transform != Transform3D.Identity) {
                        // Есть другой transform (например ScaleTransform для Y-flip)
                        // Оборачиваем его в Transform3DGroup
                        currentTransform = new Transform3DGroup();
                        currentTransform.Children.Add(modelGroup.Transform);
                        modelGroup.Transform = currentTransform;
                    } else {
                        // Нет transform
                        currentTransform = new Transform3DGroup();
                        modelGroup.Transform = currentTransform;
                    }

                    // Удаляем старую up vector трансформацию (RotateTransform) если есть
                    Transform3D? upTransform = null;
                    foreach (var t in currentTransform.Children.ToList()) {
                        if (t is RotateTransform3D rotTransform && rotTransform.Rotation is AxisAngleRotation3D axisRot) {
                            // Проверяем что это rotation вокруг X (up vector transform)
                            if (Math.Abs(axisRot.Axis.X - 1.0) < 0.01 && Math.Abs(axisRot.Axis.Y) < 0.01) {
                                upTransform = t;
                                break;
                            }
                        }
                    }
                    if (upTransform != null) {
                        currentTransform.Children.Remove(upTransform);
                    }

                    // Добавляем новую up vector трансформацию
                    if (_isZUp) {
                        // Поворот на -90° вокруг X для преобразования Y-up в Z-up
                        var rotation = new AxisAngleRotation3D(new Vector3D(1, 0, 0), -90);
                        var rotateTransform = new RotateTransform3D(rotation);
                        // Добавляем ПОСЛЕ ScaleTransform (если он есть)
                        currentTransform.Children.Add(rotateTransform);
                    }
                }
            }

            // Также обновляем pivot если он есть
            if (_pivotVisual != null) {
                if (_isZUp) {
                    var rotation = new AxisAngleRotation3D(new Vector3D(1, 0, 0), -90);
                    _pivotVisual.Transform = new RotateTransform3D(rotation);
                } else {
                    _pivotVisual.Transform = Transform3D.Identity;
                }
            }
        }

        /// <summary>
        /// Применяет все настройки viewer к загруженной модели
        /// </summary>
        private void ApplyViewerSettingsToModel() {
            // Применяем wireframe если включён
            if (_isWireframeMode) {
                UpdateModelWireframe();
            }

            // Применяем up vector если нужно
            if (_isZUp) {
                ApplyUpVectorTransform();
            }

            // Применяем pivot если нужно
            UpdatePivotVisibility();

            // Применяем силуэт человека если включён
            if (ShowHumanCheckBox?.IsChecked == true) {
                UpdateHumanSilhouette();
            }
        }

        /// <summary>
        /// Создаёт или обновляет силуэт человека (1.8м) для понимания масштаба
        /// </summary>
        private void UpdateHumanSilhouette() {
            // Убираем старый силуэт
            if (_humanSilhouette != null && viewPort3d.Children.Contains(_humanSilhouette)) {
                viewPort3d.Children.Remove(_humanSilhouette);
            }

            // Создаём новый силуэт (простая капсула: цилиндр + сфера для головы)
            var silhouette = new Model3DGroup();

            // Тело (цилиндр 0.4м диаметр, 1.5м высота от 0 до 1.5)
            var bodyMesh = new MeshBuilder();
            bodyMesh.AddCylinder(new Point3D(0, 0.75, 0), new Point3D(0, 0, 0), 0.2, 16);

            // Голова (сфера 0.2м радиус на высоте 1.7м)
            bodyMesh.AddSphere(new Point3D(0, 1.7, 0), 0.2, 12, 12);

            var geometry = bodyMesh.ToMesh();

            // Используем EmissiveMaterial для unlit отображения (полупрозрачный зелёный)
            var material = new MaterialGroup();
            material.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(100, 50, 255, 50))));
            material.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(150, 50, 200, 50))));

            var model = new GeometryModel3D(geometry, material);
            model.BackMaterial = material;
            silhouette.Children.Add(model);

            _humanSilhouette = new ModelVisual3D { Content = silhouette };

            // Применяем трансформацию up vector если нужно
            if (_isZUp) {
                var rotation = new AxisAngleRotation3D(new Vector3D(1, 0, 0), -90);
                _humanSilhouette.Transform = new RotateTransform3D(rotation);
            }

            viewPort3d.Children.Add(_humanSilhouette);
        }

        /// <summary>
        /// Вычисляет оптимальный размер pivot на основе размеров модели
        /// </summary>
        private double CalculateOptimalPivotSize() {
            double maxDimension = 0;

            // Находим максимальный размер модели
            foreach (var visual in viewPort3d.Children) {
                if (visual is ModelVisual3D modelVisual && modelVisual.Content is Model3DGroup modelGroup) {
                    var bounds = modelGroup.Bounds;
                    if (bounds != Rect3D.Empty) {
                        double sizeX = bounds.SizeX;
                        double sizeY = bounds.SizeY;
                        double sizeZ = bounds.SizeZ;
                        maxDimension = Math.Max(maxDimension, Math.Max(sizeX, Math.Max(sizeY, sizeZ)));
                    }
                }
            }

            // Pivot = 20% от максимального размера модели (но не меньше 0.5 и не больше 5)
            double pivotSize = Math.Max(0.5, Math.Min(5.0, maxDimension * 0.2));
            return pivotSize;
        }
    }
}
