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
        /// Export all marked assets (models with related materials and textures)
        /// </summary>
        private async void ExportAssetsButton_Click(object sender, RoutedEventArgs e) {
            var modelsToExport = viewModel.Models.Where(m => m.ExportToServer).ToList();
            var materialsToExport = viewModel.Materials.Where(m => m.ExportToServer).ToList();
            var texturesToExport = viewModel.Textures.Where(t => t.ExportToServer).ToList();

            // Проверяем что есть хоть что-то для экспорта
            if (!modelsToExport.Any() && !materialsToExport.Any() && !texturesToExport.Any()) {
                MessageBox.Show("No assets marked for export.\nUse 'Select' to mark models, materials, or textures for export.",
                    "Export", MessageBoxButton.OK, MessageBoxImage.Information);
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
                ? "FBX2glTF-windows-x86_64.exe"
                : modelSettings.FBX2glTFExecutablePath;
            var gltfPackPath = string.IsNullOrWhiteSpace(modelSettings.GltfPackExecutablePath)
                ? "gltfpack.exe"
                : modelSettings.GltfPackExecutablePath;

            // Получаем значения из чекбоксов
            bool generateORM = GenerateORMCheckBox.IsChecked ?? true;
            bool generateLODs = GenerateLODsCheckBox.IsChecked ?? true;

            try {
                ExportAssetsButton.IsEnabled = false;
                ExportAssetsButton.Content = "Exporting...";

                var pipeline = new Export.ModelExportPipeline(
                    projectName,
                    outputPath,
                    fbx2glTFPath: fbx2glTFPath,
                    gltfPackPath: gltfPackPath,
                    ktxPath: ktxPath
                );

                // Подписываемся на детальный прогресс экспорта
                pipeline.ProgressChanged += progress => {
                    Dispatcher.Invoke(() => {
                        ProgressTextBlock.Text = progress.ShortStatus;
                    });
                };

                // Получаем ProjectId для загрузки сохранённых настроек ресурсов
                int projectId = 0;
                if (!string.IsNullOrEmpty(viewModel.SelectedProjectId) &&
                    int.TryParse(viewModel.SelectedProjectId, out var pid)) {
                    projectId = pid;
                }

                // Получаем MasterMaterialsConfig для экспорта chunks и DefaultMasterMaterial
                var masterMaterialsConfig = viewModel.MasterMaterialsViewModel.Config;

                var options = new Export.ExportOptions {
                    ProjectId = projectId,
                    ConvertModel = true,
                    ConvertTextures = true,
                    GenerateORMTextures = generateORM,
                    UsePackedTextures = generateORM,
                    GenerateLODs = generateLODs,
                    TextureQuality = 128,
                    ApplyToksvig = true,
                    UseSavedTextureSettings = true, // Использовать настройки текстур из ResourceSettingsService
                    MasterMaterialsConfig = masterMaterialsConfig,
                    ProjectFolderPath = viewModel.MasterMaterialsViewModel.ProjectFolderPath,
                    DefaultMasterMaterial = masterMaterialsConfig?.DefaultMasterMaterial
                };

                int successCount = 0;
                int failCount = 0;

                // Собираем пути экспортированных файлов для точечной загрузки
                var exportedFiles = new List<string>();

                // Считаем общее количество элементов для экспорта
                // Материалы, связанные с моделями, не экспортируются отдельно
                var modelMaterialIds = new HashSet<int>();
                foreach (var model in modelsToExport) {
                    var modelMaterials = viewModel.Materials.Where(m =>
                        m.Parent == model.Parent ||
                        (model.Name != null && m.Name != null && m.Name.StartsWith(model.Name.Split('_')[0], StringComparison.OrdinalIgnoreCase)));
                    foreach (var mat in modelMaterials) {
                        modelMaterialIds.Add(mat.ID);
                    }
                }

                // Материалы без моделей (standalone)
                var standaloneMaterials = materialsToExport.Where(m => !modelMaterialIds.Contains(m.ID)).ToList();

                // Текстуры без материалов (standalone)
                var allMaterialTextureIds = new HashSet<int>();
                foreach (var mat in materialsToExport) {
                    if (mat.DiffuseMapId.HasValue) allMaterialTextureIds.Add(mat.DiffuseMapId.Value);
                    if (mat.NormalMapId.HasValue) allMaterialTextureIds.Add(mat.NormalMapId.Value);
                    if (mat.AOMapId.HasValue) allMaterialTextureIds.Add(mat.AOMapId.Value);
                    if (mat.GlossMapId.HasValue) allMaterialTextureIds.Add(mat.GlossMapId.Value);
                    if (mat.MetalnessMapId.HasValue) allMaterialTextureIds.Add(mat.MetalnessMapId.Value);
                    if (mat.EmissiveMapId.HasValue) allMaterialTextureIds.Add(mat.EmissiveMapId.Value);
                }
                var standaloneTextures = texturesToExport.Where(t => !allMaterialTextureIds.Contains(t.ID)).ToList();

                int totalItems = modelsToExport.Count + standaloneMaterials.Count + standaloneTextures.Count;
                int currentItem = 0;

                // Инициализируем прогресс
                ProgressBar.Value = 0;
                ProgressBar.Maximum = 100;

                // 1. Экспортируем модели (с их материалами и текстурами)
                foreach (var model in modelsToExport) {
                    try {
                        currentItem++;
                        var progressPercent = (double)currentItem / totalItems * 100;
                        ProgressBar.Value = progressPercent;
                        ExportAssetsButton.Content = $"Export {currentItem}/{totalItems}";

                        logger.Info($"Exporting model: {model.Name} ({currentItem}/{totalItems})");

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

                            // Собираем пути экспортированных файлов (ТОЛЬКО файлы, не папки!)
                            if (!string.IsNullOrEmpty(result.ConvertedModelPath)) exportedFiles.Add(result.ConvertedModelPath);
                            if (!string.IsNullOrEmpty(result.GeneratedModelJson)) exportedFiles.Add(result.GeneratedModelJson);
                            exportedFiles.AddRange(result.LODPaths.Where(p => !string.IsNullOrEmpty(p)));
                            exportedFiles.AddRange(result.GeneratedMaterialJsons.Where(p => !string.IsNullOrEmpty(p)));
                            exportedFiles.AddRange(result.ConvertedTextures.Where(p => !string.IsNullOrEmpty(p)));
                            exportedFiles.AddRange(result.GeneratedORMTextures.Where(p => !string.IsNullOrEmpty(p)));
                            exportedFiles.AddRange(result.GeneratedChunksFiles.Where(p => !string.IsNullOrEmpty(p)));
                        } else {
                            failCount++;
                            logger.Error($"Export FAILED: {model.Name} - {result.ErrorMessage}");
                        }
                    } catch (Exception ex) {
                        failCount++;
                        logger.Error(ex, $"Export exception for {model.Name}");
                    }
                }

                // 2. Экспортируем материалы без моделей (только JSON, без текстур)
                // Создаём опции для standalone материалов - только JSON
                var materialOnlyOptions = new Export.ExportOptions {
                    ProjectId = projectId,
                    ConvertModel = false,
                    ConvertTextures = false,
                    GenerateORMTextures = false,
                    MaterialJsonOnly = true, // Только JSON материала!
                    UseSavedTextureSettings = true,
                    MasterMaterialsConfig = masterMaterialsConfig,
                    ProjectFolderPath = viewModel.MasterMaterialsViewModel.ProjectFolderPath,
                    DefaultMasterMaterial = masterMaterialsConfig?.DefaultMasterMaterial
                };

                foreach (var material in standaloneMaterials) {
                    try {
                        currentItem++;
                        var progressPercent = (double)currentItem / totalItems * 100;
                        ProgressBar.Value = progressPercent;
                        ExportAssetsButton.Content = $"Export {currentItem}/{totalItems}";

                        logger.Info($"Exporting material (JSON only): {material.Name} ({currentItem}/{totalItems})");

                        var result = await pipeline.ExportMaterialAsync(
                            material,
                            viewModel.Textures,
                            folderPaths,
                            materialOnlyOptions
                        );

                        if (result.Success) {
                            successCount++;
                            logger.Info($"Export OK: {material.Name} -> {result.GeneratedMaterialJson}");

                            // Собираем пути экспортированных файлов
                            // ВАЖНО: GeneratedMaterialJson - это путь к JSON файлу, ExportPath - это папка!
                            if (!string.IsNullOrEmpty(result.GeneratedMaterialJson)) exportedFiles.Add(result.GeneratedMaterialJson);
                            exportedFiles.AddRange(result.ConvertedTextures.Where(p => !string.IsNullOrEmpty(p)));
                            exportedFiles.AddRange(result.GeneratedORMTextures.Where(p => !string.IsNullOrEmpty(p)));
                        } else {
                            failCount++;
                            logger.Error($"Export FAILED: {material.Name} - {result.ErrorMessage}");
                        }
                    } catch (Exception ex) {
                        failCount++;
                        logger.Error(ex, $"Export exception for {material.Name}");
                    }
                }

                // 3. Экспортируем текстуры без материалов
                foreach (var texture in standaloneTextures) {
                    try {
                        currentItem++;
                        var progressPercent = (double)currentItem / totalItems * 100;
                        ProgressBar.Value = progressPercent;
                        ExportAssetsButton.Content = $"Export {currentItem}/{totalItems}";

                        logger.Info($"Exporting texture: {texture.Name} ({currentItem}/{totalItems})");

                        var result = await pipeline.ExportTextureAsync(
                            texture,
                            folderPaths,
                            options
                        );

                        if (result.Success) {
                            successCount++;
                            logger.Info($"Export OK: {texture.Name} -> {result.ExportPath}");

                            // Собираем пути экспортированных файлов
                            if (!string.IsNullOrEmpty(result.ConvertedTexturePath)) exportedFiles.Add(result.ConvertedTexturePath);
                        } else {
                            failCount++;
                            logger.Error($"Export FAILED: {texture.Name} - {result.ErrorMessage}");
                        }
                    } catch (Exception ex) {
                        failCount++;
                        logger.Error(ex, $"Export exception for {texture.Name}");
                    }
                }

                ProgressBar.Value = 100;
                logger.Info($"Total exported files: {exportedFiles.Count}");

                var exportMessage = $"Export completed!\n\nSuccess: {successCount}\nFailed: {failCount}\n\nOutput: {pipeline.GetContentBasePath()}";

                // Auto-upload if enabled and export was successful
                if (successCount > 0 && (AutoUploadCheckBox.IsChecked ?? false)) {
                    var shouldUpload = MessageBox.Show(
                        exportMessage + "\n\nUpload to cloud now?",
                        "Export Result",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (shouldUpload == MessageBoxResult.Yes) {
                        // Trigger upload - только экспортированных файлов!
                        await AutoUploadAfterExportAsync(pipeline.GetContentBasePath(), exportedFiles);
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
                ExportAssetsButton.IsEnabled = true;
                ExportAssetsButton.Content = "Export";
                ProgressBar.Value = 0;
                ProgressTextBlock.Text = "";
            }
        }

        /// <summary>
        /// Автоматическая загрузка после экспорта - ТОЛЬКО указанных файлов!
        /// </summary>
        private async Task AutoUploadAfterExportAsync(string contentPath, List<string> exportedFiles) {
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

            // Если нет файлов для загрузки
            if (exportedFiles.Count == 0) {
                MessageBox.Show(
                    "No files to upload.",
                    "Upload",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            try {
                UploadToCloudButton.IsEnabled = false;
                UploadToCloudButton.Content = "Uploading...";

                using var b2Service = new B2UploadService();
                using var uploadStateService = new Data.UploadStateService();
                var uploadCoordinator = new AssetUploadCoordinator(b2Service, uploadStateService);

                var initialized = await uploadCoordinator.InitializeAsync();
                if (!initialized) {
                    MessageBox.Show(
                        "Failed to connect to Backblaze B2. Check your credentials in Settings.",
                        "Upload Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                // Строим пары (локальный путь, удаленный путь) ТОЛЬКО для экспортированных файлов
                var serverPath = System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(contentPath)); // assets/content -> server
                var filePairs = new List<(string LocalPath, string RemotePath)>();

                foreach (var localPath in exportedFiles) {
                    if (!System.IO.File.Exists(localPath)) {
                        logger.Warn($"Exported file not found: {localPath}");
                        continue;
                    }

                    // Строим удалённый путь относительно server/
                    string remotePath;
                    if (!string.IsNullOrEmpty(serverPath) && localPath.StartsWith(serverPath, StringComparison.OrdinalIgnoreCase)) {
                        var relativePath = localPath.Substring(serverPath.Length).TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                        remotePath = $"{projectName}/{relativePath.Replace('\\', '/')}";
                    } else {
                        // Fallback: просто имя файла
                        remotePath = $"{projectName}/{System.IO.Path.GetFileName(localPath)}";
                    }

                    filePairs.Add((localPath, remotePath));
                    logger.Info($"Will upload: {localPath} -> {remotePath}");
                }

                logger.Info($"Uploading {filePairs.Count} files (from {exportedFiles.Count} exported)");

                // Загружаем ТОЛЬКО экспортированные файлы
                var result = await b2Service.UploadBatchAsync(
                    filePairs,
                    progress: new Progress<B2UploadProgress>(p => {
                        Dispatcher.Invoke(() => {
                            ProgressBar.Value = p.PercentComplete * 0.9; // 90% for content
                            var fileName = System.IO.Path.GetFileName(p.CurrentFile);
                            ProgressTextBlock.Text = $"Upload: {fileName} ({p.CurrentFileIndex}/{p.TotalFiles})";
                        });
                    })
                );

                // Upload mapping.json separately (it's in server/, not in content/)
                int mappingUploaded = 0;
                if (!string.IsNullOrEmpty(serverPath)) {
                    var mappingPath = System.IO.Path.Combine(serverPath, "mapping.json");
                    if (System.IO.File.Exists(mappingPath)) {
                        try {
                            var mappingResult = await b2Service.UploadFileAsync(
                                mappingPath,
                                $"{projectName}/mapping.json",
                                null
                            );
                            if (mappingResult.Success) {
                                mappingUploaded = 1;
                                logger.Info($"Uploaded mapping.json to {projectName}/mapping.json");

                                // Save to persistence
                                var mappingRecord = new Data.UploadRecord {
                                    LocalPath = mappingPath,
                                    RemotePath = $"{projectName}/mapping.json",
                                    ContentSha1 = mappingResult.ContentSha1 ?? "",
                                    ContentLength = mappingResult.ContentLength,
                                    UploadedAt = DateTime.UtcNow,
                                    CdnUrl = mappingResult.CdnUrl ?? "",
                                    Status = "Uploaded",
                                    FileId = mappingResult.FileId,
                                    ProjectName = projectName
                                };
                                await uploadStateService.SaveUploadAsync(mappingRecord);
                            }
                        } catch (Exception ex) {
                            logger.Warn(ex, "Failed to upload mapping.json");
                        }
                    }

                    // Сохраняем записи о загрузке и обновляем статусы ресурсов
                    await SaveUploadRecordsAndUpdateStatusesAsync(result, serverPath, projectName, uploadStateService);
                }

                Dispatcher.Invoke(() => { ProgressBar.Value = 100; });

                MessageBox.Show(
                    $"Upload completed!\n\n" +
                    $"Files to upload: {filePairs.Count}\n" +
                    $"Uploaded: {result.SuccessCount + mappingUploaded}\n" +
                    $"Skipped (already exists): {result.SkippedCount}\n" +
                    $"Failed: {result.FailedCount}\n" +
                    (mappingUploaded > 0 ? "mapping.json: uploaded\n" : "") +
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
                ProgressTextBlock.Text = "";
            }
        }

        /// <summary>
        /// Smart selection: marks selected assets and their related dependencies for export.
        /// - Models: marks related materials and their textures
        /// - Materials: marks related textures
        /// - Textures: marks only the selected textures
        /// </summary>
        private void SelectRelatedButton_Click(object sender, RoutedEventArgs e) {
            logService.LogInfo("[SelectRelatedButton_Click] Button clicked");

            int modelsMarked = 0;
            int materialsMarked = 0;
            int texturesMarked = 0;

            // Get current tab to determine selection context
            var currentTab = tabControl.SelectedItem as TabItem;
            var tabHeader = currentTab?.Header?.ToString() ?? "";

            switch (tabHeader) {
                case "Models":
                    // Mark selected models in DataGrid
                    var selectedModels = ModelsDataGrid.SelectedItems.Cast<ModelResource>().ToList();
                    if (!selectedModels.Any()) {
                        MessageBox.Show(
                            "Select models in the table first",
                            "Select",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        return;
                    }

                    foreach (var model in selectedModels) {
                        if (!model.ExportToServer) {
                            model.ExportToServer = true;
                            modelsMarked++;
                        }
                    }

                    // Find and mark related materials and textures
                    var (relatedMaterials, relatedTextures) = FindRelatedAssets(selectedModels);
                    foreach (var material in relatedMaterials) {
                        if (!material.ExportToServer) {
                            material.ExportToServer = true;
                            materialsMarked++;
                        }
                    }
                    foreach (var texture in relatedTextures) {
                        if (!texture.ExportToServer) {
                            texture.ExportToServer = true;
                            texturesMarked++;
                        }
                    }
                    break;

                case "Materials":
                    // Mark selected materials, their textures, and related models
                    var selectedMaterials = MaterialsDataGrid.SelectedItems.Cast<MaterialResource>().ToList();
                    if (!selectedMaterials.Any()) {
                        MessageBox.Show(
                            "Select materials in the table first",
                            "Select",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        return;
                    }

                    // Find and mark related models (reverse lookup)
                    var relatedModelsForMaterials = FindRelatedModels(selectedMaterials);
                    foreach (var model in relatedModelsForMaterials) {
                        if (!model.ExportToServer) {
                            model.ExportToServer = true;
                            modelsMarked++;
                        }
                    }

                    foreach (var material in selectedMaterials) {
                        if (!material.ExportToServer) {
                            material.ExportToServer = true;
                            materialsMarked++;
                        }

                        // Find textures for this material
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

                        foreach (var id in textureIds.Where(id => id.HasValue)) {
                            var texture = viewModel.Textures.FirstOrDefault(t => t.ID == id!.Value);
                            if (texture != null && !texture.ExportToServer) {
                                texture.ExportToServer = true;
                                texturesMarked++;
                            }
                        }
                    }
                    break;

                case "Textures":
                    // Mark only selected textures
                    var selectedTextures = TexturesDataGrid.SelectedItems.Cast<TextureResource>().ToList();
                    if (!selectedTextures.Any()) {
                        MessageBox.Show(
                            "Select textures in the table first",
                            "Select",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        return;
                    }

                    foreach (var texture in selectedTextures) {
                        if (!texture.ExportToServer) {
                            texture.ExportToServer = true;
                            texturesMarked++;
                        }
                    }
                    break;

                default:
                    MessageBox.Show(
                        "Switch to Models, Materials, or Textures tab to select assets",
                        "Select",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
            }

            UpdateExportCounts();

            // Build result message
            var markedParts = new List<string>();
            if (modelsMarked > 0) markedParts.Add($"{modelsMarked} models");
            if (materialsMarked > 0) markedParts.Add($"{materialsMarked} materials");
            if (texturesMarked > 0) markedParts.Add($"{texturesMarked} textures");

            if (markedParts.Any()) {
                logService.LogInfo($"[SelectRelatedButton_Click] Marked: {string.Join(", ", markedParts)}");
            } else {
                logService.LogInfo("[SelectRelatedButton_Click] All selected assets already marked");
            }
        }

        /// <summary>
        /// Clears all export marks from all asset types
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

            UpdateExportCounts();
            logService.LogInfo("[ClearExportMarksButton_Click] All export marks cleared");
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
                using var uploadStateService = new Data.UploadStateService();
                var uploadCoordinator = new AssetUploadCoordinator(b2Service, uploadStateService);

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
                            ProgressBar.Value = p.PercentComplete * 0.9; // 90% for content
                            var fileName = System.IO.Path.GetFileName(p.CurrentFile);
                            ProgressTextBlock.Text = $"Upload: {fileName} ({p.CurrentFileIndex}/{p.TotalFiles})";
                        });
                    })
                );

                // Upload mapping.json separately (it's in server/, not in content/)
                int mappingUploaded = 0;
                var serverPath = System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(contentPath)); // Go up from assets/content to server
                if (!string.IsNullOrEmpty(serverPath)) {
                    var mappingPath = System.IO.Path.Combine(serverPath, "mapping.json");
                    if (System.IO.File.Exists(mappingPath)) {
                        try {
                            var mappingResult = await b2Service.UploadFileAsync(
                                mappingPath,
                                $"{projectName}/mapping.json",
                                null
                            );
                            if (mappingResult.Success) {
                                mappingUploaded = 1;
                                logger.Info($"Uploaded mapping.json to {projectName}/mapping.json");

                                // Save to persistence
                                var mappingRecord = new Data.UploadRecord {
                                    LocalPath = mappingPath,
                                    RemotePath = $"{projectName}/mapping.json",
                                    ContentSha1 = mappingResult.ContentSha1 ?? "",
                                    ContentLength = mappingResult.ContentLength,
                                    UploadedAt = DateTime.UtcNow,
                                    CdnUrl = mappingResult.CdnUrl ?? "",
                                    Status = "Uploaded",
                                    FileId = mappingResult.FileId,
                                    ProjectName = projectName
                                };
                                await uploadStateService.SaveUploadAsync(mappingRecord);
                            }
                        } catch (Exception ex) {
                            logger.Warn(ex, "Failed to upload mapping.json");
                        }
                    }

                    // Сохраняем записи о загрузке и обновляем статусы ресурсов
                    await SaveUploadRecordsAndUpdateStatusesAsync(result, serverPath, projectName, uploadStateService);
                }

                Dispatcher.Invoke(() => { ProgressBar.Value = 100; });

                MessageBox.Show(
                    $"Upload completed!\n\n" +
                    $"Uploaded: {result.SuccessCount + mappingUploaded}\n" +
                    $"Skipped (already exists): {result.SkippedCount}\n" +
                    $"Failed: {result.FailedCount}\n" +
                    (mappingUploaded > 0 ? "mapping.json: uploaded\n" : "") +
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
                ProgressTextBlock.Text = "";
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

        /// <summary>
        /// Сохраняет записи о загрузке и обновляет статусы ресурсов в UI
        /// </summary>
        private async Task SaveUploadRecordsAndUpdateStatusesAsync(
            Upload.B2BatchUploadResult uploadResult,
            string serverPath,
            string projectName,
            Data.UploadStateService uploadStateService) {

            logger.Info($"[SaveUploadRecords] Starting. ServerPath: {serverPath}, Project: {projectName}, Results: {uploadResult.Results.Count}");

            // Читаем mapping.json для получения ResourceId
            var mappingPath = Path.Combine(serverPath, "mapping.json");
            if (!File.Exists(mappingPath)) {
                logger.Warn($"[SaveUploadRecords] mapping.json not found at: {mappingPath}");
                return;
            }

            Export.MappingData? mapping;
            try {
                var json = await File.ReadAllTextAsync(mappingPath);
                mapping = Newtonsoft.Json.JsonConvert.DeserializeObject<Export.MappingData>(json);
                logger.Info($"[SaveUploadRecords] Loaded mapping.json: Models={mapping?.Models?.Count ?? 0}, Materials={mapping?.Materials?.Count ?? 0}, Textures={mapping?.Textures?.Count ?? 0}");
            } catch (Exception ex) {
                logger.Error(ex, $"[SaveUploadRecords] Failed to parse mapping.json: {mappingPath}");
                return;
            }

            if (mapping == null) {
                logger.Warn("[SaveUploadRecords] mapping is null after deserialization");
                return;
            }

            // Строим обратный индекс: relativePath -> (resourceId, resourceType)
            var pathToResource = new Dictionary<string, (int ResourceId, string ResourceType)>(StringComparer.OrdinalIgnoreCase);

            foreach (var (idStr, entry) in mapping.Models) {
                if (int.TryParse(idStr, out var id) && !string.IsNullOrEmpty(entry.Path)) {
                    pathToResource[entry.Path] = (id, "Model");
                    logger.Debug($"[SaveUploadRecords] Model {id}: {entry.Path}");
                    foreach (var lod in entry.Lods) {
                        if (!string.IsNullOrEmpty(lod.File)) {
                            pathToResource[lod.File] = (id, "Model");
                            logger.Debug($"[SaveUploadRecords] Model LOD {id}: {lod.File}");
                        }
                    }
                }
            }

            foreach (var (idStr, path) in mapping.Materials) {
                if (int.TryParse(idStr, out var id) && !string.IsNullOrEmpty(path)) {
                    pathToResource[path] = (id, "Material");
                    logger.Debug($"[SaveUploadRecords] Material {id}: {path}");
                }
            }

            foreach (var (idStr, path) in mapping.Textures) {
                if (int.TryParse(idStr, out var id) && !string.IsNullOrEmpty(path)) {
                    pathToResource[path] = (id, "Texture");
                    logger.Debug($"[SaveUploadRecords] Texture {id}: {path}");
                }
            }

            logger.Info($"[SaveUploadRecords] Built path index with {pathToResource.Count} entries");

            // Сохраняем записи о загрузке с ResourceId
            var uploadedResourceIds = new Dictionary<string, HashSet<int>> {
                ["Model"] = new(),
                ["Material"] = new(),
                ["Texture"] = new()
            };

            int savedCount = 0;
            int matchedCount = 0;

            foreach (var fileResult in uploadResult.Results.Where(r => r.Success || r.Skipped)) {
                var remotePath = fileResult.RemotePath;
                var assetsIndex = remotePath.IndexOf("assets/", StringComparison.OrdinalIgnoreCase);
                var relativePath = assetsIndex >= 0 ? remotePath.Substring(assetsIndex) : remotePath;

                int? resourceId = null;
                string? resourceType = null;

                if (pathToResource.TryGetValue(relativePath, out var resourceInfo)) {
                    resourceId = resourceInfo.ResourceId;
                    resourceType = resourceInfo.ResourceType;
                    matchedCount++;
                    logger.Debug($"[SaveUploadRecords] Matched: {relativePath} -> {resourceType} {resourceId}");
                } else {
                    logger.Debug($"[SaveUploadRecords] No match for: {relativePath}");
                }

                // Сохраняем запись в SQLite
                var record = new Data.UploadRecord {
                    LocalPath = fileResult.LocalPath ?? "",
                    RemotePath = remotePath,
                    ContentSha1 = fileResult.ContentSha1 ?? "",
                    ContentLength = fileResult.ContentLength,
                    UploadedAt = DateTime.UtcNow,
                    CdnUrl = fileResult.CdnUrl ?? "",
                    Status = "Uploaded",
                    FileId = fileResult.FileId,
                    ProjectName = projectName,
                    ResourceId = resourceId,
                    ResourceType = resourceType
                };

                try {
                    await uploadStateService.SaveUploadAsync(record);
                    savedCount++;
                } catch (Exception ex) {
                    logger.Error(ex, $"[SaveUploadRecords] Failed to save record for: {remotePath}");
                }

                if (resourceId.HasValue && resourceType != null) {
                    uploadedResourceIds[resourceType].Add(resourceId.Value);
                }
            }

            logger.Info($"[SaveUploadRecords] Saved {savedCount} records, matched {matchedCount} to resources");

            // Обновляем статусы ресурсов в UI
            Dispatcher.Invoke(() => {
                foreach (var modelId in uploadedResourceIds["Model"]) {
                    var model = viewModel.Models.FirstOrDefault(m => m.ID == modelId);
                    if (model != null) {
                        model.UploadStatus = "Uploaded";
                        model.LastUploadedAt = DateTime.UtcNow;
                    }
                }

                foreach (var materialId in uploadedResourceIds["Material"]) {
                    var material = viewModel.Materials.FirstOrDefault(m => m.ID == materialId);
                    if (material != null) {
                        material.UploadStatus = "Uploaded";
                        material.LastUploadedAt = DateTime.UtcNow;
                    }
                }

                foreach (var textureId in uploadedResourceIds["Texture"]) {
                    var texture = viewModel.Textures.FirstOrDefault(t => t.ID == textureId);
                    if (texture != null) {
                        texture.UploadStatus = "Uploaded";
                        texture.LastUploadedAt = DateTime.UtcNow;
                    }
                }
            });

            logger.Info($"[SaveUploadRecords] Updated UI statuses: {uploadedResourceIds["Model"].Count} models, {uploadedResourceIds["Material"].Count} materials, {uploadedResourceIds["Texture"].Count} textures");
        }

        /// <summary>
        /// Находит модели, связанные с материалами (обратный поиск)
        /// Использует ту же логику, что и FindRelatedAssets, но в обратном направлении
        /// </summary>
        private List<ModelResource> FindRelatedModels(List<MaterialResource> materials) {
            var relatedModels = new HashSet<ModelResource>();

            logService.LogInfo($"[FindRelatedModels] Processing {materials.Count} materials to find related models");

            foreach (var material in materials) {
                // Получаем путь папки материала
                string? materialFolderPath = null;
                if (material.Parent.HasValue && material.Parent.Value != 0) {
                    folderPaths.TryGetValue(material.Parent.Value, out materialFolderPath);
                }

                var materialBaseName = ExtractBaseName(material.Name);

                foreach (var model in viewModel.Models) {
                    bool isRelated = false;

                    // По Parent ID - модели в той же папке
                    if (material.Parent.HasValue && model.Parent == material.Parent) {
                        isRelated = true;
                    }
                    // По пути папки - модель в родительской папке материала
                    else if (!string.IsNullOrEmpty(materialFolderPath) && model.Parent.HasValue) {
                        if (folderPaths.TryGetValue(model.Parent.Value, out var modelFolderPath)) {
                            // Материал в подпапке модели
                            if (materialFolderPath.StartsWith(modelFolderPath, StringComparison.OrdinalIgnoreCase)) {
                                isRelated = true;
                            }
                        }
                    }
                    // По имени
                    else if (!string.IsNullOrEmpty(materialBaseName) && !string.IsNullOrEmpty(model.Name)) {
                        var modelBaseName = ExtractBaseName(model.Name);
                        if (materialBaseName.StartsWith(modelBaseName, StringComparison.OrdinalIgnoreCase) ||
                            modelBaseName.StartsWith(materialBaseName, StringComparison.OrdinalIgnoreCase)) {
                            isRelated = true;
                        }
                    }

                    if (isRelated) {
                        relatedModels.Add(model);
                        logService.LogInfo($"[FindRelatedModels] Found related model: {model.Name} for material: {material.Name}");
                    }
                }
            }

            return relatedModels.ToList();
        }
    }
}


