using AssetProcessor.Helpers;
using AssetProcessor.Resources;
using AssetProcessor.Services;
using AssetProcessor.Settings;
using AssetProcessor.ViewModels;
using Assimp;
using HelixToolkit.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using OxyPlot;
using OxyPlot.Axes;
using SixLabors.ImageSharp;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives; // Для использования Bitmap и Rectangle
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Xceed.Wpf.Toolkit;
using MessageBox = System.Windows.MessageBox;
using PointF = System.Drawing.PointF;
using System.Linq;
using AssetProcessor.TextureConversion.Settings;

namespace AssetProcessor {
    public enum ColorChannel {
        RGB,
        R,
        G,
        B,
        A,
    }

    public enum UVChannel {
        UV0,
        UV1
    }

    public partial class MainWindow : Window, INotifyPropertyChanged {

        private ObservableCollection<TextureResource> textures = [];
        public ObservableCollection<TextureResource> Textures {
            get { return textures; }
            set {
                textures = value;
                OnPropertyChanged(nameof(Textures));
            }
        }

        private ObservableCollection<ModelResource> models = [];
        public ObservableCollection<ModelResource> Models {
            get { return models; }
            set {
                models = value;
                OnPropertyChanged(nameof(Models));
            }
        }

        private ObservableCollection<MaterialResource> materials = [];
        public ObservableCollection<MaterialResource> Materials {
            get { return materials; }
            set {
                materials = value;
                OnPropertyChanged(nameof(Materials));
            }
        }

        private ObservableCollection<BaseResource> assets = [];
        public ObservableCollection<BaseResource> Assets {
            get { return assets; }
            set {
                assets = value;
                OnPropertyChanged(nameof(Assets));
            }
        }

        private bool isDownloadButtonEnabled = false;
        public bool IsDownloadButtonEnabled {
            get => isDownloadButtonEnabled;
            set {
                isDownloadButtonEnabled = value;
                OnPropertyChanged(nameof(IsDownloadButtonEnabled));
            }
        }

        private readonly SemaphoreSlim getAssetsSemaphore;
        private readonly SemaphoreSlim downloadSemaphore;
        private string? projectFolderPath = string.Empty;
        private string? userName = string.Empty;
        private string? userID = string.Empty;
        private string? projectName = string.Empty;
        private bool? isViewerVisible = true;
        private BitmapSource? originalBitmapSource;
        private readonly List<string> supportedFormats = [".png", ".jpg", ".jpeg"];
        private readonly List<string> excludedFormats = [".hdr", ".avif"];
        private readonly List<string> supportedModelFormats = [".fbx", ".obj"];//, ".glb"];
        private CancellationTokenSource cancellationTokenSource = new();
        private readonly PlayCanvasService playCanvasService = new();
        private Dictionary<int, string> folderPaths = new();
        private readonly Dictionary<string, BitmapImage> imageCache = new(); // Кеш для загруженных изображений
        private CancellationTokenSource? textureLoadCancellation; // Токен отмены для загрузки текстур
        private const int MaxPreviewSize = 512; // Максимальный размер изображения для превью (оптимизировано для скорости)
        private const int ThumbnailSize = 256; // Размер для быстрого превью

        private ObservableCollection<KeyValuePair<string, string>> projects = [];
        public ObservableCollection<KeyValuePair<string, string>> Projects {
            get { return projects; }
            set {
                projects = value;
                OnPropertyChanged(nameof(Projects));
            }
        }

        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly ObservableCollection<Branch> branches = [];
        public ObservableCollection<Branch> Branches {
            get { return branches; }
        }

        public MainWindow() {
            InitializeComponent();
            _ = LoadLastSettings();

            // Отображение версии приложения с информацией о бранче и коммите
            VersionTextBlock.Text = $"v{VersionHelper.GetVersionString()}";

            // Заполнение ComboBox для Color Channel
            PopulateComboBox<ColorChannel>(MaterialAOColorChannelComboBox);
            PopulateComboBox<ColorChannel>(MaterialDiffuseColorChannelComboBox);
            PopulateComboBox<ColorChannel>(MaterialSpecularColorChannelComboBox);
            PopulateComboBox<ColorChannel>(MaterialMetalnessColorChannelComboBox);
            PopulateComboBox<ColorChannel>(MaterialGlossinessColorChannelComboBox);

            // Заполнение ComboBox для UV Channel
            PopulateComboBox<UVChannel>(MaterialDiffuseUVChannelComboBox);
            PopulateComboBox<UVChannel>(MaterialSpecularUVChannelComboBox);
            PopulateComboBox<UVChannel>(MaterialNormalUVChannelComboBox);
            PopulateComboBox<UVChannel>(MaterialAOUVChannelComboBox);

            LoadModel(path: MainWindowHelpers.MODEL_PATH);

            getAssetsSemaphore = new SemaphoreSlim(AppSettings.Default.GetTexturesSemaphoreLimit);
            downloadSemaphore = new SemaphoreSlim(AppSettings.Default.DownloadSemaphoreLimit);

            projectFolderPath = AppSettings.Default.ProjectsFolderPath;
            UpdateConnectionStatus(false);

            TexturesDataGrid.ItemsSource = textures;
            ModelsDataGrid.ItemsSource = models;
            MaterialsDataGrid.ItemsSource = materials;

            TexturesDataGrid.LoadingRow += TexturesDataGrid_LoadingRow;
            TexturesDataGrid.Sorting += TexturesDataGrid_Sorting;

            DataContext = this;
            this.Closing += MainWindow_Closing;
            //LoadLastSettings();

            RenderOptions.SetBitmapScalingMode(TexturePreviewImage, BitmapScalingMode.HighQuality);
            RenderOptions.SetBitmapScalingMode(UVImage, BitmapScalingMode.HighQuality);
            RenderOptions.SetBitmapScalingMode(UVImage2, BitmapScalingMode.HighQuality);

            // Вызов асинхронного метода
            _ = InitializeAsync(); // Асинхронный метод вызывается без ожидания завершения
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string? propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #region UI Viewer
        private async void FilterButton_Click(object sender, RoutedEventArgs e) {
            if (sender is ToggleButton button) {
                string? channel = button.Tag.ToString();
                if (button.IsChecked == true) {
                    // Сброс всех остальных кнопок
                    RChannelButton.IsChecked = button == RChannelButton;
                    GChannelButton.IsChecked = button == GChannelButton;
                    BChannelButton.IsChecked = button == BChannelButton;
                    AChannelButton.IsChecked = button == AChannelButton;

                    // Применяем фильтр
                    if (!string.IsNullOrEmpty(channel)) {
                        await FilterChannelAsync(channel);
                    }
                } else {
                    // Сбрасываем фильтр, если кнопка была отжата
                    ShowOriginalImage();
                }
            }
        }

        private async Task FilterChannelAsync(string channel) {
            if (TexturePreviewImage.Source is BitmapSource bitmapSource) {
                originalBitmapSource ??= bitmapSource.Clone();
                BitmapSource filteredBitmap = await MainWindowHelpers.ApplyChannelFilterAsync(originalBitmapSource, channel);

                // Обновляем UI в основном потоке
                Dispatcher.Invoke(() => {
                    TexturePreviewImage.Source = filteredBitmap;
                    UpdateHistogram(filteredBitmap, true);  // Обновление гистограммы
                });
            }
        }

        private async void ShowOriginalImage() {
            if (originalBitmapSource != null) {
                await Dispatcher.InvokeAsync(() => {
                    TexturePreviewImage.Source = originalBitmapSource;
                    RChannelButton.IsChecked = false;
                    GChannelButton.IsChecked = false;
                    BChannelButton.IsChecked = false;
                    AChannelButton.IsChecked = false;
                    UpdateHistogram(originalBitmapSource);
                });
            }
        }

        private void UpdateHistogram(BitmapSource bitmapSource, bool isGray = false) {
            if (bitmapSource == null) return;

            PlotModel histogramModel = new();

            int[] redHistogram = new int[256];
            int[] greenHistogram = new int[256];
            int[] blueHistogram = new int[256];


            // Обработка изображения и заполнение гистограммы
            MainWindowHelpers.ProcessImage(bitmapSource, redHistogram, greenHistogram, blueHistogram);

            if (!isGray) {
                MainWindowHelpers.AddSeriesToModel(histogramModel, redHistogram, OxyColors.Red);
                MainWindowHelpers.AddSeriesToModel(histogramModel, greenHistogram, OxyColors.Green);
                MainWindowHelpers.AddSeriesToModel(histogramModel, blueHistogram, OxyColors.Blue);
            } else {
                MainWindowHelpers.AddSeriesToModel(histogramModel, redHistogram, OxyColors.Black);
            }

            histogramModel.Axes.Add(new LinearAxis {
                Position = AxisPosition.Bottom,
                IsAxisVisible = false,
                AxislineThickness = 0.5,
                MajorGridlineThickness = 0.5,
                MinorGridlineThickness = 0.5
            });
            histogramModel.Axes.Add(new LinearAxis {
                Position = AxisPosition.Left,
                IsAxisVisible = false,
                AxislineThickness = 0.5,
                MajorGridlineThickness = 0.5,
                MinorGridlineThickness = 0.5
            });

            Dispatcher.Invoke(() => HistogramPlotView.Model = histogramModel);
        }

        private async Task UpdateHistogramAsync(BitmapSource bitmapSource, bool isGray = false) {
            if (bitmapSource == null) return;

            await Task.Run(() => {
                PlotModel histogramModel = new();

                int[] redHistogram = new int[256];
                int[] greenHistogram = new int[256];
                int[] blueHistogram = new int[256];

                // Обработка изображения и заполнение гистограммы
                MainWindowHelpers.ProcessImage(bitmapSource, redHistogram, greenHistogram, blueHistogram);

                if (!isGray) {
                    MainWindowHelpers.AddSeriesToModel(histogramModel, redHistogram, OxyColors.Red);
                    MainWindowHelpers.AddSeriesToModel(histogramModel, greenHistogram, OxyColors.Green);
                    MainWindowHelpers.AddSeriesToModel(histogramModel, blueHistogram, OxyColors.Blue);
                } else {
                    MainWindowHelpers.AddSeriesToModel(histogramModel, redHistogram, OxyColors.Black);
                }

                histogramModel.Axes.Add(new LinearAxis {
                    Position = AxisPosition.Bottom,
                    IsAxisVisible = false,
                    AxislineThickness = 0.5,
                    MajorGridlineThickness = 0.5,
                    MinorGridlineThickness = 0.5
                });
                histogramModel.Axes.Add(new LinearAxis {
                    Position = AxisPosition.Left,
                    IsAxisVisible = false,
                    AxislineThickness = 0.5,
                    MajorGridlineThickness = 0.5,
                    MinorGridlineThickness = 0.5
                });

                Dispatcher.Invoke(() => HistogramPlotView.Model = histogramModel);
            });
        }

        private static void PopulateComboBox<T>(ComboBox comboBox) {
            comboBox.Items.Clear();
            foreach (object? value in Enum.GetValues(typeof(T))) {
                comboBox.Items.Add(value.ToString());
            }
        }

        #endregion

        #region Models

        private void ModelsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (ModelsDataGrid.SelectedItem is ModelResource selectedModel) {
                if (!string.IsNullOrEmpty(selectedModel.Path)) {
                    if (selectedModel.Status == "Downloaded") { // Если модель уже загружена
                                                                // Загружаем модель во вьюпорт (3D просмотрщик}
                        LoadModel(selectedModel.Path);
                        // Обновляем информацию о модели
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

        private void UpdateUVImage(Mesh mesh) {
            int width = 512;
            int height = 512;

            // Создаем bitmap для основной UV карты
            Bitmap bitmap1 = new(width, height);
            using (Graphics g = Graphics.FromImage(bitmap1)) {
                g.Clear(System.Drawing.Color.DarkGray);
                if (mesh.TextureCoordinateChannels.Length > 0 && mesh.TextureCoordinateChannels[0] != null) {
                    List<Assimp.Vector3D> uvs = mesh.TextureCoordinateChannels[0];
                    foreach (Face? face in mesh.Faces) {
                        if (face.IndexCount == 3) {
                            PointF[] points = new PointF[3];
                            for (int i = 0; i < 3; i++) {
                                int vertexIndex = face.Indices[i];
                                if (vertexIndex < uvs.Count) {
                                    Assimp.Vector3D uv = uvs[vertexIndex];
                                    points[i] = new PointF(uv.X * width, (1 - uv.Y) * height);
                                }
                            }
                            // Заливка треугольника полупрозрачным цветом
                            g.FillPolygon(new SolidBrush(System.Drawing.Color.FromArgb(186, System.Drawing.Color.OrangeRed)), points);
                            // Обводка треугольника черным цветом
                            g.DrawPolygon(Pens.DarkBlue, points);
                        }
                    }
                }
            }

            // Создаем bitmap для дополнительной UV карты
            Bitmap bitmap2 = new(width, height);
            bool hasSecondUV = mesh.TextureCoordinateChannels.Length > 1 && mesh.TextureCoordinateChannels[1] != null;
            using (Graphics g = Graphics.FromImage(bitmap2)) {
                g.Clear(System.Drawing.Color.DarkGray);
                if (hasSecondUV) {
                    List<Assimp.Vector3D> uvs = mesh.TextureCoordinateChannels[1];
                    foreach (Face? face in mesh.Faces.Where(face => face.IndexCount == 3)) {
                        PointF[] points = new PointF[3];
                        for (int i = 0; i < 3; i++) {
                            int vertexIndex = face.Indices[i];
                            if (vertexIndex < uvs.Count) {
                                Assimp.Vector3D uv = uvs[vertexIndex];
                                points[i] = new PointF(uv.X * width, (1 - uv.Y) * height);
                            }
                        }
                        // Заливка треугольника полупрозрачным цветом
                        g.FillPolygon(new SolidBrush(System.Drawing.Color.FromArgb(186, System.Drawing.Color.OrangeRed)), points);
                        // Обводка треугольника черным цветом
                        g.DrawPolygon(Pens.DarkBlue, points);
                    }
                }
            }

            // Преобразуем bitmap в BitmapSource для отображения в WPF
            BitmapSource bitmapSource1 = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                bitmap1.GetHbitmap(),
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(width, height));
            bitmap1.Dispose();

            BitmapSource bitmapSource2 = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                bitmap2.GetHbitmap(),
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(width, height));
            bitmap2.Dispose();

            Dispatcher.Invoke(() => {
                UVImage.Source = bitmapSource1;
                UVImage2.Source = bitmapSource2;
                //UVImage2Border.Visibility = hasSecondUV ? Visibility.Visible : Visibility.Collapsed;
                //Console.WriteLine($"UV Map 2 visibility: {UVImage2Border.Visibility}");
            });
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
                    DiffuseMaterial material = new(new SolidColorBrush(Colors.Gray));
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

                ModelVisual3D pivotGizmo = MainWindowHelpers.CreatePivotGizmo(transformGroup);
                viewPort3d.Children.Add(pivotGizmo);

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

        #endregion

        #region UI Event Handlers

        private void ShowTextureViewer() {
            TextureViewerScroll.Visibility = Visibility.Visible;
            ModelViewer.Visibility = Visibility.Collapsed;
            MaterialViewerScroll.Visibility = Visibility.Collapsed;
        }

        private void ShowModelViewer() {
            TextureViewerScroll.Visibility = Visibility.Collapsed;
            ModelViewer.Visibility = Visibility.Visible;
            MaterialViewerScroll.Visibility = Visibility.Collapsed;
        }

        private void ShowMaterialViewer() {
            TextureViewerScroll.Visibility = Visibility.Collapsed;
            ModelViewer.Visibility = Visibility.Collapsed;
            MaterialViewerScroll.Visibility = Visibility.Visible;
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (tabControl.SelectedItem is TabItem selectedTab) {
                switch (selectedTab.Header.ToString()) {
                    case "Textures":
                        ShowTextureViewer();
                        break;
                    case "Models":
                        ShowModelViewer();
                        break;
                    case "Materials":
                        ShowMaterialViewer();
                        break;
                }
            }
        }

        private async void ProjectsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (ProjectsComboBox.SelectedItem is KeyValuePair<string, string> selectedProject) {
                projectName = MainWindowHelpers.CleanProjectName(selectedProject.Value);
                projectFolderPath = Path.Combine(AppSettings.Default.ProjectsFolderPath, projectName);
                MainWindowHelpers.LogInfo($"Updated Project Folder Path: {projectFolderPath}");

                // Проверяем наличие JSON-файла
                bool jsonLoaded = await LoadAssetsFromJsonFileAsync();
                if (!jsonLoaded) {
                    // Если JSON-файл не найден, можно либо предложить подключение к серверу, либо отобразить сообщение.
                    MessageBox.Show("No saved data found. Please connect to the server.");
                }

                // Обновляем ветки для выбранного проекта
                List<Branch> branches = await playCanvasService.GetBranchesAsync(selectedProject.Key, AppSettings.Default.PlaycanvasApiKey, [], CancellationToken.None);
                if (branches != null && branches.Count > 0) {
                    Branches.Clear();
                    foreach (Branch branch in branches) {
                        Branches.Add(branch);
                    }
                    BranchesComboBox.SelectedIndex = 0;
                } else {
                    Branches.Clear();
                    BranchesComboBox.SelectedIndex = -1;
                }

                SaveCurrentSettings();
            }
        }

        private void BranchesComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e) {
            SaveCurrentSettings();
        }

        private async void TexturesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            // Update selection count in central control box
            UpdateSelectedTexturesCount();

            // Отменяем предыдущую загрузку, если она еще выполняется
            textureLoadCancellation?.Cancel();
            textureLoadCancellation = new CancellationTokenSource();
            CancellationToken cancellationToken = textureLoadCancellation.Token;

            if (TexturesDataGrid.SelectedItem is TextureResource selectedTexture) {
                if (!string.IsNullOrEmpty(selectedTexture.Path)) {
                    try {
                        // Обновляем информацию о текстуре сразу
                        TextureNameTextBlock.Text = "Texture Name: " + selectedTexture.Name;
                        TextureResolutionTextBlock.Text = "Resolution: " + string.Join("x", selectedTexture.Resolution);
                        AssetProcessor.Helpers.SizeConverter sizeConverter = new();
                        object size = AssetProcessor.Helpers.SizeConverter.Convert(selectedTexture.Size) ?? "Unknown size";
                        TextureSizeTextBlock.Text = "Size: " + size;

                        // Load conversion settings for this texture
                        LoadTextureConversionSettings(selectedTexture);

                        // Проверяем кеш
                        if (imageCache.TryGetValue(selectedTexture.Path, out BitmapImage? cachedImage)) {
                            // Используем кешированное изображение - мгновенно!
                            TexturePreviewImage.Source = cachedImage;
                            originalBitmapSource = cachedImage.Clone();
                            // Асинхронное обновление гистограммы
                            _ = UpdateHistogramAsync(originalBitmapSource);
                            ShowOriginalImage();
                            return;
                        }

                        // Сначала загружаем очень маленькое превью для мгновенного отклика
                        BitmapImage? thumbnailImage = LoadOptimizedImage(selectedTexture.Path, ThumbnailSize);
                        if (thumbnailImage == null) {
                            MainWindowHelpers.LogInfo($"Error loading thumbnail for texture: {selectedTexture.Name}");
                            return;
                        }

                        TexturePreviewImage.Source = thumbnailImage;

                        // Асинхронная загрузка оптимизированной текстуры с возможностью отмены
                        await Task.Run(() => {
                            if (cancellationToken.IsCancellationRequested) return;

                            // Загружаем изображение с ограничением размера (512px для баланса качества и скорости)
                            BitmapImage? bitmapImage = LoadOptimizedImage(selectedTexture.Path, MaxPreviewSize);

                            if (bitmapImage == null || cancellationToken.IsCancellationRequested) return;

                            Dispatcher.Invoke(() => {
                                if (cancellationToken.IsCancellationRequested) return;

                                // Добавляем в кеш
                                if (!imageCache.ContainsKey(selectedTexture.Path)) {
                                    imageCache[selectedTexture.Path] = bitmapImage;

                                    // Ограничиваем размер кеша (максимум 50 изображений)
                                    if (imageCache.Count > 50) {
                                        var firstKey = imageCache.Keys.First();
                                        imageCache.Remove(firstKey);
                                    }
                                }

                                TexturePreviewImage.Source = bitmapImage;

                                // Сохраняем оригинальную текстуру
                                originalBitmapSource = bitmapImage.Clone();

                                // Асинхронное обновление гистограммы
                                _ = UpdateHistogramAsync(originalBitmapSource);

                                // Сбрасываем все фильтры
                                ShowOriginalImage();
                            });
                        }, cancellationToken);
                    } catch (OperationCanceledException) {
                        // Загрузка была отменена - это нормально
                    } catch (Exception ex) {
                        MainWindowHelpers.LogError($"Error loading texture {selectedTexture.Name}: {ex.Message}");
                    }
                }
            }
        }

        private BitmapImage? LoadOptimizedImage(string path, int maxSize) {
            try {
                BitmapImage bitmapImage = new();
                bitmapImage.BeginInit();
                bitmapImage.UriSource = new Uri(path);
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;

                // Определяем размер изображения
                using (var imageStream = new FileStream(path, FileMode.Open, FileAccess.Read)) {
                    var decoder = BitmapDecoder.Create(imageStream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
                    int width = decoder.Frames[0].PixelWidth;
                    int height = decoder.Frames[0].PixelHeight;

                    // Всегда масштабируем до maxSize или меньше для максимальной производительности
                    if (width > maxSize || height > maxSize) {
                        double scale = Math.Min((double)maxSize / width, (double)maxSize / height);
                        bitmapImage.DecodePixelWidth = (int)(width * scale);
                        bitmapImage.DecodePixelHeight = (int)(height * scale);
                    }
                }

                bitmapImage.EndInit();
                bitmapImage.Freeze(); // Замораживаем изображение для безопасного использования в другом потоке
                return bitmapImage;
            } catch (Exception ex) {
                MainWindowHelpers.LogError($"Error loading optimized image from {path}: {ex.Message}");
                return null;
            }
        }

        private void TexturesDataGrid_LoadingRow(object? sender, DataGridRowEventArgs? e) {
            if (e?.Row?.DataContext is TextureResource texture) {
                // Устанавливаем цвет фона в зависимости от типа текстуры
                if (!string.IsNullOrEmpty(texture.TextureType)) {
                    var backgroundBrush = new TextureTypeToBackgroundConverter().Convert(texture.TextureType, typeof(System.Windows.Media.Brush), null!, CultureInfo.InvariantCulture) as System.Windows.Media.Brush;
                    e.Row.Background = backgroundBrush ?? System.Windows.Media.Brushes.Transparent;
                } else {
                    e.Row.Background = System.Windows.Media.Brushes.Transparent;
                }
            }
        }

        private void ToggleViewerButton_Click(object? sender, RoutedEventArgs e) {
            if (isViewerVisible == true) {
                ToggleViewButton.Content = "►";
                PreviewColumn.Width = new GridLength(0);
            } else {
                ToggleViewButton.Content = "◄";
                PreviewColumn.Width = new GridLength(300); // Вернуть исходную ширину
            }
            isViewerVisible = !isViewerVisible;
        }

        private void TexturesDataGrid_Sorting(object? sender, DataGridSortingEventArgs e) {
            if (e.Column.SortMemberPath == "Status") {
                e.Handled = true;
                ICollectionView dataView = CollectionViewSource.GetDefaultView(TexturesDataGrid.ItemsSource);
                ListSortDirection direction = e.Column.SortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
                dataView.SortDescriptions.Clear();
                dataView.SortDescriptions.Add(new SortDescription("Status", direction));
                e.Column.SortDirection = direction;
            }
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs? e) {
            SaveCurrentSettings();
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e) {
            cancellationTokenSource?.Cancel();
            cancellationTokenSource = new CancellationTokenSource();
        }

        private void Setting(object? sender, RoutedEventArgs e) {
            SettingsWindow settingsWindow = new();
            settingsWindow.ShowDialog();
        }

        private async void GetListAssets(object sender, RoutedEventArgs e) {
            try {
                CancelButton.IsEnabled = true;
                if (cancellationTokenSource != null) {
                    await TryConnect(cancellationTokenSource.Token);
                }
            } catch (Exception ex) {
                MessageBox.Show($"Error in Get ListAssets: {ex.Message}");
                MainWindowHelpers.LogError($"Error in Get List Assets: {ex}");
            } finally {
                CancelButton.IsEnabled = false;
            }
        }

        private async void Connect(object? sender, RoutedEventArgs e) {
            CancellationToken cancellationToken = cancellationTokenSource.Token;

            if (string.IsNullOrEmpty(AppSettings.Default.PlaycanvasApiKey) || string.IsNullOrEmpty(AppSettings.Default.UserName)) {
                MessageBox.Show("Please set your Playcanvas API key, and Username in the settings window.");
                SettingsWindow settingsWindow = new();
                settingsWindow.ShowDialog();
                return; // Прерываем выполнение Connect, если данные не заполнены
            } else {
                try {
                    userName = AppSettings.Default.UserName.ToLower();
                    userID = await playCanvasService.GetUserIdAsync(userName, AppSettings.Default.PlaycanvasApiKey, cancellationToken);
                    if (string.IsNullOrEmpty(userID)) {
                        throw new Exception("User ID is null or empty");
                    } else {
                        await Dispatcher.InvokeAsync(() => UpdateConnectionStatus(true, $"by userID: {userID}"));
                    }

                    Dictionary<string, string> projectsDict = await playCanvasService.GetProjectsAsync(userID, AppSettings.Default.PlaycanvasApiKey, [], cancellationToken);
                    if (projectsDict != null && projectsDict.Count > 0) {
                        string lastSelectedProjectId = AppSettings.Default.LastSelectedProjectId;

                        Projects.Clear();
                        foreach (KeyValuePair<string, string> project in projectsDict) {
                            Projects.Add(project);
                        }

                        if (!string.IsNullOrEmpty(lastSelectedProjectId) && projectsDict.ContainsKey(lastSelectedProjectId)) {
                            ProjectsComboBox.SelectedValue = lastSelectedProjectId;
                        } else {
                            ProjectsComboBox.SelectedIndex = 0;
                        }

                        if (ProjectsComboBox.SelectedItem != null) {
                            string projectId = ((KeyValuePair<string, string>)ProjectsComboBox.SelectedItem).Key;
                            await LoadBranchesAsync(projectId, cancellationToken);
                            UpdateProjectPath(projectId);
                        }
                    } else {
                        throw new Exception("Project list is empty");
                    }
                } catch (Exception ex) {
                    MessageBox.Show($"Error: {ex.Message}");
                }
            }
        }

        private async Task LoadBranchesAsync(string projectId, CancellationToken cancellationToken) {
            try {
                List<Branch> branchesList = await playCanvasService.GetBranchesAsync(projectId, AppSettings.Default.PlaycanvasApiKey, [], cancellationToken);
                if (branchesList != null && branchesList.Count > 0) {
                    Branches.Clear();
                    foreach (Branch branch in branchesList) {
                        Branches.Add(branch);
                    }

                    string lastSelectedBranchName = AppSettings.Default.LastSelectedBranchName;
                    if (!string.IsNullOrEmpty(lastSelectedBranchName)) {
                        Branch? selectedBranch = branchesList.FirstOrDefault(b => b.Name == lastSelectedBranchName);
                        if (selectedBranch != null) {
                            BranchesComboBox.SelectedValue = selectedBranch.Id;
                        } else {
                            BranchesComboBox.SelectedIndex = 0;
                        }
                    } else {
                        BranchesComboBox.SelectedIndex = 0;
                    }
                } else {
                    Branches.Clear();
                    BranchesComboBox.SelectedIndex = -1;
                }
            } catch (Exception ex) {
                MessageBox.Show($"Error loading branches: {ex.Message}");
            }
        }

        private void UpdateProjectPath(string projectId) {
            ArgumentNullException.ThrowIfNull(projectId);

            if (ProjectsComboBox.SelectedItem is KeyValuePair<string, string> selectedProject) {
                projectName = MainWindowHelpers.CleanProjectName(selectedProject.Value);
                projectFolderPath = Path.Combine(AppSettings.Default.ProjectsFolderPath, projectName);

                MainWindowHelpers.LogInfo($"Updated Project Folder Path: {projectFolderPath}");
            }
        }

        private void AboutMenu(object? sender, RoutedEventArgs e) {
            MessageBox.Show("AssetProcessor v1.0\n\nDeveloped by: SashaRX\n\n2021");
        }

        private void SettingsMenu(object? sender, RoutedEventArgs e) {
            SettingsWindow settingsWindow = new();
            settingsWindow.ShowDialog();
        }

        private void TextureConversionMenu(object? sender, RoutedEventArgs e) {
            TextureConversionWindow textureConversionWindow = new();
            textureConversionWindow.ShowDialog();
        }

        private void ExitMenu(object? sender, RoutedEventArgs e) {
            Close();
        }

        private void GridSplitter_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e) {
            GridSplitter gridSplitter = (GridSplitter)sender;

            if (gridSplitter.Parent is not Grid grid) {
                return;
            }

            double row1Height = ((RowDefinition)grid.RowDefinitions[0]).ActualHeight;
            double row2Height = ((RowDefinition)grid.RowDefinitions[1]).ActualHeight;

            // Ограничение на минимальные размеры строк
            double minHeight = 137;

            if (row1Height < minHeight || row2Height < minHeight) {
                e.Handled = true;
            }
        }

        #endregion

        #region Column Visibility Management

        private void GroupTexturesCheckBox_Changed(object sender, RoutedEventArgs e) {
            if (GroupTexturesCheckBox.IsChecked == true) {
                ICollectionView view = CollectionViewSource.GetDefaultView(TexturesDataGrid.ItemsSource);
                if (view != null && view.CanGroup) {
                    view.GroupDescriptions.Clear();
                    view.GroupDescriptions.Add(new PropertyGroupDescription("GroupName"));
                }
            } else {
                ICollectionView view = CollectionViewSource.GetDefaultView(TexturesDataGrid.ItemsSource);
                if (view != null) {
                    view.GroupDescriptions.Clear();
                }
            }
        }

        private void TextureColumnVisibility_Click(object sender, RoutedEventArgs e) {
            if (sender is MenuItem menuItem && menuItem.Tag is string columnTag) {
                int columnIndex = columnTag switch {
                    "ID" => 1,
                    "TextureName" => 2,
                    "Extension" => 3,
                    "Size" => 4,
                    "Resolution" => 5,
                    "ResizeResolution" => 6,
                    "Status" => 7,
                    _ => -1
                };

                if (columnIndex >= 0 && columnIndex < TexturesDataGrid.Columns.Count) {
                    TexturesDataGrid.Columns[columnIndex].Visibility = menuItem.IsChecked ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        private void MaterialColumnVisibility_Click(object sender, RoutedEventArgs e) {
            if (sender is MenuItem menuItem && menuItem.Tag is string columnTag) {
                int columnIndex = columnTag switch {
                    "ID" => 1,
                    "Name" => 2,
                    "Status" => 3,
                    _ => -1
                };

                if (columnIndex >= 0 && columnIndex < MaterialsDataGrid.Columns.Count) {
                    MaterialsDataGrid.Columns[columnIndex].Visibility = menuItem.IsChecked ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        #endregion

        #region Materials

        private static async Task<MaterialResource> ParseMaterialJsonAsync(string filePath) {
            try {
                string jsonContent = await File.ReadAllTextAsync(filePath);
                JObject json = JObject.Parse(jsonContent);

                JToken? data = json["data"];
                if (data != null) {

                    return new MaterialResource {
                        ID = json["id"]?.ToObject<int>() ?? 0,
                        Name = json["name"]?.ToString() ?? string.Empty,
                        CreatedAt = json["createdAt"]?.ToString() ?? string.Empty,
                        Shader = data["shader"]?.ToString() ?? string.Empty,
                        BlendType = data["blendType"]?.ToString() ?? string.Empty,
                        Cull = data["cull"]?.ToString() ?? string.Empty,
                        UseLighting = data["useLighting"]?.ToObject<bool>() ?? false,
                        TwoSidedLighting = data["twoSidedLighting"]?.ToObject<bool>() ?? false,

                        DiffuseTint = data["diffuseTint"]?.ToObject<bool>() ?? false,
                        Diffuse = data["diffuse"]?.Select(d => d.ToObject<float>()).ToList(),

                        SpecularTint = data["specularTint"]?.ToObject<bool>() ?? false,
                        Specular = data["specular"]?.Select(d => d.ToObject<float>()).ToList(),

                        AOTint = data["aoTint"]?.ToObject<bool>() ?? false,
                        AOColor = data["ao"]?.Select(d => d.ToObject<float>()).ToList(),

                        UseMetalness = data["useMetalness"]?.ToObject<bool>() ?? false,
                        MetalnessMapId = ParseTextureAssetId(data["metalnessMap"], "metalnessMap"),
                        Metalness = data["metalness"]?.ToObject<float?>(),

                        GlossMapId = ParseTextureAssetId(data["glossMap"], "glossMap"),
                        Shininess = data["shininess"]?.ToObject<float?>(),

                        Opacity = data["opacity"]?.ToObject<float?>(),
                        AlphaTest = data["alphaTest"]?.ToObject<float?>(),
                        OpacityMapId = ParseTextureAssetId(data["opacityMap"], "opacityMap"),


                        NormalMapId = ParseTextureAssetId(data["normalMap"], "normalMap"),
                        BumpMapFactor = data["bumpMapFactor"]?.ToObject<float?>(),

                        Reflectivity = data["reflectivity"]?.ToObject<float?>(),
                        RefractionIndex = data["refractionIndex"]?.ToObject<float?>(),


                        DiffuseMapId = ParseTextureAssetId(data["diffuseMap"], "diffuseMap"),

                        SpecularMapId = ParseTextureAssetId(data["specularMap"], "specularMap"),
                        SpecularityFactor = data["specularityFactor"]?.ToObject<float?>(),

                        Emissive = data["emissive"]?.Select(d => d.ToObject<float>()).ToList(),
                        EmissiveIntensity = data["emissiveIntensity"]?.ToObject<float?>(),
                        EmissiveMapId = ParseTextureAssetId(data["emissiveMap"], "emissiveMap"),

                        AOMapId = ParseTextureAssetId(data["aoMap"], "aoMap"),

                        DiffuseColorChannel = ParseColorChannel(data["diffuseMapChannel"]?.ToString() ?? string.Empty),
                        SpecularColorChannel = ParseColorChannel(data["specularMapChannel"]?.ToString() ?? string.Empty),
                        MetalnessColorChannel = ParseColorChannel(data["metalnessMapChannel"]?.ToString() ?? string.Empty),
                        GlossinessColorChannel = ParseColorChannel(data["glossMapChannel"]?.ToString() ?? string.Empty),
                        AOChannel = ParseColorChannel(data["aoMapChannel"]?.ToString() ?? string.Empty)
                    };
                }
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"Error parsing material JSON: {ex.Message}");
            }
            return null;
        }

        private static ColorChannel ParseColorChannel(string channel) {
            return channel switch {
                "r" => ColorChannel.R,
                "g" => ColorChannel.G,
                "b" => ColorChannel.B,
                "a" => ColorChannel.A,
                "rgb" => ColorChannel.RGB,
                _ => ColorChannel.R // или выберите другой дефолтный канал
            };
        }

        private static int? ParseTextureAssetId(JToken? token, string propertyName) {
            if (token == null || token.Type == JTokenType.Null) {
                logger.Debug("Свойство {PropertyName} отсутствует или имеет значение null при чтении материала.", propertyName);
                return null;
            }

            static int? ExtractAssetId(JToken? candidate) {
                if (candidate == null || candidate.Type == JTokenType.Null) {
                    return null;
                }

                return candidate.Type switch {
                    JTokenType.Integer => candidate.ToObject<int?>(),
                    JTokenType.Float => candidate.ToObject<double?>() is double value ? (int?)Convert.ToInt32(Math.Round(value, MidpointRounding.AwayFromZero)) : null,
                    JTokenType.String => int.TryParse(candidate.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : null,
                    JTokenType.Object => ExtractAssetId(candidate["asset"] ?? candidate["id"] ?? candidate["value"] ?? candidate["data"] ?? candidate["guid"] ?? candidate.FirstOrDefault()),
                    _ => null,
                };
            }

            int? parsedId = ExtractAssetId(token);
            if (parsedId.HasValue) {
                logger.Debug("Из свойства {PropertyName} получен ID текстуры {TextureId}.", propertyName, parsedId.Value);
                return parsedId;
            }

            logger.Warn("Не удалось извлечь ID текстуры из свойства {PropertyName}. Тип токена: {TokenType}. Значение: {TokenValue}", propertyName, token.Type, token.Type == JTokenType.Object ? token.ToString(Formatting.None) : token.ToString());
            return null;
        }

        private void DisplayMaterialParameters(MaterialResource parameters) {
            Dispatcher.Invoke(() => {
                MaterialIDTextBlock.Text = $"ID: {parameters.ID}";
                MaterialNameTextBlock.Text = $"Name: {parameters.Name}";
                MaterialCreatedAtTextBlock.Text = $"Created At: {parameters.CreatedAt}";
                MaterialShaderTextBlock.Text = $"Shader: {parameters.Shader}";
                MaterialBlendTypeTextBlock.Text = $"Blend Type: {parameters.BlendType}";
                MaterialCullTextBlock.Text = $"Cull: {parameters.Cull}";
                MaterialUseLightingTextBlock.Text = $"Use Lighting: {parameters.UseLighting}";
                MaterialTwoSidedLightingTextBlock.Text = $"Two-Sided Lighting: {parameters.TwoSidedLighting}";
                MaterialReflectivityTextBlock.Text = $"Reflectivity: {parameters.Reflectivity}";
                MaterialAlphaTestTextBlock.Text = $"Alpha Test: {parameters.AlphaTest}";

                UpdateHyperlinkAndVisibility(MaterialAOMapHyperlink, AOExpander, parameters.AOMapId, "AO Map", parameters);
                UpdateHyperlinkAndVisibility(MaterialDiffuseMapHyperlink, DiffuseExpander, parameters.DiffuseMapId, "Diffuse Map", parameters);
                UpdateHyperlinkAndVisibility(MaterialNormalMapHyperlink, NormalExpander, parameters.NormalMapId, "Normal Map", parameters);
                UpdateHyperlinkAndVisibility(MaterialSpecularMapHyperlink, SpecularExpander, parameters.SpecularMapId, "Specular Map", parameters);
                UpdateHyperlinkAndVisibility(MaterialMetalnessMapHyperlink, SpecularExpander, parameters.MetalnessMapId, "Metalness Map", parameters);
                UpdateHyperlinkAndVisibility(MaterialGlossMapHyperlink, SpecularExpander, parameters.GlossMapId, "Gloss Map", parameters);

                SetTintColor(MaterialDiffuseTintCheckBox, MaterialTintColorRect, TintColorPicker, parameters.DiffuseTint, parameters.Diffuse);
                SetTintColor(MaterialSpecularTintCheckBox, MaterialSpecularTintColorRect, TintSpecularColorPicker, parameters.SpecularTint, parameters.Specular);
                SetTintColor(MaterialAOTintCheckBox, MaterialAOTintColorRect, AOTintColorPicker, parameters.AOTint, parameters.AOColor);

                SetTextureImage(TextureAOPreviewImage, parameters.AOMapId);
                SetTextureImage(TextureDiffusePreviewImage, parameters.DiffuseMapId);
                SetTextureImage(TextureNormalPreviewImage, parameters.NormalMapId);
                SetTextureImage(TextureSpecularPreviewImage, parameters.SpecularMapId);
                SetTextureImage(TextureMetalnessPreviewImage, parameters.MetalnessMapId);
                SetTextureImage(TextureGlossPreviewImage, parameters.GlossMapId);


                MaterialAOVertexColorCheckBox.IsChecked = parameters.AOVertexColor;
                MaterialAOTintCheckBox.IsChecked = parameters.AOTint;

                MaterialDiffuseVertexColorCheckBox.IsChecked = parameters.DiffuseVertexColor;
                MaterialDiffuseTintCheckBox.IsChecked = parameters.DiffuseTint;

                MaterialUseMetalnessCheckBox.IsChecked = parameters.UseMetalness;

                MaterialSpecularTintCheckBox.IsChecked = parameters.SpecularTint;
                MaterialSpecularVertexColorCheckBox.IsChecked = parameters.SpecularVertexColor;

                MaterialGlossinessTextBox.Text = parameters.Shininess?.ToString() ?? "0";
                MaterialGlossinessIntensitySlider.Value = parameters.Shininess ?? 0;

                MaterialMetalnessTextBox.Text = parameters.Metalness?.ToString() ?? "0";
                MaterialMetalnessIntensitySlider.Value = parameters.Metalness ?? 0;

                MaterialBumpinessTextBox.Text = parameters.BumpMapFactor?.ToString() ?? "0";
                MaterialBumpinessIntensitySlider.Value = parameters.BumpMapFactor ?? 0;



                // Установка выбранных элементов в ComboBox для Color Channel и UV Channel
                MaterialDiffuseColorChannelComboBox.SelectedItem = parameters.DiffuseColorChannel?.ToString();
                MaterialSpecularColorChannelComboBox.SelectedItem = parameters.SpecularColorChannel?.ToString();
                MaterialMetalnessColorChannelComboBox.SelectedItem = parameters.MetalnessColorChannel?.ToString();
                MaterialGlossinessColorChannelComboBox.SelectedItem = parameters.GlossinessColorChannel?.ToString();
                MaterialAOColorChannelComboBox.SelectedItem = parameters.AOChannel?.ToString();
            });
        }

        private static void SetTintColor(CheckBox checkBox, TextBox colorRect, ColorPicker colorPicker, bool isTint, List<float>? colorValues) {
            checkBox.IsChecked = isTint;
            if (isTint && colorValues != null && colorValues.Count >= 3) {
                System.Windows.Media.Color color = System.Windows.Media.Color.FromRgb(
                    (byte)(colorValues[0] * 255),
                    (byte)(colorValues[1] * 255),
                    (byte)(colorValues[2] * 255)
                );
                colorRect.Background = new SolidColorBrush(color);
                colorRect.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";

                // Установка выбранного цвета в ColorPicker
                colorPicker.SelectedColor = color;
            } else {
                colorRect.Background = new SolidColorBrush(Colors.Transparent);
                colorRect.Text = "No Tint";
                colorPicker.SelectedColor = null;
            }
        }

        private void UpdateHyperlinkAndVisibility(Hyperlink hyperlink, Expander expander, int? mapId, string mapName, MaterialResource material) {
            if (hyperlink != null && expander != null) {
                // Устанавливаем DataContext для hyperlink, чтобы он знал к какому материалу относится
                hyperlink.DataContext = material;

                if (mapId.HasValue) {
                    TextureResource? texture = Textures.FirstOrDefault(t => t.ID == mapId.Value);
                    if (texture != null && !string.IsNullOrEmpty(texture.Name)) {
                        // Сохраняем ID в NavigateUri с пользовательской схемой для последующего извлечения
                        hyperlink.NavigateUri = new Uri($"texture://{mapId.Value}");
                        hyperlink.Inlines.Clear();
                        hyperlink.Inlines.Add(texture.Name);
                    }
                    expander.Visibility = Visibility.Visible;
                } else {
                    hyperlink.NavigateUri = null;
                    hyperlink.Inlines.Clear();
                    hyperlink.Inlines.Add($"No {mapName}");
                    expander.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void NavigateToTextureFromHyperlink(object sender, string mapType, Func<MaterialResource, int?> mapIdSelector) {
            ArgumentNullException.ThrowIfNull(sender);

            MaterialResource? material = (sender as Hyperlink)?.DataContext as MaterialResource
                                          ?? MaterialsDataGrid.SelectedItem as MaterialResource;

            if (sender is Hyperlink hyperlink)
            {
                logger.Debug("Гиперссылка нажата. NavigateUri: {NavigateUri}; Текущий текст: {HyperlinkText}",
                             hyperlink.NavigateUri,
                             string.Concat(hyperlink.Inlines.OfType<Run>().Select(r => r.Text)));
            }
            else
            {
                logger.Warn("NavigateToTextureFromHyperlink вызван отправителем типа {SenderType}, ожидалась Hyperlink.", sender.GetType().FullName);
            }

            logger.Debug("Детали клика по гиперссылке. Тип отправителя: {SenderType}; Тип DataContext: {DataContextType}; Тип выделения в таблице: {SelectedType}",
                         sender.GetType().FullName,
                         (sender as FrameworkContentElement)?.DataContext?.GetType().FullName ?? "<null>",
                         MaterialsDataGrid.SelectedItem?.GetType().FullName ?? "<null>");

            // 1) Пытаемся взять ID текстуры из Hyperlink.NavigateUri (мы сохраняем его при отрисовке)
            int? mapId = null;
            if (sender is Hyperlink link && link.NavigateUri != null &&
                string.Equals(link.NavigateUri.Scheme, "texture", StringComparison.OrdinalIgnoreCase))
            {
                string idText = link.NavigateUri.AbsoluteUri.Replace("texture://", string.Empty);
                if (int.TryParse(idText, out int parsed))
                {
                    mapId = parsed;
                }
            }

            // 2) Если в NavigateUri нет значения, пробуем взять из материала
            if (!mapId.HasValue)
            {
                if (material == null) {
                    logger.Warn("Не удалось определить материал для гиперссылки {MapType}.", mapType);
                    return;
                }

                mapId = mapIdSelector(material);
            }
            material ??= new MaterialResource { Name = "<unknown>", ID = -1 };
            if (!mapId.HasValue) {
                logger.Info("Для материала {MaterialName} ({MaterialId}) отсутствует идентификатор текстуры {MapType}.", material.Name, material.ID, mapType);
                return;
            }

            logger.Info("Запрос на переход к текстуре {MapType} с ID {TextureId} из материала {MaterialName} ({MaterialId}).",
                        mapType,
                        mapId.Value,
                        material.Name,
                        material.ID);

            Dispatcher.BeginInvoke(new Action(() => {
                if (TexturesTabItem != null) {
                    tabControl.SelectedItem = TexturesTabItem;
                    logger.Debug("Вкладка текстур активирована через TabControl.");
                }

                TextureResource? texture = Textures.FirstOrDefault(t => t.ID == mapId.Value);
                if (texture != null) {
                    ICollectionView? view = CollectionViewSource.GetDefaultView(TexturesDataGrid.ItemsSource);
                    view?.MoveCurrentTo(texture);

                    TexturesDataGrid.SelectedItem = texture;
                    TexturesDataGrid.UpdateLayout();
                    TexturesDataGrid.ScrollIntoView(texture);
                    TexturesDataGrid.Focus();

                    logger.Info("Текстура {TextureName} (ID {TextureId}) выделена и прокручена в таблице текстур.", texture.Name, texture.ID);
                } else {
                    logger.Error("Текстура с ID {TextureId} не найдена в коллекции. Всего текстур: {TextureCount}.", mapId.Value, Textures.Count);
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void MaterialDiffuseMapHyperlink_Click(object sender, RoutedEventArgs e) {
            NavigateToTextureFromHyperlink(sender, "Diffuse Map", material => material.DiffuseMapId);
        }

        private void MaterialNormalMapHyperlink_Click(object sender, RoutedEventArgs e) {
            NavigateToTextureFromHyperlink(sender, "Normal Map", material => material.NormalMapId);
        }

        private void MaterialSpecularMapHyperlink_Click(object sender, RoutedEventArgs e) {
            NavigateToTextureFromHyperlink(sender, "Specular Map", material => material.SpecularMapId);
        }

        private void MaterialMetalnessMapHyperlink_Click(object sender, RoutedEventArgs e) {
            NavigateToTextureFromHyperlink(sender, "Metalness Map", material => material.MetalnessMapId);
        }

        private void MaterialGlossMapHyperlink_Click(object sender, RoutedEventArgs e) {
            NavigateToTextureFromHyperlink(sender, "Gloss Map", material => material.GlossMapId);
        }

        private void MaterialAOMapHyperlink_Click(object sender, RoutedEventArgs e) {
            NavigateToTextureFromHyperlink(sender, "AO Map", material => material.AOMapId);
        }

        private void TexturePreview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
            if (sender is not System.Windows.Controls.Image image) {
                logger.Warn("TexturePreview_MouseLeftButtonUp вызван отправителем типа {SenderType}, ожидался Image.", sender.GetType().FullName);
                return;
            }

            MaterialResource? material = MaterialsDataGrid.SelectedItem as MaterialResource;
            if (material == null) {
                logger.Warn("Не удалось определить материал для предпросмотра текстуры.");
                return;
            }

            string textureType = image.Tag as string ?? "";
            int? textureId = textureType switch {
                "AO" => material.AOMapId,
                "Diffuse" => material.DiffuseMapId,
                "Normal" => material.NormalMapId,
                "Specular" => material.SpecularMapId,
                "Metalness" => material.MetalnessMapId,
                "Gloss" => material.GlossMapId,
                _ => null
            };

            if (!textureId.HasValue) {
                logger.Info("Для материала {MaterialName} ({MaterialId}) отсутствует идентификатор текстуры типа {TextureType}.",
                    material.Name, material.ID, textureType);
                return;
            }

            logger.Info("Клик по превью текстуры {TextureType} с ID {TextureId} из материала {MaterialName} ({MaterialId}).",
                textureType, textureId.Value, material.Name, material.ID);

            Dispatcher.BeginInvoke(new Action(() => {
                if (TexturesTabItem != null) {
                    tabControl.SelectedItem = TexturesTabItem;
                    logger.Debug("Вкладка текстур активирована через TabControl.");
                }

                TextureResource? texture = Textures.FirstOrDefault(t => t.ID == textureId.Value);
                if (texture != null) {
                    ICollectionView? view = CollectionViewSource.GetDefaultView(TexturesDataGrid.ItemsSource);
                    view?.MoveCurrentTo(texture);

                    TexturesDataGrid.SelectedItem = texture;
                    TexturesDataGrid.UpdateLayout();
                    TexturesDataGrid.ScrollIntoView(texture);
                    TexturesDataGrid.Focus();

                    logger.Info("Текстура {TextureName} (ID {TextureId}) выделена и прокручена в таблице текстур.", texture.Name, texture.ID);
                } else {
                    logger.Error("Текстура с ID {TextureId} не найдена в коллекции. Всего текстур: {TextureCount}.", textureId.Value, Textures.Count);
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private async void MaterialsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (MaterialsDataGrid.SelectedItem is MaterialResource selectedMaterial) {
                // Обновляем выбранный материал в ViewModel для фильтрации текстур
                if (DataContext is MainViewModel viewModel) {
                    viewModel.SelectedMaterial = selectedMaterial;
                }

                if (!string.IsNullOrEmpty(selectedMaterial.Path) && File.Exists(selectedMaterial.Path)) {
                    MaterialResource materialParameters = await ParseMaterialJsonAsync(selectedMaterial.Path);
                    if (materialParameters != null) {
                        selectedMaterial = materialParameters;
                        DisplayMaterialParameters(selectedMaterial); // Передаем весь объект MaterialResource
                    }
                }

                // Автоматически переключаемся на вкладку текстур и выбираем связанную текстуру
                // SwitchToTexturesTabAndSelectTexture(selectedMaterial); // Отключено: не переключаться автоматически при выборе материала
            }
        }

        private void SwitchToTexturesTabAndSelectTexture(MaterialResource material) {
            if (material == null) return;

            // Переключаемся на вкладку текстур
            if (TexturesTabItem != null) {
                tabControl.SelectedItem = TexturesTabItem;
            }

            // Ищем первую доступную текстуру, связанную с материалом
            TextureResource? textureToSelect = null;

            // Проверяем различные типы текстур в порядке приоритета
            var textureIds = new int?[] {
                material.DiffuseMapId,
                material.NormalMapId,
                material.SpecularMapId,
                material.MetalnessMapId,
                material.GlossMapId,
                material.AOMapId
            };

            foreach (var textureId in textureIds) {
                if (textureId.HasValue) {
                    var texture = Textures.FirstOrDefault(t => t.ID == textureId.Value);
                    if (texture != null) {
                        textureToSelect = texture;
                        break;
                    }
                }
            }

            // Если найдена связанная текстура, выбираем её
            if (textureToSelect != null) {
                Dispatcher.BeginInvoke(new Action(() => {
                    ICollectionView? view = CollectionViewSource.GetDefaultView(TexturesDataGrid.ItemsSource);
                    view?.MoveCurrentTo(textureToSelect);

                    TexturesDataGrid.SelectedItem = textureToSelect;
                    TexturesDataGrid.UpdateLayout();
                    TexturesDataGrid.ScrollIntoView(textureToSelect);
                    TexturesDataGrid.Focus();

                    logger.Info($"Автоматически выбрана текстура {textureToSelect.Name} (ID {textureToSelect.ID}) для материала {material.Name} (ID {material.ID})");
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            } else {
                logger.Info($"Для материала {material.Name} (ID {material.ID}) не найдено связанных текстур");
            }
        }

        private void SetTextureImage(System.Windows.Controls.Image imageControl, int? textureId) {
            if (textureId.HasValue) {
                TextureResource? texture = Textures.FirstOrDefault(t => t.ID == textureId.Value);
                if (texture != null && File.Exists(texture.Path)) {
                    BitmapImage bitmapImage = new(new Uri(texture.Path));
                    imageControl.Source = bitmapImage;
                } else {
                    imageControl.Source = null;
                }
            } else {
                imageControl.Source = null;
            }
        }

        private void TintColorPicker_SelectedColorChanged(object sender, RoutedPropertyChangedEventArgs<System.Windows.Media.Color?> e) {
            if (e.NewValue.HasValue) {
                System.Windows.Media.Color color = e.NewValue.Value;
                System.Windows.Media.Color mediaColor = System.Windows.Media.Color.FromArgb(color.A, color.R, color.G, color.B);

                MaterialTintColorRect.Background = new SolidColorBrush(mediaColor);
                MaterialTintColorRect.Text = $"#{mediaColor.A:X2}{mediaColor.R:X2}{mediaColor.G:X2}{mediaColor.B:X2}";

                double brightness = (mediaColor.R * 0.299 + mediaColor.G * 0.587 + mediaColor.B * 0.114) / 255;
                MaterialTintColorRect.Foreground = new SolidColorBrush(brightness > 0.5 ? Colors.Black : Colors.White);

                if (MaterialsDataGrid.SelectedItem is MaterialResource selectedMaterial) {
                    selectedMaterial.DiffuseTint = true;
                    selectedMaterial.Diffuse = [mediaColor.R, mediaColor.G, mediaColor.B];
                }
            }
        }

        private void AOTintColorPicker_SelectedColorChanged(object sender, RoutedPropertyChangedEventArgs<System.Windows.Media.Color?> e) {
            if (e.NewValue.HasValue) {
                System.Windows.Media.Color newColor = e.NewValue.Value;
                MaterialAOTintColorRect.Background = new SolidColorBrush(newColor);
                MaterialAOTintColorRect.Text = $"#{newColor.R:X2}{newColor.G:X2}{newColor.B:X2}";

                if (MaterialsDataGrid.SelectedItem is MaterialResource selectedMaterial) {
                    selectedMaterial.AOTint = true;
                    selectedMaterial.AOColor = [newColor.R, newColor.G, newColor.B];
                }
            }
        }

        private void TintSpecularColorPicker_SelectedColorChanged(object sender, RoutedPropertyChangedEventArgs<System.Windows.Media.Color?> e) {
            if (e.NewValue.HasValue) {
                System.Windows.Media.Color newColor = e.NewValue.Value;
                MaterialSpecularTintColorRect.Background = new SolidColorBrush(newColor);
                MaterialSpecularTintColorRect.Text = $"#{newColor.R:X2}{newColor.G:X2}{newColor.B:X2}";

                // Обновление данных материала
                if (MaterialsDataGrid.SelectedItem is MaterialResource selectedMaterial) {
                    selectedMaterial.SpecularTint = true;
                    selectedMaterial.Specular = [newColor.R, newColor.G, newColor.B];
                }
            }
        }


        #endregion

        #region Download

        private async void Download(object? sender, RoutedEventArgs? e) {
            try {
                List<BaseResource> selectedResources = [.. textures.Where(t => t.Status == "On Server" ||
                                                            t.Status == "Size Mismatch" ||
                                                            t.Status == "Corrupted" ||
                                                            t.Status == "Empty File" ||
                                                            t.Status == "Hash ERROR" ||
                                                            t.Status == "Error")
                                                .Cast<BaseResource>()
                                                .Concat(models.Where(m => m.Status == "On Server" ||
                                                                          m.Status == "Size Mismatch" ||
                                                                          m.Status == "Corrupted" ||
                                                                          m.Status == "Empty File" ||
                                                                          m.Status == "Hash ERROR" ||
                                                                          m.Status == "Error").Cast<BaseResource>())
                                                .Concat(materials.Where(m => m.Status == "On Server" ||
                                                                             m.Status == "Size Mismatch" ||
                                                                             m.Status == "Corrupted" ||
                                                                             m.Status == "Empty File" ||
                                                                             m.Status == "Hash ERROR" ||
                                                                             m.Status == "Error").Cast<BaseResource>())
                                                .OrderBy(r => r.Name)];

                IEnumerable<Task> downloadTasks = selectedResources.Select(resource => DownloadResourceAsync(resource));
                await Task.WhenAll(downloadTasks);

                // Пересчитываем индексы после завершения загрузки
                RecalculateIndices();
            } catch (Exception ex) {
                MessageBox.Show($"Error: {ex.Message}");
                MainWindowHelpers.LogError($"Error: {ex}");
            }
        }

        private async Task DownloadResourceAsync(BaseResource resource) {
            const int maxRetries = 5;

            await downloadSemaphore.WaitAsync(); // Ожидаем освобождения слота в семафоре
            try {
                for (int attempt = 1; attempt <= maxRetries; attempt++) {
                    try {
                        resource.Status = "Downloading";
                        resource.DownloadProgress = 0;

                        if (resource is MaterialResource materialResource) {
                            // Обработка загрузки материалов по ID
                            await DownloadMaterialByIdAsync(materialResource);
                        } else {
                            // Обработка загрузки файлов (текстур и моделей)
                            await DownloadFileAsync(resource);
                        }
                        break;
                    } catch (Exception ex) {
                        MainWindowHelpers.LogError($"Error downloading resource: {ex.Message}");
                        resource.Status = "Error";
                        if (attempt == maxRetries) {
                            break;
                        }
                    }
                }
            } finally {
                downloadSemaphore.Release();
            }

        }

        private static async Task DownloadMaterialByIdAsync(MaterialResource materialResource) {
            const int maxRetries = 5;
            const int delayMilliseconds = 2000;

            for (int attempt = 1; attempt <= maxRetries; attempt++) {
                try {
                    PlayCanvasService playCanvasService = new();
                    string apiKey = AppSettings.Default.PlaycanvasApiKey;
                    JObject materialJson = await playCanvasService.GetAssetByIdAsync(materialResource.ID.ToString(), apiKey, default)
                        ?? throw new Exception($"Failed to get material JSON for ID: {materialResource.ID}");

                    // Изменение: заменяем последнюю папку на файл с расширением .json
                    string directoryPath = Path.GetDirectoryName(materialResource.Path) ?? throw new InvalidOperationException();
                    string materialPath = Path.Combine(directoryPath, $"{materialResource.Name}.json");

                    Directory.CreateDirectory(directoryPath);

                    await File.WriteAllTextAsync(materialPath, materialJson.ToString(), default);
                    materialResource.Status = "Downloaded";
                    break;
                } catch (IOException ex) {
                    if (attempt == maxRetries) {
                        materialResource.Status = "Error";
                        MainWindowHelpers.LogError($"Error downloading material after {maxRetries} attempts: {ex.Message}");
                    } else {
                        MainWindowHelpers.LogError($"Attempt {attempt} failed with IOException: {ex.Message}. Retrying in {delayMilliseconds}ms...");
                        await Task.Delay(delayMilliseconds);
                    }
                } catch (Exception ex) {
                    materialResource.Status = "Error";
                    MainWindowHelpers.LogError($"Error downloading material: {ex.Message}");
                    break;
                }
            }
        }

        private static async Task DownloadFileAsync(BaseResource resource) {
            if (resource == null || string.IsNullOrEmpty(resource.Path)) { // Если путь к файлу не указан, создаем его в папке проекта
                return;
            }

            const int maxRetries = 5;
            const int delayMilliseconds = 2000;

            for (int attempt = 1; attempt <= maxRetries; attempt++) {
                try {
                    using HttpClient client = new();
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AppSettings.Default.PlaycanvasApiKey);

                    HttpResponseMessage response = await client.GetAsync(resource.Url, HttpCompletionOption.ResponseHeadersRead);

                    if (!response.IsSuccessStatusCode) {
                        throw new Exception($"Failed to download resource: {response.StatusCode}");
                    }

                    long totalBytes = response.Content.Headers.ContentLength ?? 0L;
                    byte[] buffer = new byte[8192];
                    int bytesRead = 0;

                    using (Stream stream = await response.Content.ReadAsStreamAsync())
                    using (FileStream fileStream = await FileHelper.OpenFileStreamWithRetryAsync(resource.Path, FileMode.Create, FileAccess.Write, FileShare.None)) {
                        while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0) {
                            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                            resource.DownloadProgress = (double)fileStream.Length / totalBytes * 100;
                        }
                    }

                    MainWindowHelpers.LogInfo($"File downloaded successfully: {resource.Path}");
                    if (!File.Exists(resource.Path)) {
                        MainWindowHelpers.LogError($"File was expected but not found: {resource.Path}");
                        resource.Status = "Error";
                        return;
                    }

                    // Дополнительное логирование размера файла
                    FileInfo fileInfo = new(resource.Path);
                    long fileSizeInBytes = fileInfo.Length;
                    long resourceSizeInBytes = resource.Size;
                    MainWindowHelpers.LogInfo($"File size after download: {fileSizeInBytes}");

                    double tolerance = 0.05;
                    double lowerBound = resourceSizeInBytes * (1 - tolerance);
                    double upperBound = resourceSizeInBytes * (1 + tolerance);

                    if (fileInfo.Length == 0) {
                        resource.Status = "Empty File";
                    } else if (!string.IsNullOrEmpty(resource.Hash) && FileHelper.VerifyFileHash(resource.Path, resource.Hash)) {
                        resource.Status = "Downloaded";
                    } else if (fileSizeInBytes >= lowerBound && fileSizeInBytes <= upperBound) {
                        resource.Status = "Size Mismatch";
                    } else {
                        resource.Status = "Corrupted";
                    }
                    break;
                } catch (IOException ex) {
                    if (attempt == maxRetries) {
                        resource.Status = "Error";
                        MainWindowHelpers.LogError($"Error downloading resource after {maxRetries} attempts: {ex.Message}");
                    } else {
                        MainWindowHelpers.LogError($"Attempt {attempt} failed with IOException: {ex.Message}. Retrying in {delayMilliseconds}ms...");
                        await Task.Delay(delayMilliseconds);
                    }
                } catch (Exception ex) {
                    resource.Status = "Error";
                    MainWindowHelpers.LogError($"Error downloading resource: {ex.Message}");
                    break;
                }
            }
        }

        #endregion

        #region API Methods

        private async Task TryConnect(CancellationToken cancellationToken) {
            try {
                if (ProjectsComboBox.SelectedItem == null || BranchesComboBox.SelectedItem == null) {
                    MessageBox.Show("Please select a project and a branch");
                    return;
                }

                string selectedProjectId = ((KeyValuePair<string, string>)ProjectsComboBox.SelectedItem).Key;
                string selectedBranchId = ((Branch)BranchesComboBox.SelectedItem).Id;

                JArray assetsResponse = await playCanvasService.GetAssetsAsync(selectedProjectId, selectedBranchId, AppSettings.Default.PlaycanvasApiKey, cancellationToken);
                if (assetsResponse != null) {
                    // Строим иерархию папок из списка ассетов
                    BuildFolderHierarchyFromAssets(assetsResponse);
                    // Сохраняем JSON-ответ в файл

                    if (!string.IsNullOrEmpty(projectFolderPath) && !string.IsNullOrEmpty(projectName)) {
                        string jsonFilePath = Path.Combine(Path.Combine(projectFolderPath, projectName), "assets_list.json");
                        await SaveJsonResponseToFile(assetsResponse, projectFolderPath, projectName);
                        if (!File.Exists(jsonFilePath)) {
                            MessageBox.Show($"Failed to save the JSON file to {jsonFilePath}. Please check your permissions.");
                        }
                    }


                    UpdateConnectionStatus(true);

                    textures.Clear(); // Очищаем текущий список текстур
                    models.Clear(); // Очищаем текущий список моделей
                    materials.Clear(); // Очищаем текущий список материалов

                    List<JToken> supportedAssets = [.. assetsResponse.Where(asset => asset["file"] != null)];
                    int assetCount = supportedAssets.Count;

                    await Dispatcher.InvokeAsync(() =>
                    {
                        ProgressBar.Value = 0;
                        ProgressBar.Maximum = assetCount;
                        ProgressTextBlock.Text = $"0/{assetCount}";
                    });

                    IEnumerable<Task> tasks = supportedAssets.Select(asset => Task.Run(async () =>
                    {
                        await ProcessAsset(asset, 0, cancellationToken);  // Передаем аргумент cancellationToken
                        await Dispatcher.InvokeAsync(() =>
                        {
                            ProgressBar.Value++;
                            ProgressTextBlock.Text = $"{ProgressBar.Value}/{assetCount}";
                        });
                    }));

                    await Task.WhenAll(tasks);

                    RecalculateIndices(); // Пересчитываем индексы после обработки всех ассетов
                } else {
                    UpdateConnectionStatus(false, "Failed to connect");
                }
            } catch (Exception ex) {
                MessageBox.Show($"Error in TryConnect: {ex.Message}");
                MainWindowHelpers.LogError($"Error in TryConnect: {ex}");
            }
        }

        private void BuildFolderHierarchyFromAssets(JArray assetsResponse) {
            try {
                folderPaths.Clear();

                // Извлекаем только папки из списка ассетов
                var folders = assetsResponse.Where(asset => asset["type"]?.ToString() == "folder").ToList();

                // Создаем словарь для быстрого доступа к папкам по ID
                Dictionary<int, JToken> foldersById = new();
                foreach (JToken folder in folders) {
                    int? folderId = folder["id"]?.Type == JTokenType.Integer ? (int?)folder["id"] : null;
                    if (folderId.HasValue) {
                        foldersById[folderId.Value] = folder;
                    }
                }

                // Рекурсивная функция для построения полного пути папки
                string BuildFolderPath(int folderId) {
                    if (folderPaths.ContainsKey(folderId)) {
                        return folderPaths[folderId];
                    }

                    if (!foldersById.ContainsKey(folderId)) {
                        return string.Empty;
                    }

                    JToken folder = foldersById[folderId];
                    string folderName = folder["name"]?.ToString() ?? string.Empty;
                    int? parentId = folder["parent"]?.Type == JTokenType.Integer ? (int?)folder["parent"] : null;

                    string fullPath;
                    if (parentId.HasValue && parentId.Value != 0) {
                        // Есть родительская папка - рекурсивно строим путь
                        string parentPath = BuildFolderPath(parentId.Value);
                        fullPath = string.IsNullOrEmpty(parentPath) ? folderName : Path.Combine(parentPath, folderName);
                    } else {
                        // Папка верхнего уровня (parent == 0 или null)
                        fullPath = folderName;
                    }

                    folderPaths[folderId] = fullPath;
                    return fullPath;
                }

                // Строим пути для всех папок
                foreach (var folderId in foldersById.Keys) {
                    BuildFolderPath(folderId);
                }

                MainWindowHelpers.LogInfo($"Built folder hierarchy with {folderPaths.Count} folders from assets list");
            } catch (Exception ex) {
                MainWindowHelpers.LogError($"Error building folder hierarchy from assets: {ex.Message}");
                // Продолжаем работу даже если не удалось загрузить папки
            }
        }

        private static async Task SaveJsonResponseToFile(JToken jsonResponse, string projectFolderPath, string projectName) {
            try {
                string jsonFilePath = Path.Combine(Path.Combine(projectFolderPath, projectName), "assets_list.json");

                if (!Directory.Exists(Path.Combine(projectFolderPath, projectName))) {
                    Directory.CreateDirectory(Path.Combine(projectFolderPath, projectName));
                }

                string jsonString = jsonResponse.ToString(Formatting.Indented);
                await File.WriteAllTextAsync(jsonFilePath, jsonString);

                MainWindowHelpers.LogInfo($"Assets list saved to {jsonFilePath}");
            } catch (ArgumentNullException ex) {
                MainWindowHelpers.LogError($"Argument error: {ex.Message}");
            } catch (ArgumentException ex) {
                MainWindowHelpers.LogError($"Argument error: {ex.Message}");
            } catch (Exception ex) {
                MainWindowHelpers.LogError($"Error saving assets list to JSON: {ex.Message}");
            }
        }

        private async Task ProcessAsset(JToken asset, int index, CancellationToken cancellationToken) {
            try {
                await getAssetsSemaphore.WaitAsync(cancellationToken);

                string? type = asset["type"]?.ToString() ?? string.Empty;
                string? assetPath = asset["path"]?.ToString() ?? string.Empty;
                MainWindowHelpers.LogInfo($"Processing {type}, API path: {assetPath}");

                if (type == "script" || type == "wasm" || type == "cubemap") {
                    MainWindowHelpers.LogInfo($"Unsupported asset type: {type}");
                    return;
                }

                // Обработка материала без параметра file
                if (type == "material") {
                    await ProcessMaterialAsset(asset, index, cancellationToken);
                    return;
                }

                JToken? file = asset["file"];
                if (file == null || file.Type != JTokenType.Object) {
                    MainWindowHelpers.LogError("Invalid asset file format");
                    return;
                }

                string? fileUrl = MainWindowHelpers.GetFileUrl(file);
                if (string.IsNullOrEmpty(fileUrl)) {
                    throw new Exception("File URL is null or empty");
                }

                string? extension = MainWindowHelpers.GetFileExtension(fileUrl);
                if (string.IsNullOrEmpty(extension)) {
                    throw new Exception("Unable to determine file extension");
                }

                switch (type) {
                    case "texture" when IsSupportedTextureFormat(extension):
                        await ProcessTextureAsset(asset, index, fileUrl, extension, cancellationToken);
                        break;
                    case "scene" when IsSupportedModelFormat(extension):
                        await ProcessModelAsset(asset, index, fileUrl, extension, cancellationToken);
                        break;
                    default:
                        MainWindowHelpers.LogError($"Unsupported asset type or format: {type} - {extension}");
                        break;
                }
            } catch (Exception ex) {
                MainWindowHelpers.LogError($"Error in ProcessAsset: {ex}");
            } finally {
                getAssetsSemaphore.Release();
            }
        }

        private async Task ProcessModelAsset(JToken asset, int index, string fileUrl, string extension, CancellationToken _) {
            ArgumentNullException.ThrowIfNull(asset);

            if (!string.IsNullOrEmpty(fileUrl)) {
                if (string.IsNullOrEmpty(extension)) {
                    throw new ArgumentException($"'{nameof(extension)}' cannot be null or empty.", nameof(extension));
                }

                try {
                    string? assetPath = asset["path"]?.ToString();
                    int? parentId = asset["parent"]?.Type == JTokenType.Integer ? (int?)asset["parent"] : null;
                    ModelResource model = new() {
                        ID = asset["id"]?.Type == JTokenType.Integer ? (int)(asset["id"] ?? 0) : 0,
                        Index = index,
                        Name = asset["name"]?.ToString().Split('.')[0] ?? "Unknown",
                        Size = int.TryParse(asset["file"]?["size"]?.ToString(), out int size) ? size : 0,
                        Url = fileUrl.Split('?')[0],  // Удаляем параметры запроса
                        Path = GetResourcePath(asset["name"]?.ToString(), parentId),
                        Extension = extension,
                        Status = "On Server",
                        Hash = asset["file"]?["hash"]?.ToString() ?? string.Empty,
                        Parent = parentId,
                        UVChannels = 0 // Инициализация значения UV каналов
                    };

                    await MainWindowHelpers.VerifyAndProcessResourceAsync(model, async () => {
                        MainWindowHelpers.LogInfo($"Adding model to list: {model.Name}");

                        switch (model.Status) {
                            case "Downloaded":
                                if (File.Exists(model.Path)) {
                                    AssimpContext context = new();
                                    MainWindowHelpers.LogInfo($"Attempting to import file: {model.Path}");
                                    Scene scene = context.ImportFile(model.Path, PostProcessSteps.Triangulate | PostProcessSteps.FlipUVs | PostProcessSteps.GenerateSmoothNormals);
                                    MainWindowHelpers.LogInfo($"Import result: {scene != null}");

                                    if (scene == null || scene.Meshes == null || scene.MeshCount <= 0) {
                                        MainWindowHelpers.LogError("Scene is null or has no meshes.");
                                        return;
                                    }

                                    Mesh? mesh = scene.Meshes.FirstOrDefault();
                                    if (mesh != null) {
                                        model.UVChannels = mesh.TextureCoordinateChannelCount;
                                    }
                                }
                                break;
                            case "On Server":
                                break;
                            case "Size Mismatch":
                                break;
                            case "Corrupted":
                                break;
                            case "Empty File":
                                break;
                            case "Hash ERROR":
                                break;
                            case "Error":
                                break;
                        }


                        await Dispatcher.InvokeAsync(() => models.Add(model));
                    });
                } catch (FileNotFoundException ex) {
                    MainWindowHelpers.LogError($"File not found: {ex.FileName}");
                } catch (Exception ex) {
                    MainWindowHelpers.LogError($"Error processing model: {ex.Message}");
                }
            } else {
                throw new ArgumentException($"'{nameof(fileUrl)}' cannot be null or empty.", nameof(fileUrl));
            }
        }

        private async Task ProcessTextureAsset(JToken asset, int index, string fileUrl, string extension, CancellationToken cancellationToken) {
            try {
                string textureName = asset["name"]?.ToString().Split('.')[0] ?? "Unknown";
                int? parentId = asset["parent"]?.Type == JTokenType.Integer ? (int?)asset["parent"] : null;
                TextureResource texture = new() {
                    ID = asset["id"]?.Type == JTokenType.Integer ? (int)(asset["id"] ?? 0) : 0,
                    Index = index,
                    Name = textureName,
                    Size = int.TryParse(asset["file"]?["size"]?.ToString(), out int size) ? size : 0,
                    Url = fileUrl.Split('?')[0],  // Удаляем параметры запроса
                    Path = GetResourcePath(asset["name"]?.ToString(), parentId),
                    Extension = extension,
                    Resolution = new int[2],
                    ResizeResolution = new int[2],
                    Status = "On Server",
                    Hash = asset["file"]?["hash"]?.ToString() ?? string.Empty,
                    Parent = parentId,
                    Type = asset["type"]?.ToString(), // Устанавливаем свойство Type
                    GroupName = TextureResource.ExtractBaseTextureName(textureName),
                    TextureType = TextureResource.DetermineTextureType(textureName)
                };

                await MainWindowHelpers.VerifyAndProcessResourceAsync(texture, async () => {
                    MainWindowHelpers.LogInfo($"Adding texture to list: {texture.Name}");

                    switch (texture.Status) {
                        case "Downloaded":
                            (int width, int height)? resolution = MainWindowHelpers.GetLocalImageResolution(texture.Path);
                            if (resolution.HasValue) {
                                texture.Resolution[0] = resolution.Value.width;
                                texture.Resolution[1] = resolution.Value.height;
                            }
                            break;
                        case "On Server":
                            await MainWindowHelpers.UpdateTextureResolutionAsync(texture, cancellationToken);
                            break;
                        case "Size Mismatch":
                            break;
                        case "Corrupted":
                            break;
                        case "Empty File":
                            break;
                        case "Hash ERROR":
                            break;
                        case "Error":
                            break;
                    }

                    await Dispatcher.InvokeAsync(() => textures.Add(texture));
                    Dispatcher.Invoke(() => {
                        ProgressBar.Value++;
                        ProgressTextBlock.Text = $"{ProgressBar.Value}/{textures.Count}";
                    });
                });
            } catch (Exception ex) {
                MainWindowHelpers.LogError($"Error processing texture: {ex.Message}");
            }
        }

        private async Task ProcessMaterialAsset(JToken asset, int index, CancellationToken cancellationToken) {
            try {
                string name = asset["name"]?.ToString() ?? "Unknown";
                string? assetPath = asset["path"]?.ToString();
                int? parentId = asset["parent"]?.Type == JTokenType.Integer ? (int?)asset["parent"] : null;

                MaterialResource material = new() {
                    ID = asset["id"]?.Type == JTokenType.Integer ? (int)(asset["id"] ?? 0) : 0,
                    Index = index,
                    Name = name,
                    Size = 0, // У материалов нет файла, поэтому размер 0
                    Path = GetResourcePath($"{name}.json", parentId),
                    Status = "On Server",
                    Hash = string.Empty, // У материалов нет хеша
                    Parent = parentId
                                         //TextureIds = []
                };

                await MainWindowHelpers.VerifyAndProcessResourceAsync(material, async () => {
                    MainWindowHelpers.LogInfo($"Adding material to list: {material.Name}");

                    switch (material.Status) {
                        case "Downloaded":
                            break;
                        case "On Server":
                            break;
                        case "Size Mismatch":
                            break;
                        case "Corrupted":
                            break;
                        case "Empty File":
                            break;
                        case "Hash ERROR":
                            break;
                        case "Error":
                            break;
                    }

                    PlayCanvasService playCanvasService = new();
                    string apiKey = AppSettings.Default.PlaycanvasApiKey;
                    JObject materialJson = await playCanvasService.GetAssetByIdAsync(material.ID.ToString(), apiKey, cancellationToken);

                    //if (materialJson != null && materialJson["textures"] != null && materialJson["textures"]?.Type == JTokenType.Array) {
                    //    material.TextureIds.AddRange(from textureId in materialJson["textures"]!
                    //                                 select (int)textureId);
                    //}

                    MainWindowHelpers.LogInfo($"Adding material to list: {material.Name}");

                    await Dispatcher.InvokeAsync(() => materials.Add(material));
                });
            } catch (Exception ex) {
                MainWindowHelpers.LogError($"Error processing material: {ex.Message}");
            }
        }

        private async Task InitializeAsync() {
            // Попробуйте загрузить данные из сохраненного JSON
            bool jsonLoaded = await LoadAssetsFromJsonFileAsync();
            if (!jsonLoaded) {
                MessageBox.Show("No saved data found. Please ensure the JSON file is available.");
            }
        }

        private async Task<bool> LoadAssetsFromJsonFileAsync() {
            try {
                if (String.IsNullOrEmpty(projectFolderPath) || String.IsNullOrEmpty(projectName)) {
                    throw new Exception("Project folder path or name is null or empty");
                }

                string jsonFilePath = Path.Combine(projectFolderPath, projectName, "assets_list.json");
                if (File.Exists(jsonFilePath)) {
                    string jsonContent = await File.ReadAllTextAsync(jsonFilePath);
                    JArray assetsResponse = JArray.Parse(jsonContent);

                    // Строим иерархию папок из списка ассетов
                    BuildFolderHierarchyFromAssets(assetsResponse);

                    await ProcessAssetsFromJson(assetsResponse);
                    return true;
                }
            } catch (JsonReaderException ex) {
                MessageBox.Show($"Invalid JSON format: {ex.Message}");
            } catch (Exception ex) {
                MessageBox.Show($"Error loading JSON file: {ex.Message}");
            }
            return false;
        }

        private async Task ProcessAssetsFromJson(JToken assetsResponse) {
            textures.Clear();
            models.Clear();
            materials.Clear();

            List<JToken> supportedAssets = [.. assetsResponse.Where(asset => asset["file"] != null)];
            int assetCount = supportedAssets.Count;

            await Dispatcher.InvokeAsync(() =>
            {
                ProgressBar.Value = 0;
                ProgressBar.Maximum = assetCount;
                ProgressTextBlock.Text = $"0/{assetCount}";
            });

            IEnumerable<Task> tasks = supportedAssets.Select(asset => Task.Run(async () =>
            {
                await ProcessAsset(asset, 0, CancellationToken.None); // Используем токен отмены по умолчанию
                await Dispatcher.InvokeAsync(() =>
                {
                    ProgressBar.Value++;
                    ProgressTextBlock.Text = $"{ProgressBar.Value}/{assetCount}";
                });
            }));

            await Task.WhenAll(tasks);
            RecalculateIndices(); // Пересчитываем индексы после обработки всех ассетов
        }

        #endregion

        #region Helper Methods

        private string GetResourcePath(string? fileName, int? parentId = null) {
            if (string.IsNullOrEmpty(projectFolderPath)) {
                throw new Exception("Project folder path is null or empty");
            }

            if (string.IsNullOrEmpty(projectName)) {
                throw new Exception("Project name is null or empty");
            }

            string pathProjectFolder = Path.Combine(AppSettings.Default.ProjectsFolderPath, projectName);
            string pathSourceFolder = pathProjectFolder;

            // Если указан parent ID (ID папки), используем построенную иерархию
            if (parentId.HasValue && folderPaths.ContainsKey(parentId.Value)) {
                string folderPath = folderPaths[parentId.Value];
                if (!string.IsNullOrEmpty(folderPath)) {
                    // Создаем полный путь с иерархией папок из PlayCanvas
                    pathSourceFolder = Path.Combine(pathSourceFolder, folderPath);
                    MainWindowHelpers.LogInfo($"Using folder hierarchy: {folderPath}");
                }
            }

            if (!Directory.Exists(pathSourceFolder)) {
                Directory.CreateDirectory(pathSourceFolder);
            }

            string fullPath = Path.Combine(pathSourceFolder, fileName ?? "Unknown");
            MainWindowHelpers.LogInfo($"Generated resource path: {fullPath}");
            return fullPath;
        }

        private void RecalculateIndices() {
            Dispatcher.Invoke(() => {
                int index = 1;
                foreach (TextureResource texture in textures) {
                    texture.Index = index++;
                }
                TexturesDataGrid.Items.Refresh(); // Обновляем DataGrid, чтобы отразить изменения индексов

                index = 1;
                foreach (ModelResource model in models) {
                    model.Index = index++;
                }
                ModelsDataGrid.Items.Refresh(); // Обновляем DataGrid, чтобы отразить изменения индексов

                index = 1;

                foreach (MaterialResource material in materials) {
                    material.Index = index++;
                }
                MaterialsDataGrid.Items.Refresh(); // Обновляем DataGrid, чтобы отразить изменения индексов
            });
        }

        private bool IsSupportedTextureFormat(string extension) {
            return supportedFormats.Contains(extension) && !excludedFormats.Contains(extension);
        }

        private bool IsSupportedModelFormat(string extension) {
            return supportedModelFormats.Contains(extension) && !excludedFormats.Contains(extension); // исправлено
        }

        private void UpdateConnectionStatus(bool isConnected, string message = "") {
            Dispatcher.Invoke(() => {
                if (isConnected) {
                    ConnectionStatusTextBlock.Text = string.IsNullOrEmpty(message) ? "Connected" : $"Connected: {message}";
                    ConnectionStatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
                } else {
                    ConnectionStatusTextBlock.Text = string.IsNullOrEmpty(message) ? "Disconnected" : $"Error: {message}";
                    ConnectionStatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                }
            });
        }

        private void SaveCurrentSettings() {
            if (ProjectsComboBox.SelectedItem != null) {
                AppSettings.Default.LastSelectedProjectId = ((KeyValuePair<string, string>)ProjectsComboBox.SelectedItem).Key;
            }
            if (BranchesComboBox.SelectedItem != null) {
                AppSettings.Default.LastSelectedBranchName = ((Branch)BranchesComboBox.SelectedItem).Name;
            }
            AppSettings.Default.Save();
        }

        private async Task LoadLastSettings() {
            try {
                userName = AppSettings.Default.UserName.ToLower();
                if (string.IsNullOrEmpty(userName)) {
                    throw new Exception("Username is null or empty");
                }

                CancellationToken cancellationToken = new();

                userID = await playCanvasService.GetUserIdAsync(userName, AppSettings.Default.PlaycanvasApiKey, cancellationToken);
                if (string.IsNullOrEmpty(userID)) {
                    throw new Exception("User ID is null or empty");
                } else {
                    UpdateConnectionStatus(true, $"by userID: {userID}");
                }
                Dictionary<string, string> projectsDict = await playCanvasService.GetProjectsAsync(userID, AppSettings.Default.PlaycanvasApiKey, [], cancellationToken);

                if (projectsDict != null && projectsDict.Count > 0) {
                    Projects.Clear();
                    foreach (KeyValuePair<string, string> project in projectsDict) {
                        Projects.Add(project);
                    }

                    if (!string.IsNullOrEmpty(AppSettings.Default.LastSelectedProjectId) && projectsDict.ContainsKey(AppSettings.Default.LastSelectedProjectId)) {
                        ProjectsComboBox.SelectedValue = AppSettings.Default.LastSelectedProjectId;
                    } else {
                        ProjectsComboBox.SelectedIndex = 0;
                    }

                    if (ProjectsComboBox.SelectedItem != null) {
                        string projectId = ((KeyValuePair<string, string>)ProjectsComboBox.SelectedItem).Key;
                        List<Branch> branchesList = await playCanvasService.GetBranchesAsync(projectId, AppSettings.Default.PlaycanvasApiKey, [], cancellationToken);

                        if (branchesList != null && branchesList.Count > 0) {
                            Branches.Clear();
                            foreach (Branch branch in branchesList) {
                                Branches.Add(branch);
                            }

                            if (!string.IsNullOrEmpty(AppSettings.Default.LastSelectedBranchName)) {
                                Branch? selectedBranch = branchesList.FirstOrDefault(b => b.Name == AppSettings.Default.LastSelectedBranchName);
                                if (selectedBranch != null) {
                                    BranchesComboBox.SelectedValue = selectedBranch.Id;
                                } else {
                                    BranchesComboBox.SelectedIndex = 0;
                                }
                            } else {
                                BranchesComboBox.SelectedIndex = 0;
                            }
                        }
                    }
                }
                projectFolderPath = AppSettings.Default.ProjectsFolderPath;
            } catch (Exception ex) {
                MessageBox.Show($"Error loading last settings: {ex.Message}");
            }
        }
        #endregion

        #region Texture Conversion Settings Handlers

        private bool isConversionSettingsExpanded = true;

        private void ConversionSettingsExpander_Expanded(object sender, RoutedEventArgs e) {
            isConversionSettingsExpanded = true;
        }

        private void ConversionSettingsExpander_Collapsed(object sender, RoutedEventArgs e) {
            isConversionSettingsExpanded = false;
        }

        private void ConversionSettingsPanel_SettingsChanged(object? sender, EventArgs e) {
            if (TexturesDataGrid.SelectedItem is TextureResource selectedTexture) {
                UpdateTextureConversionSettings(selectedTexture);
            }
        }

        private void UpdateTextureConversionSettings(TextureResource texture) {
            try {
                var compression = ConversionSettingsPanel.GetCompressionSettings();
                var mipProfile = ConversionSettingsPanel.GetMipProfileSettings();

                texture.CompressionFormat = compression.CompressionFormat.ToString();
                texture.PresetName = ConversionSettingsPanel.PresetName ?? "(Custom)";

                MainWindowHelpers.LogInfo($"Updated conversion settings for {texture.Name}");
            } catch (Exception ex) {
                MainWindowHelpers.LogError($"Error updating conversion settings: {ex.Message}");
            }
        }

        private void LoadTextureConversionSettings(TextureResource texture) {
            var textureType = TextureResource.DetermineTextureType(texture.Name);
            var profile = TextureConversion.Core.MipGenerationProfile.CreateDefault(
                MapTextureTypeToCore(textureType));

            var compression = TextureConversion.Core.CompressionSettings.CreateETC1SDefault();
            var compressionData = TextureConversion.Settings.CompressionSettingsData.FromCompressionSettings(compression);
            var mipProfileData = TextureConversion.Settings.MipProfileSettings.FromMipGenerationProfile(profile);

            ConversionSettingsPanel.LoadSettings(compressionData, mipProfileData, true, false);
            ConversionSettingsPanel.LoadPresets(new[] { "Albedo", "Normal", "Roughness", "Metallic" }, null);

            texture.CompressionFormat = compression.CompressionFormat.ToString();
            texture.PresetName = "(Auto)";
        }

        private TextureConversion.Core.TextureType MapTextureTypeToCore(string textureType) {
            return textureType.ToLower() switch {
                "albedo" => TextureConversion.Core.TextureType.Albedo,
                "normal" => TextureConversion.Core.TextureType.Normal,
                "roughness" => TextureConversion.Core.TextureType.Roughness,
                "metallic" => TextureConversion.Core.TextureType.Metallic,
                "ao" => TextureConversion.Core.TextureType.AmbientOcclusion,
                "emissive" => TextureConversion.Core.TextureType.Emissive,
                "gloss" => TextureConversion.Core.TextureType.Gloss,
                "height" => TextureConversion.Core.TextureType.Height,
                _ => TextureConversion.Core.TextureType.Generic
            };
        }

        #endregion

        #region Central Control Box Handlers

        private async void ProcessTexturesButton_Click(object sender, RoutedEventArgs e) {
            try {
                // Get textures to process
                var texturesToProcess = new List<TextureResource>();

                if (ProcessAllCheckBox.IsChecked == true) {
                    // Process all enabled textures
                    texturesToProcess = Textures.Where(t => !string.IsNullOrEmpty(t.Path)).ToList();
                } else {
                    // Process selected textures only
                    texturesToProcess = TexturesDataGrid.SelectedItems.Cast<TextureResource>().ToList();
                }

                if (texturesToProcess.Count == 0) {
                    MessageBox.Show("No textures selected for processing.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Load global settings
                if (globalTextureSettings == null) {
                    globalTextureSettings = TextureConversionSettingsManager.LoadSettings();
                }

                string outputDir = Path.Combine(
                    projectFolderPath ?? Environment.CurrentDirectory,
                    globalTextureSettings.DefaultOutputDirectory
                );
                Directory.CreateDirectory(outputDir);

                OutputDirectoryText.Text = outputDir;

                // Disable buttons during processing
                ProcessTexturesButton.IsEnabled = false;
                UploadTexturesButton.IsEnabled = false;

                int successCount = 0;
                int errorCount = 0;

                ProgressBar.Maximum = texturesToProcess.Count;
                ProgressBar.Value = 0;

                var basisUPath = string.IsNullOrWhiteSpace(globalTextureSettings.BasisUExecutablePath)
                    ? "basisu"
                    : globalTextureSettings.BasisUExecutablePath;

                var pipeline = new TextureConversion.Pipeline.TextureConversionPipeline(basisUPath);

                foreach (var texture in texturesToProcess) {
                    try {
                        ProgressTextBlock.Text = $"Processing {texture.Name}...";
                        MainWindowHelpers.LogInfo($"Processing texture: {texture.Name}");

                        var textureType = TextureResource.DetermineTextureType(texture.Name);
                        var mipProfile = TextureConversion.Core.MipGenerationProfile.CreateDefault(
                            MapTextureTypeToCore(textureType));

                        var compressionSettings = ConversionSettingsPanel.GetCompressionSettings()
                            .ToCompressionSettings(globalTextureSettings);

                        var outputFileName = Path.GetFileNameWithoutExtension(texture.Name);
                        var extension = compressionSettings.OutputFormat == TextureConversion.Core.OutputFormat.KTX2
                            ? ".ktx2"
                            : ".basis";
                        var outputPath = Path.Combine(outputDir, outputFileName + extension);

                        var mipmapOutputDir = ConversionSettingsPanel.SaveSeparateMipmaps
                            ? Path.Combine(outputDir, "mipmaps", outputFileName)
                            : null;

                        var result = await pipeline.ConvertTextureAsync(
                            texture.Path,
                            outputPath,
                            mipProfile,
                            compressionSettings,
                            ConversionSettingsPanel.SaveSeparateMipmaps,
                            mipmapOutputDir
                        );

                        if (result.Success) {
                            texture.CompressionFormat = compressionSettings.CompressionFormat.ToString();
                            texture.MipmapCount = result.MipLevels;
                            texture.Status = "Converted";
                            successCount++;
                            MainWindowHelpers.LogInfo($"✓ Successfully converted {texture.Name} ({result.MipLevels} mipmaps)");
                        } else {
                            texture.Status = "Error";
                            errorCount++;
                            MainWindowHelpers.LogError($"✗ Failed to convert {texture.Name}: {result.Error}");
                        }
                    } catch (Exception ex) {
                        texture.Status = "Error";
                        errorCount++;
                        MainWindowHelpers.LogError($"✗ Exception processing {texture.Name}: {ex.Message}");
                    }

                    ProgressBar.Value++;
                }

                ProgressTextBlock.Text = $"Completed: {successCount} success, {errorCount} errors";

                MessageBox.Show(
                    $"Processing completed!\n\nSuccess: {successCount}\nErrors: {errorCount}\n\nOutput: {outputDir}",
                    "Processing Complete",
                    MessageBoxButton.OK,
                    successCount == texturesToProcess.Count ? MessageBoxImage.Information : MessageBoxImage.Warning
                );

            } catch (Exception ex) {
                MainWindowHelpers.LogError($"Error during batch processing: {ex.Message}");
                MessageBox.Show($"Error during processing:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            } finally {
                ProcessTexturesButton.IsEnabled = true;
                UploadTexturesButton.IsEnabled = false; // Keep disabled until upload is implemented
                ProgressBar.Value = 0;
                ProgressTextBlock.Text = "";
            }
        }

        private void UploadTexturesButton_Click(object sender, RoutedEventArgs e) {
            MessageBox.Show(
                "Upload functionality coming soon!\n\nThis will upload converted textures to PlayCanvas.",
                "Coming Soon",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }

        private void UpdateSelectedTexturesCount() {
            int selectedCount = TexturesDataGrid.SelectedItems.Count;
            SelectedTexturesCountText.Text = selectedCount == 1
                ? "1 texture"
                : $"{selectedCount} textures";

            ProcessTexturesButton.IsEnabled = selectedCount > 0 || ProcessAllCheckBox.IsChecked == true;
        }

        private void ProcessAllCheckBox_Changed(object sender, RoutedEventArgs e) {
            UpdateSelectedTexturesCount();
        }

        #endregion
    }
}
