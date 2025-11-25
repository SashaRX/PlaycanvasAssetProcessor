using AssetProcessor.Helpers;
using AssetProcessor.Resources;
using AssetProcessor.Services;
using AssetProcessor.Services.Models;
using AssetProcessor.Settings;
using AssetProcessor.ViewModels;
using Assimp;
using HelixToolkit.Wpf;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using OxyPlot;
using OxyPlot.Axes;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives; // DragDeltaEventArgs для GridSplitter
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Xceed.Wpf.Toolkit;
using MessageBox = System.Windows.MessageBox;
using System.Linq;
using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureConversion.Settings;
using AssetProcessor.TextureViewer;

namespace AssetProcessor {
    public partial class MainWindow {
        private async void ModelsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (ModelsDataGrid.SelectedItem is ModelResource selectedModel) {
                if (!string.IsNullOrEmpty(selectedModel.Path)) {
                    if (selectedModel.Status == "Downloaded") { // Если модель уже загружена
                        // Сначала пытаемся загрузить GLB LOD файлы
                        await TryLoadGlbLodAsync(selectedModel.Path);

                        // Если GLB LOD не найдены, загружаем FBX модель в обычный вьюпорт
                        if (!_isGlbViewerActive) {
                            // Загружаем модель во вьюпорт (3D просмотрщик)
                            LoadModel(selectedModel.Path);

                            // Обновляем информацию о модели из FBX
                            AssimpContext context = new();
                            Scene scene = context.ImportFile(selectedModel.Path, PostProcessSteps.Triangulate | PostProcessSteps.FlipUVs | PostProcessSteps.GenerateSmoothNormals);
                            Mesh? mesh = scene.Meshes.FirstOrDefault();

                            if (mesh != null) {
                                string? modelName = selectedModel.Name;
                                int triangles = mesh.FaceCount;
                                int vertices = mesh.VertexCount;
                                int uvChannels = mesh.TextureCoordinateChannelCount;

                                if (!String.IsNullOrEmpty(modelName)) {
                                    UpdateModelInfo(modelName, triangles, vertices, uvChannels);
                                }

                                UpdateUVImage(mesh);
                            }
                        }
                        // Если GLB viewer активен, информация уже обновлена в TryLoadGlbLodAsync
                    }
                }
            }
        }

        private void UpdateModelInfo(string modelName, int triangles, int vertices, int uvChannels) {
            Dispatcher.Invoke(() => {
                ModelNameTextBlock.Text = $"Model Name: {modelName}";
                ModelTrianglesTextBlock.Text = $"Triangles: {triangles}";
                ModelVerticesTextBlock.Text = $"Vertices: {vertices}";
                ModelUVChannelsTextBlock.Text = $"UV Channels: {uvChannels}";
            });
        }

        /// <summary>
        /// Обновляет UV preview изображения
        /// </summary>
        /// <param name="mesh">Assimp mesh с UV координатами</param>
        /// <param name="flipV">True для FBX (загружен с FlipUVs), False для GLB (естественный top-left origin)</param>
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
        /// Создаёт bitmap с UV развёрткой
        /// </summary>
        /// <param name="flipV">True для FBX (отменяет FlipUVs для показа оригинальной развёртки), False для GLB</param>
        private static BitmapSource CreateUvBitmapSource(Mesh mesh, int channelIndex, int width, int height, bool flipV = true) {
            DrawingVisual visual = new();

            using (DrawingContext drawingContext = visual.RenderOpen()) {
                SolidColorBrush backgroundBrush = new(Color.FromRgb(169, 169, 169));
                backgroundBrush.Freeze();
                drawingContext.DrawRectangle(backgroundBrush, null, new Rect(0, 0, width, height));

                if (mesh.TextureCoordinateChannels.Length > channelIndex) {
                    List<Assimp.Vector3D>? textureCoordinates = mesh.TextureCoordinateChannels[channelIndex];

                    if (textureCoordinates != null && textureCoordinates.Count > 0) {
                        SolidColorBrush fillBrush = new(Color.FromArgb(186, 255, 69, 0));
                        fillBrush.Freeze();

                        SolidColorBrush outlineBrush = new(Color.FromRgb(0, 0, 139));
                        outlineBrush.Freeze();
                        Pen outlinePen = new(outlineBrush, 1);
                        outlinePen.Freeze();

                        foreach (Face face in mesh.Faces) {
                            if (face.IndexCount != 3) {
                                continue;
                            }

                            Point[] points = new Point[3];
                            bool isValidFace = true;

                            for (int i = 0; i < 3; i++) {
                                int vertexIndex = face.Indices[i];
                                if (vertexIndex >= textureCoordinates.Count) {
                                    isValidFace = false;
                                    break;
                                }

                                Assimp.Vector3D uv = textureCoordinates[vertexIndex];
                                // flipV: для FBX (после FlipUVs) нужно отменить flip чтобы показать оригинальную развёртку
                                // для GLB (без FlipUVs) показываем как есть (top-left origin)
                                float displayV = flipV ? (1 - uv.Y) : uv.Y;
                                points[i] = new Point(uv.X * width, displayV * height);
                            }

                            if (!isValidFace) {
                                continue;
                            }

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

        private void LoadModel(string path) {
            try {
                viewPort3d.RotateGesture = new MouseGesture(MouseAction.LeftClick);

                // Очищаем только модели, оставляя освещение
                List<ModelVisual3D> modelsToRemove = [.. viewPort3d.Children.OfType<ModelVisual3D>()];
                foreach (ModelVisual3D? model in modelsToRemove) {
                    viewPort3d.Children.Remove(model);
                }

                if (!viewPort3d.Children.OfType<DefaultLights>().Any()) {
                    viewPort3d.Children.Add(new DefaultLights());
                }

                AssimpContext importer = new();
                Scene scene = importer.ImportFile(path, PostProcessSteps.Triangulate | PostProcessSteps.FlipUVs | PostProcessSteps.GenerateSmoothNormals);

                if (scene == null || !scene.HasMeshes) {
                    MessageBox.Show("Error loading model: Scene is null or has no meshes.");
                    return;
                }

                Model3DGroup modelGroup = new();

                int totalTriangles = 0;
                int totalVertices = 0;
                int validUVChannels = 0;

                foreach (Mesh? mesh in scene.Meshes) {
                    if (mesh == null) continue;

                    MeshBuilder builder = new();

                    if (mesh.Vertices == null || mesh.Normals == null) {
                        MessageBox.Show("Error loading model: Mesh vertices or normals are null.");
                        continue;
                    }

                    for (int i = 0; i < mesh.VertexCount; i++) {
                        Assimp.Vector3D vertex = mesh.Vertices[i];
                        Assimp.Vector3D normal = mesh.Normals[i];
                        builder.Positions.Add(new Point3D(vertex.X, vertex.Y, vertex.Z));
                        builder.Normals.Add(new System.Windows.Media.Media3D.Vector3D(normal.X, normal.Y, normal.Z));

                        // Добавляем текстурные координаты, если они есть
                        if (mesh.TextureCoordinateChannels.Length > 0 && mesh.TextureCoordinateChannels[0] != null && i < mesh.TextureCoordinateChannels[0].Count) {
                            builder.TextureCoordinates.Add(new System.Windows.Point(mesh.TextureCoordinateChannels[0][i].X, mesh.TextureCoordinateChannels[0][i].Y));
                        }
                    }

                    if (mesh.Faces == null) {
                        MessageBox.Show("Error loading model: Mesh faces are null.");
                        continue;
                    }

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
                    // Используем albedo текстуру из материалов если она загружена
                    DiffuseMaterial material = (_cachedAlbedoBrush != null && geometry.TextureCoordinates.Count > 0)
                        ? new DiffuseMaterial(_cachedAlbedoBrush)
                        : new DiffuseMaterial(new SolidColorBrush(Colors.Gray));
                    GeometryModel3D model = new(geometry, material);
                    modelGroup.Children.Add(model);

                    validUVChannels = Math.Min(3, mesh.TextureCoordinateChannelCount);
                }

                Rect3D bounds = modelGroup.Bounds;
                System.Windows.Media.Media3D.Vector3D centerOffset = new(-bounds.X - bounds.SizeX / 2, -bounds.Y - bounds.SizeY / 2, -bounds.Z - bounds.SizeZ / 2);

                Transform3DGroup transformGroup = new();
                transformGroup.Children.Add(new TranslateTransform3D(centerOffset));

                modelGroup.Transform = transformGroup;

                ModelVisual3D visual3d = new() { Content = modelGroup };
                viewPort3d.Children.Add(visual3d);

                // Применяем настройки viewer (wireframe, pivot, up vector)
                ApplyViewerSettingsToModel();

                viewPort3d.ZoomExtents();

                UpdateModelInfo(modelName: Path.GetFileName(path), triangles: totalTriangles, vertices: totalVertices, uvChannels: validUVChannels);
            } catch (InvalidOperationException ex) {
                MessageBox.Show($"Error loading model: {ex.Message}");
                ResetViewport();
            } catch (Exception ex) {
                MessageBox.Show($"Error loading model: {ex.Message}");
                ResetViewport();
            }
        }

        private void ResetViewport() {
            // Очищаем только модели, оставляя освещение
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


