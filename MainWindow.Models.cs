using AssetProcessor.Helpers;
using AssetProcessor.Resources;
using Assimp;
using HelixToolkit.Wpf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using AssimpVector3D = Assimp.Vector3D;

namespace AssetProcessor {
    /// <summary>
    /// Partial class containing model display and viewport logic:
    /// - ModelsDataGrid_SelectionChanged (selection handler)
    /// - LoadModelAsync (3D model loading with Assimp)
    /// - UpdateModelInfo / UpdateUVImage / CreateUvBitmapSource (info panels)
    /// - ResetViewport (viewport cleanup)
    /// </summary>
    public partial class MainWindow {
        private CancellationTokenSource? _modelLoadCts;
        private const int ModelSelectionDebounceMs = 150;

        private async void ModelsDataGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) {
            // Cancel any pending model load from previous selection
            _modelLoadCts?.Cancel();
            _modelLoadCts?.Dispose();
            _modelLoadCts = new CancellationTokenSource();
            var ct = _modelLoadCts.Token;

            await UiAsyncHelper.ExecuteAsync(async () => {
                // Debounce: wait before starting heavy model loading
                await Task.Delay(ModelSelectionDebounceMs, ct);

                if (ModelsDataGrid.SelectedItem is ModelResource selectedModel) {
                    if (!string.IsNullOrEmpty(selectedModel.Path)) {
                        if (selectedModel.Status == "Downloaded") {
                            await TryLoadGlbLodAsync(selectedModel.Path, ct);

                            ct.ThrowIfCancellationRequested();

                            if (!_isGlbViewerActive) {
                                await LoadModelAsync(selectedModel.Path, ct);
                            }
                        }
                    }
                }
            }, nameof(ModelsDataGrid_SelectionChanged));
        }

        private void UpdateModelInfo(string modelName, int triangles, int vertices, int uvChannels) {
            Dispatcher.Invoke(() => {
                viewModel.ModelInfoName = $"Model Name: {modelName}";
                viewModel.ModelInfoTriangles = $"Triangles: {triangles}";
                viewModel.ModelInfoVertices = $"Vertices: {vertices}";
                viewModel.ModelInfoUVChannels = $"UV Channels: {uvChannels}";
            });
        }

        /// <summary>
        /// Updates UV preview images.
        /// </summary>
        /// <param name="mesh">Assimp mesh with UV coordinates</param>
        /// <param name="flipV">True for FBX (imported with FlipUVs), False for GLB (top-left origin)</param>
        private void UpdateUVImage(Mesh mesh, bool flipV = true) {
            const int width = 512;
            const int height = 512;

            BitmapSource primaryUv = CreateUvBitmapSource(mesh, 0, width, height, flipV);
            BitmapSource secondaryUv = CreateUvBitmapSource(mesh, 1, width, height, flipV);

            Dispatcher.Invoke(() => {
                UVImage.Source = primaryUv;
                UVImage2.Source = secondaryUv;
            });
        }

        /// <summary>
        /// Creates a bitmap with UV wireframe overlay.
        /// </summary>
        /// <param name="flipV">True for FBX (with FlipUVs), False for GLB</param>
        private static BitmapSource CreateUvBitmapSource(Mesh mesh, int channelIndex, int width, int height, bool flipV = true) {
            DrawingVisual visual = new();

            using (DrawingContext drawingContext = visual.RenderOpen()) {
                SolidColorBrush backgroundBrush = new(Color.FromRgb(169, 169, 169));
                backgroundBrush.Freeze();
                drawingContext.DrawRectangle(backgroundBrush, null, new Rect(0, 0, width, height));

                if (mesh.TextureCoordinateChannels.Length > channelIndex) {
                    List<AssimpVector3D>? textureCoordinates = mesh.TextureCoordinateChannels[channelIndex];

                    if (textureCoordinates != null && textureCoordinates.Count > 0) {
                        SolidColorBrush fillBrush = new(Color.FromArgb(186, 255, 69, 0));
                        fillBrush.Freeze();

                        SolidColorBrush outlineBrush = new(Color.FromRgb(0, 0, 139));
                        outlineBrush.Freeze();
                        Pen outlinePen = new(outlineBrush, 1);
                        outlinePen.Freeze();

                        foreach (Face face in mesh.Faces) {
                            if (face.IndexCount != 3) continue;

                            Point[] points = new Point[3];
                            bool isValidFace = true;

                            for (int i = 0; i < 3; i++) {
                                int vertexIndex = face.Indices[i];
                                if (vertexIndex >= textureCoordinates.Count) {
                                    isValidFace = false;
                                    break;
                                }

                                AssimpVector3D uv = textureCoordinates[vertexIndex];
                                float displayV = flipV ? (1 - uv.Y) : uv.Y;
                                points[i] = new Point(uv.X * width, displayV * height);
                            }

                            if (!isValidFace) continue;

                            StreamGeometry geometry = new();
                            using (StreamGeometryContext geometryContext = geometry.Open()) {
                                geometryContext.BeginFigure(points[0], true, true);
                                geometryContext.LineTo(points[1], true, false);
                                geometryContext.LineTo(points[2], true, false);
                            }
                            geometry.Freeze();

                            drawingContext.DrawGeometry(fillBrush, outlinePen, geometry);
                        }
                    }
                }
            }

            RenderTargetBitmap renderTarget = new(width, height, 96, 96, PixelFormats.Pbgra32);
            renderTarget.Render(visual);
            renderTarget.Freeze();

            return renderTarget;
        }

        private async Task LoadModelAsync(string path, CancellationToken ct = default) {
            try {
                // Clear viewport on UI thread
                List<ModelVisual3D> modelsToRemove = [.. viewPort3d.Children.OfType<ModelVisual3D>()];
                foreach (ModelVisual3D? model in modelsToRemove) {
                    viewPort3d.Children.Remove(model);
                }

                if (!viewPort3d.Children.OfType<DefaultLights>().Any()) {
                    viewPort3d.Children.Add(new DefaultLights());
                }

                // Load model on background thread
                var loadResult = await Task.Run(() => {
                    ct.ThrowIfCancellationRequested();
                    AssimpContext importer = new();
                    Scene scene = importer.ImportFile(path, PostProcessSteps.Triangulate | PostProcessSteps.FlipUVs | PostProcessSteps.GenerateSmoothNormals);

                    if (scene == null || !scene.HasMeshes) {
                        return (Scene: (Scene?)null, Error: "Scene is null or has no meshes.");
                    }

                    return (Scene: scene, Error: (string?)null);
                }, ct);

                if (loadResult.Scene == null) {
                    MessageBox.Show($"Error loading model: {loadResult.Error}");
                    return;
                }

                var scene = loadResult.Scene;

                ct.ThrowIfCancellationRequested();

                // Build geometry on UI thread (MeshBuilder requires UI thread)
                Model3DGroup modelGroup = new();
                int totalTriangles = 0;
                int totalVertices = 0;
                int validUVChannels = 0;
                Mesh? firstMeshWithUV = null;

                foreach (Mesh? mesh in scene.Meshes) {
                    if (mesh == null) continue;

                    MeshBuilder builder = new();

                    if (mesh.Vertices == null || mesh.Normals == null) continue;

                    for (int i = 0; i < mesh.VertexCount; i++) {
                        AssimpVector3D vertex = mesh.Vertices[i];
                        AssimpVector3D normal = mesh.Normals[i];
                        builder.Positions.Add(new Point3D(vertex.X, vertex.Y, vertex.Z));
                        builder.Normals.Add(new System.Windows.Media.Media3D.Vector3D(normal.X, normal.Y, normal.Z));

                        if (mesh.TextureCoordinateChannels.Length > 0 && mesh.TextureCoordinateChannels[0] != null && i < mesh.TextureCoordinateChannels[0].Count) {
                            builder.TextureCoordinates.Add(new System.Windows.Point(mesh.TextureCoordinateChannels[0][i].X, mesh.TextureCoordinateChannels[0][i].Y));
                        }
                    }

                    if (mesh.Faces == null) continue;

                    totalTriangles += mesh.FaceCount;
                    totalVertices += mesh.VertexCount;

                    for (int i = 0; i < mesh.FaceCount; i++) {
                        Face face = mesh.Faces[i];
                        if (face.IndexCount == 3) {
                            builder.TriangleIndices.Add(face.Indices[0]);
                            builder.TriangleIndices.Add(face.Indices[1]);
                            builder.TriangleIndices.Add(face.Indices[2]);
                        }
                    }

                    MeshGeometry3D geometry = builder.ToMesh(true);
                    DiffuseMaterial material = (_cachedAlbedoBrush != null && geometry.TextureCoordinates.Count > 0)
                        ? new DiffuseMaterial(_cachedAlbedoBrush)
                        : new DiffuseMaterial(new SolidColorBrush(Colors.Gray));
                    GeometryModel3D model = new(geometry, material);
                    modelGroup.Children.Add(model);

                    validUVChannels = Math.Min(3, mesh.TextureCoordinateChannelCount);

                    if (firstMeshWithUV == null && mesh.HasTextureCoords(0)) {
                        firstMeshWithUV = mesh;
                    }
                }

                Rect3D bounds = modelGroup.Bounds;
                System.Windows.Media.Media3D.Vector3D centerOffset = new(-bounds.X - bounds.SizeX / 2, -bounds.Y - bounds.SizeY / 2, -bounds.Z - bounds.SizeZ / 2);

                Transform3DGroup transformGroup = new();
                transformGroup.Children.Add(new TranslateTransform3D(centerOffset));

                modelGroup.Transform = transformGroup;

                ModelVisual3D visual3d = new() { Content = modelGroup };
                viewPort3d.Children.Add(visual3d);

                ApplyViewerSettingsToModel();
                viewPort3d.ZoomExtents();

                UpdateModelInfo(modelName: Path.GetFileName(path), triangles: totalTriangles, vertices: totalVertices, uvChannels: validUVChannels);

                if (firstMeshWithUV != null) {
                    UpdateUVImage(firstMeshWithUV);
                }
            } catch (InvalidOperationException ex) {
                MessageBox.Show($"Error loading model: {ex.Message}");
                ResetViewport();
            } catch (Exception ex) {
                MessageBox.Show($"Error loading model: {ex.Message}");
                ResetViewport();
            }
        }

        private void ResetViewport() {
            List<ModelVisual3D> modelsToRemove = [.. viewPort3d.Children.OfType<ModelVisual3D>()];
            foreach (ModelVisual3D model in modelsToRemove) {
                viewPort3d.Children.Remove(model);
            }

            if (!viewPort3d.Children.OfType<DefaultLights>().Any()) {
                viewPort3d.Children.Add(new DefaultLights());
            }
            viewPort3d.ZoomExtents();
        }
    }
}
