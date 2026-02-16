using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace AssetProcessor {
    /// <summary>
    /// 3D viewer overlays: wireframe rendering and human silhouette billboard.
    /// </summary>
    public partial class MainWindow {

        #region Wireframe

        private void ShowWireframeCheckBox_Changed(object sender, RoutedEventArgs e) {
            _isWireframeMode = viewModel.IsShowWireframeChecked;
            UpdateModelWireframe();
        }

        private void UpdateModelWireframe() {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            foreach (var wireframe in _wireframeLines) {
                viewPort3d.Children.Remove(wireframe);
            }
            _wireframeLines.Clear();

            if (_isWireframeMode) {
                int totalEdges = 0;
                foreach (var visual in viewPort3d.Children.ToList()) {
                    if (visual is ModelVisual3D modelVisual && modelVisual.Content is Model3DGroup modelGroup) {
                        totalEdges += CreateWireframeForModelGroup(modelGroup);
                    }
                }
                sw.Stop();
                LodLogger.Info($"Wireframe created: {totalEdges} edges in {sw.ElapsedMilliseconds}ms");
            } else {
                foreach (var kvp in _originalMaterials) {
                    kvp.Key.Material = kvp.Value;
                    kvp.Key.BackMaterial = new DiffuseMaterial(new SolidColorBrush(Colors.Red));
                }
                _originalMaterials.Clear();
            }
        }

        private int CreateWireframeForModelGroup(Model3DGroup modelGroup) {
            var allPoints = new List<Point3D>();
            AddWireframeForGroup(modelGroup, allPoints);

            if (allPoints.Count > 0) {
                var points = new Point3DCollection(allPoints.Count);
                foreach (var pt in allPoints) {
                    points.Add(pt);
                }
                points.Freeze();

                var wireframe = new LinesVisual3D {
                    Color = Colors.White,
                    Thickness = 1,
                    Points = points
                };
                wireframe.Transform = modelGroup.Transform;

                _wireframeLines.Add(wireframe);
                viewPort3d.Children.Add(wireframe);
                return allPoints.Count / 2;
            }
            return 0;
        }

        private void AddWireframeForGroup(Model3DGroup modelGroup, List<Point3D> wireframePoints) {
            foreach (var child in modelGroup.Children) {
                if (child is GeometryModel3D geoModel && geoModel.Geometry is MeshGeometry3D mesh) {
                    if (!_originalMaterials.ContainsKey(geoModel)) {
                        _originalMaterials[geoModel] = geoModel.Material;
                    }

                    geoModel.Material = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(50, 20, 20, 20)));
                    geoModel.BackMaterial = null;

                    var triangleCount = mesh.TriangleIndices.Count / 3;
                    var edges = new Dictionary<long, (int, int)>(capacity: triangleCount * 3 / 2);

                    for (int i = 0; i < mesh.TriangleIndices.Count; i += 3) {
                        if (i + 2 >= mesh.TriangleIndices.Count) break;

                        var i0 = mesh.TriangleIndices[i];
                        var i1 = mesh.TriangleIndices[i + 1];
                        var i2 = mesh.TriangleIndices[i + 2];

                        if (i0 >= mesh.Positions.Count || i1 >= mesh.Positions.Count || i2 >= mesh.Positions.Count)
                            continue;

                        TryAddEdge(edges, i0, i1);
                        TryAddEdge(edges, i1, i2);
                        TryAddEdge(edges, i2, i0);
                    }

                    foreach (var edge in edges.Values) {
                        wireframePoints.Add(mesh.Positions[edge.Item1]);
                        wireframePoints.Add(mesh.Positions[edge.Item2]);
                    }
                } else if (child is Model3DGroup childGroup) {
                    AddWireframeForGroup(childGroup, wireframePoints);
                }
            }
        }

        private void TryAddEdge(Dictionary<long, (int, int)> edges, int i0, int i1) {
            if (i0 > i1) (i0, i1) = (i1, i0);
            long key = ((long)i0 << 32) | (uint)i1;
            edges.TryAdd(key, (i0, i1));
        }

        #endregion

        #region Human Silhouette Billboard

        private void ShowHumanCheckBox_Changed(object sender, RoutedEventArgs e) {
            if (viewPort3d == null) return;

            bool showHuman = viewModel.IsShowHumanChecked;

            if (showHuman) {
                UpdateHumanSilhouette();
            } else {
                StopBillboardUpdate();
                if (_humanSilhouette != null && viewPort3d.Children.Contains(_humanSilhouette)) {
                    viewPort3d.Children.Remove(_humanSilhouette);
                }
                _humanSilhouette = null;
                _humanBillboardRotation = null;
            }
        }

        private void UpdateHumanSilhouette() {
            if (viewPort3d == null) return;

            if (_humanSilhouette != null && viewPort3d.Children.Contains(_humanSilhouette)) {
                viewPort3d.Children.Remove(_humanSilhouette);
            }

            var silhouette = new Model3DGroup();
            var planeMesh = new MeshBuilder();

            // Vertical plane in YZ (X=0), 0.9m x 1.8m
            planeMesh.Positions.Add(new Point3D(0, -0.45, 0));
            planeMesh.Positions.Add(new Point3D(0, 0.45, 0));
            planeMesh.Positions.Add(new Point3D(0, 0.45, 1.8));
            planeMesh.Positions.Add(new Point3D(0, -0.45, 1.8));

            var normal = new Vector3D(1, 0, 0);
            for (int i = 0; i < 4; i++) planeMesh.Normals.Add(normal);

            planeMesh.TextureCoordinates.Add(new Point(0, 1));
            planeMesh.TextureCoordinates.Add(new Point(1, 1));
            planeMesh.TextureCoordinates.Add(new Point(1, 0));
            planeMesh.TextureCoordinates.Add(new Point(0, 0));

            planeMesh.TriangleIndices.Add(0); planeMesh.TriangleIndices.Add(1); planeMesh.TriangleIndices.Add(2);
            planeMesh.TriangleIndices.Add(0); planeMesh.TriangleIndices.Add(2); planeMesh.TriangleIndices.Add(3);

            var geometry = planeMesh.ToMesh();

            Material material;
            try {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri("pack://application:,,,/refman.png");
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                bitmap.EndInit();
                bitmap.Freeze();

                material = new EmissiveMaterial(new ImageBrush(bitmap) {
                    Opacity = 1.0,
                    Stretch = Stretch.Fill,
                    ViewportUnits = BrushMappingMode.Absolute
                });
            } catch (Exception ex) {
                LodLogger.Warn($"Failed to load refman.png: {ex.Message}");
                material = new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(255, 0, 255, 0)));
            }

            silhouette.Children.Add(new GeometryModel3D(geometry, material) { BackMaterial = material });

            _humanSilhouette = new ModelVisual3D { Content = silhouette };
            _humanSilhouetteOffsetX = CalculateHumanSilhouetteOffset();

            var transformGroup = new Transform3DGroup();
            _humanBillboardRotation = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0));
            transformGroup.Children.Add(_humanBillboardRotation);
            transformGroup.Children.Add(new TranslateTransform3D(_humanSilhouetteOffsetX, 0, 0));
            _humanSilhouette.Transform = transformGroup;

            viewPort3d.Children.Add(_humanSilhouette);
            StartBillboardUpdate();
        }

        private double CalculateHumanSilhouetteOffset() {
            double maxDimension = 1.0;

            foreach (var visual in viewPort3d.Children) {
                if (visual == _humanSilhouette) continue;
                if (visual is ModelVisual3D modelVisual && modelVisual.Content is Model3DGroup modelGroup) {
                    var bounds = modelGroup.Bounds;
                    if (bounds != Rect3D.Empty) {
                        maxDimension = Math.Max(maxDimension, Math.Max(bounds.SizeX, bounds.SizeZ));
                    }
                }
            }

            return maxDimension * 1.5;
        }

        private void StartBillboardUpdate() {
            CompositionTarget.Rendering -= UpdateBillboard;
            CompositionTarget.Rendering += UpdateBillboard;
        }

        private void StopBillboardUpdate() {
            CompositionTarget.Rendering -= UpdateBillboard;
        }

        private void UpdateBillboard(object? sender, EventArgs e) {
            if (!_isWindowActive) return;
            if (_humanSilhouette == null || _humanBillboardRotation == null || viewPort3d?.Camera == null) return;

            if (viewPort3d.Camera is not PerspectiveCamera camera) return;

            var direction = camera.Position - new Point3D(_humanSilhouetteOffsetX, 0, 0);
            direction.Z = 0;

            double angle = Math.Atan2(direction.Y, direction.X) * (180.0 / Math.PI);

            if (_humanBillboardRotation.Rotation is AxisAngleRotation3D rotation) {
                rotation.Axis = new Vector3D(0, 0, 1);
                rotation.Angle = angle;
            }
        }

        #endregion
    }
}
