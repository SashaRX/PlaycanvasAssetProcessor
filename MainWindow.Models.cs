using AssetProcessor.Helpers;
using AssetProcessor.Resources;
using AssetProcessor.Services;
using AssetProcessor.Services.Models;
using AssetProcessor.Settings;
using AssetProcessor.Upload;
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
using System.Windows.Controls.Primitives; // DragDeltaEventArgs ��� GridSplitter
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
using AssetProcessor.ModelConversion.Settings;

namespace AssetProcessor {
    public partial class MainWindow {
        private async void ModelsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (ModelsDataGrid.SelectedItem is ModelResource selectedModel) {
                if (!string.IsNullOrEmpty(selectedModel.Path)) {
                    if (selectedModel.Status == "Downloaded") { // если модель уже скачана
                        // Сначала пробуем загрузить GLB LOD файлы
                        System.IO.File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [SelectionChanged] Before TryLoadGlbLodAsync\n");
                        await TryLoadGlbLodAsync(selectedModel.Path);
                        System.IO.File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [SelectionChanged] After TryLoadGlbLodAsync, _isGlbViewerActive={_isGlbViewerActive}\n");

                        // Если GLB LOD не найдены, загружаем FBX модель и другую информацию
                        if (!_isGlbViewerActive) {
                            System.IO.File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [SelectionChanged] Loading FBX model\n");
                            // Загружаем модель во вьюпорт (3D просмотрщик)
                            LoadModel(selectedModel.Path);

                            // Загружаем информацию о модели из FBX
                            AssimpContext context = new();
                            Scene scene = context.ImportFile(selectedModel.Path, PostProcessSteps.Triangulate | PostProcessSteps.FlipUVs | PostProcessSteps.GenerateSmoothNormals);
                            Mesh? mesh = scene.Meshes.FirstOrDefault();

                            // Update UI on main thread using loaded mesh data
                            if (mesh != null) {
                                if (!string.IsNullOrEmpty(selectedModel.Name)) {
                                    int triangles = mesh.FaceCount;
                                    int vertices = mesh.VertexCount;
                                    int uvChannels = mesh.TextureCoordinateChannelCount;
                                    UpdateModelInfo(selectedModel.Name, triangles, vertices, uvChannels);
                                }

                                if (mesh.HasTextureCoords(0)) {
                                    UpdateUVImage(mesh);
                                }
                            }
                            System.IO.File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [SelectionChanged] FBX loaded\n");
                        }
                        System.IO.File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [SelectionChanged] COMPLETE\n");
                        // Если GLB viewer активен, информация уже установлена в TryLoadGlbLodAsync
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
        /// ��������� UV preview �����������
        /// </summary>
        /// <param name="mesh">Assimp mesh � UV ������������</param>
        /// <param name="flipV">True ��� FBX (�������� � FlipUVs), False ��� GLB (������������ top-left origin)</param>
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
        /// ������ bitmap � UV ���������
        /// </summary>
        /// <param name="flipV">True ��� FBX (�������� FlipUVs ��� ������ ������������ ��������), False ��� GLB</param>
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
                                // flipV: ��� FBX (����� FlipUVs) ����� �������� flip ����� �������� ������������ ��������
                                // ��� GLB (��� FlipUVs) ���������� ��� ���� (top-left origin)
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

                // ������� ������ ������, �������� ���������
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

                        // ��������� ���������� ����������, ���� ��� ����
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
                    // ���������� albedo �������� �� ���������� ���� ��� ���������
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

                // ��������� ��������� viewer (wireframe, pivot, up vector)
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
            // ������� ������ ������, �������� ���������
            List<ModelVisual3D> modelsToRemove = [.. viewPort3d.Children.OfType<ModelVisual3D>()];
            foreach (ModelVisual3D model in modelsToRemove) {
                viewPort3d.Children.Remove(model);
            }

            if (!viewPort3d.Children.OfType<DefaultLights>().Any()) {
                viewPort3d.Children.Add(new DefaultLights());
            }
            viewPort3d.ZoomExtents();
        }

        /// <summary>
        /// Экспорт выбранных моделей со всеми связанными ресурсами
        /// </summary>
        private async void ExportModelsButton_Click(object sender, RoutedEventArgs e) {
            var modelsToExport = viewModel.Models.Where(m => m.ExportToServer).ToList();

            if (!modelsToExport.Any()) {
                MessageBox.Show(
                    "Выберите модели для экспорта (отметьте чекбокс в колонке Export)",
                    "Export",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // Проверяем что проект загружен
            if (string.IsNullOrEmpty(AppSettings.Default.ProjectsFolderPath)) {
                MessageBox.Show(
                    "Не указана папка проектов. Откройте настройки и укажите Projects Folder Path.",
                    "Export Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var projectName = ProjectName ?? "UnknownProject";
            var outputPath = AppSettings.Default.ProjectsFolderPath;

            // Загружаем настройки для получения путей к инструментам
            var textureSettings = TextureConversion.Settings.TextureConversionSettingsManager.LoadSettings();
            var ktxPath = string.IsNullOrWhiteSpace(textureSettings.KtxExecutablePath)
                ? "ktx"
                : textureSettings.KtxExecutablePath;

            // Загружаем настройки конвертации моделей для FBX2glTF и gltfpack
            var modelSettings = ModelConversionSettingsManager.LoadSettings();
            var fbx2glTFPath = string.IsNullOrWhiteSpace(modelSettings.FBX2glTFExecutablePath)
                ? "FBX2glTF-windows-x64.exe"
                : modelSettings.FBX2glTFExecutablePath;
            var gltfPackPath = string.IsNullOrWhiteSpace(modelSettings.GltfPackExecutablePath)
                ? "gltfpack.exe"
                : modelSettings.GltfPackExecutablePath;

            // Получаем значения из чекбоксов
            bool generateORM = GenerateORMCheckBox.IsChecked ?? true;
            bool generateLODs = GenerateLODsCheckBox.IsChecked ?? true;

            try {
                ExportModelsButton.IsEnabled = false;
                ExportModelsButton.Content = "Exporting...";

                var pipeline = new Export.ModelExportPipeline(
                    projectName,
                    outputPath,
                    fbx2glTFPath: fbx2glTFPath,
                    gltfPackPath: gltfPackPath,
                    ktxPath: ktxPath
                );

                // Получаем ProjectId для загрузки сохранённых настроек ресурсов
                int projectId = 0;
                if (!string.IsNullOrEmpty(viewModel.SelectedProjectId) &&
                    int.TryParse(viewModel.SelectedProjectId, out var pid)) {
                    projectId = pid;
                }

                var options = new Export.ExportOptions {
                    ProjectId = projectId,
                    ConvertModel = true,
                    ConvertTextures = true,
                    GenerateORMTextures = generateORM,
                    UsePackedTextures = generateORM,
                    GenerateLODs = generateLODs,
                    TextureQuality = 128,
                    ApplyToksvig = true,
                    UseSavedTextureSettings = true // Использовать настройки текстур из ResourceSettingsService
                };

                int successCount = 0;
                int failCount = 0;
                int totalModels = modelsToExport.Count;
                int currentModel = 0;

                // Инициализируем прогресс
                ProgressBar.Value = 0;
                ProgressBar.Maximum = 100;

                foreach (var model in modelsToExport) {
                    try {
                        currentModel++;
                        var progressPercent = (double)currentModel / totalModels * 100;
                        ProgressBar.Value = progressPercent;
                        ExportModelsButton.Content = $"Export {currentModel}/{totalModels}";

                        logger.Info($"Exporting model: {model.Name} ({currentModel}/{totalModels})");

                        var result = await pipeline.ExportModelAsync(
                            model,
                            viewModel.Materials,
                            viewModel.Textures,
                            folderPaths,
                            options
                        );

                        if (result.Success) {
                            successCount++;
                            logger.Info($"Export OK: {model.Name} -> {result.ExportPath}");
                        } else {
                            failCount++;
                            logger.Error($"Export FAILED: {model.Name} - {result.ErrorMessage}");
                        }
                    } catch (Exception ex) {
                        failCount++;
                        logger.Error(ex, $"Export exception for {model.Name}");
                    }
                }

                ProgressBar.Value = 100;

                var exportMessage = $"Export completed!\n\nSuccess: {successCount}\nFailed: {failCount}\n\nOutput: {pipeline.GetContentBasePath()}";

                // Auto-upload if enabled and export was successful
                if (successCount > 0 && (AutoUploadCheckBox.IsChecked ?? false)) {
                    var shouldUpload = MessageBox.Show(
                        exportMessage + "\n\nUpload to cloud now?",
                        "Export Result",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (shouldUpload == MessageBoxResult.Yes) {
                        // Trigger upload
                        await AutoUploadAfterExportAsync(pipeline.GetContentBasePath());
                    }
                } else {
                    MessageBox.Show(
                        exportMessage,
                        "Export Result",
                        MessageBoxButton.OK,
                        successCount > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
                }

            } catch (Exception ex) {
                logger.Error(ex, "Export failed");
                MessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            } finally {
                ExportModelsButton.IsEnabled = true;
                ExportModelsButton.Content = "Export";
                ProgressBar.Value = 0;
            }
        }

        /// <summary>
        /// Автоматическая загрузка после экспорта
        /// </summary>
        private async Task AutoUploadAfterExportAsync(string contentPath) {
            var projectName = ProjectName ?? "UnknownProject";

            // Проверяем настройки B2
            if (string.IsNullOrEmpty(AppSettings.Default.B2KeyId) ||
                string.IsNullOrEmpty(AppSettings.Default.B2BucketName)) {
                MessageBox.Show(
                    "Backblaze B2 credentials not configured. Go to Settings -> CDN/Upload to configure.",
                    "Upload Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try {
                UploadToCloudButton.IsEnabled = false;
                UploadToCloudButton.Content = "Uploading...";

                using var b2Service = new B2UploadService();
                var uploadCoordinator = new AssetUploadCoordinator(b2Service);

                var initialized = await uploadCoordinator.InitializeAsync();
                if (!initialized) {
                    MessageBox.Show(
                        "Failed to connect to Backblaze B2. Check your credentials in Settings.",
                        "Upload Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                var result = await b2Service.UploadDirectoryAsync(
                    contentPath,
                    projectName,
                    "*",
                    recursive: true,
                    progress: new Progress<B2UploadProgress>(p => {
                        Dispatcher.Invoke(() => {
                            ProgressBar.Value = p.PercentComplete;
                        });
                    })
                );

                MessageBox.Show(
                    $"Upload completed!\n\n" +
                    $"Uploaded: {result.SuccessCount}\n" +
                    $"Skipped (already exists): {result.SkippedCount}\n" +
                    $"Failed: {result.FailedCount}\n" +
                    $"Duration: {result.Duration.TotalSeconds:F1}s",
                    "Upload Result",
                    MessageBoxButton.OK,
                    result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);

            } catch (Exception ex) {
                logger.Error(ex, "Auto-upload failed");
                MessageBox.Show($"Upload failed: {ex.Message}", "Upload Error", MessageBoxButton.OK, MessageBoxImage.Error);
            } finally {
                UploadToCloudButton.IsEnabled = true;
                UploadToCloudButton.Content = "Upload to Cloud";
                ProgressBar.Value = 0;
            }
        }

        /// <summary>
        /// Помечает связанные материалы и текстуры для экспорта
        /// </summary>
        private void MarkRelatedButton_Click(object sender, RoutedEventArgs e) {
            logService.LogInfo("[MarkRelatedButton_Click] Button clicked");
            var modelsToExport = viewModel.Models.Where(m => m.ExportToServer).ToList();
            logService.LogInfo($"[MarkRelatedButton_Click] Models to export: {modelsToExport.Count}");

            if (!modelsToExport.Any()) {
                MessageBox.Show(
                    "Сначала отметьте модели для экспорта",
                    "Mark Related",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // Находим связанные материалы и текстуры
            var (relatedMaterials, relatedTextures) = FindRelatedAssets(modelsToExport);

            // Помечаем их для экспорта
            foreach (var material in relatedMaterials) {
                material.ExportToServer = true;
            }

            foreach (var texture in relatedTextures) {
                texture.ExportToServer = true;
            }

            UpdateModelExportCounts();
            UpdateSelectedTexturesCount();

            MessageBox.Show(
                $"Отмечено для экспорта:\n\n{relatedMaterials.Count} материалов\n{relatedTextures.Count} текстур",
                "Mark Related",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        /// <summary>
        /// Очищает все отметки экспорта
        /// </summary>
        private void ClearExportMarksButton_Click(object sender, RoutedEventArgs e) {
            foreach (var model in viewModel.Models) {
                model.ExportToServer = false;
            }

            foreach (var material in viewModel.Materials) {
                material.ExportToServer = false;
            }

            foreach (var texture in viewModel.Textures) {
                texture.ExportToServer = false;
            }

            UpdateModelExportCounts();
            UpdateSelectedTexturesCount();
        }

        /// <summary>
        /// Загрузка экспортированных ресурсов на Backblaze B2
        /// </summary>
        private async void UploadToCloudButton_Click(object sender, RoutedEventArgs e) {
            var projectName = ProjectName ?? "UnknownProject";
            var outputPath = AppSettings.Default.ProjectsFolderPath;

            if (string.IsNullOrEmpty(outputPath)) {
                MessageBox.Show(
                    "Не указана папка проектов. Откройте настройки и укажите Projects Folder Path.",
                    "Upload Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Проверяем настройки B2
            if (string.IsNullOrEmpty(AppSettings.Default.B2KeyId) ||
                string.IsNullOrEmpty(AppSettings.Default.B2BucketName)) {
                MessageBox.Show(
                    "Backblaze B2 credentials not configured. Go to Settings -> CDN/Upload to configure.",
                    "Upload Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var contentPath = Path.Combine(outputPath, projectName, "server", "assets", "content");
            if (!Directory.Exists(contentPath)) {
                MessageBox.Show(
                    $"Export folder not found: {contentPath}\n\nPlease export models first.",
                    "Upload Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try {
                UploadToCloudButton.IsEnabled = false;
                UploadToCloudButton.Content = "Uploading...";

                using var b2Service = new B2UploadService();
                var uploadCoordinator = new AssetUploadCoordinator(b2Service);

                // Подписываемся на события прогресса
                uploadCoordinator.UploadProgressChanged += (s, args) => {
                    Dispatcher.Invoke(() => {
                        ProgressBar.Value = args.OverallProgress;
                    });
                };

                // Инициализируем сервис
                var initialized = await uploadCoordinator.InitializeAsync();
                if (!initialized) {
                    MessageBox.Show(
                        "Failed to connect to Backblaze B2. Check your credentials in Settings.",
                        "Upload Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                // Загружаем всю папку content
                var result = await b2Service.UploadDirectoryAsync(
                    contentPath,
                    projectName,
                    "*",
                    recursive: true,
                    progress: new Progress<B2UploadProgress>(p => {
                        Dispatcher.Invoke(() => {
                            ProgressBar.Value = p.PercentComplete;
                        });
                    })
                );

                MessageBox.Show(
                    $"Upload completed!\n\n" +
                    $"Uploaded: {result.SuccessCount}\n" +
                    $"Skipped (already exists): {result.SkippedCount}\n" +
                    $"Failed: {result.FailedCount}\n" +
                    $"Duration: {result.Duration.TotalSeconds:F1}s",
                    "Upload Result",
                    MessageBoxButton.OK,
                    result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);

            } catch (Exception ex) {
                logger.Error(ex, "Upload failed");
                MessageBox.Show($"Upload failed: {ex.Message}", "Upload Error", MessageBoxButton.OK, MessageBoxImage.Error);
            } finally {
                UploadToCloudButton.IsEnabled = true;
                UploadToCloudButton.Content = "Upload to Cloud";
                ProgressBar.Value = 0;
            }
        }

        /// <summary>
        /// Обновляет счётчики в панели экспорта
        /// </summary>
        private void UpdateModelExportCounts() {
            var modelsToExport = viewModel.Models.Where(m => m.ExportToServer).ToList();
            SelectedModelsCountText.Text = $"{modelsToExport.Count} models";

            if (modelsToExport.Any()) {
                var (relatedMaterials, relatedTextures) = FindRelatedAssets(modelsToExport);
                RelatedAssetsCountText.Text = $"{relatedMaterials.Count} materials, {relatedTextures.Count} textures";
            } else {
                RelatedAssetsCountText.Text = "0 materials, 0 textures";
            }
        }

        /// <summary>
        /// Находит материалы и текстуры, связанные с моделями
        /// </summary>
        private (List<MaterialResource>, List<TextureResource>) FindRelatedAssets(List<ModelResource> models) {
            var relatedMaterials = new List<MaterialResource>();
            var relatedTextures = new HashSet<TextureResource>();

            logService.LogInfo($"[FindRelatedAssets] Processing {models.Count} models, {viewModel.Materials.Count} materials, {viewModel.Textures.Count} textures");

            foreach (var model in models) {
                // Получаем путь папки модели
                string? modelFolderPath = null;
                if (model.Parent.HasValue && model.Parent.Value != 0) {
                    folderPaths.TryGetValue(model.Parent.Value, out modelFolderPath);
                }

                var modelBaseName = ExtractBaseName(model.Name);
                logService.LogInfo($"[FindRelatedAssets] Model: {model.Name}, Parent: {model.Parent}, FolderPath: {modelFolderPath ?? "null"}, BaseName: {modelBaseName}");

                foreach (var material in viewModel.Materials) {
                    bool isRelated = false;

                    // По Parent ID - материалы в той же папке
                    if (model.Parent.HasValue && material.Parent == model.Parent) {
                        isRelated = true;
                    }
                    // По пути папки
                    else if (!string.IsNullOrEmpty(modelFolderPath) && material.Parent.HasValue) {
                        if (folderPaths.TryGetValue(material.Parent.Value, out var materialFolderPath)) {
                            if (materialFolderPath.StartsWith(modelFolderPath, StringComparison.OrdinalIgnoreCase)) {
                                isRelated = true;
                            }
                        }
                    }
                    // По имени
                    else if (!string.IsNullOrEmpty(modelBaseName) && !string.IsNullOrEmpty(material.Name)) {
                        var materialBaseName = ExtractBaseName(material.Name);
                        if (materialBaseName.StartsWith(modelBaseName, StringComparison.OrdinalIgnoreCase) ||
                            modelBaseName.StartsWith(materialBaseName, StringComparison.OrdinalIgnoreCase)) {
                            isRelated = true;
                        }
                    }

                    if (isRelated && !relatedMaterials.Contains(material)) {
                        relatedMaterials.Add(material);
                        logService.LogInfo($"[FindRelatedAssets] Found related material: {material.Name}, Parent: {material.Parent}");

                        // Добавляем все текстуры материала
                        var textureIds = new List<int?> {
                            material.DiffuseMapId,
                            material.NormalMapId,
                            material.SpecularMapId,
                            material.GlossMapId,
                            material.MetalnessMapId,
                            material.AOMapId,
                            material.EmissiveMapId,
                            material.OpacityMapId
                        };

                        logService.LogInfo($"[FindRelatedAssets] Material {material.Name} texture IDs: AO={material.AOMapId}, Gloss={material.GlossMapId}, Metal={material.MetalnessMapId}, Diffuse={material.DiffuseMapId}, Normal={material.NormalMapId}");

                        foreach (var id in textureIds.Where(id => id.HasValue)) {
                            var textureId = id!.Value;
                            var texture = viewModel.Textures.FirstOrDefault(t => t.ID == textureId);
                            if (texture != null) {
                                relatedTextures.Add(texture);
                                logService.LogInfo($"[FindRelatedAssets] Found texture ID {textureId}: {texture.Name}");
                            } else {
                                logService.LogWarn($"[FindRelatedAssets] Texture ID {textureId} NOT FOUND in viewModel.Textures!");
                            }
                        }
                    }
                }
            }

            return (relatedMaterials, relatedTextures.ToList());
        }

        private string ExtractBaseName(string? name) {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            var baseName = Path.GetFileNameWithoutExtension(name);
            var suffixes = new[] { "_mat", "_material", "_mtl" };
            foreach (var suffix in suffixes) {
                if (baseName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) {
                    return baseName.Substring(0, baseName.Length - suffix.Length);
                }
            }
            return baseName;
        }
    }
}


