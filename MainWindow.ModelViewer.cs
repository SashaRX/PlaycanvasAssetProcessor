using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace AssetProcessor {
    /// <summary>
    /// Логика для управления 3D viewer (pivot, wireframe, up vector, human silhouette)
    /// </summary>
    public partial class MainWindow {
        private ModelVisual3D? _pivotVisual; // Изменено с CoordinateSystemVisual3D на ModelVisual3D для emissive pivot
        private ModelVisual3D? _humanSilhouette; // Силуэт человека для масштаба (billboard плоскость)
        private RotateTransform3D? _humanBillboardRotation; // Rotation для billboard эффекта
        private double _humanSilhouetteOffsetX = 2.0; // Адаптивное смещение billboard по X
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
            // Проверка что viewport инициализирован
            if (viewPort3d == null) return;

            bool showPivot = ShowPivotCheckBox.IsChecked == true;

            // Убираем старый pivot
            if (_pivotVisual != null && viewPort3d.Children.Contains(_pivotVisual)) {
                viewPort3d.Children.Remove(_pivotVisual);
            }

            if (showPivot) {
                // Вычисляем адаптивный размер на основе модели
                double pivotSize = CalculateOptimalPivotSize();

                // Создаём новый emissive pivot с правильным размером
                _pivotVisual = CreateEmissivePivot(pivotSize);

                // Применяем трансформацию up vector если нужно
                if (_isZUp) {
                    var rotation = new AxisAngleRotation3D(new Vector3D(1, 0, 0), -90);
                    _pivotVisual.Transform = new RotateTransform3D(rotation);
                }

                viewPort3d.Children.Add(_pivotVisual);
            }
        }

        /// <summary>
        /// Создаёт unshaded (emissive) pivot с 3 стрелками X, Y, Z
        /// </summary>
        private ModelVisual3D CreateEmissivePivot(double size) {
            var pivot = new Model3DGroup();
            double arrowRadius = size * 0.02; // Толщина стрелок
            double coneHeight = size * 0.15; // Высота конуса
            double coneRadius = size * 0.05; // Радиус конуса

            // X axis (Red)
            pivot.Children.Add(CreateArrow(
                new Point3D(0, 0, 0),
                new Point3D(size, 0, 0),
                arrowRadius,
                coneHeight,
                coneRadius,
                Colors.Red
            ));

            // Y axis (Green)
            pivot.Children.Add(CreateArrow(
                new Point3D(0, 0, 0),
                new Point3D(0, size, 0),
                arrowRadius,
                coneHeight,
                coneRadius,
                Colors.Lime
            ));

            // Z axis (Blue)
            pivot.Children.Add(CreateArrow(
                new Point3D(0, 0, 0),
                new Point3D(0, 0, size),
                arrowRadius,
                coneHeight,
                coneRadius,
                Colors.Blue
            ));

            return new ModelVisual3D { Content = pivot };
        }

        /// <summary>
        /// Создаёт одну стрелку с emissive материалом
        /// </summary>
        private GeometryModel3D CreateArrow(Point3D start, Point3D end, double shaftRadius, double coneHeight, double coneRadius, Color color) {
            var mesh = new MeshBuilder();
            var direction = new Vector3D(end.X - start.X, end.Y - start.Y, end.Z - start.Z);
            var length = direction.Length;
            direction.Normalize();

            // Стержень стрелки
            var shaftEnd = new Point3D(
                start.X + direction.X * (length - coneHeight),
                start.Y + direction.Y * (length - coneHeight),
                start.Z + direction.Z * (length - coneHeight)
            );
            mesh.AddCylinder(start, shaftEnd, shaftRadius, 8);

            // Конус стрелки (baseRadius, topRadius=0 для острого конуса, height, caps, thetaDiv)
            mesh.AddCone(shaftEnd, direction, coneRadius, 0, coneHeight, false, false, 12);

            var geometry = mesh.ToMesh();

            // Emissive материал для unlit отображения
            var material = new EmissiveMaterial(new SolidColorBrush(color));

            return new GeometryModel3D(geometry, material);
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
            // Проверка что viewport инициализирован
            if (viewPort3d == null) return;

            bool showHuman = ShowHumanCheckBox.IsChecked == true;

            if (showHuman) {
                UpdateHumanSilhouette();
            } else {
                // Останавливаем billboard обновление
                StopBillboardUpdate();

                // Убираем силуэт
                if (_humanSilhouette != null && viewPort3d.Children.Contains(_humanSilhouette)) {
                    viewPort3d.Children.Remove(_humanSilhouette);
                }
                _humanSilhouette = null;
                _humanBillboardRotation = null;
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
                // ВАЖНО: Пропускаем billboard человека - он всегда в мировом Y-up пространстве!
                if (visual == _humanSilhouette) {
                    continue;
                }

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
                        // Поворот на +90° вокруг X для преобразования Y-up в Z-up (Z вверх, Y вперёд)
                        var rotation = new AxisAngleRotation3D(new Vector3D(1, 0, 0), 90);
                        var rotateTransform = new RotateTransform3D(rotation);
                        // Добавляем ПОСЛЕ ScaleTransform (если он есть)
                        currentTransform.Children.Add(rotateTransform);
                    }
                }
            }

            // Также обновляем pivot если он есть
            if (_pivotVisual != null) {
                if (_isZUp) {
                    var rotation = new AxisAngleRotation3D(new Vector3D(1, 0, 0), 90);
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
        /// Создаёт или обновляет силуэт человека (1.8м) как billboard плоскость 1:2
        /// </summary>
        private void UpdateHumanSilhouette() {
            // Проверка что viewport инициализирован
            if (viewPort3d == null) return;

            // Убираем старый силуэт
            if (_humanSilhouette != null && viewPort3d.Children.Contains(_humanSilhouette)) {
                viewPort3d.Children.Remove(_humanSilhouette);
            }

            // Создаём ВЕРТИКАЛЬНУЮ плоскость 1:2 (0.9м x 1.8м)
            // Плоскость стоит вертикально в мировом Y-up пространстве
            var silhouette = new Model3DGroup();
            var planeMesh = new MeshBuilder();

            // ВЕРТИКАЛЬНАЯ плоскость в YZ (X=0), высота по Z - для Z-up системы!
            // Размер: 0.9м (ширина) x 1.8м (высота) - пропорции человека
            var p0 = new Point3D(0, -0.45, 0);   // нижний левый
            var p1 = new Point3D(0, 0.45, 0);    // нижний правый
            var p2 = new Point3D(0, 0.45, 1.8);  // верхний правый (высота 1.8м!)
            var p3 = new Point3D(0, -0.45, 1.8); // верхний левый

            planeMesh.Positions.Add(p0);
            planeMesh.Positions.Add(p1);
            planeMesh.Positions.Add(p2);
            planeMesh.Positions.Add(p3);

            // Нормали смотрят вперед по +X (перпендикулярно плоскости YZ)
            var normal = new Vector3D(1, 0, 0);
            planeMesh.Normals.Add(normal);
            planeMesh.Normals.Add(normal);
            planeMesh.Normals.Add(normal);
            planeMesh.Normals.Add(normal);

            // UV координаты
            planeMesh.TextureCoordinates.Add(new Point(0, 1));
            planeMesh.TextureCoordinates.Add(new Point(1, 1));
            planeMesh.TextureCoordinates.Add(new Point(1, 0));
            planeMesh.TextureCoordinates.Add(new Point(0, 0));

            // Два треугольника (CCW winding order)
            planeMesh.TriangleIndices.Add(0);
            planeMesh.TriangleIndices.Add(1);
            planeMesh.TriangleIndices.Add(2);
            planeMesh.TriangleIndices.Add(0);
            planeMesh.TriangleIndices.Add(2);
            planeMesh.TriangleIndices.Add(3);

            var geometry = planeMesh.ToMesh();

            // Загружаем текстуру refman.png
            Material material;
            try {
                var uri = new Uri("pack://application:,,,/refman.png");
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = uri;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                bitmap.EndInit();
                bitmap.Freeze();

                var brush = new ImageBrush(bitmap) {
                    Opacity = 1.0, // Полная непрозрачность brush, альфа берется из PNG
                    Stretch = Stretch.Fill,
                    ViewportUnits = BrushMappingMode.Absolute
                };

                // Только EmissiveMaterial для unlit отображения с правильной прозрачностью
                material = new EmissiveMaterial(brush);

                LodLogger.Info("Loaded refman.png texture for billboard");
            } catch (Exception ex) {
                LodLogger.Warn($"Failed to load refman.png: {ex.Message}");
                var fallbackBrush = new SolidColorBrush(Color.FromArgb(255, 0, 255, 0));
                material = new EmissiveMaterial(fallbackBrush);
            }

            var model = new GeometryModel3D(geometry, material){
                BackMaterial = material // Двусторонний рендеринг
            };

            silhouette.Children.Add(model);

            _humanSilhouette = new ModelVisual3D { Content = silhouette };

            // Вычисляем адаптивное смещение на основе размеров модели
            _humanSilhouetteOffsetX = CalculateHumanSilhouetteOffset();

            // Billboard rotation (только Y-axis, в мировом пространстве, БЕЗ up vector transform)
            // Плоскость в XY plane (Z=0), смещаем в сторону по X, поворачиваем вокруг Y чтобы смотрел на камеру
            var transformGroup = new Transform3DGroup();
            _humanBillboardRotation = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0));
            transformGroup.Children.Add(_humanBillboardRotation);
            transformGroup.Children.Add(new TranslateTransform3D(_humanSilhouetteOffsetX, 0, 0)); // Смещение в сторону по X
            _humanSilhouette.Transform = transformGroup;

            viewPort3d.Children.Add(_humanSilhouette);

            // Запускаем billboard обновление если ещё не запущено
            StartBillboardUpdate();
        }

        /// <summary>
        /// Вычисляет адаптивное смещение для силуэта человека на основе размеров модели
        /// </summary>
        private double CalculateHumanSilhouetteOffset() {
            double maxDimension = 1.0; // Значение по умолчанию

            // Находим максимальный размер модели
            foreach (var visual in viewPort3d.Children) {
                if (visual == _humanSilhouette) continue; // Пропускаем сам силуэт

                if (visual is ModelVisual3D modelVisual && modelVisual.Content is Model3DGroup modelGroup) {
                    var bounds = modelGroup.Bounds;
                    if (bounds != Rect3D.Empty) {
                        double sizeX = bounds.SizeX;
                        double sizeZ = bounds.SizeZ;
                        maxDimension = Math.Max(maxDimension, Math.Max(sizeX, sizeZ));
                    }
                }
            }

            // Смещаем на 150% от максимального размера модели (чтобы силуэт был сбоку, но видно)
            return maxDimension * 1.5;
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

        /// <summary>
        /// Запускает обновление billboard rotation (cylindrical billboard - только горизонтальный поворот)
        /// </summary>
        private void StartBillboardUpdate() {
            // Подписываемся на CompositionTarget.Rendering только если ещё не подписаны
            // Проверяем через weak event чтобы не создавать дубликаты
            System.Windows.Media.CompositionTarget.Rendering -= UpdateBillboard;
            System.Windows.Media.CompositionTarget.Rendering += UpdateBillboard;
        }

        /// <summary>
        /// Останавливает обновление billboard rotation
        /// </summary>
        private void StopBillboardUpdate() {
            System.Windows.Media.CompositionTarget.Rendering -= UpdateBillboard;
        }

        /// <summary>
        /// Обновляет billboard rotation каждый кадр (cylindrical billboard - только Y-axis rotation в мировом пространстве)
        /// </summary>
        private void UpdateBillboard(object? sender, EventArgs e) {
            // Skip when window is inactive to prevent GPU conflicts during Alt+Tab
            if (!_isWindowActive) {
                return;
            }

            // Проверяем что всё инициализировано
            if (_humanSilhouette == null || _humanBillboardRotation == null || viewPort3d?.Camera == null) {
                return;
            }

            // Получаем позицию камеры
            var camera = viewPort3d.Camera as PerspectiveCamera;
            if (camera == null) return;

            var cameraPos = camera.Position;
            var billboardPos = new Point3D(_humanSilhouetteOffsetX, 0, 0); // Адаптивное смещение в сторону по X

            // Вычисляем направление от billboard к камере
            var direction = cameraPos - billboardPos;

            // Cylindrical billboard: вращение вокруг Z (вертикальной оси в Z-up системе)
            // Проецируем на XY плоскость (убираем Z компоненту)
            direction.Z = 0;

            // Вычисляем угол на XY плоскости вокруг Z оси
            double angle = Math.Atan2(direction.Y, direction.X) * (180.0 / Math.PI);

            // Поворачиваем вокруг Z (вертикальная ось в Z-up)
            if (_humanBillboardRotation.Rotation is AxisAngleRotation3D rotation)
            {
                rotation.Axis = new Vector3D(0, 0, 1);
                rotation.Angle = angle;
            }
        }

        /// <summary>
        /// ScrollViewer PreviewMouseWheel - backup handler (main zoom is via ComponentDispatcher).
        /// </summary>
        private void ModelViewerScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
            // Main zoom handling is in ComponentDispatcher_ThreadFilterMessage
            // This is a backup in case WPF events reach here
            var mousePos = e.GetPosition(viewPort3d);
            if (mousePos.X >= 0 && mousePos.Y >= 0 &&
                mousePos.X <= viewPort3d.ActualWidth && mousePos.Y <= viewPort3d.ActualHeight) {
                ZoomCamera(e.Delta);
                e.Handled = true;
            }
        }

        /// <summary>
        /// Tunneling PreviewMouseWheel handler on Border - backup handler.
        /// </summary>
        private void HelixViewportBorder_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
            ZoomCamera(e.Delta);
            e.Handled = true;
        }

        /// <summary>
        /// Zooms the camera by the specified wheel delta.
        /// </summary>
        private void ZoomCamera(int wheelDelta) {
            if (viewPort3d.Camera is not PerspectiveCamera camera) {
                return;
            }

            // Calculate zoom delta (positive = zoom in, negative = zoom out)
            double zoomDelta = wheelDelta / 120.0 * 0.1; // 120 = standard wheel delta

            // Get current look direction and distance
            var lookDir = camera.LookDirection;
            double currentDistance = lookDir.Length;

            // Calculate new distance (minimum 0.1 to avoid flipping)
            double newDistance = Math.Max(0.1, currentDistance * (1 - zoomDelta));

            // Move camera along look direction
            lookDir.Normalize();
            var target = camera.Position + camera.LookDirection;
            camera.Position = target - lookDir * newDistance;
            camera.LookDirection = lookDir * newDistance;
        }

        /// <summary>
        /// Bubbling MouseWheel handler - fires AFTER HelixViewport3D has processed the event.
        /// Just mark as handled to prevent parent ScrollViewer from scrolling.
        /// </summary>
        private void HelixViewportBorder_MouseWheel(object sender, MouseWheelEventArgs e) {
            // HelixViewport3D has already handled the zoom by this point (bubbling phase)
            // Mark as handled to prevent ScrollViewer from scrolling
            e.Handled = true;
        }
    }
}
