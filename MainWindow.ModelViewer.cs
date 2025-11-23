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
                        ArrowLengths = 20, // Длина осей
                        Diameter = 0.5,    // Толщина осей
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
            foreach (var visual in viewPort3d.Children) {
                if (visual is ModelVisual3D modelVisual && modelVisual.Content is Model3DGroup modelGroup) {
                    ApplyWireframeToModelGroup(modelGroup, _isWireframeMode);
                }
            }
        }

        /// <summary>
        /// Применяет wireframe режим к Model3DGroup рекурсивно
        /// </summary>
        private void ApplyWireframeToModelGroup(Model3DGroup modelGroup, bool wireframe) {
            foreach (var child in modelGroup.Children) {
                if (child is GeometryModel3D geoModel) {
                    if (wireframe) {
                        // Wireframe: тёмный материал + белые рёбра
                        var wireframeMaterial = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(30, 30, 30)));
                        var edgeMaterial = new EmissiveMaterial(new SolidColorBrush(Colors.White));

                        var materialGroup = new MaterialGroup();
                        materialGroup.Children.Add(wireframeMaterial);
                        materialGroup.Children.Add(edgeMaterial);

                        geoModel.Material = materialGroup;
                        geoModel.BackMaterial = null; // Отключаем backface culling для wireframe
                    } else {
                        // Solid mode: восстанавливаем нормальные материалы
                        RestoreNormalMaterial(geoModel);
                    }
                } else if (child is Model3DGroup childGroup) {
                    ApplyWireframeToModelGroup(childGroup, wireframe);
                }
            }
        }

        /// <summary>
        /// Восстанавливает нормальный материал для модели
        /// </summary>
        private void RestoreNormalMaterial(GeometryModel3D geoModel) {
            var frontMaterial = new DiffuseMaterial(new SolidColorBrush(Colors.LightGray));
            var backMaterial = new DiffuseMaterial(new SolidColorBrush(Colors.Red));
            var emissiveMaterial = new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(40, 40, 40)));

            var materialGroup = new MaterialGroup();
            materialGroup.Children.Add(frontMaterial);
            materialGroup.Children.Add(emissiveMaterial);

            geoModel.Material = materialGroup;
            geoModel.BackMaterial = backMaterial;
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
