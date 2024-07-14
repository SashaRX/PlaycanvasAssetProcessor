using Newtonsoft.Json.Linq;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.Wpf;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
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
using System.Windows.Controls.Primitives; // Для использования Bitmap и Rectangle
using System.Drawing;
using System.Drawing.Imaging; // Для использования ImageAttributes и ColorMatrix
using TexTool.Helpers;
using TexTool.Resources;
using Image = SixLabors.ImageSharp.Image;
using SixLabors.ImageSharp.Memory;

namespace TexTool {
    public partial class MainWindow : Window, INotifyPropertyChanged {
        private readonly ObservableCollection<TextureResource> textures = [];
        private readonly SemaphoreSlim? semaphore = new(Settings.Default.SemaphoreLimit);
        private readonly SemaphoreSlim getTexturesSemaphore;
        private readonly SemaphoreSlim downloadSemaphore;

        private string? projectFolderPath = string.Empty;
        private string? userName = string.Empty;
        private string? userID = string.Empty;
        private string? projectName = string.Empty;

        private bool? isViewerVisible = true;
        private BitmapSource? originalBitmapSource;

        private readonly List<string> supportedFormats = [".png", ".jpg", ".jpeg"];
        private readonly List<string> excludedFormats = [".hdr", ".avif"];

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

            getTexturesSemaphore = new SemaphoreSlim(Settings.Default.GetTexturesSemaphoreLimit);
            downloadSemaphore = new SemaphoreSlim(Settings.Default.DownloadSemaphoreLimit);

            projectFolderPath = Settings.Default.ProjectsFolderPath;
            UpdateConnectionStatus(false);
            TexturesDataGrid.ItemsSource = textures;
            TexturesDataGrid.LoadingRow += TexturesDataGrid_LoadingRow;
            TexturesDataGrid.Sorting += TexturesDataGrid_Sorting;
            DataContext = this;
            this.Closing += MainWindow_Closing;
            LoadLastSettings();

            RenderOptions.SetBitmapScalingMode(TexturePreviewImage, BitmapScalingMode.HighQuality);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string? propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool isDownloadButtonEnabled = false;
        public bool IsDownloadButtonEnabled {
            get => isDownloadButtonEnabled;
            set {
                isDownloadButtonEnabled = value;
                OnPropertyChanged(nameof(IsDownloadButtonEnabled));
            }
        }

        #region UI Event Handlers

        private void ProjectsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            _ = LoadBranchesAsync();
        }

        private void BranchesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            SaveCurrentSettings();
        }

        private void TexturesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (TexturesDataGrid.SelectedItem is TextureResource selectedTexture) {
                if (!string.IsNullOrEmpty(selectedTexture.Path)) {
                    var bitmapImage = new BitmapImage(new Uri(selectedTexture.Path));
                    TexturePreviewImage.Source = bitmapImage;

                    TextureNameTextBlock.Text = "Texture Name: " + selectedTexture.Name;
                    TextureResolutionTextBlock.Text = "Resolution: " + string.Join("x", selectedTexture.Resolution);
                    var sizeConverter = new SizeConverter();
                    TextureSizeTextBlock.Text = "Size: " + sizeConverter.Convert(selectedTexture.Size, targetType: null, parameter: null, culture: null);

                    // Сохраняем оригинальную текстуру и обновляем гистограмму
                    originalBitmapSource = bitmapImage.Clone();
                    UpdateHistogram(originalBitmapSource);

                    // Сбрасываем все фильтры
                    ShowOriginalImage();
                }
            }
        }

        private void TexturesDataGrid_LoadingRow(object? sender, DataGridRowEventArgs? e) {
            if (e?.Row?.DataContext is TextureResource texture) {
                if (texture.Status != null) {
                    var backgroundBrush = (System.Windows.Media.Brush?)new StatusToBackgroundConverter().Convert(texture.Status, typeof(System.Windows.Media.Brush), null, CultureInfo.InvariantCulture);
                    e.Row.Background = backgroundBrush ?? System.Windows.Media.Brushes.Transparent;
                } else {
                    e.Row.Background = System.Windows.Media.Brushes.Transparent;
                }
            }
        }


        private static BitmapSource ApplyChannelFilter(BitmapSource source, string channel) {
            using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(BitmapSourceToArray(source));

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

            return BitmapToBitmapSource(image);
        }

        private static void ProcessChannel(Image<Rgba32> image, Func<Rgba32, Rgba32> transform) {
            int width = image.Width;
            int height = image.Height;
            int numberOfChunks = Environment.ProcessorCount; // Количество потоков для параллельной обработки
            int chunkHeight = height / numberOfChunks;

            Parallel.For(0, numberOfChunks, chunk =>
            {
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

        private static byte[] BitmapSourceToArray(BitmapSource bitmapSource) {
            BmpBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
            using MemoryStream stream = new();
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

        private void FilterButton_Click(object sender, RoutedEventArgs e) {
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
                        FilterChannel(channel);
                    }
                } else {
                    // Сбрасываем фильтр, если кнопка была отжата
                    ShowOriginalImage();
                }
            }
        }

        private void FilterChannel(string channel) {
            if (TexturePreviewImage.Source is BitmapSource bitmapSource) {
                originalBitmapSource ??= bitmapSource.Clone();
                var filteredBitmap = ApplyChannelFilter(originalBitmapSource, channel);
                TexturePreviewImage.Source = filteredBitmap;

                UpdateHistogram(filteredBitmap);  // Обновление гистограммы
            }
        }

        private void ShowOriginalImage() {
            if (originalBitmapSource != null) {
                TexturePreviewImage.Source = originalBitmapSource;
                RChannelButton.IsChecked = false;
                GChannelButton.IsChecked = false;
                BChannelButton.IsChecked = false;
                AChannelButton.IsChecked = false;
                UpdateHistogram(originalBitmapSource);
            }
        }

        private void UpdateHistogram(BitmapSource bitmapSource) {
            if (bitmapSource == null) return;

            if (bitmapSource == null) return;

            var histogramModel = new PlotModel {
                //Title = "Histogram",
                //TitleFontSize = 10,
                //TitleFontWeight = OxyPlot.FontWeights.Bold
            };

            var redColor = OxyColor.FromAColor(100, OxyColors.Red);
            var greenColor = OxyColor.FromAColor(100, OxyColors.Green);
            var blueColor = OxyColor.FromAColor(100, OxyColors.Blue);

            var redSeries = new AreaSeries { Color = OxyColors.Red, Fill = redColor, StrokeThickness = 1 };
            var greenSeries = new AreaSeries { Color = OxyColors.Green, Fill = greenColor, StrokeThickness = 1 };
            var blueSeries = new AreaSeries { Color = OxyColors.Blue, Fill = blueColor, StrokeThickness = 1 };

            int[] redHistogram = new int[256];
            int[] greenHistogram = new int[256];
            int[] blueHistogram = new int[256];

            using (var image = SixLabors.ImageSharp.Image.Load<Rgba32>(BitmapSourceToArray(bitmapSource))) {
                for (int y = 0; y < image.Height; y++) {
                    Span<Rgba32> pixelRow = image.Frames.RootFrame.DangerousGetPixelRowMemory(y).Span;
                    for (int x = 0; x < pixelRow.Length; x++) {
                        var pixel = pixelRow[x];
                        redHistogram[pixel.R]++;
                        greenHistogram[pixel.G]++;
                        blueHistogram[pixel.B]++;
                    }
                }
            }

            // Применение сглаживания к данным гистограммы
            double[] smoothedRedHistogram = MovingAverage(redHistogram, 32);
            double[] smoothedGreenHistogram = MovingAverage(greenHistogram, 32);
            double[] smoothedBlueHistogram = MovingAverage(blueHistogram, 32);

            for (int i = 0; i < 256; i++) {
                redSeries.Points.Add(new DataPoint(i, smoothedRedHistogram[i]));
                greenSeries.Points.Add(new DataPoint(i, smoothedGreenHistogram[i]));
                blueSeries.Points.Add(new DataPoint(i, smoothedBlueHistogram[i]));
                redSeries.Points2.Add(new DataPoint(i, 0)); // добавляем точку с нулевым значением для закрашивания области
                greenSeries.Points2.Add(new DataPoint(i, 0)); // добавляем точку с нулевым значением для закрашивания области
                blueSeries.Points2.Add(new DataPoint(i, 0)); // добавляем точку с нулевым значением для закрашивания области
            }

            histogramModel.Series.Add(redSeries);
            histogramModel.Series.Add(greenSeries);
            histogramModel.Series.Add(blueSeries);

            histogramModel.Axes.Add(new LinearAxis {
                Position = AxisPosition.Bottom,
                IsAxisVisible = false,
                //Title = "Intensity",
                //FontSize = 9,
                //TitleFontSize = 9,
                AxislineThickness = 0.5,
                MajorGridlineThickness = 0.5,
                MinorGridlineThickness = 0.5
            });
            histogramModel.Axes.Add(new LinearAxis {
                Position = AxisPosition.Left,
                IsAxisVisible = false,
                //Title = "Count",
                //FontSize = 9,
                //TitleFontSize = 9,
                AxislineThickness = 0.5,
                MajorGridlineThickness = 0.5,
                MinorGridlineThickness = 0.5
            });

            HistogramPlotView.Model = histogramModel;
        }

        // Метод для вычисления скользящего среднего
        private static double[] MovingAverage(int[] data, int period) {
            double[] result = new double[data.Length];
            double sum = 0;

            for (int i = 0; i < data.Length; i++) {
                sum += data[i];
                if (i >= period) {
                    sum -= data[i - period];
                    result[i] = sum / period;
                } else {
                    result[i] = sum / (i + 1);
                }
            }

            return result;
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

        private async void Download(object sender, RoutedEventArgs e) {
            try {
                var selectedTextures = textures.Where(t => t.Status == "On Server" || 
                                                           t.Status == "Size Mismatch" ||
                                                           t.Status == "Corrupted" ||
                                                           t.Status == "Empty File" ||
                                                           t.Status == "Hash ERROR" ||
                                                           t.Status == "Error").OrderBy(t => t.Name).ToList();
                var downloadTasks = selectedTextures.Select(texture => DownloadTextureAsync(texture));
                await Task.WhenAll(downloadTasks);

                // Пересчитываем индексы после завершения загрузки
                RecalculateTextureIndices();
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

                    textures.Clear(); // Очищаем текущий список текстур

                    var supportedAssets = assets.Where(asset => asset["file"] != null && asset["type"]?.ToString() == "texture" && IsSupportedFormat(asset["file"]?["url"]?.ToString() ?? string.Empty)).ToList();
                    int textureCount = supportedAssets.Count;
                    int currentIndex = 1; // Счетчик для индексов

                    await Dispatcher.InvokeAsync(() => {
                        ProgressBar.Value = 0;
                        ProgressBar.Maximum = textureCount;
                        ProgressTextBlock.Text = $"0/{textureCount}";
                    });

                    var tasks = supportedAssets.Select(asset => Task.Run(async () => {
                        await ProcessAsset(asset, currentIndex++, cancellationToken);
                        await Dispatcher.InvokeAsync(() => {
                            ProgressBar.Value++;
                            ProgressTextBlock.Text = $"{ProgressBar.Value}/{textureCount}";
                        });
                    }));

                    await Task.WhenAll(tasks);

                    RecalculateTextureIndices(); // Пересчитываем индексы после обработки всех ассетов
                } else {
                    UpdateConnectionStatus(false, "Failed to connect");
                }
            } catch (Exception ex) {
                MessageBox.Show($"Error in TryConnect: {ex.Message}");
                LogError($"Error in TryConnect: {ex}");
            }
        }

        private async Task ProcessAsset(JToken asset, int index, CancellationToken cancellationToken) {
            if (semaphore == null) throw new InvalidOperationException("Semaphore is not initialized.");
            await getTexturesSemaphore.WaitAsync(cancellationToken);
            try {
                var file = asset["file"];
                if (file != null) {
                    string? fileUrl = file["url"] != null ? $"{Settings.Default.BaseUrl}{file["url"]}" : string.Empty;

                    if (string.IsNullOrEmpty(fileUrl)) {
                        throw new Exception("File URL is null or empty");
                    }

                    string? extension = Path.GetExtension(fileUrl.Split('?')[0])?.ToLowerInvariant();
                    if (string.IsNullOrEmpty(extension)) {
                        throw new Exception("Unable to determine file extension");
                    }

                    if (!IsSupportedFormat(fileUrl)) {
                        throw new Exception($"Unsupported image format: {extension}");
                    }

                    if (string.IsNullOrEmpty(projectFolderPath)) {
                        throw new Exception("Project folder path is null or empty");
                    }

                    if (string.IsNullOrEmpty(projectName)) {
                        throw new Exception("Project name is null or empty");
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

                    var texture = new TextureResource {
                        Index = index,
                        Name = asset["name"]?.ToString().Split('.')[0] ?? "Unknown",
                        Size = int.TryParse(file["size"]?.ToString(), out var size) ? size : 0,
                        Url = fileUrl.Split('?')[0],  // Удаляем параметры запроса
                        Path = Path.Combine(pathSourceFolder, asset["name"]?.ToString() ?? "Unknown"),
                        Extension = extension,
                        Resolution = [0, 0],
                        ResizeResolution = [0, 0],
                        Status = "On Server",
                        Hash = file["hash"]?.ToString() ?? string.Empty
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

                    Dispatcher.Invoke(() =>
                    {
                        ProgressBar.Value++;
                        ProgressTextBlock.Text = $"{ProgressBar.Value}/{textures.Count}";
                    });
                }
            } catch (Exception ex) {
                LogError($"Error in ProcessAsset: {ex}");
            } finally {
                getTexturesSemaphore.Release();
            }
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

        private bool IsSupportedFormat(string fileUrl) {
            string? cleanUrl = fileUrl.Split('?')[0];
            string? extension = Path.GetExtension(cleanUrl)?.ToLowerInvariant();
            if (string.IsNullOrEmpty(extension)) {
                return false;
            }
            return supportedFormats.Contains(extension) && !excludedFormats.Contains(extension);
        }

        private static readonly object logLock = new();

        private static bool FileExistsWithLogging(string filePath) {
            try {
                LogError($"Checking if file exists: {filePath}");
                return File.Exists(filePath);
            } catch (Exception ex) {
                LogError($"Exception while checking file existence: {filePath}, Exception: {ex.Message}");
                return false;
            }
        }

        private static void LogError(string message) {
            string logFilePath = "error_log.txt";
            lock (logLock) {
                File.AppendAllText(logFilePath, $"{DateTime.Now}: {message}\n");
            }
        }

        #endregion
    }
}
