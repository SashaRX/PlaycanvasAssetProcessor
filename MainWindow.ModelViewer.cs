using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace AssetProcessor {
    /// <summary>
    /// 3D viewer controls: pivot, up vector, camera zoom, viewer settings.
    /// Wireframe and human silhouette are in ModelViewer.Overlays.cs.
    /// </summary>
    public partial class MainWindow {
        private ModelVisual3D? _pivotVisual;
        private ModelVisual3D? _humanSilhouette;
        private RotateTransform3D? _humanBillboardRotation;
        private double _humanSilhouetteOffsetX = 2.0;
        private bool _isWireframeMode = false;
        private bool _isZUp = false;
        private readonly List<LinesVisual3D> _wireframeLines = new();
        private readonly Dictionary<GeometryModel3D, Material> _originalMaterials = new();

        #region Pivot

        private void ShowPivotCheckBox_Changed(object sender, RoutedEventArgs e) {
            UpdatePivotVisibility();
        }

        private void UpdatePivotVisibility() {
            if (viewPort3d == null) return;

            bool showPivot = viewModel.IsShowPivotChecked;

            if (_pivotVisual != null && viewPort3d.Children.Contains(_pivotVisual)) {
                viewPort3d.Children.Remove(_pivotVisual);
            }

            if (showPivot) {
                double pivotSize = CalculateOptimalPivotSize();
                _pivotVisual = CreateEmissivePivot(pivotSize);

                if (_isZUp) {
                    _pivotVisual.Transform = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), -90));
                }

                viewPort3d.Children.Add(_pivotVisual);
            }
        }

        private ModelVisual3D CreateEmissivePivot(double size) {
            var pivot = new Model3DGroup();
            double arrowRadius = size * 0.02;
            double coneHeight = size * 0.15;
            double coneRadius = size * 0.05;

            pivot.Children.Add(CreateArrow(new Point3D(0, 0, 0), new Point3D(size, 0, 0), arrowRadius, coneHeight, coneRadius, Colors.Red));
            pivot.Children.Add(CreateArrow(new Point3D(0, 0, 0), new Point3D(0, size, 0), arrowRadius, coneHeight, coneRadius, Colors.Lime));
            pivot.Children.Add(CreateArrow(new Point3D(0, 0, 0), new Point3D(0, 0, size), arrowRadius, coneHeight, coneRadius, Colors.Blue));

            return new ModelVisual3D { Content = pivot };
        }

        private GeometryModel3D CreateArrow(Point3D start, Point3D end, double shaftRadius, double coneHeight, double coneRadius, Color color) {
            var mesh = new MeshBuilder();
            var direction = new Vector3D(end.X - start.X, end.Y - start.Y, end.Z - start.Z);
            var length = direction.Length;
            direction.Normalize();

            var shaftEnd = new Point3D(
                start.X + direction.X * (length - coneHeight),
                start.Y + direction.Y * (length - coneHeight),
                start.Z + direction.Z * (length - coneHeight));
            mesh.AddCylinder(start, shaftEnd, shaftRadius, 8);
            mesh.AddCone(shaftEnd, direction, coneRadius, 0, coneHeight, false, false, 12);

            return new GeometryModel3D(mesh.ToMesh(), new EmissiveMaterial(new SolidColorBrush(color)));
        }

        private double CalculateOptimalPivotSize() {
            double maxDimension = 0;

            foreach (var visual in viewPort3d.Children) {
                if (visual is ModelVisual3D modelVisual && modelVisual.Content is Model3DGroup modelGroup) {
                    var bounds = modelGroup.Bounds;
                    if (bounds != Rect3D.Empty) {
                        maxDimension = Math.Max(maxDimension, Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ)));
                    }
                }
            }

            return Math.Max(0.5, Math.Min(5.0, maxDimension * 0.2));
        }

        #endregion

        #region Up Vector

        private void UpVectorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (UpVectorComboBox.SelectedIndex == -1) return;

            bool newIsZUp = UpVectorComboBox.SelectedIndex == 1;
            if (newIsZUp != _isZUp) {
                _isZUp = newIsZUp;
                ApplyUpVectorTransform();
            }
        }

        private void ApplyUpVectorTransform() {
            foreach (var visual in viewPort3d.Children.ToList()) {
                if (visual == _humanSilhouette) continue;

                if (visual is ModelVisual3D modelVisual && modelVisual.Content is Model3DGroup modelGroup) {
                    Transform3DGroup currentTransform;

                    if (modelGroup.Transform is Transform3DGroup existingGroup) {
                        currentTransform = existingGroup;
                    } else if (modelGroup.Transform != null && modelGroup.Transform != Transform3D.Identity) {
                        currentTransform = new Transform3DGroup();
                        currentTransform.Children.Add(modelGroup.Transform);
                        modelGroup.Transform = currentTransform;
                    } else {
                        currentTransform = new Transform3DGroup();
                        modelGroup.Transform = currentTransform;
                    }

                    // Remove old up vector rotation
                    Transform3D? upTransform = null;
                    foreach (var t in currentTransform.Children.ToList()) {
                        if (t is RotateTransform3D rotTransform && rotTransform.Rotation is AxisAngleRotation3D axisRot) {
                            if (Math.Abs(axisRot.Axis.X - 1.0) < 0.01 && Math.Abs(axisRot.Axis.Y) < 0.01) {
                                upTransform = t;
                                break;
                            }
                        }
                    }
                    if (upTransform != null) {
                        currentTransform.Children.Remove(upTransform);
                    }

                    if (_isZUp) {
                        currentTransform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 90)));
                    }
                }
            }

            if (_pivotVisual != null) {
                _pivotVisual.Transform = _isZUp
                    ? new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 90))
                    : Transform3D.Identity;
            }
        }

        #endregion

        #region Viewer Settings

        private void ApplyViewerSettingsToModel() {
            if (_isWireframeMode) UpdateModelWireframe();
            if (_isZUp) ApplyUpVectorTransform();
            UpdatePivotVisibility();
            if (viewModel.IsShowHumanChecked) UpdateHumanSilhouette();
        }

        #endregion

        #region Camera Zoom

        private void ModelViewerScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
            var mousePos = e.GetPosition(viewPort3d);
            if (mousePos.X >= 0 && mousePos.Y >= 0 &&
                mousePos.X <= viewPort3d.ActualWidth && mousePos.Y <= viewPort3d.ActualHeight) {
                ZoomCamera(e.Delta);
                e.Handled = true;
            }
        }

        private void HelixViewportBorder_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
            ZoomCamera(e.Delta);
            e.Handled = true;
        }

        private void ZoomCamera(int wheelDelta) {
            if (viewPort3d.Camera is not PerspectiveCamera camera) return;

            double zoomDelta = wheelDelta / 120.0 * 0.1;
            var lookDir = camera.LookDirection;
            double currentDistance = lookDir.Length;
            double newDistance = Math.Max(0.1, currentDistance * (1 - zoomDelta));

            lookDir.Normalize();
            var target = camera.Position + camera.LookDirection;
            camera.Position = target - lookDir * newDistance;
            camera.LookDirection = lookDir * newDistance;
        }

        private void HelixViewportBorder_MouseWheel(object sender, MouseWheelEventArgs e) {
            e.Handled = true;
        }

        #endregion
    }
}
