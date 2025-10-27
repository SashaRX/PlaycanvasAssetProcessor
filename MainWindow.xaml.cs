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
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
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

    public enum ConnectionState {
        Disconnected,    // Не подключены - кнопка "Connect"
        UpToDate,        // Проект загружен, актуален - кнопка "Refresh" (проверить обновления)
        NeedsDownload    // Нужно скачать (первый раз ИЛИ есть обновления) - кнопка "Download"
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
        private GlobalTextureConversionSettings? globalTextureSettings; // Глобальные настройки конвертации текстур
        private ConnectionState currentConnectionState = ConnectionState.Disconnected; // Текущее состояние подключения
        private const int MaxPreviewSize = 512; // Максимальный размер изображения для превью (оптимизировано для скорости)
        private const int ThumbnailSize = 256; // Размер для быстрого превью
        private const double MinPreviewZoom = 0.1;
        private const double MaxPreviewZoom = 8.0;
        private const double MinPreviewContentHeight = 128.0;
        private const double MaxPreviewContentHeight = 512.0;
        private const double DefaultPreviewContentHeight = 300.0;
        private double currentPreviewZoom = 1.0;
        private double fitPreviewZoom = 1.0;
        private bool isKtxPreviewActive;
        private int currentMipLevel;
        private bool isUpdatingMipLevel;
        private List<KtxMipLevel>? currentKtxMipmaps;
        private readonly Dictionary<string, KtxPreviewCacheEntry> ktxPreviewCache = new(StringComparer.OrdinalIgnoreCase);
        private enum TexturePreviewSourceMode {
            Source,
            Ktx2
        }

        private TexturePreviewSourceMode currentPreviewSourceMode = TexturePreviewSourceMode.Source;
        private bool isSourcePreviewAvailable;
        private bool isKtxPreviewAvailable;
        private bool isUserPreviewSelection;
        private bool isUpdatingPreviewSourceControls;
        private bool isUserZooming;
        private bool isMiddleButtonPanning;
        private Point lastPanPoint;
        private BitmapSource? originalFileBitmapSource;
        private static readonly Regex MipLevelRegex = new(@"(?:_level|_mip|_)(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private sealed class KtxPreviewCacheEntry {
            public required DateTime LastWriteTimeUtc { get; init; }
            public required List<KtxMipLevel> Mipmaps { get; init; }
        }

        private sealed class KtxMipLevel {
            public required int Level { get; init; }
            public required BitmapSource Bitmap { get; init; }
            public required int Width { get; init; }
            public required int Height { get; init; }
        }

        private readonly HashSet<string> ignoredAssetTypes = new(StringComparer.OrdinalIgnoreCase) { "script", "wasm", "cubemap" };
        private readonly HashSet<string> reportedIgnoredAssetTypes = new(StringComparer.OrdinalIgnoreCase);
        private readonly object ignoredAssetTypesLock = new();
        private bool isBranchInitializationInProgress;

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
            UpdatePreviewContentHeight(DefaultPreviewContentHeight);
            ResetPreviewState();
            _ = InitializeOnStartup();

            // Подписка на события панели настроек конвертации
            ConversionSettingsPanel.AutoDetectRequested += ConversionSettingsPanel_AutoDetectRequested;
            ConversionSettingsPanel.ConvertRequested += ConversionSettingsPanel_ConvertRequested;

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

            // Примечание: InitializeOnStartup() уже вызывается выше (строка 144)
            // и корректно обрабатывает загрузку локальных файлов без показа MessageBox
            // Пресеты инициализируются в TextureConversionSettingsPanel
        }

        private void ConversionSettingsPanel_AutoDetectRequested(object? sender, EventArgs e) {
            var selectedTexture = TexturesDataGrid.SelectedItem as TextureResource;
            if (selectedTexture != null && !string.IsNullOrEmpty(selectedTexture.Name)) {
                bool found = ConversionSettingsPanel.AutoDetectPresetByFileName(selectedTexture.Name);
                if (found) {
                    MainWindowHelpers.LogInfo($"Auto-detected preset for '{selectedTexture.Name}'");
                } else {
                    MainWindowHelpers.LogInfo($"No matching preset found for '{selectedTexture.Name}'");
                }
            } else {
                MessageBox.Show("Please select a texture first.", "No Texture Selected", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ConversionSettingsPanel_ConvertRequested(object? sender, EventArgs e) {
            // Вызываем основную функцию конвертации
            ProcessTexturesButton_Click(sender ?? this, new RoutedEventArgs());
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

        private void ResetPreviewState() {
            currentPreviewZoom = 1.0;
            fitPreviewZoom = 1.0;
            isUserZooming = false;
            ApplyZoomTransform();
            UpdateZoomText();
            EndTexturePreviewPan();
            TexturePreviewScrollViewer?.ScrollToHome();
            TexturePreviewScrollViewer?.ScrollToLeftEnd();
            isKtxPreviewActive = false;
            currentMipLevel = 0;
            currentKtxMipmaps = null;
            originalBitmapSource = null;
            originalFileBitmapSource = null;
            currentPreviewSourceMode = TexturePreviewSourceMode.Source;
            isSourcePreviewAvailable = false;
            isKtxPreviewAvailable = false;
            isUserPreviewSelection = false;
            HideMipmapControls();
            UpdatePreviewSourceControls();
        }

        private void UpdatePreviewSourceControls() {
            if (PreviewSourceOriginalRadioButton == null || PreviewSourceKtxRadioButton == null) {
                return;
            }

            isUpdatingPreviewSourceControls = true;

            try {
                PreviewSourceOriginalRadioButton.IsEnabled = isSourcePreviewAvailable;
                PreviewSourceKtxRadioButton.IsEnabled = isKtxPreviewAvailable;

                PreviewSourceOriginalRadioButton.IsChecked = currentPreviewSourceMode == TexturePreviewSourceMode.Source;
                PreviewSourceKtxRadioButton.IsChecked = currentPreviewSourceMode == TexturePreviewSourceMode.Ktx2;
            } finally {
                isUpdatingPreviewSourceControls = false;
            }
        }

        private void TextureViewerScroll_SizeChanged(object sender, SizeChangedEventArgs e) {
            ClampPreviewContentHeight();
            RecalculateFitZoom();
        }

        private void TexturePreviewScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e) {
            if (e.WidthChanged || e.HeightChanged) {
                RecalculateFitZoom();
            }
        }

        private void PreviewHeightGridSplitter_DragDelta(object sender, DragDeltaEventArgs e) {
            if (PreviewContentRow == null) {
                return;
            }

            double desiredHeight = PreviewContentRow.ActualHeight + e.VerticalChange;
            UpdatePreviewContentHeight(desiredHeight);
            RecalculateFitZoom();
            e.Handled = true;
        }

        private void ClampPreviewContentHeight() {
            if (PreviewContentRow == null) {
                return;
            }

            double currentHeight = PreviewContentRow.ActualHeight;

            if (currentHeight <= 0) {
                if (PreviewContentRow.Height.IsAbsolute && PreviewContentRow.Height.Value > 0) {
                    currentHeight = PreviewContentRow.Height.Value;
                } else {
                    currentHeight = DefaultPreviewContentHeight;
                }
            }

            UpdatePreviewContentHeight(currentHeight);
        }

        private void UpdatePreviewContentHeight(double desiredHeight) {
            if (PreviewContentRow == null) {
                return;
            }

            double clampedHeight = Math.Clamp(desiredHeight, MinPreviewContentHeight, MaxPreviewContentHeight);
            PreviewContentRow.Height = new GridLength(clampedHeight);
        }

        private void PreviewSourceRadioButton_Checked(object sender, RoutedEventArgs e) {
            if (isUpdatingPreviewSourceControls) {
                return;
            }

            if (sender == PreviewSourceOriginalRadioButton) {
                SetPreviewSourceMode(TexturePreviewSourceMode.Source, initiatedByUser: true);
            } else if (sender == PreviewSourceKtxRadioButton) {
                SetPreviewSourceMode(TexturePreviewSourceMode.Ktx2, initiatedByUser: true);
            }
        }

        private void SetPreviewSourceMode(TexturePreviewSourceMode mode, bool initiatedByUser) {
            if (initiatedByUser) {
                isUserPreviewSelection = true;
            }

            if (mode == TexturePreviewSourceMode.Ktx2 && !isKtxPreviewAvailable) {
                UpdatePreviewSourceControls();
                return;
            }

            if (mode == TexturePreviewSourceMode.Source && !isSourcePreviewAvailable) {
                UpdatePreviewSourceControls();
                return;
            }

            currentPreviewSourceMode = mode;

            if (mode == TexturePreviewSourceMode.Source) {
                isKtxPreviewActive = false;
                HideMipmapControls();

                if (originalFileBitmapSource != null) {
                    originalBitmapSource = originalFileBitmapSource;
                    _ = UpdateHistogramAsync(originalBitmapSource);
                    ShowOriginalImage();
                } else {
                    TexturePreviewImage.Source = null;
                }
            } else if (currentKtxMipmaps != null && currentKtxMipmaps.Count > 0) {
                isKtxPreviewActive = true;
                UpdateMipmapControls(currentKtxMipmaps);
                SetCurrentMipLevel(currentMipLevel);
            }

            UpdatePreviewSourceControls();
        }

        private void ApplyZoomTransform() {
            if (TexturePreviewScaleTransform != null) {
                TexturePreviewScaleTransform.ScaleX = currentPreviewZoom;
                TexturePreviewScaleTransform.ScaleY = currentPreviewZoom;
            }

            TexturePreviewScrollViewer?.UpdateLayout();
        }

        private void UpdateZoomText() {
            if (TextureZoomTextBlock != null) {
                TextureZoomTextBlock.Text = $"Масштаб: {Math.Round(currentPreviewZoom * 100, 0)}%";
            }
        }

        private void RecalculateFitZoom(bool forceApply = false) {
            if (TexturePreviewScrollViewer == null || TexturePreviewImage?.Source is not BitmapSource bitmapSource) {
                fitPreviewZoom = 1.0;
                return;
            }

            double viewportWidth = TexturePreviewScrollViewer.ViewportWidth;
            double viewportHeight = TexturePreviewScrollViewer.ViewportHeight;

            if (double.IsNaN(viewportWidth) || viewportWidth <= 0) {
                viewportWidth = TexturePreviewScrollViewer.ActualWidth;
            }

            if (double.IsNaN(viewportHeight) || viewportHeight <= 0) {
                viewportHeight = TexturePreviewScrollViewer.ActualHeight;
            }

            if (viewportWidth <= 0 || viewportHeight <= 0 || bitmapSource.PixelWidth <= 0 || bitmapSource.PixelHeight <= 0) {
                fitPreviewZoom = 1.0;
                return;
            }

            double scaleX = viewportWidth / bitmapSource.PixelWidth;
            double scaleY = viewportHeight / bitmapSource.PixelHeight;
            double targetZoom = Math.Min(scaleX, scaleY);

            if (double.IsNaN(targetZoom) || double.IsInfinity(targetZoom)) {
                targetZoom = 1.0;
            }

            fitPreviewZoom = Math.Clamp(targetZoom, MinPreviewZoom, 1.0);
            double minZoom = Math.Max(fitPreviewZoom, MinPreviewZoom);

            if (forceApply || !isUserZooming) {
                bool zoomChanged = Math.Abs(currentPreviewZoom - minZoom) > 0.001;
                currentPreviewZoom = minZoom;

                if (zoomChanged || forceApply) {
                    ApplyZoomTransform();
                    UpdateZoomText();
                }
            } else if (currentPreviewZoom < minZoom - 0.001) {
                currentPreviewZoom = minZoom;
                ApplyZoomTransform();
                UpdateZoomText();
            }
        }

        private void HideMipmapControls() {
            if (MipmapSliderPanel != null) {
                MipmapSliderPanel.Visibility = Visibility.Collapsed;
            }

            if (MipmapLevelSlider != null) {
                isUpdatingMipLevel = true;
                MipmapLevelSlider.Value = 0;
                MipmapLevelSlider.Maximum = 0;
                MipmapLevelSlider.IsEnabled = false;
                isUpdatingMipLevel = false;
            }

            if (MipmapInfoTextBlock != null) {
                MipmapInfoTextBlock.Text = string.Empty;
            }
        }

        private void UpdateMipmapControls(IReadOnlyList<KtxMipLevel> mipmaps) {
            if (MipmapSliderPanel == null || MipmapLevelSlider == null || MipmapInfoTextBlock == null) {
                return;
            }

            isUpdatingMipLevel = true;

            try {
                MipmapSliderPanel.Visibility = Visibility.Visible;
                MipmapLevelSlider.Minimum = 0;
                MipmapLevelSlider.Maximum = Math.Max(0, mipmaps.Count - 1);
                MipmapLevelSlider.Value = 0;
                MipmapLevelSlider.IsEnabled = mipmaps.Count > 1;
                MipmapInfoTextBlock.Text = mipmaps.Count > 0
                    ? $"Мип-уровень 0 из {Math.Max(0, mipmaps.Count - 1)} — {mipmaps[0].Width}×{mipmaps[0].Height}"
                    : "Мип-уровни недоступны";
            } finally {
                isUpdatingMipLevel = false;
            }
        }

        private void UpdateMipmapInfo(KtxMipLevel mipLevel, int totalLevels) {
            if (MipmapInfoTextBlock != null) {
                int maxLevel = Math.Max(0, totalLevels - 1);
                MipmapInfoTextBlock.Text = $"Мип-уровень {mipLevel.Level} из {maxLevel} — {mipLevel.Width}×{mipLevel.Height}";
            }
        }

        private void SetCurrentMipLevel(int level, bool updateSlider = true) {
            if (currentKtxMipmaps == null || currentKtxMipmaps.Count == 0) {
                return;
            }

            int clampedLevel = Math.Clamp(level, 0, currentKtxMipmaps.Count - 1);
            currentMipLevel = clampedLevel;

            if (updateSlider && MipmapLevelSlider != null) {
                isUpdatingMipLevel = true;
                MipmapLevelSlider.Value = clampedLevel;
                isUpdatingMipLevel = false;
            }

            var mip = currentKtxMipmaps[clampedLevel];
            originalBitmapSource = mip.Bitmap.Clone();
            ShowOriginalImage();
            UpdateMipmapInfo(mip, currentKtxMipmaps.Count);
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

        private void TexturePreviewScrollViewer_PreviewMouseDown(object sender, MouseButtonEventArgs e) {
            if (e.MiddleButton == MouseButtonState.Pressed && TexturePreviewScrollViewer != null) {
                isMiddleButtonPanning = true;
                lastPanPoint = e.GetPosition(TexturePreviewScrollViewer);
                TexturePreviewScrollViewer.Cursor = Cursors.ScrollAll;
                Mouse.Capture(TexturePreviewScrollViewer);
                e.Handled = true;
            }
        }

        private void TexturePreviewScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e) {
            if (!isMiddleButtonPanning || TexturePreviewScrollViewer == null) {
                return;
            }

            Point currentPoint = e.GetPosition(TexturePreviewScrollViewer);
            double deltaX = currentPoint.X - lastPanPoint.X;
            double deltaY = currentPoint.Y - lastPanPoint.Y;

            if (Math.Abs(deltaX) > double.Epsilon) {
                TexturePreviewScrollViewer.ScrollToHorizontalOffset(TexturePreviewScrollViewer.HorizontalOffset - deltaX);
            }

            if (Math.Abs(deltaY) > double.Epsilon) {
                TexturePreviewScrollViewer.ScrollToVerticalOffset(TexturePreviewScrollViewer.VerticalOffset - deltaY);
            }

            lastPanPoint = currentPoint;
            e.Handled = true;
        }

        private void TexturePreviewScrollViewer_PreviewMouseUp(object sender, MouseButtonEventArgs e) {
            if (e.ChangedButton == MouseButton.Middle) {
                EndTexturePreviewPan();
                e.Handled = true;
            }
        }

        private void TexturePreviewScrollViewer_MouseLeave(object sender, MouseEventArgs e) {
            if (isMiddleButtonPanning) {
                EndTexturePreviewPan();
            }
        }

        private void TexturePreviewScrollViewer_LostMouseCapture(object sender, MouseEventArgs e) {
            if (isMiddleButtonPanning) {
                EndTexturePreviewPan();
            }
        }

        private void EndTexturePreviewPan() {
            if (!isMiddleButtonPanning) {
                return;
            }

            isMiddleButtonPanning = false;
            Mouse.Capture(null);

            if (TexturePreviewScrollViewer != null) {
                TexturePreviewScrollViewer.Cursor = Cursors.Arrow;
            }
        }

        private void TexturePreviewScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
            double zoomFactor = e.Delta > 0 ? 1.1 : 0.9;
            double minZoom = Math.Max(fitPreviewZoom, MinPreviewZoom);
            double newZoom = Math.Clamp(currentPreviewZoom * zoomFactor, minZoom, MaxPreviewZoom);

            if (Math.Abs(newZoom - currentPreviewZoom) < 0.001) {
                return;
            }

            currentPreviewZoom = newZoom;
            isUserZooming = true;
            ApplyZoomTransform();
            UpdateZoomText();
            e.Handled = true;
        }

        private void MipmapLevelSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            if (isUpdatingMipLevel || !isKtxPreviewActive) {
                return;
            }

            int newLevel = (int)Math.Round(e.NewValue);
            if (newLevel != currentMipLevel) {
                SetCurrentMipLevel(newLevel, updateSlider: false);
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
                    isUserZooming = false;
                });

                _ = Dispatcher.BeginInvoke(new Action(() => RecalculateFitZoom(forceApply: true)), DispatcherPriority.Background);
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
            var items = new List<string>();
            foreach (object? value in Enum.GetValues(typeof(T))) {
                items.Add(value.ToString() ?? "");
            }
            comboBox.ItemsSource = items;
        }

        #endregion

        #region Models

        private void ModelsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (ModelsDataGrid.SelectedItem is ModelResource selectedModel) {
                if (!string.IsNullOrEmpty(selectedModel.Path)) {
                    if (selectedModel.Status == "Downloaded") { // Если модель уже загружена
                                                                // Загружаем модель во вьюпорт (3D просмотрщик)
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
            const int width = 512;
            const int height = 512;

            BitmapSource primaryUv = CreateUvBitmapSource(mesh, 0, width, height);
            BitmapSource secondaryUv = CreateUvBitmapSource(mesh, 1, width, height);

            Dispatcher.Invoke(() => {
                UVImage.Source = primaryUv;
                UVImage2.Source = secondaryUv;
            });
        }

        private static BitmapSource CreateUvBitmapSource(Mesh mesh, int channelIndex, int width, int height) {
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
                                points[i] = new Point(uv.X * width, (1 - uv.Y) * height);
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
                        TextureOperationsGroupBox.Visibility = Visibility.Visible;
                        break;
                    case "Models":
                        ShowModelViewer();
                        TextureOperationsGroupBox.Visibility = Visibility.Collapsed;
                        break;
                    case "Materials":
                        ShowMaterialViewer();
                        TextureOperationsGroupBox.Visibility = Visibility.Collapsed;
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
                    // Если JSON-файл не найден, просто логируем (без MessageBox)
                    MainWindowHelpers.LogInfo($"No local data found for project '{projectName}'. User can connect to server to download.");
                }

                // Обновляем ветки для выбранного проекта
                isBranchInitializationInProgress = true;
                try {
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
                } finally {
                    isBranchInitializationInProgress = false;
                }

                SaveCurrentSettings();

                // Проверяем состояние проекта если уже подключены
                if (currentConnectionState != ConnectionState.Disconnected) {
                    await CheckProjectState();
                }
            }
        }

        private async void BranchesComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e) {
            SaveCurrentSettings();

            if (isBranchInitializationInProgress) {
                return;
            }

            // Проверяем состояние проекта если уже подключены
            if (currentConnectionState != ConnectionState.Disconnected) {
                await CheckProjectState();
            }
        }

        private async void TexturesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            // Update selection count in central control box
            UpdateSelectedTexturesCount();

            // Отменяем предыдущую загрузку, если она еще выполняется
            textureLoadCancellation?.Cancel();
            textureLoadCancellation = new CancellationTokenSource();
            CancellationToken cancellationToken = textureLoadCancellation.Token;

            if (TexturesDataGrid.SelectedItem is TextureResource selectedTexture) {
                ResetPreviewState();
                TexturePreviewImage.Source = null;

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

                        Task<bool> ktxPreviewTask = TryLoadKtx2PreviewAsync(selectedTexture, cancellationToken);

                        await LoadSourcePreviewAsync(selectedTexture, cancellationToken);

                        bool ktxLoaded = await ktxPreviewTask;

                        if (!ktxLoaded) {
                            await Dispatcher.InvokeAsync(() => {
                                if (cancellationToken.IsCancellationRequested) {
                                    return;
                                }

                                isKtxPreviewAvailable = false;

                                if (!isUserPreviewSelection && currentPreviewSourceMode == TexturePreviewSourceMode.Ktx2) {
                                    SetPreviewSourceMode(TexturePreviewSourceMode.Source, initiatedByUser: false);
                                } else {
                                    UpdatePreviewSourceControls();
                                }
                            });
                        }
                    } catch (OperationCanceledException) {
                        // Загрузка была отменена - это нормально
                    } catch (Exception ex) {
                        MainWindowHelpers.LogError($"Error loading texture {selectedTexture.Name}: {ex.Message}");
                    }
                }
            }
        }

        private async Task<bool> TryLoadKtx2PreviewAsync(TextureResource selectedTexture, CancellationToken cancellationToken) {
            string? ktxPath = GetExistingKtx2Path(selectedTexture.Path);
            if (ktxPath == null) {
                return false;
            }

            try {
                List<KtxMipLevel> mipmaps = await LoadKtx2MipmapsAsync(ktxPath, cancellationToken);
                if (mipmaps.Count == 0 || cancellationToken.IsCancellationRequested) {
                    return false;
                }

                await Dispatcher.InvokeAsync(() => {
                    if (cancellationToken.IsCancellationRequested) {
                        return;
                    }

                    currentKtxMipmaps = mipmaps;
                    currentMipLevel = 0;
                    isKtxPreviewAvailable = true;

                    if (!isUserPreviewSelection || currentPreviewSourceMode == TexturePreviewSourceMode.Ktx2) {
                        SetPreviewSourceMode(TexturePreviewSourceMode.Ktx2, initiatedByUser: false);
                    } else {
                        UpdatePreviewSourceControls();
                    }
                });

                return true;
            } catch (OperationCanceledException) {
                return false;
            } catch (Exception ex) {
                logger.Warn(ex, $"Не удалось загрузить предпросмотр KTX2: {ktxPath}");
                return false;
            }
        }

        private async Task LoadSourcePreviewAsync(TextureResource selectedTexture, CancellationToken cancellationToken) {
            string? texturePath = selectedTexture.Path;
            if (string.IsNullOrEmpty(texturePath)) {
                return;
            }

            if (imageCache.TryGetValue(texturePath, out BitmapImage? cachedImage)) {
                await Dispatcher.InvokeAsync(() => {
                    if (cancellationToken.IsCancellationRequested) {
                        return;
                    }

                    originalFileBitmapSource = cachedImage;
                    isSourcePreviewAvailable = true;

                    if (currentPreviewSourceMode == TexturePreviewSourceMode.Source) {
                        originalBitmapSource = cachedImage;
                        _ = UpdateHistogramAsync(originalBitmapSource);
                        ShowOriginalImage();
                    }

                    UpdatePreviewSourceControls();
                });

                return;
            }

            BitmapImage? thumbnailImage = LoadOptimizedImage(texturePath, ThumbnailSize);
            if (thumbnailImage == null) {
                MainWindowHelpers.LogInfo($"Error loading thumbnail for texture: {selectedTexture.Name}");
                return;
            }

            await Dispatcher.InvokeAsync(() => {
                if (cancellationToken.IsCancellationRequested) {
                    return;
                }

                originalFileBitmapSource = thumbnailImage;
                isSourcePreviewAvailable = true;

                if (currentPreviewSourceMode == TexturePreviewSourceMode.Source) {
                    originalBitmapSource = thumbnailImage;
                    _ = UpdateHistogramAsync(originalBitmapSource);
                    ShowOriginalImage();
                }

                UpdatePreviewSourceControls();
            });

            if (cancellationToken.IsCancellationRequested) {
                return;
            }

            try {
                await Task.Run(() => {
                    if (cancellationToken.IsCancellationRequested) {
                        return;
                    }

                    BitmapImage? bitmapImage = LoadOptimizedImage(texturePath, MaxPreviewSize);

                    if (bitmapImage == null || cancellationToken.IsCancellationRequested) {
                        return;
                    }

                    Dispatcher.Invoke(() => {
                        if (cancellationToken.IsCancellationRequested) {
                            return;
                        }

                        if (!imageCache.ContainsKey(texturePath)) {
                            imageCache[texturePath] = bitmapImage;

                            if (imageCache.Count > 50) {
                                string firstKey = imageCache.Keys.First();
                                imageCache.Remove(firstKey);
                            }
                        }

                        originalFileBitmapSource = bitmapImage;
                        isSourcePreviewAvailable = true;

                        if (currentPreviewSourceMode == TexturePreviewSourceMode.Source) {
                            originalBitmapSource = bitmapImage;
                            _ = UpdateHistogramAsync(originalBitmapSource);
                            ShowOriginalImage();
                        }

                        UpdatePreviewSourceControls();
                    });
                }, cancellationToken);
            } catch (OperationCanceledException) {
                // Прерывание загрузки допустимо при смене выбора
            }
        }

        private string? GetExistingKtx2Path(string? sourcePath) {
            if (string.IsNullOrEmpty(sourcePath)) {
                return null;
            }

            string? directory = Path.GetDirectoryName(sourcePath);
            if (string.IsNullOrEmpty(directory)) {
                return null;
            }

            string baseName = Path.GetFileNameWithoutExtension(sourcePath);
            string normalizedBaseName = TextureResource.ExtractBaseTextureName(baseName);

            string directPath = Path.Combine(directory, baseName + ".ktx2");
            if (File.Exists(directPath)) {
                return directPath;
            }

            if (!string.IsNullOrWhiteSpace(normalizedBaseName) &&
                !normalizedBaseName.Equals(baseName, StringComparison.OrdinalIgnoreCase)) {
                string normalizedDirectPath = Path.Combine(directory, normalizedBaseName + ".ktx2");
                if (File.Exists(normalizedDirectPath)) {
                    return normalizedDirectPath;
                }
            }

            string? sameDirectoryMatch = TryFindKtx2InDirectory(directory, baseName, normalizedBaseName, SearchOption.TopDirectoryOnly);
            if (!string.IsNullOrEmpty(sameDirectoryMatch)) {
                return sameDirectoryMatch;
            }

            string? defaultOutputRoot = ResolveDefaultKtxSearchRoot(directory);
            if (!string.IsNullOrEmpty(defaultOutputRoot)) {
                string? outputMatch = TryFindKtx2InDirectory(defaultOutputRoot, baseName, normalizedBaseName, SearchOption.AllDirectories);
                if (!string.IsNullOrEmpty(outputMatch)) {
                    return outputMatch;
                }
            }

            return null;
        }

        private string? ResolveDefaultKtxSearchRoot(string sourceDirectory) {
            try {
                globalTextureSettings ??= TextureConversionSettingsManager.LoadSettings();
            } catch (Exception ex) {
                logger.Debug(ex, "Не удалось загрузить настройки конвертации для определения каталога KTX2.");
                return null;
            }

            string? configuredDirectory = globalTextureSettings?.DefaultOutputDirectory;
            if (string.IsNullOrWhiteSpace(configuredDirectory)) {
                return null;
            }

            List<string> candidates = new();

            if (Path.IsPathRooted(configuredDirectory)) {
                candidates.Add(configuredDirectory);
            } else {
                candidates.Add(Path.Combine(sourceDirectory, configuredDirectory));

                if (!string.IsNullOrEmpty(projectFolderPath)) {
                    candidates.Add(Path.Combine(projectFolderPath!, configuredDirectory));
                }
            }

            foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase)) {
                if (Directory.Exists(candidate)) {
                    return candidate;
                }
            }

            return null;
        }

        private string? TryFindKtx2InDirectory(string directory, string baseName, string normalizedBaseName, SearchOption searchOption) {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) {
                return null;
            }

            string? bestMatch = null;
            DateTime bestTime = DateTime.MinValue;
            int bestScore = -1;
            string? newestFile = null;
            DateTime newestTime = DateTime.MinValue;

            try {
                foreach (string file in Directory.EnumerateFiles(directory, "*.ktx2", searchOption)) {
                    DateTime writeTime = File.GetLastWriteTimeUtc(file);

                    if (writeTime > newestTime) {
                        newestTime = writeTime;
                        newestFile = file;
                    }

                    int score = GetKtxMatchScore(Path.GetFileNameWithoutExtension(file), baseName, normalizedBaseName);
                    if (score < 0) {
                        continue;
                    }

                    if (score > bestScore || (score == bestScore && writeTime > bestTime)) {
                        bestScore = score;
                        bestTime = writeTime;
                        bestMatch = file;
                    }
                }
            } catch (UnauthorizedAccessException ex) {
                logger.Debug(ex, $"Нет доступа к каталогу {directory} для поиска KTX2.");
                return null;
            } catch (DirectoryNotFoundException) {
                return null;
            } catch (IOException ex) {
                logger.Debug(ex, $"Ошибка при сканировании каталога {directory} для поиска KTX2.");
                return null;
            }

            return bestMatch ?? newestFile;
        }

        private static int GetKtxMatchScore(string candidateName, string baseName, string normalizedBaseName) {
            if (string.IsNullOrWhiteSpace(candidateName)) {
                return -1;
            }

            static bool ContainsOrdinal(string source, string value) =>
                source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;

            if (candidateName.Equals(baseName, StringComparison.OrdinalIgnoreCase)) {
                return 500;
            }

            if (!string.IsNullOrWhiteSpace(normalizedBaseName) &&
                candidateName.Equals(normalizedBaseName, StringComparison.OrdinalIgnoreCase)) {
                return 450;
            }

            if (candidateName.StartsWith(baseName, StringComparison.OrdinalIgnoreCase)) {
                return 400;
            }

            if (!string.IsNullOrWhiteSpace(normalizedBaseName) &&
                candidateName.StartsWith(normalizedBaseName, StringComparison.OrdinalIgnoreCase)) {
                return 350;
            }

            if (!string.IsNullOrWhiteSpace(normalizedBaseName) && ContainsOrdinal(candidateName, normalizedBaseName)) {
                return 250;
            }

            if (ContainsOrdinal(candidateName, baseName)) {
                return 200;
            }

            return -1;
        }

        private async Task<List<KtxMipLevel>> LoadKtx2MipmapsAsync(string ktxPath, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();

            FileInfo fileInfo = new(ktxPath);
            DateTime lastWriteTimeUtc = fileInfo.LastWriteTimeUtc;

            if (ktxPreviewCache.TryGetValue(ktxPath, out KtxPreviewCacheEntry? cacheEntry) && cacheEntry.LastWriteTimeUtc == lastWriteTimeUtc) {
                return cacheEntry.Mipmaps;
            }

            return await Task.Run(() => ExtractKtxMipmaps(ktxPath, lastWriteTimeUtc, cancellationToken), cancellationToken);
        }

        private List<KtxMipLevel> ExtractKtxMipmaps(string ktxPath, DateTime lastWriteTimeUtc, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();

            string basisuPath = GetBasisuExecutablePath();
            string tempDirectory = Path.Combine(Path.GetTempPath(), "PlaycanvasAssetProcessor", "Preview", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDirectory);

            try {
                if (!string.IsNullOrEmpty(Path.GetDirectoryName(basisuPath)) && !File.Exists(basisuPath)) {
                    throw new FileNotFoundException($"Не удалось найти исполняемый файл basisu по пути '{basisuPath}'. Укажите корректный путь в настройках конвертации текстур.", basisuPath);
                }

                ProcessStartInfo startInfo = new() {
                    FileName = basisuPath,
                    Arguments = $"-ktx2_to_png -output_path \"{tempDirectory}\" \"{ktxPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using Process process = new() { StartInfo = startInfo };
                try {
                    if (!process.Start()) {
                        throw new InvalidOperationException("Не удалось запустить basisu для извлечения предпросмотра KTX2.");
                    }
                } catch (Win32Exception ex) {
                    throw new InvalidOperationException("Не удалось запустить basisu для извлечения предпросмотра KTX2. Проверьте путь к утилите в настройках и наличие прав на запуск.", ex);
                } catch (Exception ex) {
                    throw new InvalidOperationException("Не удалось запустить basisu для извлечения предпросмотра KTX2.", ex);
                }

                string standardOutput = process.StandardOutput.ReadToEnd();
                string standardError = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0) {
                    logger.Warn($"basisu завершился с кодом {process.ExitCode} при обработке {ktxPath}. StdOut: {standardOutput}. StdErr: {standardError}");
                    throw new InvalidOperationException($"basisu завершился с кодом {process.ExitCode} при подготовке предпросмотра.");
                }

                cancellationToken.ThrowIfCancellationRequested();

                string[] pngFiles = Directory.GetFiles(tempDirectory, "*.png", SearchOption.TopDirectoryOnly);
                if (pngFiles.Length == 0) {
                    throw new InvalidOperationException("basisu не сгенерировал PNG-файлы для предпросмотра KTX2.");
                }

                List<KtxMipLevel> mipmaps = pngFiles
                    .Select(path => new { Path = path, Level = ParseMipLevelFromFile(path) })
                    .OrderBy(entry => entry.Level)
                    .Select(entry => CreateMipLevel(entry.Path, entry.Level))
                    .ToList();

                ktxPreviewCache[ktxPath] = new KtxPreviewCacheEntry {
                    LastWriteTimeUtc = lastWriteTimeUtc,
                    Mipmaps = mipmaps
                };

                return mipmaps;
            } finally {
                try {
                    Directory.Delete(tempDirectory, true);
                } catch (Exception cleanupEx) {
                    logger.Debug(cleanupEx, $"Не удалось удалить временную директорию предпросмотра: {tempDirectory}");
                }
            }
        }

        private static int ParseMipLevelFromFile(string filePath) {
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            Match match = MipLevelRegex.Match(fileName);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int level)) {
                return level;
            }

            Match fallback = Regex.Match(fileName, @"(\d+)$");
            if (fallback.Success && int.TryParse(fallback.Value, out int fallbackLevel)) {
                return fallbackLevel;
            }

            return 0;
        }

        private KtxMipLevel CreateMipLevel(string filePath, int level) {
            BitmapImage bitmap = new();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(filePath);
            bitmap.EndInit();
            bitmap.Freeze();

            return new KtxMipLevel {
                Level = level,
                Bitmap = bitmap,
                Width = bitmap.PixelWidth,
                Height = bitmap.PixelHeight
            };
        }

        private string GetBasisuExecutablePath() {
            if (globalTextureSettings == null) {
                globalTextureSettings = TextureConversionSettingsManager.LoadSettings();
            }

            return string.IsNullOrWhiteSpace(globalTextureSettings?.BasisUExecutablePath)
                ? "basisu"
                : globalTextureSettings!.BasisUExecutablePath;
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
                // Initialize conversion settings for the texture if not already set
                if (string.IsNullOrEmpty(texture.CompressionFormat)) {
                    InitializeTextureConversionSettings(texture);
                }

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

        private async void Connect(object? sender, RoutedEventArgs? e) {
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

                        // Проверяем состояние проекта (скачан ли, нужно ли обновить)
                        await CheckProjectState();
                    } else {
                        throw new Exception("Project list is empty");
                    }
                } catch (Exception ex) {
                    MessageBox.Show($"Error: {ex.Message}");
                    UpdateConnectionButton(ConnectionState.Disconnected);
                }
            }
        }

        /// <summary>
        /// Проверяет состояние проекта (скачан ли, есть ли обновления)
        /// </summary>
        private async Task CheckProjectState() {
            try {
                if (string.IsNullOrEmpty(projectFolderPath) || string.IsNullOrEmpty(projectName)) {
                    UpdateConnectionButton(ConnectionState.NeedsDownload);
                    return;
                }

                // Проверяем наличие assets_list.json
                string assetsListPath = Path.Combine(projectFolderPath, "assets_list.json");

                if (!File.Exists(assetsListPath)) {
                    // Проект не скачан - нужна загрузка
                    MainWindowHelpers.LogInfo("Project not downloaded yet - assets_list.json not found");
                    UpdateConnectionButton(ConnectionState.NeedsDownload);
                    return;
                }

                // Проект скачан, проверяем hash для определения обновлений
                MainWindowHelpers.LogInfo("Project found, checking for updates...");
                bool hasUpdates = await CheckForUpdates();

                if (hasUpdates) {
                    // Есть обновления - нужна загрузка
                    UpdateConnectionButton(ConnectionState.NeedsDownload);
                } else {
                    // Проект актуален - можно только обновить вручную
                    UpdateConnectionButton(ConnectionState.UpToDate);
                }
            } catch (Exception ex) {
                MainWindowHelpers.LogError($"Error checking project state: {ex.Message}");
                UpdateConnectionButton(ConnectionState.NeedsDownload);
            }
        }

        /// <summary>
        /// Проверяет наличие обновлений на сервере
        /// </summary>
        /// <returns>true если есть обновления, false если все актуально</returns>
        private async Task<bool> CheckForUpdates() {
            try {
                if (ProjectsComboBox.SelectedItem == null || BranchesComboBox.SelectedItem == null) {
                    return false;
                }

                string selectedProjectId = ((KeyValuePair<string, string>)ProjectsComboBox.SelectedItem).Key;
                string selectedBranchId = ((Branch)BranchesComboBox.SelectedItem).Id;
                string assetsListPath = Path.Combine(projectFolderPath ?? "", "assets_list.json");

                if (!File.Exists(assetsListPath)) {
                    return true; // Файл не существует = нужно скачать
                }

                // Получаем локальный JSON
                string localJson = await File.ReadAllTextAsync(assetsListPath);
                JToken? localData = JsonConvert.DeserializeObject<JToken>(localJson);

                // Получаем серверный JSON
                JArray serverData = await playCanvasService.GetAssetsAsync(selectedProjectId, selectedBranchId, AppSettings.Default.PlaycanvasApiKey, CancellationToken.None);

                // Сравниваем hash или количество ассетов
                string localHash = ComputeHash(localJson);
                string serverHash = ComputeHash(serverData.ToString());

                bool hasChanges = localHash != serverHash;

                if (hasChanges) {
                    MainWindowHelpers.LogInfo($"Project has updates: local hash {localHash.Substring(0, 8)}... != server hash {serverHash.Substring(0, 8)}...");
                } else {
                    MainWindowHelpers.LogInfo("Project is up to date");
                }

                return hasChanges;
            } catch (Exception ex) {
                MainWindowHelpers.LogError($"Error checking for updates: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Вычисляет MD5 hash для строки (для сравнения JSON)
        /// </summary>
        private string ComputeHash(string input) {
            using var md5 = System.Security.Cryptography.MD5.Create();
            byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(input);
            byte[] hashBytes = md5.ComputeHash(inputBytes);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
        }

        private async Task LoadBranchesAsync(string projectId, CancellationToken cancellationToken) {
            try {
                isBranchInitializationInProgress = true;

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
            } finally {
                isBranchInitializationInProgress = false;
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

            MaterialResource? material = (sender as Hyperlink)?.DataContext as MaterialResource?? MaterialsDataGrid.SelectedItem as MaterialResource;

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
                        string jsonFilePath = Path.Combine(projectFolderPath, "assets_list.json");
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
                string jsonFilePath = Path.Combine(projectFolderPath, "assets_list.json");

                if (!Directory.Exists(projectFolderPath)) {
                    Directory.CreateDirectory(projectFolderPath);
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

                if (!string.IsNullOrEmpty(type) && ignoredAssetTypes.Contains(type)) {
                    lock (ignoredAssetTypesLock) {
                        if (reportedIgnoredAssetTypes.Add(type)) {
                            MainWindowHelpers.LogInfo($"Asset type '{type}' is currently ignored (stub handler).");
                        }
                    }
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

                string jsonFilePath = Path.Combine(projectFolderPath, "assets_list.json");
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

        /// <summary>
        /// Обновляет текст и состояние динамической кнопки подключения
        /// </summary>
        private void UpdateConnectionButton(ConnectionState newState) {
            currentConnectionState = newState;

            Dispatcher.Invoke(() => {
                bool hasSelection = ProjectsComboBox.SelectedItem != null && BranchesComboBox.SelectedItem != null;

                switch (currentConnectionState) {
                    case ConnectionState.Disconnected:
                        DynamicConnectionButton.Content = "Connect";
                        DynamicConnectionButton.ToolTip = "Connect to PlayCanvas and load projects";
                        DynamicConnectionButton.IsEnabled = true;
                        DynamicConnectionButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 240, 240)); // Grey
                        break;

                    case ConnectionState.UpToDate:
                        DynamicConnectionButton.Content = "Refresh";
                        DynamicConnectionButton.ToolTip = "Check for updates from PlayCanvas server";
                        DynamicConnectionButton.IsEnabled = hasSelection;
                        DynamicConnectionButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(173, 216, 230)); // Light blue
                        break;

                    case ConnectionState.NeedsDownload:
                        DynamicConnectionButton.Content = "Download";
                        DynamicConnectionButton.ToolTip = "Download assets from PlayCanvas (list + files)";
                        DynamicConnectionButton.IsEnabled = hasSelection;
                        DynamicConnectionButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(144, 238, 144)); // Light green
                        break;
                }
            });
        }

        /// <summary>
        /// Обработчик клика по динамической кнопке подключения
        /// </summary>
        private async void DynamicConnectionButton_Click(object sender, RoutedEventArgs e) {
            switch (currentConnectionState) {
                case ConnectionState.Disconnected:
                    // Подключаемся к PlayCanvas и загружаем список проектов
                    ConnectToPlayCanvas();
                    break;

                case ConnectionState.UpToDate:
                    // Проверяем наличие обновлений на сервере
                    await RefreshFromServer();
                    break;

                case ConnectionState.NeedsDownload:
                    // Скачиваем список ассетов + файлы
                    await DownloadFromServer();
                    break;
            }
        }

        /// <summary>
        /// Подключение к PlayCanvas - загружает список проектов и веток
        /// </summary>
        private void ConnectToPlayCanvas() {
            // Вызываем существующий метод Connect
            Connect(null, null);
        }

        /// <summary>
        /// Проверяет наличие обновлений на сервере (Refresh button)
        /// Сравнивает hash локального assets_list.json с серверным
        /// </summary>
        private async Task RefreshFromServer() {
            try {
                DynamicConnectionButton.IsEnabled = false;

                bool hasUpdates = await CheckForUpdates();

                if (hasUpdates) {
                    // Есть обновления - переключаем на кнопку Download
                    UpdateConnectionButton(ConnectionState.NeedsDownload);
                    MessageBox.Show("Updates available! Click Download to get them.", "Updates Found", MessageBoxButton.OK, MessageBoxImage.Information);
                } else {
                    // Обновлений нет
                    MessageBox.Show("Project is up to date!", "No Updates", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            } catch (Exception ex) {
                MessageBox.Show($"Error checking for updates: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                MainWindowHelpers.LogError($"Error in RefreshFromServer: {ex}");
            } finally {
                DynamicConnectionButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// Скачивает список ассетов с сервера + загружает все файлы (Download button)
        /// </summary>
        private async Task DownloadFromServer() {
            try {
                CancelButton.IsEnabled = true;
                DynamicConnectionButton.IsEnabled = false;

                if (cancellationTokenSource != null) {
                    // Загружаем список ассетов (assets_list.json) с сервера
                    await TryConnect(cancellationTokenSource.Token);

                    // Теперь скачиваем файлы (текстуры, модели, материалы)
                    Download(null, null);

                    // После успешной загрузки переключаем на Refresh
                    UpdateConnectionButton(ConnectionState.UpToDate);
                }
            } catch (Exception ex) {
                MessageBox.Show($"Error downloading: {ex.Message}", "Download Error", MessageBoxButton.OK, MessageBoxImage.Error);
                MainWindowHelpers.LogError($"Error in DownloadFromServer: {ex}");
            } finally {
                CancelButton.IsEnabled = false;
                DynamicConnectionButton.IsEnabled = true;
            }
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

        /// <summary>
        /// Инициализация при запуске программы - проверяет локальные файлы БЕЗ подключения к серверу
        /// </summary>
        private async Task InitializeOnStartup() {
            try {
                MainWindowHelpers.LogInfo("=== Initializing on startup ===");

                // Загружаем сохраненные настройки
                string lastProjectId = AppSettings.Default.LastSelectedProjectId;
                string lastBranchName = AppSettings.Default.LastSelectedBranchName;

                if (string.IsNullOrEmpty(lastProjectId)) {
                    // Нет сохраненного проекта - показываем кнопку Connect
                    MainWindowHelpers.LogInfo("No saved project found - showing Connect button");
                    UpdateConnectionButton(ConnectionState.Disconnected);
                    return;
                }

                // Определяем имя проекта из настроек (нужно получить из сохраненного ID)
                // Временно используем ProjectsFolderPath для поиска
                string projectsRoot = AppSettings.Default.ProjectsFolderPath;
                if (string.IsNullOrEmpty(projectsRoot) || !Directory.Exists(projectsRoot)) {
                    MainWindowHelpers.LogInfo("Projects folder not found - showing Connect button");
                    UpdateConnectionButton(ConnectionState.Disconnected);
                    return;
                }

                // Ищем папки проектов
                var projectFolders = Directory.GetDirectories(projectsRoot);
                foreach (var folder in projectFolders) {
                    string folderName = Path.GetFileName(folder);
                    string assetsListPath = Path.Combine(folder, "assets_list.json");

                    if (File.Exists(assetsListPath)) {
                        // Нашли локальный проект!
                        MainWindowHelpers.LogInfo($"Found local project: {folderName}");

                        projectName = folderName;
                        projectFolderPath = folder;

                        // Загружаем данные локально
                        bool loaded = await LoadAssetsFromJsonFileAsync();

                        if (loaded) {
                            MainWindowHelpers.LogInfo($"Local project loaded successfully: {projectName}");

                            // Показываем пользователю что проект загружен локально
                            UpdateConnectionStatus(true, $"Loaded offline: {projectName}");

                            // Добавляем проект в ComboBox (как минимум локальное имя)
                            // Реальный ID будет получен при подключении
                            if (!Projects.Any(p => p.Value == projectName)) {
                                Projects.Add(new KeyValuePair<string, string>(lastProjectId, projectName));
                                ProjectsComboBox.SelectedValue = lastProjectId;
                            }

                            // Устанавливаем состояние "Refresh" - проект загружен, можно проверить обновления
                            UpdateConnectionButton(ConnectionState.UpToDate);
                            return;
                        }
                    }
                }

                // Не нашли локальных файлов
                MainWindowHelpers.LogInfo("No local project files found - showing Connect button");
                UpdateConnectionButton(ConnectionState.Disconnected);

            } catch (Exception ex) {
                MainWindowHelpers.LogError($"Error during startup initialization: {ex.Message}");
                UpdateConnectionButton(ConnectionState.Disconnected);
            }
        }

        /// <summary>
        /// Загружает последние настройки и подключается к серверу (старый метод)
        /// </summary>
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

        private void ConversionSettingsExpander_Expanded(object sender, RoutedEventArgs e) {
            // Settings expanded - could save state if needed
        }

        private void ConversionSettingsExpander_Collapsed(object sender, RoutedEventArgs e) {
            // Settings collapsed - could save state if needed
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
            var textureType = TextureResource.DetermineTextureType(texture.Name ?? "");
            var profile = TextureConversion.Core.MipGenerationProfile.CreateDefault(
                MapTextureTypeToCore(textureType));

            var compression = TextureConversion.Core.CompressionSettings.CreateETC1SDefault();
            var compressionData = TextureConversion.Settings.CompressionSettingsData.FromCompressionSettings(compression);
            var mipProfileData = TextureConversion.Settings.MipProfileSettings.FromMipGenerationProfile(profile);

            ConversionSettingsPanel.LoadSettings(compressionData, mipProfileData, true, false);
            // LoadPresets removed - presets are now managed globally through PresetManager

            texture.CompressionFormat = compression.CompressionFormat.ToString();

            // Auto-detect preset by filename if not already set
            if (string.IsNullOrEmpty(texture.PresetName)) {
                var presetManager = new TextureConversion.Settings.PresetManager();
                var matchedPreset = presetManager.FindPresetByFileName(texture.Name ?? "");
                texture.PresetName = matchedPreset?.Name ?? "";
            }
        }

        // Initialize compression format and preset for texture without updating UI panel
        private void InitializeTextureConversionSettings(TextureResource texture) {
            var textureType = TextureResource.DetermineTextureType(texture.Name ?? "");
            var profile = TextureConversion.Core.MipGenerationProfile.CreateDefault(
                MapTextureTypeToCore(textureType));
            var compression = TextureConversion.Core.CompressionSettings.CreateETC1SDefault();

            texture.CompressionFormat = compression.CompressionFormat.ToString();

            // Auto-detect preset by filename if not already set
            if (string.IsNullOrEmpty(texture.PresetName)) {
                var presetManager = new TextureConversion.Settings.PresetManager();
                var matchedPreset = presetManager.FindPresetByFileName(texture.Name ?? "");
                texture.PresetName = matchedPreset?.Name ?? "";
            }

            // Check if compressed file (.ktx2 or .basis) already exists and set CompressedSize
            if (!string.IsNullOrEmpty(texture.Path) && File.Exists(texture.Path)) {
                var sourceDir = Path.GetDirectoryName(texture.Path);
                var sourceFileName = Path.GetFileNameWithoutExtension(texture.Path);

                if (!string.IsNullOrEmpty(sourceDir) && !string.IsNullOrEmpty(sourceFileName)) {
                    // Check for .ktx2 file first
                    var ktx2Path = Path.Combine(sourceDir, sourceFileName + ".ktx2");
                    if (File.Exists(ktx2Path)) {
                        var fileInfo = new FileInfo(ktx2Path);
                        texture.CompressedSize = fileInfo.Length;
                    } else {
                        // Check for .basis file as fallback
                        var basisPath = Path.Combine(sourceDir, sourceFileName + ".basis");
                        if (File.Exists(basisPath)) {
                            var fileInfo = new FileInfo(basisPath);
                            texture.CompressedSize = fileInfo.Length;
                        } else {
                            texture.CompressedSize = 0;
                        }
                    }
                }
            }
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

                // Disable buttons during processing
                ProcessTexturesButton.IsEnabled = false;
                UploadTexturesButton.IsEnabled = false;

                int successCount = 0;
                int errorCount = 0;
                var errorMessages = new List<string>();

                ProgressBar.Maximum = texturesToProcess.Count;
                ProgressBar.Value = 0;

                var toktxPath = string.IsNullOrWhiteSpace(globalTextureSettings.ToktxExecutablePath)
                    ? "toktx"
                    : globalTextureSettings.ToktxExecutablePath;

                var pipeline = new TextureConversion.Pipeline.TextureConversionPipeline(toktxPath);

                foreach (var texture in texturesToProcess) {
                    try {
                        if (string.IsNullOrEmpty(texture.Path)) {
                            var errorMsg = $"{texture.Name ?? "Unknown"}: Empty file path";
                            MainWindowHelpers.LogError($"Skipping texture with empty path: {texture.Name ?? "Unknown"}");
                            errorMessages.Add(errorMsg);
                            errorCount++;
                            continue;
                        }

                        ProgressTextBlock.Text = $"Processing {texture.Name}...";
                        MainWindowHelpers.LogInfo($"Processing texture: {texture.Name}");

                        // Автоматически определяем тип текстуры по имени файла
                        var textureType = TextureResource.DetermineTextureType(texture.Name ?? "");
                        texture.TextureType = textureType; // Сохраняем определённый тип

                        // Создаём профиль мипмапов на основе типа текстуры
                        var mipProfile = TextureConversion.Core.MipGenerationProfile.CreateDefault(
                            MapTextureTypeToCore(textureType));

                        // Используем глобальные настройки из TextureConversionSettingsPanel
                        var compressionSettings = ConversionSettingsPanel.GetCompressionSettings()
                            .ToCompressionSettings(globalTextureSettings!); // Already checked for null above

                        // Save converted file in the same directory as source file
                        var sourceDir = Path.GetDirectoryName(texture.Path) ?? Environment.CurrentDirectory;
                        // Use Path.GetFileNameWithoutExtension from the actual file path, not the display name
                        var sourceFileName = Path.GetFileNameWithoutExtension(texture.Path);
                        var extension = compressionSettings.OutputFormat == TextureConversion.Core.OutputFormat.KTX2
                            ? ".ktx2"
                            : ".basis";
                        var outputPath = Path.Combine(sourceDir, sourceFileName + extension);

                        MainWindowHelpers.LogInfo($"=== CONVERSION START ===");
                        MainWindowHelpers.LogInfo($"  Texture Name: {texture.Name}");
                        MainWindowHelpers.LogInfo($"  Source Path: {texture.Path}");
                        MainWindowHelpers.LogInfo($"  Source Dir: {sourceDir}");
                        MainWindowHelpers.LogInfo($"  Source FileName: {sourceFileName}");
                        MainWindowHelpers.LogInfo($"  Extension: {extension}");
                        MainWindowHelpers.LogInfo($"  Expected Output: {outputPath}");
                        MainWindowHelpers.LogInfo($"========================");

                        var mipmapOutputDir = ConversionSettingsPanel.SaveSeparateMipmaps
                            ? Path.Combine(sourceDir, "mipmaps", sourceFileName)
                            : null;

                        // Получаем настройки Toksvig из панели настроек
                        var toksvigSettings = ConversionSettingsPanel.GetToksvigSettings();

                        var result = await pipeline.ConvertTextureAsync(
                            texture.Path,
                            outputPath,
                            mipProfile,
                            compressionSettings,
                            toksvigSettings,
                            ConversionSettingsPanel.SaveSeparateMipmaps,
                            mipmapOutputDir
                        );

                        if (result.Success) {
                            texture.CompressionFormat = compressionSettings.CompressionFormat.ToString();
                            texture.MipmapCount = result.MipLevels;
                            texture.Status = "Converted";

                            // Сохраняем информацию о Toksvig коррекции
                            if (result.ToksvigApplied) {
                                texture.ToksvigEnabled = true;
                                texture.NormalMapPath = result.NormalMapUsed;
                            }

                            // Сохраняем имя пресета, если оно не установлено
                            if (string.IsNullOrEmpty(texture.PresetName) || texture.PresetName == "(Auto)") {
                                // Пытаемся определить пресет по имени файла
                                var presetManager = new TextureConversion.Settings.PresetManager();
                                var matchedPreset = presetManager.FindPresetByFileName(texture.Name ?? "");
                                if (matchedPreset != null) {
                                    texture.PresetName = matchedPreset.Name;
                                } else {
                                    // Используем имя выбранного в панели пресета
                                    var selectedPreset = ConversionSettingsPanel.PresetName;
                                    texture.PresetName = string.IsNullOrEmpty(selectedPreset) ? "(Custom)" : selectedPreset;
                                }
                            }

                            // Записываем размер сжатого файла
                            MainWindowHelpers.LogInfo($"=== CHECKING OUTPUT FILE ===");
                            MainWindowHelpers.LogInfo($"  Expected path: {outputPath}");
                            MainWindowHelpers.LogInfo($"  File.Exists check...");

                            // Ждем немного, чтобы файл точно был записан на диск
                            await Task.Delay(300);

                            // Обновляем FileInfo для получения актуальных данных
                            bool fileFound = false;
                            long fileSize = 0;
                            string actualPath = outputPath;

                            // Проверка 1: Ожидаемый путь
                            if (File.Exists(outputPath)) {
                                var fileInfo = new FileInfo(outputPath);
                                fileInfo.Refresh(); // Обновляем информацию о файле
                                fileSize = fileInfo.Length;
                                fileFound = true;
                                MainWindowHelpers.LogInfo($"  ✓ Found at expected path! Size: {fileSize} bytes");
                            } else {
                                MainWindowHelpers.LogInfo($"  ✗ NOT found at expected path");

                                // Проверка 2: Поиск всех файлов в директории с нужным расширением
                                MainWindowHelpers.LogInfo($"  Searching directory: {sourceDir}");
                                if (Directory.Exists(sourceDir)) {
                                    var allFiles = Directory.GetFiles(sourceDir, $"*{extension}");
                                    MainWindowHelpers.LogInfo($"  Found {allFiles.Length} {extension} files in directory");

                                    foreach (var file in allFiles) {
                                        MainWindowHelpers.LogInfo($"    - {Path.GetFileName(file)} ({new FileInfo(file).Length} bytes)");
                                    }

                                    // Ищем файл по базовому имени (без учёта регистра)
                                    var matchingFile = allFiles.FirstOrDefault(f =>
                                        Path.GetFileNameWithoutExtension(f).Equals(sourceFileName, StringComparison.OrdinalIgnoreCase));

                                    if (matchingFile != null) {
                                        actualPath = matchingFile;
                                        var fileInfo = new FileInfo(matchingFile);
                                        fileSize = fileInfo.Length;
                                        fileFound = true;
                                        MainWindowHelpers.LogInfo($"  ✓ Found matching file: {matchingFile} ({fileSize} bytes)");
                                    }
                                }
                            }

                            if (fileFound && fileSize > 0) {
                                texture.CompressedSize = fileSize;
                                MainWindowHelpers.LogInfo($"  ✓ CompressedSize set to: {texture.CompressedSize} bytes ({fileSize / 1024.0:F1} KB)");
                                MainWindowHelpers.LogInfo($"✓ Successfully converted {texture.Name}");
                                MainWindowHelpers.LogInfo($"  Mipmaps: {result.MipLevels}, Size: {fileSize / 1024.0:F1} KB, Path: {actualPath}");
                            } else {
                                MainWindowHelpers.LogError($"  ✗ OUTPUT FILE NOT FOUND OR EMPTY!");
                                MainWindowHelpers.LogError($"  Expected: {outputPath}");
                                MainWindowHelpers.LogError($"  Please check basisu conversion output");
                                texture.CompressedSize = 0;
                            }
                            MainWindowHelpers.LogInfo($"============================");

                            successCount++;
                        } else {
                            texture.Status = "Error";
                            errorCount++;
                            var errorMsg = $"{texture.Name}: {result.Error ?? "Unknown error"}";
                            errorMessages.Add(errorMsg);
                            MainWindowHelpers.LogError($"✗ Failed to convert {texture.Name}: {result.Error}");
                        }
                    } catch (Exception ex) {
                        texture.Status = "Error";
                        errorCount++;
                        var errorMsg = $"{texture.Name}: {ex.Message}";
                        errorMessages.Add(errorMsg);
                        MainWindowHelpers.LogError($"✗ Exception processing {texture.Name}: {ex.Message}\n{ex.StackTrace}");
                    }

                    ProgressBar.Value++;
                }

                // Force DataGrid to refresh and display updated CompressedSize values
                TexturesDataGrid.Items.Refresh();

                ProgressTextBlock.Text = $"Completed: {successCount} success, {errorCount} errors";

                // Build result message
                var resultMessage = $"Processing completed!\n\nSuccess: {successCount}\nErrors: {errorCount}";

                if (errorCount > 0 && errorMessages.Count > 0) {
                    resultMessage += "\n\nError details:";
                    var errorsToShow = errorMessages.Take(10).ToList();
                    foreach (var error in errorsToShow) {
                        resultMessage += $"\n• {error}";
                    }
                    if (errorMessages.Count > 10) {
                        resultMessage += $"\n... and {errorMessages.Count - 10} more errors (see log file for details)";
                    }
                } else if (successCount > 0) {
                    resultMessage += "\n\nConverted files saved next to source images.";
                }

                MessageBox.Show(
                    resultMessage,
                    "Processing Complete",
                    MessageBoxButton.OK,
                    successCount == texturesToProcess.Count ? MessageBoxImage.Information : MessageBoxImage.Warning
                );

            } catch (Exception ex) {
                MainWindowHelpers.LogError($"Error during batch processing: {ex.Message}");
                MessageBox.Show($"Error during processing:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            } finally {
                // Принудительно обновляем DataGrid для отображения изменений
                TexturesDataGrid.Items.Refresh();

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

        private void AutoDetectAllButton_Click(object sender, RoutedEventArgs e) {
            var texturesToProcess = ProcessAllCheckBox.IsChecked == true
                ? textures.ToList()
                : TexturesDataGrid.SelectedItems.Cast<TextureResource>().ToList();

            if (texturesToProcess.Count == 0) {
                MessageBox.Show("Please select textures first or enable 'Process All'.",
                    "No Textures Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int matchedCount = 0;
            int notMatchedCount = 0;

            foreach (var texture in texturesToProcess) {
                if (!string.IsNullOrEmpty(texture.Name)) {
                    bool found = ConversionSettingsPanel.AutoDetectPresetByFileName(texture.Name);
                    if (found) {
                        // Get the detected preset name and store it
                        var presetManager = new TextureConversion.Settings.PresetManager();
                        var matchedPreset = presetManager.FindPresetByFileName(texture.Name);
                        if (matchedPreset != null) {
                            texture.PresetName = matchedPreset.Name;
                            matchedCount++;
                        }
                    } else {
                        notMatchedCount++;
                    }
                }
            }

            // Refresh DataGrid to show updated PresetName values
            TexturesDataGrid.Items.Refresh();

            MainWindowHelpers.LogInfo($"Auto-detect completed: {matchedCount} matched, {notMatchedCount} not matched");
            MessageBox.Show($"Auto-detect completed:\n\n" +
                          $"✓ Matched: {matchedCount}\n" +
                          $"✗ Not matched: {notMatchedCount}",
                "Auto-detect Results", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // Context menu handlers for texture rows
        private void ProcessSelectedTextures_Click(object sender, RoutedEventArgs e) {
            ProcessTexturesButton_Click(sender, e);
        }

        private void UploadTexture_Click(object sender, RoutedEventArgs e) {
            UploadTexturesButton_Click(sender, e);
        }

        private void OpenFileLocation_Click(object sender, RoutedEventArgs e) {
            if (TexturesDataGrid.SelectedItem is TextureResource texture && !string.IsNullOrEmpty(texture.Path)) {
                try {
                    var directory = System.IO.Path.GetDirectoryName(texture.Path);
                    if (!string.IsNullOrEmpty(directory) && System.IO.Directory.Exists(directory)) {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                            FileName = directory,
                            UseShellExecute = true
                        });
                    } else {
                        MessageBox.Show("Directory not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                } catch (Exception ex) {
                    MessageBox.Show($"Failed to open file location: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CopyTexturePath_Click(object sender, RoutedEventArgs e) {
            if (TexturesDataGrid.SelectedItem is TextureResource texture && !string.IsNullOrEmpty(texture.Path)) {
                try {
                    System.Windows.Clipboard.SetText(texture.Path);
                    MessageBox.Show("Path copied to clipboard!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                } catch (Exception ex) {
                    MessageBox.Show($"Failed to copy path: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void RefreshPreview_Click(object sender, RoutedEventArgs e) {
            if (TexturesDataGrid.SelectedItem is TextureResource texture) {
                // Trigger a refresh by re-selecting the texture
                TexturesDataGrid_SelectionChanged(TexturesDataGrid, null!);
            }
        }

        #endregion
    }
}
