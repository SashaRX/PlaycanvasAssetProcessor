using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using OxyPlot;
using OxyPlot.Axes;

using SixLabors.ImageSharp;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;

using System.Net.Http;
using System.Net.Http.Headers;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

using System.Windows.Controls.Primitives; // Для использования Bitmap и Rectangle

using Assimp;
using HelixToolkit.Wpf;

using System.Windows.Input;
using System.Drawing;
using PointF = System.Drawing.PointF;

using TexTool.Helpers;
using TexTool.Resources;
using TexTool.Services;
using TexTool.Settings;
using System.Windows.Documents;
using System.Diagnostics;

namespace TexTool {
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

        private ObservableCollection<KeyValuePair<string, string>> projects = [];
        public ObservableCollection<KeyValuePair<string, string>> Projects {
            get { return projects; }
            set {
                projects = value;
                OnPropertyChanged(nameof(Projects));
            }
        }

        private readonly ObservableCollection<Branch> branches = [];
        public ObservableCollection<Branch> Branches {
            get { return branches; }
        }

        public MainWindow() {
            InitializeComponent();

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
            LoadLastSettings();

            RenderOptions.SetBitmapScalingMode(TexturePreviewImage, BitmapScalingMode.HighQuality);
            RenderOptions.SetBitmapScalingMode(UVImage, BitmapScalingMode.HighQuality);
            RenderOptions.SetBitmapScalingMode(UVImage2, BitmapScalingMode.HighQuality);
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
                var filteredBitmap = await MainWindowHelpers.ApplyChannelFilterAsync(originalBitmapSource, channel);

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

            var histogramModel = new PlotModel();

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

        #endregion

        #region Models

        private void ModelsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (ModelsDataGrid.SelectedItem is ModelResource selectedModel) {
                if (!string.IsNullOrEmpty(selectedModel.Path)) {
                    if (selectedModel.Status == "Downloaded") { // Если модель уже загружена
                                                                             // Загружаем модель во вьюпорт (3D просмотрщик}
                        LoadModel(selectedModel.Path);
                        // Обновляем информацию о модели
                        var context = new AssimpContext();
                        var scene = context.ImportFile(selectedModel.Path, PostProcessSteps.Triangulate | PostProcessSteps.FlipUVs | PostProcessSteps.GenerateSmoothNormals);
                        var mesh = scene.Meshes.FirstOrDefault();

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
                    var uvs = mesh.TextureCoordinateChannels[0];
                    foreach (var face in mesh.Faces) {
                        if (face.IndexCount == 3) {
                            PointF[] points = new PointF[3];
                            for (int i = 0; i < 3; i++) {
                                int vertexIndex = face.Indices[i];
                                if (vertexIndex < uvs.Count) {
                                    var uv = uvs[vertexIndex];
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
                    var uvs = mesh.TextureCoordinateChannels[1];
                    foreach (var face in mesh.Faces) {
                        if (face.IndexCount == 3) {
                            PointF[] points = new PointF[3];
                            for (int i = 0; i < 3; i++) {
                                int vertexIndex = face.Indices[i];
                                if (vertexIndex < uvs.Count) {
                                    var uv = uvs[vertexIndex];
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

            // Преобразуем bitmap в BitmapSource для отображения в WPF
            var bitmapSource1 = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                bitmap1.GetHbitmap(),
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(width, height));
            bitmap1.Dispose();

            var bitmapSource2 = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
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
                var modelsToRemove = viewPort3d.Children.OfType<ModelVisual3D>().ToList();
                foreach (var model in modelsToRemove) {
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

                foreach (var mesh in scene.Meshes) {
                    if (mesh == null) continue;

                    MeshBuilder builder = new();

                    if (mesh.Vertices == null || mesh.Normals == null) {
                        MessageBox.Show("Error loading model: Mesh vertices or normals are null.");
                        continue;
                    }

                    for (int i = 0; i < mesh.VertexCount; i++) {
                        var vertex = mesh.Vertices[i];
                        var normal = mesh.Normals[i];
                        builder.Positions.Add(new Point3D(vertex.X, vertex.Y, vertex.Z));
                        builder.Normals.Add(new System.Windows.Media.Media3D.Vector3D(normal.X, normal.Y, normal.Z));

                        // Добавляем текстурные координаты, если они есть
                        if (mesh.TextureCoordinateChannels.Length > 0 && mesh.TextureCoordinateChannels[0] != null && i < mesh.TextureCoordinateChannels[0].Count) {
                            var texCoord = mesh.TextureCoordinateChannels[0][i];
                            builder.TextureCoordinates.Add(new System.Windows.Point(texCoord.X, texCoord.Y));
                        }
                    }

                    if (mesh.Faces == null) {
                        MessageBox.Show("Error loading model: Mesh faces are null.");
                        continue;
                    }

                    totalTriangles += mesh.FaceCount;
                    totalVertices += mesh.VertexCount;

                    for (int i = 0; i < mesh.FaceCount; i++) {
                        var face = mesh.Faces[i];
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
            var modelsToRemove = viewPort3d.Children.OfType<ModelVisual3D>().ToList();
            foreach (var model in modelsToRemove) {
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
            TextureViewer.Visibility = Visibility.Visible;
            ModelViewer.Visibility = Visibility.Collapsed;
            MaterialViewer.Visibility = Visibility.Collapsed;
        }

        private void ShowModelViewer() {
            TextureViewer.Visibility = Visibility.Collapsed;
            ModelViewer.Visibility = Visibility.Visible;
            MaterialViewer.Visibility = Visibility.Collapsed;
        }

        private void ShowMaterialViewer() {
            TextureViewer.Visibility = Visibility.Collapsed;
            ModelViewer.Visibility = Visibility.Collapsed;
            MaterialViewer.Visibility = Visibility.Visible;
        }

        private void HideViewers() {
            TextureViewer.Visibility = Visibility.Collapsed;
            ModelViewer.Visibility = Visibility.Collapsed;
            MaterialViewer.Visibility = Visibility.Collapsed;
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

        private async void ProjectsComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e) {
            if (ProjectsComboBox.SelectedItem is KeyValuePair<string, string> selectedProject) {
                projectName = MainWindowHelpers.CleanProjectName(selectedProject.Value);
                projectFolderPath = Path.Combine(AppSettings.Default.ProjectsFolderPath, projectName);
                MainWindowHelpers.LogInfo($"Updated Project Folder Path: {projectFolderPath}");

                // Обновляем ветки для выбранного проекта
                var branchesList = await playCanvasService.GetBranchesAsync(selectedProject.Key, AppSettings.Default.PlaycanvasApiKey, CancellationToken.None);
                if (branchesList != null && branchesList.Count > 0) {
                    Branches.Clear();
                    foreach (var branch in branchesList) {
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
            if (TexturesDataGrid.SelectedItem is TextureResource selectedTexture) {
                if (!string.IsNullOrEmpty(selectedTexture.Path)) {
                    // Загружаем уменьшенное изображение
                    BitmapImage? thumbnailImage = MainWindowHelpers.CreateThumbnailImage(selectedTexture.Path);
                    if (thumbnailImage == null) {
                        MainWindowHelpers.LogInfo($"Error loading thumbnail for texture: {selectedTexture.Name}");
                        return;
                    }

                    TexturePreviewImage.Source = thumbnailImage;

                    await Dispatcher.InvokeAsync(() => {
                        TextureNameTextBlock.Text = "Texture Name: " + selectedTexture.Name;
                        TextureResolutionTextBlock.Text = "Resolution: " + string.Join("x", selectedTexture.Resolution);

                        Helpers.SizeConverter sizeConverter = new();
                        object size = sizeConverter.Convert(selectedTexture.Size) ?? "Unknown size";
                        TextureSizeTextBlock.Text = "Size: " + size;
                    });

                    // Асинхронная загрузка полной текстуры
                    await Task.Run(() => {
                        var bitmapImage = new BitmapImage(new Uri(selectedTexture.Path));
                        bitmapImage.Freeze(); // Замораживаем изображение для безопасного использования в другом потоке

                        Dispatcher.Invoke(() => {
                            TexturePreviewImage.Source = bitmapImage;

                            // Сохраняем оригинальную текстуру и обновляем гистограмму
                            originalBitmapSource = bitmapImage.Clone();
                            UpdateHistogram(originalBitmapSource);

                            // Сбрасываем все фильтры
                            ShowOriginalImage();
                        });
                    });
                }
            }
        }

        private async void MaterialsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (MaterialsDataGrid.SelectedItem is MaterialResource selectedMaterial) {
                if (!string.IsNullOrEmpty(selectedMaterial.Path) && File.Exists(selectedMaterial.Path)) {
                    var materialParameters = await ParseMaterialJsonAsync(selectedMaterial.Path);
                    if (materialParameters != null) {
                        selectedMaterial.DiffuseMapId = materialParameters.DiffuseMapId; // Устанавливаем DiffuseMapId
                        DisplayMaterialParameters(materialParameters); // Передаем весь объект MaterialResource
                        ShowMaterialViewer();
                    } else {
                        MainWindowHelpers.LogError($"Error: Could not parse material JSON for {selectedMaterial.Name}");
                        HideViewers(); // Hide viewers if there is an error
                    }
                } else {
                    MainWindowHelpers.LogError($"Error: Material file not found for {selectedMaterial.Name}");
                    HideViewers(); // Hide viewers if the file is not found
                }
            }
        }

        private void TexturesDataGrid_LoadingRow(object? sender, DataGridRowEventArgs? e) {
            if (e?.Row?.DataContext is TextureResource texture) {
                if (texture.Status != null) {
                    var backgroundBrush = (System.Windows.Media.Brush?)new StatusToBackgroundConverter().Convert(texture.Status, typeof(System.Windows.Media.Brush), parameter: 0, CultureInfo.InvariantCulture);
                    e.Row.Background = backgroundBrush ?? System.Windows.Media.Brushes.Transparent;
                } else {
                    e.Row.Background = System.Windows.Media.Brushes.Transparent;
                }
            }
        }

        private void ToggleViewerButton_Click(object? sender, RoutedEventArgs e) {
            if (isViewerVisible == true) {
                ToggleViewButton.Content = "►";
                TexturePreviewColumn.Width = new GridLength(0);
            } else {
                ToggleViewButton.Content = "◄";
                TexturePreviewColumn.Width = new GridLength(300); // Вернуть исходную ширину
            }
            isViewerVisible = !isViewerVisible;
        }

        private void TexturesDataGrid_Sorting(object? sender, DataGridSortingEventArgs e) {
            if (e.Column.SortMemberPath == "Status") {
                e.Handled = true;
                var dataView = CollectionViewSource.GetDefaultView(TexturesDataGrid.ItemsSource);
                var direction = (e.Column.SortDirection != ListSortDirection.Ascending) ? ListSortDirection.Ascending : ListSortDirection.Descending;
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
            var settingsWindow = new SettingsWindow();
            settingsWindow.ShowDialog();
        }

        private async void GetListAssets(object? sender, RoutedEventArgs? e) {
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
            var cancellationToken = cancellationTokenSource.Token;

            if (string.IsNullOrEmpty(AppSettings.Default.PlaycanvasApiKey) || string.IsNullOrEmpty(AppSettings.Default.UserName)) {
                MessageBox.Show("Please set your Playcanvas API key, and Username in the settings window.");
            } else {
                try {
                    userName = AppSettings.Default.UserName.ToLower();
                    userID = await playCanvasService.GetUserIdAsync(userName, AppSettings.Default.PlaycanvasApiKey, cancellationToken);
                    if (string.IsNullOrEmpty(userID)) {
                        throw new Exception("User ID is null or empty");
                    } else {
                        await Dispatcher.InvokeAsync(() => UpdateConnectionStatus(true, $"by userID: {userID}"));
                    }

                    var projectsDict = await playCanvasService.GetProjectsAsync(userID, AppSettings.Default.PlaycanvasApiKey, cancellationToken);
                    if (projectsDict != null && projectsDict.Count > 0) {
                        string lastSelectedProjectId = AppSettings.Default.LastSelectedProjectId;

                        Projects.Clear();
                        foreach (var project in projectsDict) {
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
                var branchesList = await playCanvasService.GetBranchesAsync(projectId, AppSettings.Default.PlaycanvasApiKey, cancellationToken);
                if (branchesList != null && branchesList.Count > 0) {
                    Branches.Clear();
                    foreach (var branch in branchesList) {
                        Branches.Add(branch);
                    }

                    string lastSelectedBranchName = AppSettings.Default.LastSelectedBranchName;
                    if (!string.IsNullOrEmpty(lastSelectedBranchName)) {
                        var selectedBranch = branchesList.FirstOrDefault(b => b.Name == lastSelectedBranchName);
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
            MessageBox.Show("TexTool v1.0\n\nDeveloped by: SashaRX\n\n2021");
        }

        private void SettingsMenu(object? sender, RoutedEventArgs e) {
            var settingsWindow = new SettingsWindow();
            settingsWindow.ShowDialog();
        }

        private void ExitMenu(object? sender, RoutedEventArgs e) {
            Close();
        }

        private void GridSplitter_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e) {
            GridSplitter gridSplitter = (GridSplitter)sender;

            if (gridSplitter.Parent is not Grid grid) {
                return;
            }

            var row1Height = ((RowDefinition)grid.RowDefinitions[0]).ActualHeight;
            var row2Height = ((RowDefinition)grid.RowDefinitions[1]).ActualHeight;

            // Ограничение на минимальные размеры строк
            double minHeight = 137;

            if (row1Height < minHeight || row2Height < minHeight) {
                e.Handled = true;
            }
        }

        #endregion

        #region Materials

        public class MaterialParameters {
            public string? ID { get; set; }
            public string? Name { get; set; }
            public string? Type { get; set; }
            public string? CreatedAt { get; set; }
            public string? Shader { get; set; }
            public string? BlendType { get; set; }
            public string? Cull { get; set; }
            public string? UseLighting { get; set; }
            public string? TwoSidedLighting { get; set; }
            public string? UseMetalness { get; set; }
            public string? Metalness { get; set; }
            public string? Shininess { get; set; }
            public string? Opacity { get; set; }
            public string? BumpMapFactor { get; set; }
            public string? Reflectivity { get; set; }
            public string? AlphaTest { get; set; }
            public bool DiffuseTint { get; set; }
            public List<double>? Diffuse { get; set; }
            public int? DiffuseMapId { get; set; }
        }

        private static async Task<MaterialResource> ParseMaterialJsonAsync(string filePath) {
            try {
                string jsonContent = await File.ReadAllTextAsync(filePath);
                var json = JObject.Parse(jsonContent);

                var data = json["data"];
                if (data != null) {
                    var material = new MaterialResource {
                        ID = json["id"]?.ToObject<int>() ?? 0,
                        Name = json["name"]?.ToString() ?? string.Empty,
                        CreatedAt = json["createdAt"]?.ToString() ?? string.Empty,
                        Shader = data["shader"]?.ToString() ?? string.Empty,
                        BlendType = data["blendType"]?.ToString() ?? string.Empty,
                        Cull = data["cull"]?.ToString() ?? string.Empty,
                        UseLighting = data["useLighting"]?.ToString() ?? string.Empty,
                        TwoSidedLighting = data["twoSidedLighting"]?.ToString() ?? string.Empty,
                        UseMetalness = data["useMetalness"]?.ToString() ?? string.Empty,
                        Metalness = data["metalness"]?.ToString() ?? string.Empty,
                        Shininess = data["shininess"]?.ToString() ?? string.Empty,
                        Opacity = data["opacity"]?.ToString() ?? string.Empty,
                        BumpMapFactor = data["bumpMapFactor"]?.ToString() ?? string.Empty,
                        Reflectivity = data["reflectivity"]?.ToString() ?? string.Empty,
                        AlphaTest = data["alphaTest"]?.ToString() ?? string.Empty,
                        DiffuseTint = data["diffuseTint"]?.ToObject<bool>() ?? false,
                        Diffuse = data["diffuse"]?.Select(d => d.ToObject<double>()).ToList(),
                        DiffuseMapId = data["diffuseMap"]?.Type == JTokenType.Integer ? data["diffuseMap"]?.ToObject<int?>() : null
                    };

                    System.Diagnostics.Debug.WriteLine($"Parsed material parameters: ID={material.ID}, DiffuseMapId={material.DiffuseMapId}");

                    return material;
                }
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"Error parsing material JSON: {ex.Message}");
            }
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
                MaterialUseMetalnessTextBlock.Text = $"Use Metalness: {parameters.UseMetalness}";
                MaterialMetalnessTextBlock.Text = $"Metalness: {parameters.Metalness}";
                MaterialShininessTextBlock.Text = $"Shininess: {parameters.Shininess}";
                MaterialOpacityTextBlock.Text = $"Opacity: {parameters.Opacity}";
                MaterialBumpMapFactorTextBlock.Text = $"Bump Map Factor: {parameters.BumpMapFactor}";
                MaterialReflectivityTextBlock.Text = $"Reflectivity: {parameters.Reflectivity}";
                MaterialAlphaTestTextBlock.Text = $"Alpha Test: {parameters.AlphaTest}";

                // Показать цвет Diffuse
                if (parameters.DiffuseTint && parameters.Diffuse != null) {
                    var color = System.Windows.Media.Color.FromRgb(
                        (byte)(parameters.Diffuse[0] * 255),
                        (byte)(parameters.Diffuse[1] * 255),
                        (byte)(parameters.Diffuse[2] * 255)
                    );
                    MaterialTintColorRect.Fill = new SolidColorBrush(color);
                } else {
                    MaterialTintColorRect.Fill = System.Windows.Media.Brushes.Transparent;
                }

                // Обновление гиперссылки для DiffuseMap
                if (parameters.DiffuseMapId.HasValue) {
                    var texture = Textures.FirstOrDefault(t => t.ID == parameters.DiffuseMapId.Value);
                    if (texture != null) {
                        if (!string.IsNullOrEmpty(texture.Name)) {
                            MaterialDiffuseMapHyperlink.NavigateUri = new Uri(texture.Name, UriKind.Relative);
                            MaterialDiffuseMapHyperlink.Inlines.Clear();
                            MaterialDiffuseMapHyperlink.Inlines.Add(texture.Name);
                        }
                    }
                } else {
                    MaterialDiffuseMapHyperlink.NavigateUri = null;
                    MaterialDiffuseMapHyperlink.Inlines.Clear();
                    MaterialDiffuseMapHyperlink.Inlines.Add("No Diffuse Map");
                }

                // Обновление гиперссылки для MetalnessMap
                if (parameters.MetalnessMapId.HasValue) {
                    var texture = Textures.FirstOrDefault(t => t.ID == parameters.MetalnessMapId.Value);
                    if (texture != null) {
                        if (!string.IsNullOrEmpty(texture.Name)) {
                            MaterialMetalnessMapHyperlink.NavigateUri = new Uri(texture.Name, UriKind.Relative);
                            MaterialMetalnessMapHyperlink.Inlines.Clear();
                            MaterialMetalnessMapHyperlink.Inlines.Add(texture.Name);
                        }
                    }
                } else {
                    MaterialMetalnessMapHyperlink.NavigateUri = null;
                    MaterialMetalnessMapHyperlink.Inlines.Clear();
                    MaterialMetalnessMapHyperlink.Inlines.Add("No Metalness Map");
                }

                // Обновление гиперссылки для NormalMap
                if (parameters.NormalMapId.HasValue) {
                    var texture = Textures.FirstOrDefault(t => t.ID == parameters.NormalMapId.Value);
                    if (texture != null) {
                        if (!string.IsNullOrEmpty(texture.Name)) {
                            MaterialNormalMapHyperlink.NavigateUri = new Uri(texture.Name, UriKind.Relative);
                            MaterialNormalMapHyperlink.Inlines.Clear();
                            MaterialNormalMapHyperlink.Inlines.Add(texture.Name);
                        }
                    }
                } else {
                    MaterialNormalMapHyperlink.NavigateUri = null;
                    MaterialNormalMapHyperlink.Inlines.Clear();
                    MaterialNormalMapHyperlink.Inlines.Add("No Normal Map");
                }
            });
        }

        private void MaterialDiffuseMapHyperlink_Click(object sender, RoutedEventArgs e) {
            if (sender is Hyperlink hyperlink) {
                if (MaterialsDataGrid.SelectedItem is MaterialResource material && material.DiffuseMapId.HasValue) {
                    System.Diagnostics.Debug.WriteLine($"Switching to Textures tab and selecting texture with ID: {material.DiffuseMapId.Value}");

                    // Переключение на вкладку "Textures"
                    var texturesTab = tabControl.Items.OfType<TabItem>().FirstOrDefault(tab => tab.Header.ToString() == "Textures");
                    if (texturesTab != null) {
                        tabControl.SelectedItem = texturesTab;
                    }

                    // Поставим небольшую задержку перед выбором текстуры, чтобы убедиться, что переключение вкладки завершилось
                    Dispatcher.InvokeAsync(() => {
                        // Выбор текстуры в списке текстур
                        var texture = Textures.FirstOrDefault(t => t.ID == material.DiffuseMapId.Value);
                        if (texture != null) {
                            TexturesDataGrid.SelectedItem = texture;
                            TexturesDataGrid.ScrollIntoView(texture);
                            System.Diagnostics.Debug.WriteLine($"Texture with ID: {texture.ID} selected.");
                        } else {
                            System.Diagnostics.Debug.WriteLine($"Texture with ID {material.DiffuseMapId.Value} not found.");
                        }
                    }, System.Windows.Threading.DispatcherPriority.Background);
                } else {
                    System.Diagnostics.Debug.WriteLine("DiffuseMapId is not set or is null.");
                }
            }
        }

        private void MaterialMetalnessMapHyperlink_Click(object sender, RoutedEventArgs e) {
            if (sender is Hyperlink hyperlink) {
                var material = MaterialsDataGrid.SelectedItem as MaterialResource;
                if (material != null && material.MetalnessMapId.HasValue) {
                    // Переключение на вкладку "Textures"
                    var texturesTab = tabControl.Items.OfType<TabItem>().FirstOrDefault(tab => tab.Header.ToString() == "Textures");
                    if (texturesTab != null) {
                        tabControl.SelectedItem = texturesTab;
                    }

                    // Поставим небольшую задержку перед выбором текстуры, чтобы убедиться, что переключение вкладки завершилось
                    Dispatcher.InvokeAsync(() => {
                        // Выбор текстуры в списке текстур
                        var texture = Textures.FirstOrDefault(t => t.ID == material.MetalnessMapId.Value);
                        if (texture != null) {
                            TexturesDataGrid.SelectedItem = texture;
                            TexturesDataGrid.ScrollIntoView(texture);
                        } else {
                            System.Diagnostics.Debug.WriteLine($"Texture with ID {material.MetalnessMapId.Value} not found.");
                        }
                    }, System.Windows.Threading.DispatcherPriority.Background);
                } else {
                    System.Diagnostics.Debug.WriteLine("MetalnessMapId is not set or is null.");
                }
            }
        }

        private void MaterialNormalMapHyperlink_Click(object sender, RoutedEventArgs e) {
            if (sender is Hyperlink hyperlink) {
                var material = MaterialsDataGrid.SelectedItem as MaterialResource;
                if (material != null && material.NormalMapId.HasValue) {
                    // Переключение на вкладку "Textures"
                    var texturesTab = tabControl.Items.OfType<TabItem>().FirstOrDefault(tab => tab.Header.ToString() == "Textures");
                    if (texturesTab != null) {
                        tabControl.SelectedItem = texturesTab;
                    }

                    // Поставим небольшую задержку перед выбором текстуры, чтобы убедиться, что переключение вкладки завершилось
                    Dispatcher.InvokeAsync(() => {
                        // Выбор текстуры в списке текстур
                        var texture = Textures.FirstOrDefault(t => t.ID == material.NormalMapId.Value);
                        if (texture != null) {
                            TexturesDataGrid.SelectedItem = texture;
                            TexturesDataGrid.ScrollIntoView(texture);
                        } else {
                            System.Diagnostics.Debug.WriteLine($"Texture with ID {material.NormalMapId.Value} not found.");
                        }
                    }, System.Windows.Threading.DispatcherPriority.Background);
                } else {
                    System.Diagnostics.Debug.WriteLine("NormalMapId is not set or is null.");
                }
            }
        }

        public class BooleanToBrushConverter : IValueConverter {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
                if (value is bool boolValue) {
                    return boolValue ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Red;
                }
                return System.Windows.Media.Brushes.Transparent;
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
                throw new NotImplementedException();
            }
        }

        public class DiffuseMapIdToTextConverter : IValueConverter {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
                if (value is int diffuseMapId) {
                    return $"Diffuse Map ID: {diffuseMapId}";
                }
                return "No Diffuse Map";
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
                throw new NotImplementedException();
            }
        }




        #endregion

        #region Download

        private async void Download(object? sender, RoutedEventArgs? e) {
            try {
                var selectedResources = textures.Where(t => t.Status == "On Server" ||
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
                                                .OrderBy(r => r.Name)
                                                .ToList();

                var downloadTasks = selectedResources.Select(resource => DownloadResourceAsync(resource));
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
                    var playCanvasService = new PlayCanvasService();
                    var apiKey = AppSettings.Default.PlaycanvasApiKey;
                    var materialJson = await playCanvasService.GetAssetByIdAsync(materialResource.ID.ToString(), apiKey, default)
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
            if (resource == null || string.IsNullOrEmpty(resource.Path)){ // Если путь к файлу не указан, создаем его в папке проекта
                return;
            }

            const int maxRetries = 5;
            const int delayMilliseconds = 2000;

            for (int attempt = 1; attempt <= maxRetries; attempt++) {
                try {
                    using var client = new HttpClient();
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AppSettings.Default.PlaycanvasApiKey);

                    var response = await client.GetAsync(resource.Url, HttpCompletionOption.ResponseHeadersRead);

                    if (!response.IsSuccessStatusCode) {
                        throw new Exception($"Failed to download resource: {response.StatusCode}");
                    }

                    var totalBytes = response.Content.Headers.ContentLength ?? 0L;
                    var buffer = new byte[8192];
                    var bytesRead = 0;

                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = await FileHelper.OpenFileStreamWithRetryAsync(resource.Path, FileMode.Create, FileAccess.Write, FileShare.None)) {
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
                    var fileInfo = new FileInfo(resource.Path);
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

                var selectedProjectId = ((KeyValuePair<string, string>)ProjectsComboBox.SelectedItem).Key;
                var selectedBranchId = ((Branch)BranchesComboBox.SelectedItem).Id;

                var assetsResponse = await playCanvasService.GetAssetsAsync(selectedProjectId, selectedBranchId, AppSettings.Default.PlaycanvasApiKey, cancellationToken);
                if (assetsResponse != null) {
                    // Сохраняем JSON-ответ в файл
                    if (string.IsNullOrEmpty(projectFolderPath) || string.IsNullOrEmpty(projectName)){
                        return;
                    }
                    await SaveJsonResponseToFile(assetsResponse, projectFolderPath, projectName);

                    UpdateConnectionStatus(true);

                    textures.Clear(); // Очищаем текущий список текстур
                    models.Clear(); // Очищаем текущий список моделей
                    materials.Clear(); // Очищаем текущий список материалов

                    var supportedAssets = assetsResponse.Where(asset => asset["file"] != null).ToList();
                    int assetCount = supportedAssets.Count;

                    await Dispatcher.InvokeAsync(() =>
                    {
                        ProgressBar.Value = 0;
                        ProgressBar.Maximum = assetCount;
                        ProgressTextBlock.Text = $"0/{assetCount}";
                    });

                    var tasks = supportedAssets.Select(asset => Task.Run(async () =>
                    {
                        await ProcessAsset(asset, assetCount, cancellationToken);  // Передаем аргумент cancellationToken
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

        private static async Task SaveJsonResponseToFile(JToken jsonResponse, string projectFolderPath, string projectName) {
            try {
                // Convert JToken to JSON string
                string jsonString = jsonResponse.ToString(Formatting.Indented);

                // Determine the file path
                string jsonFilePath = Path.Combine(Path.Combine(projectFolderPath, projectName), "assets_list.json");

                System.Diagnostics.Debug.WriteLine($"Saving JSON to file: {jsonFilePath}");

                // Save JSON to file
                await File.WriteAllTextAsync(jsonFilePath, jsonString);

                MainWindowHelpers.LogInfo($"Assets list saved to {jsonFilePath}");
            } catch (Exception ex) {
                MainWindowHelpers.LogError($"Error saving assets list to JSON: {ex.Message}");
            }
        }

        private async Task ProcessAsset(JToken asset, int index, CancellationToken cancellationToken) {
            try {
                await getAssetsSemaphore.WaitAsync(cancellationToken);

                string? type = asset["type"]?.ToString() ?? string.Empty;
                MainWindowHelpers.LogInfo($"Processing {type}");

                if (type == "script" || type == "wasm" || type == "cubemap") {
                    MainWindowHelpers.LogInfo($"Unsupported asset type: {type}");
                    return;
                }

                // Обработка материала без параметра file
                if (type == "material") {
                    await ProcessMaterialAsset(asset, index, cancellationToken);
                    return;
                }

                var file = asset["file"];
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

        private async Task ProcessModelAsset(JToken asset, int index, string fileUrl, string extension, CancellationToken cancellationToken) {
            try {
                var model = new ModelResource {
                    ID = asset["id"]?.Type == JTokenType.Integer ? (int)(asset["id"] ?? 0) : 0,
                    Index = index,
                    Name = asset["name"]?.ToString().Split('.')[0] ?? "Unknown",
                    Size = int.TryParse(asset["file"]?["size"]?.ToString(), out var size) ? size : 0,
                    Url = fileUrl.Split('?')[0],  // Удаляем параметры запроса
                    Path = GetResourcePath("models", asset["name"]?.ToString()),
                    Extension = extension,
                    Status = "On Server",
                    Hash = asset["file"]?["hash"]?.ToString() ?? string.Empty,
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
        }

        private async Task ProcessTextureAsset(JToken asset, int index, string fileUrl, string extension, CancellationToken cancellationToken) {
            try {
                var texture = new TextureResource {
                    ID = asset["id"]?.Type == JTokenType.Integer ? (int)(asset["id"] ?? 0) : 0,
                    Index = index,
                    Name = asset["name"]?.ToString().Split('.')[0] ?? "Unknown",
                    Size = int.TryParse(asset["file"]?["size"]?.ToString(), out var size) ? size : 0,
                    Url = fileUrl.Split('?')[0],  // Удаляем параметры запроса
                    Path = GetResourcePath("textures", asset["name"]?.ToString()),
                    Extension = extension,
                    Resolution = new int[2],
                    ResizeResolution = new int[2],
                    Status = "On Server",
                    Hash = asset["file"]?["hash"]?.ToString() ?? string.Empty,
                    Type = asset["type"]?.ToString() // Устанавливаем свойство Type
                };

                await MainWindowHelpers.VerifyAndProcessResourceAsync(texture, async () => {
                    MainWindowHelpers.LogInfo($"Adding texture to list: {texture.Name}");

                    switch (texture.Status) {
                        case "Downloaded":
                            var resolution = MainWindowHelpers.GetLocalImageResolution(texture.Path);
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

                var material = new MaterialResource {
                    ID = asset["id"]?.Type == JTokenType.Integer ? (int)(asset["id"] ?? 0) : 0,
                    Index = index,
                    Name = name,
                    Size = 0, // У материалов нет файла, поэтому размер 0
                    Path = GetResourcePath("materials", $"{name}.json"),
                    Status = "On Server",
                    Hash = string.Empty, // У материалов нет хеша
                    TextureIds = []
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

                    var playCanvasService = new PlayCanvasService();
                    var apiKey = AppSettings.Default.PlaycanvasApiKey;
                    var materialJson = await playCanvasService.GetAssetByIdAsync(material.ID.ToString(), apiKey, cancellationToken);

                    if (materialJson != null && materialJson["textures"] != null && materialJson["textures"]?.Type == JTokenType.Array) {
                        material.TextureIds.AddRange(from textureId in materialJson["textures"]!
                                                     select (int)textureId);
                    }

                    MainWindowHelpers.LogInfo($"Adding material to list: {material.Name}");

                    await Dispatcher.InvokeAsync(() => materials.Add(material));
                });
            } catch (Exception ex) {
                MainWindowHelpers.LogError($"Error processing material: {ex.Message}");
            }
        }

        #endregion

        #region Helper Methods

        private string GetResourcePath(string folder, string? fileName) {
            if (string.IsNullOrEmpty(projectFolderPath)) {
                throw new Exception("Project folder path is null or empty");
            }

            if (string.IsNullOrEmpty(projectName)) {
                throw new Exception("Project name is null or empty");
            }

            string pathProjectFolder = Path.Combine(AppSettings.Default.ProjectsFolderPath, projectName);
            string pathResourceFolder = Path.Combine(pathProjectFolder, folder);
            string pathSourceFolder = Path.Combine(pathResourceFolder, "source");

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
                foreach (var texture in textures) {
                    texture.Index = index++;
                }
                TexturesDataGrid.Items.Refresh(); // Обновляем DataGrid, чтобы отразить изменения индексов

                index = 1;
                foreach (var model in models) {
                    model.Index = index++;
                }
                ModelsDataGrid.Items.Refresh(); // Обновляем DataGrid, чтобы отразить изменения индексов

                index = 1;

                foreach (var material in materials) {
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

        private async void LoadLastSettings() {
            try {
                userName = AppSettings.Default.UserName.ToLower();
                if (string.IsNullOrEmpty(userName)) {
                    throw new Exception("Username is null or empty");
                }

                var cancellationToken = new CancellationToken();

                userID = await playCanvasService.GetUserIdAsync(userName, AppSettings.Default.PlaycanvasApiKey, cancellationToken);
                if (string.IsNullOrEmpty(userID)) {
                    throw new Exception("User ID is null or empty");
                } else {
                    UpdateConnectionStatus(true, $"by userID: {userID}");
                }
                var projectsDict = await playCanvasService.GetProjectsAsync(userID, AppSettings.Default.PlaycanvasApiKey, cancellationToken);

                if (projectsDict != null && projectsDict.Count > 0) {
                    Projects.Clear();
                    foreach (var project in projectsDict) {
                        Projects.Add(project);
                    }

                    if (!string.IsNullOrEmpty(AppSettings.Default.LastSelectedProjectId) && projectsDict.ContainsKey(AppSettings.Default.LastSelectedProjectId)) {
                        ProjectsComboBox.SelectedValue = AppSettings.Default.LastSelectedProjectId;
                    } else {
                        ProjectsComboBox.SelectedIndex = 0;
                    }

                    if (ProjectsComboBox.SelectedItem != null) {
                        string projectId = ((KeyValuePair<string, string>)ProjectsComboBox.SelectedItem).Key;
                        var branchesList = await playCanvasService.GetBranchesAsync(projectId, AppSettings.Default.PlaycanvasApiKey, cancellationToken);

                        if (branchesList != null && branchesList.Count > 0) {
                            Branches.Clear();
                            foreach (var branch in branchesList) {
                                Branches.Add(branch);
                            }

                            BranchesComboBox.ItemsSource = Branches;
                            BranchesComboBox.DisplayMemberPath = "Name";
                            BranchesComboBox.SelectedValuePath = "Id";

                            if (!string.IsNullOrEmpty(AppSettings.Default.LastSelectedBranchName)) {
                                var selectedBranch = branchesList.FirstOrDefault(b => b.Name == AppSettings.Default.LastSelectedBranchName);
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
    }
}