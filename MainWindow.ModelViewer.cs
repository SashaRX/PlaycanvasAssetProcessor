using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace AssetProcessor {
    /// <summary>
    /// Логика для управления 3D viewer (pivot, wireframe, up vector)
    /// </summary>
    public partial class MainWindow {
        private CoordinateSystemVisual3D? _pivotVisual;
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
        /// Обновляет видимость pivot visualization
        /// </summary>
        private void UpdatePivotVisibility() {
            bool showPivot = ShowPivotCheckBox.IsChecked == true;

            if (showPivot) {
                // Создаём pivot если ещё не создан
                if (_pivotVisual == null) {
                    _pivotVisual = new CoordinateSystemVisual3D {
                        ArrowLengths = 2 // Длина осей (уменьшено с 20 до 2)
                    };
                }

                // Добавляем в viewport если ещё не добавлен
                if (!viewPort3d.Children.Contains(_pivotVisual)) {
                    viewPort3d.Children.Add(_pivotVisual);
                }
            } else {
                // Удаляем pivot из viewport
                if (_pivotVisual != null && viewPort3d.Children.Contains(_pivotVisual)) {
                    viewPort3d.Children.Remove(_pivotVisual);
                }
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
        /// Обновляет wireframe режим для всех моделей в viewport
        /// </summary>
        private void UpdateModelWireframe() {
            // Удаляем все старые wireframe линии
            foreach (var wireframe in _wireframeLines) {
                viewPort3d.Children.Remove(wireframe);
            }
            _wireframeLines.Clear();

            if (_isWireframeMode) {
                // Создаём wireframe для всех моделей
                foreach (var visual in viewPort3d.Children) {
                    if (visual is ModelVisual3D modelVisual && modelVisual.Content is Model3DGroup modelGroup) {
                        CreateWireframeForModelGroup(modelGroup);
                    }
                }
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
        private void CreateWireframeForModelGroup(Model3DGroup modelGroup) {
            foreach (var child in modelGroup.Children) {
                if (child is GeometryModel3D geoModel && geoModel.Geometry is MeshGeometry3D mesh) {
                    // Сохраняем оригинальный материал
                    if (!_originalMaterials.ContainsKey(geoModel)) {
                        _originalMaterials[geoModel] = geoModel.Material;
                    }

                    // Делаем модель полупрозрачной/тёмной
                    var darkMaterial = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(50, 20, 20, 20)));
                    geoModel.Material = darkMaterial;
                    geoModel.BackMaterial = darkMaterial;

                    // Создаём линии для всех рёбер треугольников
                    var wireframe = new LinesVisual3D {
                        Color = Colors.White,
                        Thickness = 1
                    };

                    // Применяем ту же трансформацию что и у модели
                    wireframe.Transform = modelGroup.Transform;

                    // Добавляем рёбра для каждого треугольника
                    for (int i = 0; i < mesh.TriangleIndices.Count; i += 3) {
                        if (i + 2 >= mesh.TriangleIndices.Count) break;

                        var i0 = mesh.TriangleIndices[i];
                        var i1 = mesh.TriangleIndices[i + 1];
                        var i2 = mesh.TriangleIndices[i + 2];

                        if (i0 >= mesh.Positions.Count || i1 >= mesh.Positions.Count || i2 >= mesh.Positions.Count)
                            continue;

                        var p0 = mesh.Positions[i0];
                        var p1 = mesh.Positions[i1];
                        var p2 = mesh.Positions[i2];

                        // Три ребра треугольника
                        wireframe.Points.Add(p0);
                        wireframe.Points.Add(p1);

                        wireframe.Points.Add(p1);
                        wireframe.Points.Add(p2);

                        wireframe.Points.Add(p2);
                        wireframe.Points.Add(p0);
                    }

                    _wireframeLines.Add(wireframe);
                    viewPort3d.Children.Add(wireframe);

                } else if (child is Model3DGroup childGroup) {
                    CreateWireframeForModelGroup(childGroup);
                }
            }
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
            foreach (var visual in viewPort3d.Children) {
                if (visual is ModelVisual3D modelVisual && modelVisual.Content is Model3DGroup modelGroup) {
                    var currentTransform = modelGroup.Transform as Transform3DGroup;

                    if (currentTransform == null) {
                        currentTransform = new Transform3DGroup();
                        modelGroup.Transform = currentTransform;
                    }

                    // Удаляем старую up vector трансформацию если есть
                    Transform3D? upTransform = null;
                    foreach (var t in currentTransform.Children) {
                        if (t is RotateTransform3D rotTransform && rotTransform.Rotation is AxisAngleRotation3D) {
                            upTransform = t;
                            break;
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
                        currentTransform.Children.Insert(0, rotateTransform); // В начало
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
        }
    }
}
