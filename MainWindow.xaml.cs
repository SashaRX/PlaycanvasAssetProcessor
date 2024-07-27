using Newtonsoft.Json.Linq;

using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.Wpf;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Processing.Processors;
using SixLabors.ImageSharp.Advanced;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Numerics;  // Добавляем это пространство имен

using System.Net.Http;
using System.Net.Http.Headers;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

using System.Windows.Controls.Primitives; // Для использования Bitmap и Rectangle
using System.Windows.Media.TextFormatting;

using Assimp;
using HelixToolkit.Wpf;

using System.Windows.Input;
using System.Drawing;
using System.Linq;
using PointF = System.Drawing.PointF;

using System.Security.Policy;

using TexTool.Helpers;
using TexTool.Resources;
using TexTool.Services;

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

        private ObservableCollection<MaterialResource> materials = new();
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

        private readonly SemaphoreSlim? semaphore = new(Settings.Default.SemaphoreLimit);
        private readonly SemaphoreSlim getAssetsSemaphore;
        private readonly SemaphoreSlim downloadSemaphore;

        private string? projectFolderPath = string.Empty;
        private string? userName = string.Empty;
        private string? userID = string.Empty;
        private string? projectName = string.Empty;

        private static readonly object logLock = new();

        private bool? isViewerVisible = true;
        private BitmapSource? originalBitmapSource;

        private const string MODEL_PATH = "C:\\models\\carbitLamp\\ao.fbx";

        private readonly List<string> supportedFormats = [".png", ".jpg", ".jpeg"];
        private readonly List<string> excludedFormats = [".hdr", ".avif"];
        private readonly List<string> supportedModelFormats = [".fbx", ".obj", ".glb"];

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

            LoadModel(MODEL_PATH);

            getAssetsSemaphore = new SemaphoreSlim(Settings.Default.GetTexturesSemaphoreLimit);
            downloadSemaphore = new SemaphoreSlim(Settings.Default.DownloadSemaphoreLimit);

            projectFolderPath = Settings.Default.ProjectsFolderPath;
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

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (tabControl.SelectedItem is TabItem selectedTab) {
                if (selectedTab.Header.ToString() == "Textures") {
                    TextureViewer.Visibility = Visibility.Visible;
                    ModelViewer.Visibility = Visibility.Collapsed;
                } else if (selectedTab.Header.ToString() == "Models") {
                    TextureViewer.Visibility = Visibility.Collapsed;
                    ModelViewer.Visibility = Visibility.Visible;
                }
            }
        }

        private static byte[] BitmapSourceToArray(BitmapSource bitmapSource) {
            var encoder = new PngBitmapEncoder(); // Или любой другой доступный энкодер
            encoder.Frames.Add(BitmapFrame.Create((BitmapSource)bitmapSource.Clone()));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }

        private static BitmapSource BitmapToBitmapSource(SixLabors.ImageSharp.Image<Rgba32> image) {
            using MemoryStream memoryStream = new();
            image.SaveAsBmp(memoryStream);
            memoryStream.Seek(0, SeekOrigin.Begin);
            BitmapImage bitmapImage = new();
            bitmapImage.BeginInit();
            bitmapImage.StreamSource = memoryStream;
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.EndInit();
            return bitmapImage;
        }

        private static BitmapImage CreateThumbnailImage(string imagePath) {
            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.UriSource = new Uri(imagePath);
            bitmapImage.DecodePixelWidth = 64; // Задаем ширину уменьшенного изображения
            bitmapImage.DecodePixelHeight = 64; // Задаем высоту уменьшенного изображения
            bitmapImage.EndInit();
            return bitmapImage;
        }

        private static async Task<BitmapSource> ApplyChannelFilterAsync(BitmapSource source, string channel) {
            using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(BitmapSourceToArray(source));

            await Task.Run(() => {
                switch (channel) {
                    case "R":
                        ProcessChannel(image, (pixel) => new Rgba32(pixel.R, pixel.R, pixel.R, pixel.A));
                        break;
                    case "G":
                        ProcessChannel(image, (pixel) => new Rgba32(pixel.G, pixel.G, pixel.G, pixel.A));
                        break;
                    case "B":
                        ProcessChannel(image, (pixel) => new Rgba32(pixel.B, pixel.B, pixel.B, pixel.A));
                        break;
                    case "A":
                        ProcessChannel(image, (pixel) => new Rgba32(pixel.A, pixel.A, pixel.A, pixel.A));
                        break;
                }
            });

            return BitmapToBitmapSource(image);
        }

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
                var filteredBitmap = await ApplyChannelFilterAsync(originalBitmapSource, channel);

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
            ProcessImage(bitmapSource, redHistogram, greenHistogram, blueHistogram);

            if (!isGray) {
                AddSeriesToModel(histogramModel, redHistogram, OxyColors.Red);
                AddSeriesToModel(histogramModel, greenHistogram, OxyColors.Green);
                AddSeriesToModel(histogramModel, blueHistogram, OxyColors.Blue);
            } else {
                AddSeriesToModel(histogramModel, redHistogram, OxyColors.Black);
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

        private static void ProcessChannel(Image<Rgba32> image, Func<Rgba32, Rgba32> transform) {
            int width = image.Width;
            int height = image.Height;
            int numberOfChunks = Environment.ProcessorCount; // Количество потоков для параллельной обработки
            int chunkHeight = height / numberOfChunks;

            Parallel.For(0, numberOfChunks, chunk => {
                int startY = chunk * chunkHeight;
                int endY = (chunk == numberOfChunks - 1) ? height : startY + chunkHeight;

                for (int y = startY; y < endY; y++) {
                    Span<Rgba32> pixelRow = image.Frames.RootFrame.DangerousGetPixelRowMemory(y).Span;
                    for (int x = 0; x < width; x++) {
                        pixelRow[x] = transform(pixelRow[x]);
                    }
                }
            });
        }

        private static void ProcessImage(BitmapSource bitmapSource, int[] redHistogram, int[] greenHistogram, int[] blueHistogram) {
            using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(BitmapSourceToArray(bitmapSource));
            Parallel.For(0, image.Height, y => {
                Span<Rgba32> pixelRow = image.Frames.RootFrame.DangerousGetPixelRowMemory(y).Span;
                for (int x = 0; x < pixelRow.Length; x++) {
                    var pixel = pixelRow[x];
                    redHistogram[pixel.R]++;
                    greenHistogram[pixel.G]++;
                    blueHistogram[pixel.B]++;
                }
            });
        }

        private static void AddSeriesToModel(PlotModel model, int[] histogram, OxyColor color) {
            var colorWithAlpha = OxyColor.FromAColor(100, color);
            var series = new AreaSeries { Color = color, Fill = colorWithAlpha, StrokeThickness = 1 };

            double[] smoothedHistogram = MovingAverage(histogram, 32);

            for (int i = 0; i < 256; i++) {
                series.Points.Add(new DataPoint(i, smoothedHistogram[i]));
                series.Points2.Add(new DataPoint(i, 0));
            }

            model.Series.Add(series);
        }

        private static double[] MovingAverage(int[] values, int windowSize) {
            double[] result = new double[values.Length];
            double sum = 0;
            for (int i = 0; i < values.Length; i++) {
                sum += values[i];
                if (i >= windowSize) {
                    sum -= values[i - windowSize];
                }
                result[i] = sum / Math.Min(windowSize, i + 1);
            }
            return result;
        }




        #endregion

        #region Models

        private void ModelsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (ModelsDataGrid.SelectedItem is ModelResource selectedModel) {
                if (!string.IsNullOrEmpty(selectedModel.Path)) {
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

                ModelVisual3D pivotGizmo = CreatePivotGizmo(transformGroup);
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

        private static ModelVisual3D CreatePivotGizmo(Transform3DGroup transformGroup) {
            double axisLength = 10.0;
            double thickness = 0.1;
            double coneHeight = 1.0;
            double coneRadius = 0.3;

            // Создаем оси с конусами на концах
            GeometryModel3D xAxisModel = CreateArrowModel(new Point3D(0, 0, 0), new Point3D(axisLength, 0, 0), thickness, coneHeight, coneRadius, Colors.Red);
            GeometryModel3D yAxisModel = CreateArrowModel(new Point3D(0, 0, 0), new Point3D(0, axisLength, 0), thickness, coneHeight, coneRadius, Colors.Green);
            GeometryModel3D zAxisModel = CreateArrowModel(new Point3D(0, 0, 0), new Point3D(0, 0, axisLength), thickness, coneHeight, coneRadius, Colors.Blue);

            // Создаем группу для гизмо
            Model3DGroup group = new Model3DGroup();
            group.Children.Add(xAxisModel);
            group.Children.Add(yAxisModel);
            group.Children.Add(zAxisModel);

            // Создаем ModelVisual3D для осей
            ModelVisual3D modelVisual = new ModelVisual3D { Content = group, Transform = transformGroup };

            // Создаем подписи для осей
            BillboardTextVisual3D xLabel = CreateTextLabel("X", Colors.Red, new Point3D(axisLength + 0.5, 0, 0));
            BillboardTextVisual3D yLabel = CreateTextLabel("Y", Colors.Green, new Point3D(0, axisLength + 0.5, 0));
            BillboardTextVisual3D zLabel = CreateTextLabel("Z", Colors.Blue, new Point3D(0, 0, axisLength + 0.5));

            // Применяем те же трансформации к подписям
            xLabel.Transform = transformGroup;
            yLabel.Transform = transformGroup;
            zLabel.Transform = transformGroup;

            // Добавляем подписи к модели
            var gizmoGroup = new ModelVisual3D();
            gizmoGroup.Children.Add(modelVisual);
            gizmoGroup.Children.Add(xLabel);
            gizmoGroup.Children.Add(yLabel);
            gizmoGroup.Children.Add(zLabel);

            return gizmoGroup;
        }

        private static GeometryModel3D CreateArrowModel(Point3D start, Point3D end, double thickness, double coneHeight, double coneRadius, System.Windows.Media.Color color) {
            MeshBuilder meshBuilder = new MeshBuilder();
            var direction = new System.Windows.Media.Media3D.Vector3D(end.X - start.X, end.Y - start.Y, end.Z - start.Z);
            direction.Normalize();
            var cylinderEnd = end - direction * coneHeight;
            meshBuilder.AddCylinder(start, cylinderEnd, thickness, 36, false, true);
            meshBuilder.AddCone(cylinderEnd,
                                direction,
                                coneRadius,
                                0,
                                coneHeight,
                                true,
                                false,
                                36);
            var geometry = meshBuilder.ToMesh(true);
            var material = new EmissiveMaterial(new SolidColorBrush(color));
            return new GeometryModel3D(geometry, material);
        }

        private static BillboardTextVisual3D CreateTextLabel(string text, System.Windows.Media.Color color, Point3D position) {
            var textBlock = new TextBlock {
                Text = text,
                Foreground = new SolidColorBrush(color),
                Background = System.Windows.Media.Brushes.Transparent
            };

            var visualBrush = new VisualBrush(textBlock);
            var material = new EmissiveMaterial(visualBrush);

            return new BillboardTextVisual3D {
                Text = text,
                Position = position,
                Foreground = new SolidColorBrush(color),
                Background = System.Windows.Media.Brushes.Transparent,
                Material = material
            };
        }




        #endregion

        #region UI Event Handlers

        private void ProjectsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            _ = LoadBranchesAsync();
        }

        private void BranchesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            SaveCurrentSettings();
        }

        private async void TexturesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (TexturesDataGrid.SelectedItem is TextureResource selectedTexture) {
                if (!string.IsNullOrEmpty(selectedTexture.Path)) {
                    // Загружаем уменьшенное изображение
                    var thumbnailImage = CreateThumbnailImage(selectedTexture.Path);
                    TexturePreviewImage.Source = thumbnailImage;

                    await Dispatcher.InvokeAsync(() => {
                        TextureNameTextBlock.Text = "Texture Name: " + selectedTexture.Name;
                        TextureResolutionTextBlock.Text = "Resolution: " + string.Join("x", selectedTexture.Resolution);

                        SizeConverter sizeConverter = new();
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

        private void MaterialsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (MaterialsDataGrid.SelectedItem is MaterialResource selectedMaterial) {
                if (!string.IsNullOrEmpty(selectedMaterial.Path)) {
                    // Обработка выбранного материала
                    // Например, можно отобразить информацию о материале или его текстуры
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

        private void ToggleViewerButton_Click(object sender, RoutedEventArgs e) {
            if (isViewerVisible == true) {
                ToggleViewButton.Content = "►";
                TexturePreviewColumn.Width = new GridLength(0);
            } else {
                ToggleViewButton.Content = "◄";
                TexturePreviewColumn.Width = new GridLength(300); // Вернуть исходную ширину
            }
            isViewerVisible = !isViewerVisible;
        }

        private void TexturesDataGrid_Sorting(object sender, DataGridSortingEventArgs e) {
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

        private void Setting(object sender, RoutedEventArgs e) {
            var settingsWindow = new SettingsWindow();
            settingsWindow.ShowDialog();
        }

        private void ClearTextures(object sender, RoutedEventArgs e) {
            textures?.Clear();
        }

        private async void GetListTextures(object? sender, RoutedEventArgs? e) {
            try {
                CancelButton.IsEnabled = true;
                if (cancellationTokenSource != null) {
                    await TryConnect(cancellationTokenSource.Token);
                }
            } catch (Exception ex) {
                MessageBox.Show($"Error in GetListTextures: {ex.Message}");
                LogError($"Error in GetListTextures: {ex}");
            } finally {
                CancelButton.IsEnabled = false;
            }
        }

        private async void Connect(object sender, RoutedEventArgs e) {
            var cancellationToken = cancellationTokenSource.Token;

            if (string.IsNullOrEmpty(Settings.Default.PlaycanvasApiKey) || string.IsNullOrEmpty(Settings.Default.Username)) {
                MessageBox.Show("Please set your Playcanvas API key, and Username in the settings window.");
            } else {
                try {
                    userName = Settings.Default.Username.ToLower();
                    userID = await playCanvasService.GetUserIdAsync(userName, Settings.Default.PlaycanvasApiKey, cancellationToken);
                    if (string.IsNullOrEmpty(userID)) {
                        throw new Exception("User ID is null or empty");
                    } else {
                        await Dispatcher.InvokeAsync(() => UpdateConnectionStatus(true, $"by userID: {userID}"));
                    }

                    var projectsDict = await playCanvasService.GetProjectsAsync(userID, Settings.Default.PlaycanvasApiKey, cancellationToken);
                    if (projectsDict != null && projectsDict.Count > 0) {
                        string lastSelectedProjectId = Settings.Default.LastSelectedProjectId;
                        string lastSelectedBranchName = Settings.Default.LastSelectedBranchName;

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
                            var branchesList = await playCanvasService.GetBranchesAsync(projectId, Settings.Default.PlaycanvasApiKey, cancellationToken);

                            if (branchesList != null && branchesList.Count > 0) {
                                Branches.Clear();
                                foreach (var branch in branchesList) {
                                    Branches.Add(branch);
                                }

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
                            }
                        }
                    } else {
                        throw new Exception("Project list is empty");
                    }
                } catch (Exception ex) {
                    MessageBox.Show($"Error: {ex.Message}");
                }
            }
        }

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
                                                .OrderBy(r => r.Name)
                                                .ToList();

                var downloadTasks = selectedResources.Select(resource => DownloadResourceAsync(resource));
                await Task.WhenAll(downloadTasks);

                // Пересчитываем индексы после завершения загрузки
                RecalculateTextureIndices();
                RecalculateModelIndices();
            } catch (Exception ex) {
                MessageBox.Show($"Error: {ex.Message}");
                LogError($"Error: {ex}");
            }
        }

        private void AboutMenu(object sender, RoutedEventArgs e) {
            MessageBox.Show("TexTool v1.0\n\nDeveloped by: Your Name\n\n2021");
        }

        private void SettingsMenu(object sender, RoutedEventArgs e) {
            var settingsWindow = new SettingsWindow();
            settingsWindow.ShowDialog();
        }

        private void ExitMenu(object sender, RoutedEventArgs e) {
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

        #region API Methods

        private async Task TryConnect(CancellationToken cancellationToken) {
            try {
                if (ProjectsComboBox.SelectedItem == null || BranchesComboBox.SelectedItem == null) {
                    MessageBox.Show("Please select a project and a branch");
                    return;
                }

                var selectedProjectId = ((KeyValuePair<string, string>)ProjectsComboBox.SelectedItem).Key;
                var selectedBranchId = ((Branch)BranchesComboBox.SelectedItem).Id;

                var assets = await playCanvasService.GetAssetsAsync(selectedProjectId, selectedBranchId, Settings.Default.PlaycanvasApiKey, cancellationToken);
                if (assets != null) {
                    UpdateConnectionStatus(true);

                    if (textures != null && models != null) {
                        if (textures.Count > 0 && models.Count > 0) {
                            textures.Clear(); // Очищаем текущий список текстур
                            models.Clear(); // Очищаем текущий список моделей
                        }
                    }

                    var supportedAssets = assets.Where(asset => asset["file"] != null).ToList();
                    int assetCount = supportedAssets.Count;
                    int currentIndex = 1; // Счетчик для индексов

                    await Dispatcher.InvokeAsync(() => {
                        ProgressBar.Value = 0;
                        ProgressBar.Maximum = assetCount;
                        ProgressTextBlock.Text = $"0/{assetCount}";
                    });

                    var tasks = supportedAssets.Select(asset => Task.Run(async () => {
                        await ProcessAsset(asset, currentIndex++, cancellationToken);
                        await Dispatcher.InvokeAsync(() => {
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
                LogError($"Error in TryConnect: {ex}");
            }
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
            });
        }

        private async Task ProcessAsset(JToken asset, int index, CancellationToken cancellationToken) {
            if (semaphore == null) throw new InvalidOperationException("Semaphore is not initialized.");
            await getAssetsSemaphore.WaitAsync(cancellationToken);
            try {
                var file = asset["file"];
                if (file != null && file.Type == JTokenType.Object) {
                    string baseUrl = "https://playcanvas.com";
                    string? relativeUrl = file["url"]?.ToString();
                    string? fileUrl = !string.IsNullOrEmpty(relativeUrl) ? new Uri(new Uri(baseUrl), relativeUrl).ToString() : string.Empty;

                    if (string.IsNullOrEmpty(fileUrl)) {
                        throw new Exception("File URL is null or empty");
                    }

                    string? extension = Path.GetExtension(fileUrl.Split('?')[0])?.ToLowerInvariant();
                    if (string.IsNullOrEmpty(extension)) {
                        throw new Exception("Unable to determine file extension");
                    }

                    string type = asset["type"]?.ToString() ?? string.Empty;

                    if (type == "texture" && IsSupportedTextureFormat(extension)) {
                        await ProcessTextureAsset(asset, index, fileUrl, extension, cancellationToken);
                    } else if (type == "scene" && IsSupportedModelFormat(extension)) {
                        await ProcessModelAsset(asset, index, fileUrl, extension, cancellationToken);
                    } else {
                        LogError($"Unsupported asset type or format: {type} - {extension}");
                    }
                }
            } catch (Exception ex) {
                LogError($"Error in ProcessAsset: {ex}");
            } finally {
                getAssetsSemaphore.Release();
            }
        }

        private bool IsSupportedTextureFormat(string extension) {
            return supportedFormats.Contains(extension) && !excludedFormats.Contains(extension);
        }

        private bool IsSupportedModelFormat(string extension) {
            return supportedModelFormats.Contains(extension) && !excludedFormats.Contains(extension); // исправлено
        }

        private async Task ProcessTextureAsset(JToken asset, int index, string fileUrl, string extension, CancellationToken cancellationToken) {
            #region path
            if (string.IsNullOrEmpty(projectFolderPath)) {
                throw new Exception("Project folder path is null or empty");
            }

            if (string.IsNullOrEmpty(projectName)) {
                throw new Exception("Project name is null или empty");
            }

            string? pathProjectFolder = Path.Combine(projectFolderPath, projectName);
            string? pathTextureFolder = Path.Combine(pathProjectFolder, "textures");
            string? pathSourceFolder = Path.Combine(pathTextureFolder, "source");

            if (string.IsNullOrEmpty(pathSourceFolder)) {
                throw new Exception("Source folder path is null or empty");
            }

            if (!Directory.Exists(pathSourceFolder)) {
                Directory.CreateDirectory(pathSourceFolder);
            }

            string? path = Path.Combine(pathSourceFolder, asset["name"]?.ToString() ?? "Unknown");
            #endregion

            var texture = new TextureResource {
                Index = index,
                Name = asset["name"]?.ToString().Split('.')[0] ?? "Unknown",
                Size = int.TryParse(asset["file"]?["size"]?.ToString(), out var size) ? size : 0,
                Url = fileUrl.Split('?')[0],  // Удаляем параметры запроса
                Path = path,
                Extension = extension,
                Resolution = new int[2],
                ResizeResolution = new int[2],
                Status = "On Server",
                Hash = asset["file"]?["hash"]?.ToString() ?? string.Empty,
                Type = asset["type"]?.ToString() // Устанавливаем свойство Type
            };

            if (!FileExistsWithLogging(texture.Path)) {
                texture.Status = "On Server";
            } else {
                var fileInfo = new FileInfo(texture.Path);
                if (fileInfo.Length == 0) {
                    texture.Status = "Empty File";
                } else {
                    long fileSizeInBytes = fileInfo.Length;
                    long textureSizeInBytes = texture.Size;
                    double tolerance = 0.05;
                    double lowerBound = textureSizeInBytes * (1 - tolerance);
                    double upperBound = textureSizeInBytes * (1 + tolerance);

                    if (fileSizeInBytes >= lowerBound && fileSizeInBytes <= upperBound) {
                        if (!string.IsNullOrEmpty(texture.Hash) && !FileHelper.IsFileIntact(texture.Path, texture.Hash, texture.Size)) {
                            texture.Status = "Hash ERROR";
                            LogError($"{texture.Name} hash mismatch for file: {texture.Path}, expected hash: {texture.Hash}");
                        } else {
                            texture.Status = "Downloaded";
                        }
                    } else {
                        texture.Status = "Size Mismatch";
                        LogError($"{texture.Name} size Mismatch: fileSizeInBytes: {fileSizeInBytes} and textureSizeInBytes: {textureSizeInBytes}");
                    }
                }
            }

            await Dispatcher.InvokeAsync(() => textures.Add(texture));
            await UpdateTextureResolutionAsync(texture, cancellationToken);

            Dispatcher.Invoke(() => {
                ProgressBar.Value++;
                ProgressTextBlock.Text = $"{ProgressBar.Value}/{textures.Count}";
            });
        }


        private async Task ProcessMaterialAsset(JToken asset, int id, int index, string fileUrl, string extension, CancellationToken cancellationToken) {
            #region path
            if (string.IsNullOrEmpty(projectFolderPath)) {
                throw new Exception("Project folder path is null or empty");
            }

            if (string.IsNullOrEmpty(projectName)) {
                throw new Exception("Project name is null or empty");
            }

            string pathProjectFolder = Path.Combine(projectFolderPath, projectName);
            string pathMaterialFolder = Path.Combine(pathProjectFolder, "materials");

            if (string.IsNullOrEmpty(pathMaterialFolder)) {
                throw new Exception("Material folder path is null or empty");
            }

            if (!Directory.Exists(pathMaterialFolder)) {
                Directory.CreateDirectory(pathMaterialFolder);
            }
            #endregion

            var material = new MaterialResource {
                ID = id,
                Index = index,
                Name = asset["name"]?.ToString().Split('.')[0] ?? "Unknown",
                Size = int.TryParse(asset["file"]?["size"]?.ToString(), out var size) ? size : 0,
                Url = fileUrl.Split('?')[0],  // Удаляем параметры запроса
                Path = Path.Combine(pathMaterialFolder, asset["name"]?.ToString() ?? "Unknown"),
                Extension = extension,
                Status = "On Server",
                Hash = asset["file"]?["hash"]?.ToString() ?? string.Empty,
                TextureIds = []
            };

            // Предполагается, что JSON материал содержит информацию о связанных текстурах
            var json = await DownloadJsonAsync(material.Url);
            foreach (var textureId in json["textures"]) {
                material.TextureIds.Add(textureId.ToObject<int>());
            }

            await Dispatcher.InvokeAsync(() => materials.Add(material));
            await DownloadMaterialAsync(material);
        }

        private async Task ProcessModelAsset(JToken asset, int index, string fileUrl, string extension, CancellationToken cancellationToken) {
            #region path
            if (string.IsNullOrEmpty(projectFolderPath)) {
                throw new Exception("Project folder path is null or empty");
            }

            if (string.IsNullOrEmpty(projectName)) {
                throw new Exception("Project name is null или empty");
            }

            string? pathProjectFolder = Path.Combine(projectFolderPath, projectName);
            string? pathTextureFolder = Path.Combine(pathProjectFolder, "models");
            string? pathSourceFolder = Path.Combine(pathTextureFolder, "source");

            if (string.IsNullOrEmpty(pathSourceFolder)) {
                throw new Exception("Source folder path is null or empty");
            }

            if (!Directory.Exists(pathSourceFolder)) {
                Directory.CreateDirectory(pathSourceFolder);
            }

            string? path = Path.Combine(pathSourceFolder, asset["name"]?.ToString() ?? "Unknown");
            #endregion

            var model = new ModelResource {
                Index = index,
                Name = asset["name"]?.ToString().Split('.')[0] ?? "Unknown",
                Size = int.TryParse(asset["file"]?["size"]?.ToString(), out var size) ? size : 0,
                Url = fileUrl.Split('?')[0],  // Удаляем параметры запроса
                Path = path,
                Extension = extension,
                Status = "On Server",
                Hash = asset["file"]?["hash"]?.ToString() ?? string.Empty,
                UVChannels = 0 // Инициализация значения UV каналов
            };

            if (!FileExistsWithLogging(model.Path)) {
                model.Status = "On Server";
            } else {
                var fileInfo = new FileInfo(model.Path);
                if (fileInfo.Length == 0) {
                    model.Status = "Empty File";
                } else {
                    long fileSizeInBytes = fileInfo.Length;
                    long textureSizeInBytes = model.Size;
                    double tolerance = 0.05;
                    double lowerBound = textureSizeInBytes * (1 - tolerance);
                    double upperBound = textureSizeInBytes * (1 + tolerance);

                    if (fileSizeInBytes >= lowerBound && fileSizeInBytes <= upperBound) {
                        if (!string.IsNullOrEmpty(model.Hash) && !FileHelper.IsFileIntact(model.Path, model.Hash, model.Size)) {
                            model.Status = "Hash ERROR";
                            LogError($"{model.Name} hash mismatch for file: {model.Path}, expected hash: {model.Hash}");
                        } else {
                            model.Status = "Downloaded";
                        }
                    } else {
                        model.Status = "Size Mismatch";
                        LogError($"{model.Name} size Mismatch: fileSizeInBytes: {fileSizeInBytes} and textureSizeInBytes: {textureSizeInBytes}");
                    }
                }
            }

            // Определяем количество UV каналов
            var context = new AssimpContext();
            var scene = context.ImportFile(model.Path, PostProcessSteps.Triangulate | PostProcessSteps.FlipUVs | PostProcessSteps.GenerateSmoothNormals);
            var mesh = scene.Meshes.FirstOrDefault();
            if (mesh != null) {
                model.UVChannels = mesh.TextureCoordinateChannelCount;
            }

            await Dispatcher.InvokeAsync(() => models.Add(model));
        }



        private async Task LoadBranchesAsync() {
            if (ProjectsComboBox.SelectedItem != null) {
                string projectId = ((KeyValuePair<string, string>)ProjectsComboBox.SelectedItem).Key;
                try {
                    if (string.IsNullOrEmpty(projectId)) {
                        throw new Exception("Project ID is null or empty");
                    }

                    var branchesList = await playCanvasService.GetBranchesAsync(projectId, Settings.Default.PlaycanvasApiKey, cancellationTokenSource.Token);

                    Branches.Clear();
                    foreach (var branch in branchesList) {
                        Branches.Add(branch);
                    }

                    BranchesComboBox.ItemsSource = Branches;
                    BranchesComboBox.DisplayMemberPath = "Name";
                    BranchesComboBox.SelectedValuePath = "Id";

                    if (branchesList.Count > 0) {
                        BranchesComboBox.SelectedIndex = 0;
                    }
                    SaveCurrentSettings();
                } catch (Exception ex) {
                    MessageBox.Show($"Error: {ex.Message}");
                    UpdateConnectionStatus(false, ex.Message);
                }
            }
        }

        private async Task DownloadAsync(BaseResource resource) {
            if (resource is TextureResource texture) {
                await DownloadTextureAsync(texture);
            } else if (resource is ModelResource model) {
                await DownloadModelAsync(model);
            }
        }

        private async Task DownloadTextureAsync(TextureResource texture) {
            const int maxRetries = 5;
            const int delayMilliseconds = 2000;

            await downloadSemaphore.WaitAsync(); // Ожидаем освобождения слота в семафоре
            try {
                for (int attempt = 1; attempt <= maxRetries; attempt++) {
                    try {
                        texture.Status = "Downloading";
                        texture.DownloadProgress = 0;

                        if (string.IsNullOrEmpty(texture.Url) || string.IsNullOrEmpty(texture.Path)) {
                            throw new Exception("Invalid texture URL or path.");
                        }

                        using var client = new HttpClient();
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Settings.Default.PlaycanvasApiKey);

                        var response = await client.GetAsync(texture.Url, HttpCompletionOption.ResponseHeadersRead);

                        if (!response.IsSuccessStatusCode) {
                            throw new Exception($"Failed to download texture: {response.StatusCode}");
                        }

                        var totalBytes = response.Content.Headers.ContentLength ?? 0L;
                        var buffer = new byte[8192];
                        var bytesRead = 0;

                        using (var stream = await response.Content.ReadAsStreamAsync())
                        using (var fileStream = await FileHelper.OpenFileStreamWithRetryAsync(texture.Path, FileMode.Create, FileAccess.Write, FileShare.None)) {
                            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0) {
                                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                                texture.DownloadProgress = (double)fileStream.Length / totalBytes * 100;
                            }
                        }

                        var fileInfo = new FileInfo(texture.Path);
                        long fileSizeInBytes = fileInfo.Length;
                        long textureSizeInBytes = texture.Size;

                        double tolerance = 0.05;
                        double lowerBound = textureSizeInBytes * (1 - tolerance);
                        double upperBound = textureSizeInBytes * (1 + tolerance);

                        if (fileInfo.Length == 0) {
                            texture.Status = "Empty File";
                        } else if (!string.IsNullOrEmpty(texture.Hash) && FileHelper.VerifyFileHash(texture.Path, texture.Hash)) {
                            texture.Status = "Downloaded";
                        } else if (fileSizeInBytes >= lowerBound && fileSizeInBytes <= upperBound) {
                            texture.Status = "Size Mismatch";
                        } else {
                            texture.Status = "Corrupted";
                        }
                        break;
                    } catch (IOException ex) {
                        if (attempt == maxRetries) {
                            texture.Status = "Error";
                            LogError($"Error downloading texture after {maxRetries} attempts: {ex.Message}");
                        } else {
                            LogError($"Attempt {attempt} failed with IOException: {ex.Message}. Retrying in {delayMilliseconds}ms...");
                            await Task.Delay(delayMilliseconds);
                        }
                    } catch (Exception ex) {
                        texture.Status = "Error";
                        LogError($"Error downloading texture: {ex.Message}");
                        break;
                    }
                }
            } finally {
                downloadSemaphore.Release(); // Освобождаем слот в семафоре
            }
        }

        private async Task DownloadModelAsync(ModelResource model) {
            const int maxRetries = 5;
            const int delayMilliseconds = 2000;

            await downloadSemaphore.WaitAsync(); // Ожидаем освобождения слота в семафоре
            try {
                for (int attempt = 1; attempt <= maxRetries; attempt++) {
                    try {
                        model.Status = "Downloading";
                        model.DownloadProgress = 0;

                        if (string.IsNullOrEmpty(model.Url) || string.IsNullOrEmpty(model.Path)) {
                            throw new Exception("Invalid model URL or path.");
                        }

                        using var client = new HttpClient();
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Settings.Default.PlaycanvasApiKey);

                        var response = await client.GetAsync(model.Url, HttpCompletionOption.ResponseHeadersRead);

                        if (!response.IsSuccessStatusCode) {
                            throw new Exception($"Failed to download model: {response.StatusCode}");
                        }

                        var totalBytes = response.Content.Headers.ContentLength ?? 0L;
                        var buffer = new byte[8192];
                        var bytesRead = 0;

                        using (var stream = await response.Content.ReadAsStreamAsync())
                        using (var fileStream = await FileHelper.OpenFileStreamWithRetryAsync(model.Path, FileMode.Create, FileAccess.Write, FileShare.None)) {
                            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0) {
                                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                                model.DownloadProgress = (double)fileStream.Length / totalBytes * 100;
                            }
                        }

                        var fileInfo = new FileInfo(model.Path);
                        long fileSizeInBytes = fileInfo.Length;
                        long modelSizeInBytes = model.Size;

                        double tolerance = 0.05;
                        double lowerBound = modelSizeInBytes * (1 - tolerance);
                        double upperBound = modelSizeInBytes * (1 + tolerance);

                        if (fileInfo.Length == 0) {
                            model.Status = "Empty File";
                        } else if (!string.IsNullOrEmpty(model.Hash) && FileHelper.VerifyFileHash(model.Path, model.Hash)) {
                            model.Status = "Downloaded";
                        } else if (fileSizeInBytes >= lowerBound && fileSizeInBytes <= upperBound) {
                            model.Status = "Size Mismatch";
                        } else {
                            model.Status = "Corrupted";
                        }
                        break;
                    } catch (IOException ex) {
                        if (attempt == maxRetries) {
                            model.Status = "Error";
                            LogError($"Error downloading model after {maxRetries} attempts: {ex.Message}");
                        } else {
                            LogError($"Attempt {attempt} failed with IOException: {ex.Message}. Retrying in {delayMilliseconds}ms...");
                            await Task.Delay(delayMilliseconds);
                        }
                    } catch (Exception ex) {
                        model.Status = "Error";
                        LogError($"Error downloading model: {ex.Message}");
                        break;
                    }
                }
            } finally {
                downloadSemaphore.Release(); // Освобождаем слот в семафоре
            }
        }

        private async Task DownloadMaterialAsync(MaterialResource material) {
            const int maxRetries = 5;
            const int delayMilliseconds = 2000;

            await downloadSemaphore.WaitAsync(); // Ожидаем освобождения слота в семафоре
            try {
                for (int attempt = 1; attempt <= maxRetries; attempt++) {
                    try {
                        material.Status = "Downloading";
                        material.DownloadProgress = 0;

                        if (string.IsNullOrEmpty(material.Url) || string.IsNullOrEmpty(material.Path)) {
                            throw new Exception("Invalid material URL or path.");
                        }

                        using var client = new HttpClient();
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Settings.Default.PlaycanvasApiKey);

                        var response = await client.GetAsync(material.Url, HttpCompletionOption.ResponseHeadersRead);

                        if (!response.IsSuccessStatusCode) {
                            throw new Exception($"Failed to download material: {response.StatusCode}");
                        }

                        var totalBytes = response.Content.Headers.ContentLength ?? 0L;
                        var buffer = new byte[8192];
                        var bytesRead = 0;

                        using var stream = await response.Content.ReadAsStreamAsync();
                        using var fileStream = await FileHelper.OpenFileStreamWithRetryAsync(material.Path, FileMode.Create, FileAccess.Write, FileShare.None);
                        while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0) {
                            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                            material.DownloadProgress = (double)fileStream.Length / totalBytes * 100;
                        }

                        material.Status = "Downloaded";
                        break;
                    } catch (IOException ex) {
                        if (attempt == maxRetries) {
                            material.Status = "Error";
                            LogError($"Error downloading material after {maxRetries} attempts: {ex.Message}");
                        } else {
                            LogError($"Attempt {attempt} failed with IOException: {ex.Message}. Retrying in {delayMilliseconds}ms...");
                            await Task.Delay(delayMilliseconds);
                        }
                    } catch (Exception ex) {
                        material.Status = "Error";
                        LogError($"Error downloading material: {ex.Message}");
                        break;
                    }
                }
            } finally {
                downloadSemaphore.Release(); // Освобождаем слот в семафоре
            }
        }

        private static async Task<JObject> DownloadJsonAsync(string url) {
            using HttpClient client = new();
            var response = await client.GetStringAsync(url);
            return JObject.Parse(response);
        }





        private static async Task ParseMaterialJsonAsync(MaterialResource material) {
            try {
                if (material != null && !String.IsNullOrEmpty(material.MaterialJsonPath)) {
                    var jsonContent = await File.ReadAllTextAsync(path: material.MaterialJsonPath);
                    var jsonObject = JObject.Parse(jsonContent);


                    var textureIds = jsonObject["textures"]?.Values<int>().ToList();
                    if (textureIds != null) {
                        material.TextureIds.AddRange(textureIds);
                    }
                }
            } catch (Exception ex) {
                LogError($"Error parsing material JSON: {ex.Message}");
                material.Status = "Error";
            }
        }

        private async Task DownloadMaterialJsonAsync(MaterialResource material) {
            const int maxRetries = 5;
            const int delayMilliseconds = 2000;

            await downloadSemaphore.WaitAsync();
            try {
                for (int attempt = 1; attempt <= maxRetries; attempt++) {
                    try {
                        material.Status = "Downloading";
                        material.DownloadProgress = 0;

                        if (string.IsNullOrEmpty(material.MaterialJsonUrl) || string.IsNullOrEmpty(material.MaterialJsonPath)) {
                            throw new Exception("Invalid material URL or path.");
                        }

                        using var client = new HttpClient();
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Settings.Default.PlaycanvasApiKey);

                        var response = await client.GetAsync(material.MaterialJsonUrl, HttpCompletionOption.ResponseHeadersRead);

                        if (!response.IsSuccessStatusCode) {
                            throw new Exception($"Failed to download material: {response.StatusCode}");
                        }

                        var totalBytes = response.Content.Headers.ContentLength ?? 0L;
                        var buffer = new byte[8192];
                        var bytesRead = 0;

                        using (var stream = await response.Content.ReadAsStreamAsync())
                        using (var fileStream = await FileHelper.OpenFileStreamWithRetryAsync(material.MaterialJsonPath, FileMode.Create, FileAccess.Write, FileShare.None)) {
                            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0) {
                                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                                material.DownloadProgress = (double)fileStream.Length / totalBytes * 100;
                            }
                        }

                        material.Status = "Downloaded";
                        break;
                    } catch (IOException ex) {
                        if (attempt == maxRetries) {
                            material.Status = "Error";
                            LogError($"Error downloading material after {maxRetries} attempts: {ex.Message}");
                        } else {
                            LogError($"Attempt {attempt} failed with IOException: {ex.Message}. Retrying in {delayMilliseconds}ms...");
                            await Task.Delay(delayMilliseconds);
                        }
                    } catch (Exception ex) {
                        material.Status = "Error";
                        LogError($"Error downloading material: {ex.Message}");
                        break;
                    }
                }
            } finally {
                downloadSemaphore.Release();
            }
        }

        private async Task DownloadResourceAsync(BaseResource resource) {
            const int maxRetries = 5;
            const int delayMilliseconds = 2000;

            await downloadSemaphore.WaitAsync(); // Ожидаем освобождения слота в семафоре
            try {
                for (int attempt = 1; attempt <= maxRetries; attempt++) {
                    try {
                        resource.Status = "Downloading";
                        resource.DownloadProgress = 0;

                        if (string.IsNullOrEmpty(resource.Url) || string.IsNullOrEmpty(resource.Path)) {
                            throw new Exception("Invalid resource URL or path.");
                        }

                        using var client = new HttpClient();
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Settings.Default.PlaycanvasApiKey);

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

                        var fileInfo = new FileInfo(resource.Path);
                        long fileSizeInBytes = fileInfo.Length;
                        long resourceSizeInBytes = resource.Size;

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
                            LogError($"Error downloading resource after {maxRetries} attempts: {ex.Message}");
                        } else {
                            LogError($"Attempt {attempt} failed with IOException: {ex.Message}. Retrying in {delayMilliseconds}ms...");
                            await Task.Delay(delayMilliseconds);
                        }
                    } catch (Exception ex) {
                        resource.Status = "Error";
                        LogError($"Error downloading resource: {ex.Message}");
                        break;
                    }
                }
            } finally {
                downloadSemaphore.Release(); // Освобождаем слот в семафоре
            }
        }

        #endregion

        #region Helper Methods

        private static async Task UpdateTextureResolutionAsync(TextureResource texture, CancellationToken cancellationToken) {
            ArgumentNullException.ThrowIfNull(texture);

            if (string.IsNullOrEmpty(texture.Url)) {
                texture.Resolution = [0, 0];
                texture.Status = "Error";
                return;
            }

            try {
                var resolution = await ImageHelper.GetImageResolutionAsync(texture.Url, cancellationToken);
                texture.Resolution = [resolution.Width, resolution.Height];
                LogError($"Successfully retrieved resolution for {texture.Url}: {resolution.Width}x{resolution.Height}");
            } catch (Exception ex) {
                texture.Resolution = [0, 0];
                texture.Status = "Error";
                LogError($"Exception in UpdateTextureResolutionAsync for {texture.Url}: {ex.Message}");
            }
        }

        private void RecalculateTextureIndices() {
            Dispatcher.Invoke(() => {
                int index = 1;
                foreach (var texture in textures) {
                    texture.Index = index++;
                }
                TexturesDataGrid.Items.Refresh(); // Обновляем DataGrid, чтобы отразить изменения индексов
            });
        }

        private void RecalculateModelIndices() {
            Dispatcher.Invoke(() => {
                int index = 1;
                foreach (var model in models) {
                    model.Index = index++;
                }
                ModelsDataGrid.Items.Refresh(); // Обновляем DataGrid, чтобы отразить изменения индексов
            });
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
                Settings.Default.LastSelectedProjectId = ((KeyValuePair<string, string>)ProjectsComboBox.SelectedItem).Key;
            }
            if (BranchesComboBox.SelectedItem != null) {
                Settings.Default.LastSelectedBranchName = ((Branch)BranchesComboBox.SelectedItem).Name;
            }
            Settings.Default.Save();
        }

        private async void LoadLastSettings() {
            try {
                userName = Settings.Default.Username.ToLower();
                if (string.IsNullOrEmpty(userName)) {
                    throw new Exception("Username is null or empty");
                }

                var cancellationToken = new CancellationToken();

                userID = await playCanvasService.GetUserIdAsync(userName, Settings.Default.PlaycanvasApiKey, cancellationToken);
                if (string.IsNullOrEmpty(userID)) {
                    throw new Exception("User ID is null or empty");
                } else {
                    UpdateConnectionStatus(true, $"by userID: {userID}");
                }
                var projectsDict = await playCanvasService.GetProjectsAsync(userID, Settings.Default.PlaycanvasApiKey, cancellationToken);

                if (projectsDict != null && projectsDict.Count > 0) {
                    Projects.Clear();
                    foreach (var project in projectsDict) {
                        Projects.Add(project);
                    }

                    if (!string.IsNullOrEmpty(Settings.Default.LastSelectedProjectId) && projectsDict.ContainsKey(Settings.Default.LastSelectedProjectId)) {
                        ProjectsComboBox.SelectedValue = Settings.Default.LastSelectedProjectId;
                    } else {
                        ProjectsComboBox.SelectedIndex = 0;
                    }

                    if (ProjectsComboBox.SelectedItem != null) {
                        string? selectedItemString = ProjectsComboBox.SelectedItem.ToString();
                        if (selectedItemString != null)
                            projectName = System.Text.RegularExpressions.Regex.Replace(input: selectedItemString.Split(',')[1], @"[\[\]]", "").Trim();
                    }
                }

                if (ProjectsComboBox.SelectedItem != null) {
                    string projectId = ((KeyValuePair<string, string>)ProjectsComboBox.SelectedItem).Key;
                    var branchesList = await playCanvasService.GetBranchesAsync(projectId, Settings.Default.PlaycanvasApiKey, cancellationToken);

                    if (branchesList != null && branchesList.Count > 0) {
                        Branches.Clear();
                        foreach (var branch in branchesList) {
                            Branches.Add(branch);
                        }

                        BranchesComboBox.ItemsSource = Branches; // Привязываем данные к ComboBox
                        BranchesComboBox.DisplayMemberPath = "Name"; // Отображаем только имена веток
                        BranchesComboBox.SelectedValuePath = "Id"; // Сохраняем идентификаторы веток

                        if (!string.IsNullOrEmpty(Settings.Default.LastSelectedBranchName)) {
                            var selectedBranch = branchesList.FirstOrDefault(b => b.Name == Settings.Default.LastSelectedBranchName);
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

                projectFolderPath = Settings.Default.ProjectsFolderPath; // Загрузка пути к проектной папке
            } catch (Exception ex) {
                MessageBox.Show($"Error loading last settings: {ex.Message}");
            }
        }

        private static bool FileExistsWithLogging(string filePath) {
            try {
                LogError($"Checking if file exists: {filePath}");
                return File.Exists(filePath);
            } catch (Exception ex) {
                LogError($"Exception while checking file existence: {filePath}, Exception: {ex.Message}");
                return false;
            }
        }

        private static void LogInfo(string message) {
            string logFilePath = "info_log.txt";
            lock (logLock) {
                File.AppendAllText(logFilePath, $"{DateTime.Now}: {message}\n");
            }
        }

        private static void LogError(string? message) {
            string logFilePath = "error_log.txt";
            lock (logLock) {
                File.AppendAllText(logFilePath, $"{DateTime.Now}: {message}\n");
                // Вывод сообщения в консоль IDE
                System.Diagnostics.Debug.WriteLine($"{DateTime.Now}: {message}");
            }
        }

        #endregion
    }
}