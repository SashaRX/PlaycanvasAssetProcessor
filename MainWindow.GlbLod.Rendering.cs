using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Assimp;
using AssetProcessor.ModelConversion.Viewer;
using AssetProcessor.ModelConversion.Core;

namespace AssetProcessor {
    /// <summary>
    /// Partial class containing GLB/FBX model rendering and UV preview:
    /// - ConvertAssimpSceneToWpfModel (FBX → WPF Model3DGroup)
    /// - ConvertSharpGlbToWpfModel (SharpGLTF → WPF Model3DGroup)
    /// - UpdateUVImageFromSharpGlb (UV wireframe rendering)
    /// - DrawLine (Bresenham's line algorithm)
    /// </summary>
    public partial class MainWindow {

        /// <summary>
        /// Converts Assimp Scene to WPF Model3DGroup.
        /// Handles Z-up → Y-up axis conversion for FBX files.
        /// </summary>
        private Model3DGroup ConvertAssimpSceneToWpfModel(Scene scene) {
            var modelGroup = new Model3DGroup();

            // Determine coordinate system from FBX metadata
            // UpAxis: 0 = X, 1 = Y, 2 = Z (default = 2 for 3ds Max)
            bool isZUp = true;
            if (scene.Metadata != null && scene.Metadata.TryGetValue("UpAxis", out var upAxisEntry)) {
                if (upAxisEntry.Data is int upAxis) {
                    isZUp = upAxis == 2;
                    LodLogger.Info($"FBX UpAxis: {upAxis} (isZUp={isZUp})");
                }
            }

            foreach (var mesh in scene.Meshes) {
                var geometry = new MeshGeometry3D();

                // Vertices and normals with axis conversion
                for (int i = 0; i < mesh.VertexCount; i++) {
                    var vertex = mesh.Vertices[i];
                    var normal = mesh.Normals[i];

                    if (isZUp) {
                        // Z-up → Y-up: swap Y↔Z
                        geometry.Positions.Add(new Point3D(vertex.X, vertex.Z, vertex.Y));
                        geometry.Normals.Add(new System.Windows.Media.Media3D.Vector3D(normal.X, normal.Z, normal.Y));
                    } else {
                        geometry.Positions.Add(new Point3D(vertex.X, vertex.Y, vertex.Z));
                        geometry.Normals.Add(new System.Windows.Media.Media3D.Vector3D(normal.X, normal.Y, normal.Z));
                    }
                }

                // UV coordinates
                if (mesh.TextureCoordinateChannelCount > 0 && mesh.HasTextureCoords(0)) {
                    var uvChannel = mesh.TextureCoordinateChannels[0];
                    var safeCount = Math.Min(mesh.VertexCount, uvChannel.Count);

                    if (uvChannel.Count != mesh.VertexCount) {
                        LodLogger.Warn($"UV count ({uvChannel.Count}) != vertex count ({mesh.VertexCount}), using safe count: {safeCount}");
                    }

                    for (int i = 0; i < safeCount; i++) {
                        var uv = uvChannel[i];
                        geometry.TextureCoordinates.Add(new System.Windows.Point(uv.X, uv.Y));
                    }

                    // Fill missing UVs with zeros
                    for (int i = safeCount; i < mesh.VertexCount; i++) {
                        geometry.TextureCoordinates.Add(new System.Windows.Point(0, 0));
                    }
                }

                // Triangle indices
                for (int i = 0; i < mesh.FaceCount; i++) {
                    var face = mesh.Faces[i];
                    if (face.IndexCount == 3) {
                        geometry.TriangleIndices.Add(face.Indices[0]);
                        geometry.TriangleIndices.Add(face.Indices[1]);
                        geometry.TriangleIndices.Add(face.Indices[2]);
                    }
                }

                // Material — use albedo texture if available
                DiffuseMaterial frontMaterial = (_cachedAlbedoBrush != null && geometry.TextureCoordinates.Count > 0)
                    ? new DiffuseMaterial(_cachedAlbedoBrush)
                    : new DiffuseMaterial(new SolidColorBrush(Colors.LightGray));

                var backMaterial = new DiffuseMaterial(new SolidColorBrush(Colors.DarkRed));
                var emissiveMaterial = new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(30, 30, 30)));

                var materialGroup = new MaterialGroup();
                materialGroup.Children.Add(frontMaterial);
                materialGroup.Children.Add(emissiveMaterial);

                var model = new GeometryModel3D(geometry, materialGroup);
                model.BackMaterial = backMaterial;
                modelGroup.Children.Add(model);
            }

            // Base rotation: align forward with HelixToolkit camera (glTF +Z forward → camera -Z)
            var baseTransform = new Transform3DGroup();
            baseTransform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new System.Windows.Media.Media3D.Vector3D(0, 1, 0), 180)));
            modelGroup.Transform = baseTransform;

            LogModelBounds(modelGroup, "Assimp→WPF");
            return modelGroup;
        }

        /// <summary>
        /// Converts SharpGLTF data to WPF Model3DGroup.
        /// SharpGLTF automatically decodes KHR_mesh_quantization.
        /// </summary>
        private Model3DGroup ConvertSharpGlbToWpfModel(SharpGlbLoader.GlbData glbData) {
            var modelGroup = new Model3DGroup();

            foreach (var meshData in glbData.Meshes) {
                var geometry = new MeshGeometry3D();

                // Normals must match positions count exactly
                bool hasValidNormals = meshData.Normals.Count == meshData.Positions.Count;
                if (meshData.Normals.Count > 0 && !hasValidNormals) {
                    LodLogger.Warn($"Normals count ({meshData.Normals.Count}) != positions ({meshData.Positions.Count}), skipping normals");
                }

                for (int i = 0; i < meshData.Positions.Count; i++) {
                    var pos = meshData.Positions[i];
                    geometry.Positions.Add(new Point3D(pos.X, pos.Y, pos.Z));

                    if (hasValidNormals) {
                        var normal = meshData.Normals[i];
                        geometry.Normals.Add(new System.Windows.Media.Media3D.Vector3D(normal.X, normal.Y, normal.Z));
                    }
                }

                // UV coordinates (SharpGLTF already decoded quantization)
                foreach (var uv in meshData.TextureCoordinates) {
                    geometry.TextureCoordinates.Add(new System.Windows.Point(uv.X, uv.Y));
                }

                // Triangle indices
                for (int i = 0; i < meshData.Indices.Count; i += 3) {
                    if (i + 2 < meshData.Indices.Count) {
                        geometry.TriangleIndices.Add(meshData.Indices[i]);
                        geometry.TriangleIndices.Add(meshData.Indices[i + 1]);
                        geometry.TriangleIndices.Add(meshData.Indices[i + 2]);
                    }
                }

                // Material — use albedo texture if available
                DiffuseMaterial frontMaterial = (_cachedAlbedoBrush != null && geometry.TextureCoordinates.Count > 0)
                    ? new DiffuseMaterial(_cachedAlbedoBrush)
                    : new DiffuseMaterial(new SolidColorBrush(Colors.LightGray));

                var backMaterial = new DiffuseMaterial(new SolidColorBrush(Colors.DarkRed));
                var emissiveMaterial = new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(30, 30, 30)));

                var materialGroup = new MaterialGroup();
                materialGroup.Children.Add(frontMaterial);
                materialGroup.Children.Add(emissiveMaterial);

                var model = new GeometryModel3D(geometry, materialGroup);
                model.BackMaterial = backMaterial;
                modelGroup.Children.Add(model);
            }

            // Base rotation: align forward with HelixToolkit camera
            var baseTransform = new Transform3DGroup();
            baseTransform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new System.Windows.Media.Media3D.Vector3D(0, 1, 0), 180)));
            modelGroup.Transform = baseTransform;

            LogModelBounds(modelGroup, "SharpGLTF→WPF");
            return modelGroup;
        }

        /// <summary>
        /// Logs model bounds from transformed positions.
        /// </summary>
        private void LogModelBounds(Model3DGroup modelGroup, string label) {
            var minX = double.MaxValue; var minY = double.MaxValue; var minZ = double.MaxValue;
            var maxX = double.MinValue; var maxY = double.MinValue; var maxZ = double.MinValue;

            var transform = modelGroup.Transform ?? Transform3D.Identity;

            foreach (var child in modelGroup.Children) {
                if (child is GeometryModel3D geoModel && geoModel.Geometry is MeshGeometry3D mesh) {
                    foreach (var pos in mesh.Positions) {
                        var t = transform.Transform(pos);
                        minX = Math.Min(minX, t.X); minY = Math.Min(minY, t.Y); minZ = Math.Min(minZ, t.Z);
                        maxX = Math.Max(maxX, t.X); maxY = Math.Max(maxY, t.Y); maxZ = Math.Max(maxZ, t.Z);
                    }
                }
            }

            LodLogger.Info($"[{label}] Bounds: ({minX:F2},{minY:F2},{minZ:F2})→({maxX:F2},{maxY:F2},{maxZ:F2}), size: {maxX - minX:F2}×{maxY - minY:F2}×{maxZ - minZ:F2}");
        }

        /// <summary>
        /// Updates UV preview from SharpGLTF mesh data with quantization correction.
        /// </summary>
        private void UpdateUVImageFromSharpGlb(SharpGlbLoader.MeshData meshData, LodLevel lodLevel) {
            try {
                if (meshData.TextureCoordinates.Count == 0) return;

                const int width = 512;
                const int height = 512;

                // Validate UV0 count matches vertex count (indices reference vertices)
                if (meshData.TextureCoordinates.Count != meshData.Positions.Count) {
                    LodLogger.Warn($"[UV] UV0 count ({meshData.TextureCoordinates.Count}) != vertex count ({meshData.Positions.Count}), skipping");
                    return;
                }

                // Apply quantization scale correction if needed
                float uvScaleU = 1.0f, uvScaleV = 1.0f;
                if (_lodQuantizationInfos.TryGetValue(lodLevel, out var quantInfo) &&
                    GlbQuantizationAnalyzer.NeedsUVScaling(quantInfo)) {
                    uvScaleU = quantInfo.UVScaleU;
                    uvScaleV = quantInfo.UVScaleV;
                    LodLogger.Info($"[UV] Scale correction: U={uvScaleU:F3}x, V={uvScaleV:F3}x (LOD {lodLevel})");
                }

                // Build point array from UV coordinates
                var points = new System.Windows.Point[meshData.TextureCoordinates.Count];
                for (int i = 0; i < meshData.TextureCoordinates.Count; i++) {
                    var uv = meshData.TextureCoordinates[i];
                    points[i] = new System.Windows.Point(uv.X * uvScaleU * width, uv.Y * uvScaleV * height);
                }

                // Render UV0 wireframe
                var bitmap = RenderUVWireframe(points, meshData.Indices, width, height);

                // Render UV1 (lightmap) if available
                WriteableBitmap? bitmap2 = null;
                if (meshData.TextureCoordinates2.Count == meshData.Positions.Count) {
                    var points2 = new System.Windows.Point[meshData.TextureCoordinates2.Count];
                    for (int i = 0; i < meshData.TextureCoordinates2.Count; i++) {
                        var uv = meshData.TextureCoordinates2[i];
                        points2[i] = new System.Windows.Point(uv.X * uvScaleU * width, uv.Y * uvScaleV * height);
                    }

                    // Validate max index before rendering
                    if (meshData.Indices.Count > 0 && meshData.Indices.Max() < points2.Length) {
                        bitmap2 = RenderUVWireframe(points2, meshData.Indices, width, height);
                    } else if (meshData.Indices.Count > 0) {
                        LodLogger.Warn($"[UV] UV1 max index ({meshData.Indices.Max()}) >= UV1 count ({points2.Length}), skipping");
                    }
                } else if (meshData.TextureCoordinates2.Count > 0) {
                    LodLogger.Warn($"[UV] UV1 count ({meshData.TextureCoordinates2.Count}) != vertex count ({meshData.Positions.Count}), skipping");
                }

                Dispatcher.Invoke(() => {
                    UVImage.Source = bitmap;
                    UVImage2.Source = bitmap2;
                });

            } catch (Exception ex) {
                LodLogger.Error(ex, "[UV] Failed to update UV image");
            }
        }

        /// <summary>
        /// Renders a UV wireframe onto a WriteableBitmap.
        /// </summary>
        private WriteableBitmap RenderUVWireframe(System.Windows.Point[] points, List<int> indices, int width, int height) {
            var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);

            bitmap.Lock();
            try {
                var pixels = new byte[width * height * 4];

                // Dark grey background
                for (int i = 0; i < pixels.Length; i += 4) {
                    pixels[i] = 40; pixels[i + 1] = 40; pixels[i + 2] = 40; pixels[i + 3] = 255;
                }

                // Draw triangle wireframe
                for (int i = 0; i < indices.Count; i += 3) {
                    if (i + 2 >= indices.Count) break;

                    var i0 = indices[i];
                    var i1 = indices[i + 1];
                    var i2 = indices[i + 2];

                    if (i0 < 0 || i0 >= points.Length || i1 < 0 || i1 >= points.Length || i2 < 0 || i2 >= points.Length) continue;

                    DrawLine(pixels, width, height, points[i0], points[i1], 0, 255, 0);
                    DrawLine(pixels, width, height, points[i1], points[i2], 0, 255, 0);
                    DrawLine(pixels, width, height, points[i2], points[i0], 0, 255, 0);
                }

                bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
            } finally {
                bitmap.Unlock();
            }

            return bitmap;
        }

        /// <summary>
        /// Draws a line on pixel array using Bresenham's algorithm.
        /// </summary>
        private static void DrawLine(byte[] pixels, int width, int height, System.Windows.Point p0, System.Windows.Point p1, byte r, byte g, byte b) {
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
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx) { err += dx; y0 += sy; }
            }
        }
    }
}
