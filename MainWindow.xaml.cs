using AssetProcessor.Exceptions;
using AssetProcessor.Helpers;
using AssetProcessor.Infrastructure.Enums;
using AssetProcessor.Resources;
using AssetProcessor.Services;
using AssetProcessor.Services.Models;
using AssetProcessor.Settings;
using AssetProcessor.ViewModels;
using Assimp;
using HelixToolkit.Wpf;
using CommunityToolkit.Mvvm.Input;
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
using AssetProcessor.Windows;
using Newtonsoft.Json.Linq;

namespace AssetProcessor {
    public partial class MainWindow : Window, INotifyPropertyChanged {
        // ��������� ������ � MainViewModel - ������� ������������� ����������
        // viewModel.Textures, viewModel.Models, viewModel.Materials, Assets ������ �������� ����� viewModel

        private readonly List<string> supportedFormats = [".png", ".jpg", ".jpeg"];
        private readonly List<string> excludedFormats = [".hdr", ".avif"];
        private readonly List<string> supportedModelFormats = [".fbx", ".obj", ".glb"];

        private readonly SemaphoreSlim getAssetsSemaphore;
        private readonly SemaphoreSlim downloadSemaphore;
        private bool? isViewerVisible = true;
        private bool isUpdatingChannelButtons = false; // Flag to prevent recursive button updates
        private CancellationTokenSource cancellationTokenSource = new();
        private readonly IPlayCanvasService playCanvasService;
        private readonly IHistogramCoordinator histogramCoordinator;
        private readonly ITextureChannelService textureChannelService;
        private readonly ITexturePreviewService texturePreviewService;
        private readonly IProjectSelectionService projectSelectionService;
        private readonly ILogService logService;
        private readonly ILocalCacheService localCacheService;
        private readonly IProjectAssetService projectAssetService;
        private readonly IPreviewRendererCoordinator previewRendererCoordinator;
        private readonly IAssetResourceService assetResourceService;
        private readonly IConnectionStateService connectionStateService;
        private readonly IAssetJsonParserService assetJsonParserService;
        private readonly IORMTextureService ormTextureService;
        private readonly IFileStatusScannerService fileStatusScannerService;
        private Dictionary<int, string> folderPaths = new();
        private CancellationTokenSource? textureLoadCancellation; // ����� ������ ��� �������� �������
        private GlobalTextureConversionSettings? globalTextureSettings; // ���������� ��������� ����������� �������
        private ConversionSettingsManager? conversionSettingsManager; // �������� ���������� �����������
        private const int MaxPreviewSize = 2048; // ������������ ������ ����������� ��� ������ (������� �������� ��� ���������� ���������)
        private const int ThumbnailSize = 512; // ������ ��� �������� ������ (��������� ��� ������ ����������)
        private const double MinPreviewColumnWidth = 256.0;
        private const double MaxPreviewColumnWidth = 512.0;
        private const double MinPreviewContentHeight = 128.0;
        private const double MaxPreviewContentHeight = double.PositiveInfinity;
        private const double DefaultPreviewContentHeight = 300.0;
        private static readonly TextureConversion.Settings.PresetManager cachedPresetManager = new(); // Кэшированный PresetManager для ускорения загрузки данных
        private readonly ConcurrentDictionary<string, object> texturesBeingChecked = new(StringComparer.OrdinalIgnoreCase); // ������������ �������, ��� ������� ��� �������� �������� CompressedSize
        private readonly Dictionary<(DataGrid, string), ListSortDirection> _sortDirections = new(); // Track sort directions per DataGrid+column
        private FileSystemWatcher? projectFileWatcher; // Monitors project folder for file deletions
        private readonly ConcurrentQueue<string> pendingDeletedPaths = new(); // Queue of paths to process
        private int fileWatcherRefreshPending; // 0 = no refresh pending, 1 = refresh scheduled
        private string? selectedORMSubGroupName; // Имя выбранной ORM подгруппы для визуального выделения
        private string? ProjectFolderPath => projectSelectionService.ProjectFolderPath;
        private string? ProjectName => projectSelectionService.ProjectName;
        private string? UserId => projectSelectionService.UserId;
        private string? UserName => projectSelectionService.UserName;

        // Projects � Branches ������ � MainViewModel - ������� ������������� ����������

        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// �������� �������������� PlayCanvas API ���� �� ��������.
        /// ������������ ��� ���� ������� PlayCanvas API ��� ���������� ������ � ������������� ����������.
        /// </summary>
        private static string? GetDecryptedApiKey() {
            if (!AppSettings.Default.TryGetDecryptedPlaycanvasApiKey(out string? apiKey)) {
                logger.Error("�� ������� ������������ PlayCanvas API ���� �� ��������");
                return null;
            }
            return apiKey;
        }

        private readonly MainViewModel viewModel;

        public MainViewModel ViewModel { get; }

        public MainWindow(
            IPlayCanvasService playCanvasService,
            IHistogramCoordinator histogramCoordinator,
            ITextureChannelService textureChannelService,
            ITexturePreviewService texturePreviewService,
            IProjectSelectionService projectSelectionService,
            ILogService logService,
            ILocalCacheService localCacheService,
            IProjectAssetService projectAssetService,
            IPreviewRendererCoordinator previewRendererCoordinator,
            IAssetResourceService assetResourceService,
            IConnectionStateService connectionStateService,
            IAssetJsonParserService assetJsonParserService,
            IORMTextureService ormTextureService,
            IFileStatusScannerService fileStatusScannerService,
            MainViewModel viewModel) {
            this.playCanvasService = playCanvasService ?? throw new ArgumentNullException(nameof(playCanvasService));
            this.histogramCoordinator = histogramCoordinator ?? throw new ArgumentNullException(nameof(histogramCoordinator));
            this.textureChannelService = textureChannelService ?? throw new ArgumentNullException(nameof(textureChannelService));
            this.texturePreviewService = texturePreviewService ?? throw new ArgumentNullException(nameof(texturePreviewService));
            this.projectSelectionService = projectSelectionService ?? throw new ArgumentNullException(nameof(projectSelectionService));
            this.logService = logService ?? throw new ArgumentNullException(nameof(logService));
            this.localCacheService = localCacheService ?? throw new ArgumentNullException(nameof(localCacheService));
            this.projectAssetService = projectAssetService ?? throw new ArgumentNullException(nameof(projectAssetService));
            this.previewRendererCoordinator = previewRendererCoordinator ?? throw new ArgumentNullException(nameof(previewRendererCoordinator));
            this.assetResourceService = assetResourceService ?? throw new ArgumentNullException(nameof(assetResourceService));
            this.connectionStateService = connectionStateService ?? throw new ArgumentNullException(nameof(connectionStateService));
            this.assetJsonParserService = assetJsonParserService ?? throw new ArgumentNullException(nameof(assetJsonParserService));
            this.ormTextureService = ormTextureService ?? throw new ArgumentNullException(nameof(ormTextureService));
            this.fileStatusScannerService = fileStatusScannerService ?? throw new ArgumentNullException(nameof(fileStatusScannerService));
            this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            ViewModel = this.viewModel;

            InitializeComponent();
            UpdatePreviewContentHeight(DefaultPreviewContentHeight);
            ResetPreviewState();
            _ = InitializeOnStartup();

            // ������������� ConversionSettings
            InitializeConversionSettings();

            viewModel.ConversionSettingsProvider = ConversionSettingsPanel;
            viewModel.TextureProcessingCompleted += ViewModel_TextureProcessingCompleted;
            viewModel.TexturePreviewLoaded += ViewModel_TexturePreviewLoaded;

            // �������� �� ������� ������ �������� �����������
            ConversionSettingsPanel.AutoDetectRequested += ConversionSettingsPanel_AutoDetectRequested;
            ConversionSettingsPanel.ConvertRequested += ConversionSettingsPanel_ConvertRequested;

            // ����������� ������ ���������� � ����������� � ������ � �������
            VersionTextBlock.Text = $"v{VersionHelper.GetVersionString()}";

            // ���������� ComboBox ��� Color Channel
            ComboBoxHelper.PopulateComboBox<ColorChannel>(MaterialAOColorChannelComboBox);
            ComboBoxHelper.PopulateComboBox<ColorChannel>(MaterialDiffuseColorChannelComboBox);
            ComboBoxHelper.PopulateComboBox<ColorChannel>(MaterialSpecularColorChannelComboBox);
            ComboBoxHelper.PopulateComboBox<ColorChannel>(MaterialMetalnessColorChannelComboBox);
            ComboBoxHelper.PopulateComboBox<ColorChannel>(MaterialGlossinessColorChannelComboBox);

            // ���������� ComboBox ��� UV Channel
            ComboBoxHelper.PopulateComboBox<UVChannel>(MaterialDiffuseUVChannelComboBox);
            ComboBoxHelper.PopulateComboBox<UVChannel>(MaterialSpecularUVChannelComboBox);
            ComboBoxHelper.PopulateComboBox<UVChannel>(MaterialNormalUVChannelComboBox);
            ComboBoxHelper.PopulateComboBox<UVChannel>(MaterialAOUVChannelComboBox);

            LoadModel(path: MainWindowHelpers.MODEL_PATH);

            getAssetsSemaphore = new SemaphoreSlim(AppSettings.Default.GetTexturesSemaphoreLimit);
            downloadSemaphore = new SemaphoreSlim(AppSettings.Default.DownloadSemaphoreLimit);

            projectSelectionService.InitializeProjectsFolder(AppSettings.Default.ProjectsFolderPath);
            UpdateConnectionStatus(false);

            TexturesDataGrid.LoadingRow += TexturesDataGrid_LoadingRow;
            TexturesDataGrid.Sorting += TexturesDataGrid_Sorting;

            viewModel.ProjectSelectionChanged += ViewModel_ProjectSelectionChanged;
            viewModel.BranchSelectionChanged += ViewModel_BranchSelectionChanged;

            this.Closing += MainWindow_Closing;
            //LoadLastSettings();

            RenderOptions.SetBitmapScalingMode(UVImage, BitmapScalingMode.HighQuality);
            RenderOptions.SetBitmapScalingMode(UVImage2, BitmapScalingMode.HighQuality);

            // Setup D3D11 render loop
            this.Loaded += MainWindow_Loaded;
            CompositionTarget.Rendering += OnD3D11Rendering;

            // ����������: InitializeOnStartup() ��� ���������� ���� (������ 144)
            // � ��������� ������������ �������� ��������� ������ ��� ������ MessageBox
            // ������� ���������������� � TextureConversionSettingsPanel
        }

        private void ConversionSettingsPanel_AutoDetectRequested(object? sender, EventArgs e) {
            viewModel.AutoDetectPresetsCommand.Execute(TexturesDataGrid.SelectedItems);
        }

        private async void ConversionSettingsPanel_ConvertRequested(object? sender, EventArgs e) {
            if (viewModel.ProcessTexturesCommand is IAsyncRelayCommand<IList?> command) {
                await command.ExecuteAsync(TexturesDataGrid.SelectedItems);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string? propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Имя выбранной ORM подгруппы для визуального выделения в DataGrid
        /// </summary>
        public string? SelectedORMSubGroupName {
            get => selectedORMSubGroupName;
            set {
                if (selectedORMSubGroupName != value) {
                    selectedORMSubGroupName = value;
                    OnPropertyChanged(nameof(SelectedORMSubGroupName));
                }
            }
        }

        private void HandlePreviewRendererChanged(bool useD3D11) {
            _ = ApplyRendererPreferenceAsync(useD3D11);
        }

        private async void ViewModel_ProjectSelectionChanged(object? sender, ProjectSelectionChangedEventArgs e) {
            await HandleProjectSelectionChangedAsync();
        }

        private async void ViewModel_BranchSelectionChanged(object? sender, BranchSelectionChangedEventArgs e) {
            await HandleBranchSelectionChangedAsync();
        }

        private Task ApplyRendererPreferenceAsync(bool useD3D11) {
            TexturePreviewContext context = CreateTexturePreviewContext();
            return previewRendererCoordinator.SwitchRendererAsync(context, useD3D11);
        }

        private TexturePreviewContext CreateTexturePreviewContext() {
            return new TexturePreviewContext {
                D3D11TextureViewer = D3D11TextureViewer,
                WpfTexturePreviewImage = WpfTexturePreviewImage,
                IsKtx2Loading = IsKtx2Loading,
                LoadKtx2ToD3D11ViewerAsync = LoadKtx2ToD3D11ViewerAsync,
                IsSRGBTexture = IsSRGBTexture,
                LoadTextureToD3D11Viewer = LoadTextureToD3D11Viewer,
                PrepareForWpfDisplay = PrepareForWPFDisplay,
                ShowMipmapControls = ShowMipmapControls,
                LogInfo = message => logger.Info(message),
                LogError = (exception, message) => logger.Error(exception, message),
                LogWarn = message => logger.Warn(message)
            };
        }


        


        


        #region UI Event Handlers

        private void ShowTextureViewer() {
            TextureViewerScroll.Visibility = Visibility.Visible;
            ModelViewerScroll.Visibility = Visibility.Collapsed;
            MaterialViewerScroll.Visibility = Visibility.Collapsed;
        }

        private void ShowModelViewer() {
            TextureViewerScroll.Visibility = Visibility.Collapsed;
            ModelViewerScroll.Visibility = Visibility.Visible;
            MaterialViewerScroll.Visibility = Visibility.Collapsed;
        }

        private void ShowMaterialViewer() {
            TextureViewerScroll.Visibility = Visibility.Collapsed;
            ModelViewerScroll.Visibility = Visibility.Collapsed;
            MaterialViewerScroll.Visibility = Visibility.Visible;
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (tabControl.SelectedItem is TabItem selectedTab) {
                switch (selectedTab.Header.ToString()) {
                    case "Textures":
                        ShowTextureViewer();
                        TextureOperationsGroupBox.Visibility = Visibility.Visible;
                        ModelExportGroupBox.Visibility = Visibility.Collapsed;
                        break;
                    case "Models":
                        ShowModelViewer();
                        TextureOperationsGroupBox.Visibility = Visibility.Collapsed;
                        ModelExportGroupBox.Visibility = Visibility.Visible;
                        UpdateModelExportCounts();
                        break;
                    case "Materials":
                        ShowMaterialViewer();
                        TextureOperationsGroupBox.Visibility = Visibility.Collapsed;
                        ModelExportGroupBox.Visibility = Visibility.Collapsed;
                        break;
                }
            }
        }

        private async Task HandleProjectSelectionChangedAsync() {
            if (projectSelectionService.IsProjectInitializationInProgress) {
                logService.LogInfo("Skipping project selection - initialization in progress");
                return;
            }

            if (ProjectsComboBox.SelectedItem is not KeyValuePair<string, string> selectedProject) {
                return;
            }

            logService.LogInfo("Calling LoadAssetsFromJsonFileAsync after project selection");
            bool jsonLoaded = await LoadAssetsFromJsonFileAsync();
            if (!jsonLoaded) {
                logService.LogInfo($"No local data found for project '{ProjectName}'. User can connect to server to download.");
            }

            SaveCurrentSettings();

            if (connectionStateService.CurrentState != ConnectionState.Disconnected) {
                await CheckProjectState();
            }
        }

        private async Task HandleBranchSelectionChangedAsync() {
            SaveCurrentSettings();

            if (projectSelectionService.IsBranchInitializationInProgress) {
                return;
            }

            if (connectionStateService.CurrentState != ConnectionState.Disconnected) {
                await CheckProjectState();
            }
        }

        private async void TexturesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            // Cancel any pending texture load immediately
            textureLoadCancellation?.Cancel();
            textureLoadCancellation = new CancellationTokenSource();
            CancellationToken cancellationToken = textureLoadCancellation.Token;

            // Снимаем выделение с ORM подгруппы при выборе обычной строки
            if (TexturesDataGrid.SelectedItem != null) {
                SelectedORMSubGroupName = null;
            }

            // Update selection count and command state (lightweight, no delay needed)
            UpdateSelectedTexturesCount();
            viewModel.SelectedTexture = TexturesDataGrid.SelectedItem as TextureResource;
            viewModel.ProcessTexturesCommand.NotifyCanExecuteChanged();

            // Debounce: wait before starting heavy texture loading
            // This prevents loading textures during rapid scrolling
            try {
                await Task.Delay(150, cancellationToken);
            } catch (OperationCanceledException) {
                return; // Another selection happened, abort this one
            }

            logService.LogInfo($"[TexturesDataGrid_SelectionChanged] Processing after debounce");

            // Check if selected item is an ORM texture (virtual texture for packing)
            if (TexturesDataGrid.SelectedItem is ORMTextureResource ormTexture) {
                logService.LogInfo($"[TexturesDataGrid_SelectionChanged] Selected ORM texture: {ormTexture.Name}");

                // Hide conversion settings panel, show ORM panel
                if (ConversionSettingsExpander != null) {
                    ConversionSettingsExpander.Visibility = Visibility.Collapsed;
                }

                logService.LogInfo($"[TexturesDataGrid_SelectionChanged] ORMPanel is null: {ORMPanel == null}");
                if (ORMPanel != null) {
                    logService.LogInfo($"[TexturesDataGrid_SelectionChanged] Setting ORMPanel visibility and initializing...");
                    ORMPanel.Visibility = Visibility.Visible;

                    // Initialize ORM panel with available viewModel.Textures (exclude other ORM textures)
                    var availableTextures = viewModel.Textures.Where(t => !(t is ORMTextureResource)).ToList();
                    logService.LogInfo($"[TexturesDataGrid_SelectionChanged] availableTextures count: {availableTextures.Count}");
                    ORMPanel.Initialize(this, availableTextures);
                    ORMPanel.SetORMTexture(ormTexture);
                    logService.LogInfo($"[TexturesDataGrid_SelectionChanged] ORMPanel initialized and texture set");
                } else {
                    logService.LogInfo($"[TexturesDataGrid_SelectionChanged] ERROR: ORMPanel is NULL! Cannot initialize ORM settings.");
                }

                // Load preview and histogram for packed ORM textures
                if (!string.IsNullOrEmpty(ormTexture.Path) && File.Exists(ormTexture.Path)) {
                    logService.LogInfo($"[TexturesDataGrid_SelectionChanged] ORM texture is packed, loading preview from: {ormTexture.Path}");

                    // CRITICAL: Reset preview state before loading ORM texture
                    // This clears OriginalBitmapSource from previous texture to ensure histogram uses ORM data
                    ResetPreviewState();
                    ClearD3D11Viewer();

                    // Update texture info
                    TextureNameTextBlock.Text = "Texture Name: " + ormTexture.Name;
                    TextureColorSpaceTextBlock.Text = "Color Space: Linear (ORM)";

                    // Load the packed KTX2 file for preview and histogram
                    try {
                        bool ktxLoaded = false;

                        if (texturePreviewService.IsUsingD3D11Renderer) {
                            // D3D11 MODE: Try native KTX2 loading
                            logService.LogInfo($"[TexturesDataGrid_SelectionChanged] Loading packed ORM to D3D11: {ormTexture.Name}");
                            ktxLoaded = await TryLoadKtx2ToD3D11Async(ormTexture, cancellationToken);

                            if (!ktxLoaded) {
                                // Fallback: Try extracting PNG from KTX2 using ktx extract
                                logService.LogInfo($"[TexturesDataGrid_SelectionChanged] D3D11 native loading failed, trying PNG extraction: {ormTexture.Name}");
                                Task<bool> ktxPreviewTask = TryLoadKtx2PreviewAsync(ormTexture, cancellationToken);
                                ktxLoaded = await ktxPreviewTask;
                            }
                        } else {
                            // WPF MODE: Extract PNG from KTX2
                            Task<bool> ktxPreviewTask = TryLoadKtx2PreviewAsync(ormTexture, cancellationToken);
                            ktxLoaded = await ktxPreviewTask;
                        }

                        // Extract histogram for packed ORM textures
                        if (ktxLoaded && !cancellationToken.IsCancellationRequested) {
                            string? ormPath = ormTexture.Path;
                            string ormName = ormTexture.Name;
                            logger.Info($"[ORM Histogram] Starting extraction for: {ormName}, path: {ormPath}");

                            _ = Task.Run(async () => {
                                try {
                                    if (string.IsNullOrEmpty(ormPath)) {
                                        logger.Warn($"[ORM Histogram] Path is empty for: {ormName}");
                                        return;
                                    }

                                    logger.Info($"[ORM Histogram] Extracting mipmaps from: {ormPath}");

                                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                                    var mipmaps = await texturePreviewService.LoadKtx2MipmapsAsync(ormPath, linkedCts.Token).ConfigureAwait(false);
                                    logger.Info($"[ORM Histogram] Extracted {mipmaps.Count} mipmaps for: {ormName}");

                                    if (mipmaps.Count > 0 && !linkedCts.Token.IsCancellationRequested) {
                                        var mip0Bitmap = mipmaps[0].Bitmap;
                                        logger.Info($"[ORM Histogram] Got mip0 bitmap {mip0Bitmap.PixelWidth}x{mip0Bitmap.PixelHeight}, updating histogram...");
                                        _ = Dispatcher.BeginInvoke(new Action(() => {
                                            if (!cancellationToken.IsCancellationRequested) {
                                                texturePreviewService.OriginalFileBitmapSource = mip0Bitmap;
                                                UpdateHistogram(mip0Bitmap);
                                                logger.Info($"[ORM Histogram] Histogram updated for: {ormName}");
                                            } else {
                                                logger.Info($"[ORM Histogram] Cancelled before UI update for: {ormName}");
                                            }
                                        }));
                                    } else {
                                        logger.Warn($"[ORM Histogram] No mipmaps or cancelled for: {ormName}");
                                    }
                                } catch (OperationCanceledException) {
                                    logger.Info($"[ORM Histogram] Extraction cancelled/timeout for: {ormName}");
                                } catch (Exception ex) {
                                    logger.Warn(ex, $"[ORM Histogram] Failed to extract for: {ormName}");
                                }
                            });
                        } else {
                            logger.Info($"[ORM Histogram] Skipped - ktxLoaded={ktxLoaded}, cancelled={cancellationToken.IsCancellationRequested}");
                        }

                        if (!ktxLoaded) {
                            // Use BeginInvoke to avoid deadlock
                            _ = Dispatcher.BeginInvoke(new Action(() => {
                                if (cancellationToken.IsCancellationRequested) return;

                                texturePreviewService.IsKtxPreviewAvailable = false;
                                TextureFormatTextBlock.Text = "Format: KTX2 (preview unavailable)";

                                // Show error message
                                logService.LogWarn($"Failed to load preview for packed ORM texture: {ormTexture.Name}");
                            }));
                        }
                    } catch (OperationCanceledException) {
                        logService.LogInfo($"[TexturesDataGrid_SelectionChanged] Cancelled for ORM: {ormTexture.Name}");
                    } catch (Exception ex) {
                        logService.LogError($"Error loading packed ORM texture {ormTexture.Name}: {ex.Message}");
                        ResetPreviewState();
                        ClearD3D11Viewer();
                    }
                } else {
                    // Not packed yet - clear preview
                    logService.LogInfo($"[TexturesDataGrid_SelectionChanged] ORM texture not packed yet, clearing preview");
                    ResetPreviewState();
                    ClearD3D11Viewer();

                    // Show info that it's not packed yet
                    TextureNameTextBlock.Text = "Texture Name: " + ormTexture.Name;
                    TextureResolutionTextBlock.Text = "Resolution: Not packed yet";
                    TextureSizeTextBlock.Text = "Size: N/A";
                    TextureColorSpaceTextBlock.Text = "Color Space: Linear (ORM)";
                    TextureFormatTextBlock.Text = "Format: Not packed";
                }

                return; // Exit early for ORM textures
            }

            if (TexturesDataGrid.SelectedItem is TextureResource selectedTexture) {
                logService.LogInfo($"[TexturesDataGrid_SelectionChanged] Selected texture: {selectedTexture.Name}, Path: {selectedTexture.Path ?? "NULL"}");

                // Show conversion settings panel, hide ORM panel (for regular viewModel.Textures)
                if (ConversionSettingsExpander != null) {
                    ConversionSettingsExpander.Visibility = Visibility.Visible;
                }
                if (ORMPanel != null) {
                    ORMPanel.Visibility = Visibility.Collapsed;
                }

                ResetPreviewState();
                ClearD3D11Viewer();

                if (!string.IsNullOrEmpty(selectedTexture.Path)) {
                    logService.LogInfo($"[TexturesDataGrid_SelectionChanged] Path is valid, entering main load block");
                    try {
                        // ��������� ���������� � �������� �����
                        TextureNameTextBlock.Text = "Texture Name: " + selectedTexture.Name;
                        TextureResolutionTextBlock.Text = "Resolution: " + string.Join("x", selectedTexture.Resolution);
                        AssetProcessor.Helpers.SizeConverter sizeConverter = new();
                        object size = AssetProcessor.Helpers.SizeConverter.Convert(selectedTexture.Size) ?? "Unknown size";
                        TextureSizeTextBlock.Text = "Size: " + size;

                        // Add color space info
                        bool isSRGB = IsSRGBTexture(selectedTexture);
                        string colorSpace = isSRGB ? "sRGB" : "Linear";
                        string textureType = selectedTexture.TextureType ?? "Unknown";
                        TextureColorSpaceTextBlock.Text = $"Color Space: {colorSpace} ({textureType})";

                        // Format will be updated when texture is loaded
                        TextureFormatTextBlock.Text = "Format: Loading...";

                        // Load conversion settings for this texture (lightweight)
                        logService.LogInfo($"[TextureSelection] Loading conversion settings for: {selectedTexture.Name}");
                        LoadTextureConversionSettings(selectedTexture);
                        logService.LogInfo($"[TextureSelection] Conversion settings loaded for: {selectedTexture.Name}");

                        // Check cancellation before starting heavy operations
                        cancellationToken.ThrowIfCancellationRequested();
                        logService.LogInfo($"[TextureSelection] Starting texture load for: {selectedTexture.Name}");

                        // FIXED: Separate D3D11 native KTX2 loading from PNG extraction
                        // to prevent conflicts
                        bool ktxLoaded = false;

                        if (texturePreviewService.IsUsingD3D11Renderer) {
                            // D3D11 MODE: Try D3D11 native KTX2 loading (always use native when D3D11 is active)
                            logService.LogInfo($"[TextureSelection] Attempting KTX2 load for: {selectedTexture.Name}");
                            ktxLoaded = await TryLoadKtx2ToD3D11Async(selectedTexture, cancellationToken);
                            logService.LogInfo($"[TextureSelection] KTX2 load result for {selectedTexture.Name}: {ktxLoaded}");

                            if (ktxLoaded) {
                                // KTX2 loaded successfully to D3D11, still load source for histogram/info
                                // If user is in Source mode, show the PNG; otherwise just load for histogram
                                bool showInViewer = (texturePreviewService.CurrentPreviewSourceMode == TexturePreviewSourceMode.Source);
                                logService.LogInfo($"[TextureSelection] Loading source preview for {selectedTexture.Name}, showInViewer: {showInViewer}");
                                await LoadSourcePreviewAsync(selectedTexture, cancellationToken, loadToViewer: showInViewer);
                                logService.LogInfo($"[TextureSelection] Source preview loaded for: {selectedTexture.Name}");
                            } else {
                                // No KTX2 or failed, fallback to source preview
                                logService.LogInfo($"[TextureSelection] No KTX2, loading source preview for: {selectedTexture.Name}");
                                await LoadSourcePreviewAsync(selectedTexture, cancellationToken, loadToViewer: true);
                                logService.LogInfo($"[TextureSelection] Source preview loaded for: {selectedTexture.Name}");
                            }
                        } else {
                            // WPF MODE: Use PNG extraction for mipmaps (old method)
                            Task<bool> ktxPreviewTask = TryLoadKtx2PreviewAsync(selectedTexture, cancellationToken);
                            await LoadSourcePreviewAsync(selectedTexture, cancellationToken, loadToViewer: true);
                            ktxLoaded = await ktxPreviewTask;
                        }

                        if (!ktxLoaded) {
                            // Use BeginInvoke to avoid deadlock
                            _ = Dispatcher.BeginInvoke(new Action(() => {
                                if (cancellationToken.IsCancellationRequested) {
                                    return;
                                }

                                texturePreviewService.IsKtxPreviewAvailable = false;

                                if (!texturePreviewService.IsUserPreviewSelection && texturePreviewService.CurrentPreviewSourceMode == TexturePreviewSourceMode.Ktx2) {
                                    SetPreviewSourceMode(TexturePreviewSourceMode.Source, initiatedByUser: false);
                                } else {
                                    UpdatePreviewSourceControls();
                                }
                            }));
                        }
                    } catch (OperationCanceledException) {
                        logService.LogInfo($"[TextureSelection] Cancelled for: {selectedTexture.Name}");
                        // �������� ���� �������� - ��� ���������
                    } catch (Exception ex) {
                        logService.LogError($"Error loading texture {selectedTexture.Name}: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Refreshes the DataGrid and reloads the preview for the currently selected texture.
        /// Called after ORM packing to update the UI (row colors and preview).
        /// </summary>
        public async void RefreshCurrentTexture() {
            logService.LogInfo("[RefreshCurrentTexture] Refreshing DataGrid and preview");

            // Save the selected item
            var selectedItem = TexturesDataGrid.SelectedItem;

            // Force complete DataGrid refresh by rebinding ItemsSource
            // This ensures DataTriggers are re-evaluated
            var itemsSource = TexturesDataGrid.ItemsSource;
            TexturesDataGrid.ItemsSource = null;
            TexturesDataGrid.ItemsSource = itemsSource;

            // Restore selection
            TexturesDataGrid.SelectedItem = selectedItem;

            // Reload the preview by simulating selection changed
            if (selectedItem != null) {
                textureLoadCancellation?.Cancel();
                textureLoadCancellation = new CancellationTokenSource();

                // Small delay to allow DataGrid to rebind
                await Task.Delay(100);

                // Trigger selection changed logic manually
                logService.LogInfo($"[RefreshCurrentTexture] Triggering preview reload for: {(selectedItem as TextureResource)?.Name ?? "unknown"}");

                TexturesDataGrid_SelectionChanged(TexturesDataGrid, new SelectionChangedEventArgs(
                    System.Windows.Controls.Primitives.Selector.SelectionChangedEvent,
                    new List<object>(),
                    new List<object> { selectedItem }
                ));
            }
        }

        /// <summary>
        /// NEW METHOD: Load KTX2 directly to D3D11 viewer (native format, all mipmaps).
        /// </summary>
        

        private async Task<bool> TryLoadKtx2ToD3D11Async(TextureResource selectedTexture, CancellationToken cancellationToken) {
            string? ktxPath = texturePreviewService.GetExistingKtx2Path(selectedTexture.Path, ProjectFolderPath);
            if (ktxPath == null) {
                logger.Info($"KTX2 file not found for: {selectedTexture.Path}");
                return false;
            }

            logger.Info($"Found KTX2 file: {ktxPath}");

            try {
                // Load KTX2 directly to D3D11 (no PNG extraction)
                logger.Info($"[TryLoadKtx2ToD3D11Async] Calling LoadKtx2ToD3D11ViewerAsync for: {ktxPath}");
                bool loaded = await LoadKtx2ToD3D11ViewerAsync(ktxPath);
                logger.Info($"[TryLoadKtx2ToD3D11Async] LoadKtx2ToD3D11ViewerAsync returned: {loaded}, cancelled: {cancellationToken.IsCancellationRequested}");

                if (!loaded || cancellationToken.IsCancellationRequested) {
                    logger.Warn($"Failed to load KTX2 to D3D11 viewer: {ktxPath}");
                    return false;
                }

                logger.Info($"Loaded KTX2 directly to D3D11 viewer: {ktxPath}");

                // Use BeginInvoke (fire-and-forget) to avoid deadlock when UI thread is busy
                _ = Dispatcher.BeginInvoke(new Action(() => {
                    if (cancellationToken.IsCancellationRequested) {
                        return;
                    }

                    // Save current loaded texture paths for preview renderer switching
                    texturePreviewService.CurrentLoadedTexturePath = selectedTexture.Path;
                    texturePreviewService.CurrentLoadedKtx2Path = ktxPath;

                    // Mark KTX2 preview as available
                    texturePreviewService.IsKtxPreviewAvailable = true;
                    texturePreviewService.IsKtxPreviewActive = true;

                    // Clear old mipmap data (we're using D3D11 native mipmaps now)
                    texturePreviewService.CurrentKtxMipmaps?.Clear();
                    texturePreviewService.CurrentMipLevel = 0;

                    // Update UI to show KTX2 is active
                    if (!texturePreviewService.IsUserPreviewSelection || texturePreviewService.CurrentPreviewSourceMode == TexturePreviewSourceMode.Ktx2) {
                        SetPreviewSourceMode(TexturePreviewSourceMode.Ktx2, initiatedByUser: false);
                    } else {
                        UpdatePreviewSourceControls();
                    }
                }));

                return true;
            } catch (OperationCanceledException) {
                return false;
            } catch (Exception ex) {
                logger.Warn(ex, $"Failed to load KTX2 preview: {ktxPath}");
                return false;
            }
        }

        /// <summary>
        /// OLD METHOD: Extract KTX2 mipmaps to PNG files and load them.
        /// Used when useD3D11NativeKtx2 = false.
        /// </summary>
        

        private async Task<bool> TryLoadKtx2PreviewAsync(TextureResource selectedTexture, CancellationToken cancellationToken) {
            string? ktxPath = texturePreviewService.GetExistingKtx2Path(selectedTexture.Path, ProjectFolderPath);
            if (ktxPath == null) {
                logger.Info($"KTX2 file not found for: {selectedTexture.Path}");
                return false;
            }

            logger.Info($"Found KTX2 file: {ktxPath}");

            try {
                // OLD METHOD: Extract to PNG files
                List<KtxMipLevel> mipmaps = await texturePreviewService.LoadKtx2MipmapsAsync(ktxPath, cancellationToken);
                if (mipmaps.Count == 0 || cancellationToken.IsCancellationRequested) {
                    logger.Warn($"Failed to extract mipmaps from KTX2: {ktxPath}");
                    return false;
                }

                logger.Info($"Extracted {mipmaps.Count} mipmaps from KTX2");

                // Use BeginInvoke to avoid deadlock
                _ = Dispatcher.BeginInvoke(new Action(() => {
                    if (cancellationToken.IsCancellationRequested) {
                        return;
                    }

                    texturePreviewService.CurrentKtxMipmaps.Clear();
                    foreach (var mipmap in mipmaps) {
                        texturePreviewService.CurrentKtxMipmaps.Add(mipmap);
                    }
                    texturePreviewService.CurrentMipLevel = 0;
                    texturePreviewService.IsKtxPreviewAvailable = true;

                    if (!texturePreviewService.IsUserPreviewSelection || texturePreviewService.CurrentPreviewSourceMode == TexturePreviewSourceMode.Ktx2) {
                        SetPreviewSourceMode(TexturePreviewSourceMode.Ktx2, initiatedByUser: false);
                    } else {
                        UpdatePreviewSourceControls();
                    }
                }));

                return true;
            } catch (OperationCanceledException) {
                return false;
            } catch (Exception ex) {
                logger.Warn(ex, $"Failed to load KTX2 preview: {ktxPath}");
                return false;
            }
        }


        private async Task LoadSourcePreviewAsync(TextureResource selectedTexture, CancellationToken cancellationToken, bool loadToViewer = true) {
            // Reset channel masks when loading new texture
            // BUT: Don't reset if Normal mode was auto-enabled for normal maps
            if (texturePreviewService.CurrentActiveChannelMask != "Normal") {
                texturePreviewService.CurrentActiveChannelMask = null;
                // Use BeginInvoke to avoid deadlock when called from background thread
                _ = Dispatcher.BeginInvoke(new Action(() => {
                    UpdateChannelButtonsState();
                    // Reset D3D11 renderer mask
                    if (texturePreviewService.IsUsingD3D11Renderer && D3D11TextureViewer?.Renderer != null) {
                        D3D11TextureViewer.Renderer.SetChannelMask(0xFFFFFFFF);
                        D3D11TextureViewer.Renderer.RestoreOriginalGamma();
                    }
                }));
            } else {
                logger.Info("LoadSourcePreviewAsync: Skipping mask reset - Normal mode is active for normal map");
            }

            // Store currently selected texture for sRGB detection
            texturePreviewService.CurrentSelectedTexture = selectedTexture;

            string? texturePath = selectedTexture.Path;
            if (string.IsNullOrEmpty(texturePath)) {
                return;
            }

            if (texturePreviewService.GetCachedImage(texturePath) is BitmapImage cachedImage) {
                // Use BeginInvoke to avoid deadlock
                _ = Dispatcher.BeginInvoke(new Action(() => {
                    if (cancellationToken.IsCancellationRequested) {
                        return;
                    }

                    // Save current loaded texture path for preview renderer switching
                    texturePreviewService.CurrentLoadedTexturePath = texturePath;

                    texturePreviewService.OriginalFileBitmapSource = cachedImage;
                    texturePreviewService.IsSourcePreviewAvailable = true;

                    // Only show in viewer if loadToViewer=true (not when KTX2 is already loaded)
                    if (loadToViewer && texturePreviewService.CurrentPreviewSourceMode == TexturePreviewSourceMode.Source) {
                        texturePreviewService.OriginalBitmapSource = cachedImage;
                        ShowOriginalImage();
                    }

                    // Always update histogram when source image is loaded (even if showing KTX2)
                    // Use the source bitmap for histogram calculation
                    _ = UpdateHistogramAsync(cachedImage);

                    UpdatePreviewSourceControls();
                }));

                return;
            }

            BitmapImage? thumbnailImage = texturePreviewService.LoadOptimizedImage(texturePath, ThumbnailSize);
            if (thumbnailImage == null) {
                logService.LogInfo($"Error loading thumbnail for texture: {selectedTexture.Name}");
                return;
            }

            // Use BeginInvoke to avoid deadlock
            _ = Dispatcher.BeginInvoke(new Action(() => {
                if (cancellationToken.IsCancellationRequested) {
                    return;
                }

                // Save current loaded texture path for preview renderer switching
                texturePreviewService.CurrentLoadedTexturePath = texturePath;

                texturePreviewService.OriginalFileBitmapSource = thumbnailImage;
                texturePreviewService.IsSourcePreviewAvailable = true;

                // Only show in viewer if loadToViewer=true (not when KTX2 is already loaded)
                if (loadToViewer && texturePreviewService.CurrentPreviewSourceMode == TexturePreviewSourceMode.Source) {
                    texturePreviewService.OriginalBitmapSource = thumbnailImage;
                    ShowOriginalImage();
                }

                // Always update histogram when source image is loaded (even if showing KTX2)
                // Use the source bitmap for histogram calculation
                _ = UpdateHistogramAsync(thumbnailImage);

                UpdatePreviewSourceControls();
            }));

            if (cancellationToken.IsCancellationRequested) {
                return;
            }

            try {
                await Task.Run(() => {
                    if (cancellationToken.IsCancellationRequested) {
                        return;
                    }

                    BitmapImage? bitmapImage = texturePreviewService.LoadOptimizedImage(texturePath, MaxPreviewSize);

                    if (bitmapImage == null || cancellationToken.IsCancellationRequested) {
                        return;
                    }

                    // Use BeginInvoke to avoid deadlock when called from background thread
                    _ = Dispatcher.BeginInvoke(new Action(() => {
                        if (cancellationToken.IsCancellationRequested) {
                            return;
                        }

                        texturePreviewService.CacheImage(texturePath, bitmapImage);

                        texturePreviewService.OriginalFileBitmapSource = bitmapImage;
                        texturePreviewService.IsSourcePreviewAvailable = true;

                        // Only show in viewer if loadToViewer=true (not when KTX2 is already loaded)
                        if (loadToViewer && texturePreviewService.CurrentPreviewSourceMode == TexturePreviewSourceMode.Source) {
                            texturePreviewService.OriginalBitmapSource = bitmapImage;
                            ShowOriginalImage();
                        }

                        // Always update histogram when full-resolution image is loaded (even if showing KTX2)
                        // This replaces the thumbnail-based histogram with accurate full-image data
                        _ = UpdateHistogramAsync(bitmapImage);

                        UpdatePreviewSourceControls();
                    }));
                }, cancellationToken);
            } catch (OperationCanceledException) {
                // ���������� �������� ��������� ��� ����� ������
            }
        }

        


private void AboutMenu(object? sender, RoutedEventArgs e) {
            MessageBox.Show("AssetProcessor v1.0\n\nDeveloped by: SashaRX\n\n2021");
        }

        private void SettingsMenu(object? sender, RoutedEventArgs e) {
            SettingsWindow settingsWindow = new();
            // Subscribe to preview renderer changes
            settingsWindow.OnPreviewRendererChanged += HandlePreviewRendererChanged;
            settingsWindow.ShowDialog();
            // Unsubscribe after window closes
            settingsWindow.OnPreviewRendererChanged -= HandlePreviewRendererChanged;
        }


        private void ExitMenu(object? sender, RoutedEventArgs e) {
            Close();
        }

        private void ThemeAuto_Click(object sender, RoutedEventArgs e) {
            ThemeAutoMenuItem.IsChecked = true;
            ThemeLightMenuItem.IsChecked = false;
            ThemeDarkMenuItem.IsChecked = false;
            ThemeHelper.CurrentMode = Helpers.ThemeMode.Auto;
            DarkThemeCheckBox.IsChecked = ThemeHelper.IsDarkTheme;
            RefreshHistogramForTheme();
        }

        private void ThemeLight_Click(object sender, RoutedEventArgs e) {
            ThemeAutoMenuItem.IsChecked = false;
            ThemeLightMenuItem.IsChecked = true;
            ThemeDarkMenuItem.IsChecked = false;
            ThemeHelper.CurrentMode = Helpers.ThemeMode.Light;
            DarkThemeCheckBox.IsChecked = false;
            RefreshHistogramForTheme();
        }

        private void ThemeDark_Click(object sender, RoutedEventArgs e) {
            ThemeAutoMenuItem.IsChecked = false;
            ThemeLightMenuItem.IsChecked = false;
            ThemeDarkMenuItem.IsChecked = true;
            ThemeHelper.CurrentMode = Helpers.ThemeMode.Dark;
            DarkThemeCheckBox.IsChecked = true;
            RefreshHistogramForTheme();
        }

        private void DarkThemeCheckBox_Click(object sender, RoutedEventArgs e) {
            bool isDark = DarkThemeCheckBox.IsChecked == true;

            // Update menu items to match
            ThemeAutoMenuItem.IsChecked = false;
            ThemeLightMenuItem.IsChecked = !isDark;
            ThemeDarkMenuItem.IsChecked = isDark;

            // Apply theme
            ThemeHelper.CurrentMode = isDark ? Helpers.ThemeMode.Dark : Helpers.ThemeMode.Light;
            RefreshHistogramForTheme();
        }

        private void RefreshHistogramForTheme() {
            // Rebuild histogram with new theme colors if a texture is currently displayed
            if (texturePreviewService?.OriginalBitmapSource != null) {
                UpdateHistogram(texturePreviewService.OriginalBitmapSource);
            }
        }

        private void GridSplitter_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e) {
            GridSplitter gridSplitter = (GridSplitter)sender;

            if (gridSplitter.Parent is not Grid grid) {
                return;
            }

            double row1Height = ((RowDefinition)grid.RowDefinitions[0]).ActualHeight;
            double row2Height = ((RowDefinition)grid.RowDefinitions[1]).ActualHeight;

            // ����������� �� ����������� ������� �����
            double minHeight = 137;

            if (row1Height < minHeight || row2Height < minHeight) {
                e.Handled = true;
            }
        }

                private static readonly TextureTypeToBackgroundConverter textureTypeConverter = new();

private void TexturesDataGrid_LoadingRow(object? sender, DataGridRowEventArgs? e) {
            if (e?.Row?.DataContext is TextureResource texture) {
                // Initialize conversion settings for the texture if not already set
                // ��������� ������ ���� ���, ����� �� �������� ������� �������� ��� ������ �����������
                if (string.IsNullOrEmpty(texture.CompressionFormat)) {
                    InitializeTextureConversionSettings(texture);
                }

                // �� ������������� ���� ���� ����� - �� ��� ���������� ����� Style � XAML
                // ��� ������������� ������ �������� ��� ������ ����������� ������ �� ����� ����������
                // ���� ���� ����������� ����� DataTrigger � DataGrid.RowStyle
            }
        }

        // Column header definitions: (Full name, Short name, MinWidthForFull)
        // MinWidthForFull = minimum column width needed to show full name
        private static readonly (string Full, string Short, double MinWidthForFull)[] TextureColumnHeaders = [
            ("№", "№", 30),                      // 0 - Index
            ("ID", "ID", 30),                    // 1 - ID
            ("Texture Name", "Name", 100),       // 2 - Name
            ("Extension", "Ext", 65),            // 3 - Extension
            ("Size", "Size", 40),                // 4 - Size
            ("Compressed", "Comp", 85),          // 5 - Compressed Size
            ("Resolution", "Res", 80),           // 6 - Resolution
            ("Resize", "Rsz", 55),               // 7 - Resize Resolution
            ("Format", "Fmt", 55),               // 8 - Compression Format
            ("Mipmaps", "Mip", 60),              // 9 - Mipmaps
            ("Preset", "Prs", 55),               // 10 - Preset
            ("Status", "St", 55)                 // 11 - Status/Progress
        ];

        // Track current header state per column to avoid unnecessary updates
        private readonly bool[] _columnUsingShortHeader = new bool[12];

        private readonly Dictionary<DataGrid, double[]> _previousColumnWidths = new();
        private bool _isAdjustingColumns = false;

        private void TexturesDataGrid_SizeChanged(object sender, SizeChangedEventArgs e) {
            if (sender is not DataGrid grid) return;
            InitializeGridColumnsIfNeeded(grid);
            SubscribeDataGridColumnResizing(grid);
            UpdateColumnHeadersBasedOnWidth(grid);
            FillRemainingSpace(grid);
        }

        private void TexturesDataGrid_ColumnDisplayIndexChanged(object? sender, DataGridColumnEventArgs e) {
            // After column reorder - recalculate, fill space, and save order
            _ = Dispatcher.BeginInvoke(new Action(() => {
                SubscribeToColumnWidthChanges(TexturesDataGrid);
                SubscribeDataGridColumnResizing(TexturesDataGrid);
                FillRemainingSpace(TexturesDataGrid);
                SaveColumnOrder(TexturesDataGrid);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void SubscribeToColumnWidthChanges() => SubscribeToColumnWidthChanges(TexturesDataGrid);

        private void SubscribeToColumnWidthChanges(DataGrid grid) {
            if (grid == null || grid.Columns.Count == 0) return;

            var widths = new double[grid.Columns.Count];
            for (int i = 0; i < grid.Columns.Count; i++) {
                widths[i] = grid.Columns[i].ActualWidth;

                var descriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
                    DataGridColumn.ActualWidthProperty, typeof(DataGridColumn));
                descriptor?.RemoveValueChanged(grid.Columns[i], OnColumnWidthChanged);
                descriptor?.AddValueChanged(grid.Columns[i], OnColumnWidthChanged);
            }
            _previousColumnWidths[grid] = widths;

            // Subscribe to left gripper thumb events after visual tree is ready
            Dispatcher.BeginInvoke(() => SubscribeToLeftGripperThumbs(), DispatcherPriority.Loaded);
        }

        private readonly HashSet<DataGrid> _leftGripperSubscribedGrids = new();

        private void SubscribeToLeftGripperThumbs() {
            // Subscribe to all DataGrids
            SubscribeDataGridColumnResizing(TexturesDataGrid);
            SubscribeDataGridColumnResizing(ModelsDataGrid);
            SubscribeDataGridColumnResizing(MaterialsDataGrid);
        }

        private void SubscribeDataGridColumnResizing(DataGrid grid) {
            if (grid == null) return;

            // Subscribe to column headers for left edge mouse handling
            var columnHeaders = FindVisualChildren<DataGridColumnHeader>(grid).ToList();

            // Skip if no headers found yet (grid not visible) or already fully subscribed
            if (columnHeaders.Count == 0) return;

            // Check if we need to subscribe (new headers may appear)
            bool hasNewHeaders = false;
            foreach (var header in columnHeaders) {
                if (header.Column == null) continue;
                // Check if this header already has our handler
                hasNewHeaders = true;
                header.PreviewMouseLeftButtonDown -= OnHeaderMouseDown;
                header.PreviewMouseLeftButtonDown += OnHeaderMouseDown;
                header.PreviewMouseMove -= OnHeaderMouseMoveForCursor;
                header.PreviewMouseMove += OnHeaderMouseMoveForCursor;
            }

            if (!hasNewHeaders) return;

            // Subscribe to move/up events on DataGrid level for dragging (only once per grid)
            if (!_leftGripperSubscribedGrids.Contains(grid)) {
                grid.PreviewMouseMove -= OnDataGridMouseMove;
                grid.PreviewMouseMove += OnDataGridMouseMove;
                grid.PreviewMouseLeftButtonUp -= OnDataGridMouseUp;
                grid.PreviewMouseLeftButtonUp += OnDataGridMouseUp;
                grid.LostMouseCapture -= OnDataGridLostCapture;
                grid.LostMouseCapture += OnDataGridLostCapture;
                _leftGripperSubscribedGrids.Add(grid);
            }
        }

        private bool _isLeftGripperDragging;
        private Point _leftGripperStartPoint;
        private DataGridColumn? _leftGripperColumn;
        private List<DataGridColumn>? _leftGripperLeftColumns;
        private List<DataGridColumn>? _leftGripperRightColumns;
        private Dictionary<DataGridColumn, double>? _leftGripperColumnWidths;
        private double _leftGripperColumnWidth;
        private DataGrid? _currentResizingDataGrid;
        private const double LeftGripperZoneWidth = 10;

        private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e) {
            if (sender is not DataGridColumnHeader header || header.Column == null) {
                return;
            }

            var dataGrid = FindParentDataGrid(header);
            if (dataGrid == null) return;

            Point pos = e.GetPosition(header);
            double headerWidth = header.ActualWidth;
            int currentIndex = dataGrid.Columns.IndexOf(header.Column);

            // Check if click is on LEFT edge (resize with left neighbor)
            if (pos.X <= LeftGripperZoneWidth) {
                var columnsToLeft = GetVisibleColumnsBefore(dataGrid, currentIndex);
                if (columnsToLeft.Count == 0) return;

                StartLeftGripperDrag(dataGrid, header.Column, columnsToLeft, e);
                return;
            }

            // Check if click is on RIGHT edge (resize with right neighbor)
            if (pos.X >= headerWidth - LeftGripperZoneWidth) {
                var columnsToRight = GetVisibleColumnsAfter(dataGrid, currentIndex);
                if (columnsToRight.Count == 0) return;

                var rightNeighbor = columnsToRight[0];
                var leftColumnsForRight = GetVisibleColumnsBefore(dataGrid, dataGrid.Columns.IndexOf(rightNeighbor));

                StartLeftGripperDrag(dataGrid, rightNeighbor, leftColumnsForRight, e);
                return;
            }
        }

        private DataGrid? FindParentDataGrid(DependencyObject element) {
            while (element != null) {
                if (element is DataGrid dg) return dg;
                element = VisualTreeHelper.GetParent(element);
            }
            return null;
        }

        private void StartLeftGripperDrag(DataGrid dataGrid, DataGridColumn currentColumn, List<DataGridColumn> columnsToLeft, MouseButtonEventArgs e) {
            _isLeftGripperDragging = true;
            _currentResizingDataGrid = dataGrid;
            _leftGripperStartPoint = e.GetPosition(dataGrid);
            _leftGripperColumn = currentColumn;
            _leftGripperLeftColumns = columnsToLeft;
            _leftGripperColumnWidth = currentColumn.ActualWidth;

            int currentIndex = dataGrid.Columns.IndexOf(currentColumn);
            _leftGripperRightColumns = GetVisibleColumnsAfter(dataGrid, currentIndex);

            _leftGripperColumnWidths = new Dictionary<DataGridColumn, double>();
            foreach (var col in _leftGripperLeftColumns) {
                _leftGripperColumnWidths[col] = col.ActualWidth;
                if (col.Width.IsStar) {
                    col.Width = new DataGridLength(col.ActualWidth);
                }
            }
            foreach (var col in _leftGripperRightColumns) {
                _leftGripperColumnWidths[col] = col.ActualWidth;
                if (col.Width.IsStar) {
                    col.Width = new DataGridLength(col.ActualWidth);
                }
            }
            if (_leftGripperColumn.Width.IsStar) {
                _leftGripperColumn.Width = new DataGridLength(_leftGripperColumnWidth);
            }

            dataGrid.CaptureMouse();
            dataGrid.Cursor = Cursors.SizeWE;
            e.Handled = true;
        }

        private void OnHeaderMouseMoveForCursor(object sender, MouseEventArgs e) {
            if (sender is not DataGridColumnHeader header || header.Column == null) return;
            if (_isLeftGripperDragging) return;

            var dataGrid = FindParentDataGrid(header);
            if (dataGrid == null) return;

            Point pos = e.GetPosition(header);
            double headerWidth = header.ActualWidth;
            int colIndex = dataGrid.Columns.IndexOf(header.Column);

            if (pos.X <= LeftGripperZoneWidth) {
                var leftCols = GetVisibleColumnsBefore(dataGrid, colIndex);
                if (leftCols.Count > 0) {
                    header.Cursor = Cursors.SizeWE;
                    return;
                }
            }

            if (pos.X >= headerWidth - LeftGripperZoneWidth) {
                var rightCols = GetVisibleColumnsAfter(dataGrid, colIndex);
                if (rightCols.Count > 0) {
                    header.Cursor = Cursors.SizeWE;
                    return;
                }
            }

            header.Cursor = null;
        }

        private void OnDataGridMouseMove(object sender, MouseEventArgs e) {
            if (!_isLeftGripperDragging || _currentResizingDataGrid == null) return;
            if (_leftGripperColumn == null || _leftGripperLeftColumns == null || _leftGripperColumnWidths == null) {
                return;
            }

            if (e.LeftButton != MouseButtonState.Pressed) {
                StopLeftGripperDrag();
                return;
            }

            Point currentPoint = e.GetPosition(_currentResizingDataGrid);
            double delta = currentPoint.X - _leftGripperStartPoint.X;
            _leftGripperStartPoint = currentPoint;

            if (Math.Abs(delta) < 1) return;

            double currentMin = _leftGripperColumn.MinWidth > 0 ? _leftGripperColumn.MinWidth : 30;

            _isAdjustingColumns = true;
            try {
                if (delta < 0) {
                    // Dragging LEFT - shrink columns to the left, expand current
                    // Balance is maintained: shrink left = expand current (total width unchanged)
                    double remainingShrink = -delta;

                    double totalShrinkable = 0;
                    foreach (var col in _leftGripperLeftColumns) {
                        double colMin = col.MinWidth > 0 ? col.MinWidth : 30;
                        totalShrinkable += _leftGripperColumnWidths[col] - colMin;
                    }
                    remainingShrink = Math.Min(remainingShrink, Math.Max(0, totalShrinkable));

                    for (int i = 0; i < _leftGripperLeftColumns.Count && remainingShrink > 0; i++) {
                        var col = _leftGripperLeftColumns[i];
                        double colMin = col.MinWidth > 0 ? col.MinWidth : 30;
                        double colWidth = _leftGripperColumnWidths[col];
                        double available = colWidth - colMin;

                        if (available > 0) {
                            double shrink = Math.Min(remainingShrink, available);
                            _leftGripperColumnWidths[col] = colWidth - shrink;
                            col.Width = new DataGridLength(_leftGripperColumnWidths[col]);
                            _leftGripperColumnWidth += shrink;
                            remainingShrink -= shrink;
                        }
                    }
                    _leftGripperColumn.Width = new DataGridLength(_leftGripperColumnWidth);
                } else if (delta > 0 && _leftGripperRightColumns != null) {
                    // Dragging RIGHT - shrink current and columns to the right, expand left
                    double totalShrinkable = Math.Max(0, _leftGripperColumnWidth - currentMin);
                    foreach (var col in _leftGripperRightColumns) {
                        if (!_leftGripperColumnWidths.ContainsKey(col)) continue;
                        double colMin = col.MinWidth > 0 ? col.MinWidth : 30;
                        totalShrinkable += Math.Max(0, _leftGripperColumnWidths[col] - colMin);
                    }

                    double remainingShrink = Math.Min(delta, totalShrinkable);
                    double originalRemaining = remainingShrink;

                    double currentAvailable = _leftGripperColumnWidth - currentMin;
                    double shrinkFromCurrent = Math.Min(remainingShrink, Math.Max(0, currentAvailable));
                    if (shrinkFromCurrent > 0) {
                        _leftGripperColumnWidth -= shrinkFromCurrent;
                        remainingShrink -= shrinkFromCurrent;
                    }

                    for (int i = 0; i < _leftGripperRightColumns.Count && remainingShrink > 0; i++) {
                        var col = _leftGripperRightColumns[i];
                        if (!_leftGripperColumnWidths.ContainsKey(col)) continue;
                        double colMin = col.MinWidth > 0 ? col.MinWidth : 30;
                        double colWidth = _leftGripperColumnWidths[col];
                        double available = colWidth - colMin;

                        if (available > 0) {
                            double shrink = Math.Min(remainingShrink, available);
                            _leftGripperColumnWidths[col] = colWidth - shrink;
                            col.Width = new DataGridLength(_leftGripperColumnWidths[col]);
                            remainingShrink -= shrink;
                        }
                    }

                    double totalShrunk = originalRemaining - remainingShrink;
                    if (totalShrunk > 0 && _leftGripperLeftColumns.Count > 0) {
                        _leftGripperColumn.Width = new DataGridLength(_leftGripperColumnWidth);

                        var leftNeighbor = _leftGripperLeftColumns[0];
                        if (_leftGripperColumnWidths.ContainsKey(leftNeighbor)) {
                            _leftGripperColumnWidths[leftNeighbor] += totalShrunk;
                            leftNeighbor.Width = new DataGridLength(_leftGripperColumnWidths[leftNeighbor]);
                        }
                    }
                }

                UpdateStoredWidths(_currentResizingDataGrid);
                if (_currentResizingDataGrid == TexturesDataGrid) {
                    UpdateColumnHeadersBasedOnWidth(TexturesDataGrid);
                }
            } finally {
                _isAdjustingColumns = false;
            }

            e.Handled = true;
        }

        private void StopLeftGripperDrag() {
            if (_isLeftGripperDragging && _currentResizingDataGrid != null) {
                var gridToSave = _currentResizingDataGrid;
                _isLeftGripperDragging = false;
                _leftGripperColumn = null;
                _leftGripperLeftColumns = null;
                _leftGripperRightColumns = null;
                _leftGripperColumnWidths = null;
                // Save reference before ReleaseMouseCapture triggers LostMouseCapture
                var grid = _currentResizingDataGrid;
                _currentResizingDataGrid = null;
                grid.ReleaseMouseCapture();
                grid.Cursor = null;
                SaveColumnWidthsDebounced(gridToSave);
            }
        }

        private void OnDataGridMouseUp(object sender, MouseButtonEventArgs e) {
            if (_isLeftGripperDragging) {
                StopLeftGripperDrag();
                e.Handled = true;
            }
        }

        private void OnDataGridLostCapture(object sender, MouseEventArgs e) {
            _isLeftGripperDragging = false;
            _leftGripperColumn = null;
            _leftGripperLeftColumns = null;
            _leftGripperRightColumns = null;
            _leftGripperColumnWidths = null;
            if (_currentResizingDataGrid != null) {
                _currentResizingDataGrid.Cursor = null;
                _currentResizingDataGrid = null;
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject {
            if (parent == null) yield break;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild) {
                    yield return typedChild;
                }
                foreach (var descendant in FindVisualChildren<T>(child)) {
                    yield return descendant;
                }
            }
        }

        private void OnColumnWidthChanged(object? sender, EventArgs e) {
            if (_isAdjustingColumns || sender is not DataGridColumn changedColumn) return;
            if (changedColumn.Visibility != Visibility.Visible) return;

            // Find which DataGrid owns this column
            DataGrid? grid = null;
            foreach (var g in new[] { TexturesDataGrid, ModelsDataGrid, MaterialsDataGrid }) {
                if (g.Columns.Contains(changedColumn)) {
                    grid = g;
                    break;
                }
            }
            if (grid == null) return;

            if (!_previousColumnWidths.TryGetValue(grid, out var prevWidths) || prevWidths.Length == 0) return;

            _isAdjustingColumns = true;
            try {
                int changedIndex = grid.Columns.IndexOf(changedColumn);
                if (changedIndex < 0 || changedIndex >= prevWidths.Length) return;

                double oldWidth = prevWidths[changedIndex];
                double newWidth = changedColumn.ActualWidth;
                double delta = newWidth - oldWidth;

                if (Math.Abs(delta) < 1) return;

                double remainingDelta = delta;

                if (delta > 0) {
                    // Column is EXPANDING - shrink columns to the RIGHT first
                    var columnsToRight = GetVisibleColumnsAfter(grid, changedIndex);
                    remainingDelta = ShrinkColumns(columnsToRight, remainingDelta);

                    // If still have remaining, try shrinking columns to the LEFT
                    if (Math.Abs(remainingDelta) >= 1) {
                        var columnsToLeft = GetVisibleColumnsBefore(grid, changedIndex);
                        remainingDelta = ShrinkColumns(columnsToLeft, remainingDelta);
                    }
                } else {
                    // Column is SHRINKING - expand the nearest neighbor to fill space
                    var columnsToRight = GetVisibleColumnsAfter(grid, changedIndex);
                    if (columnsToRight.Count > 0) {
                        var rightNeighbor = columnsToRight[0];
                        double expandBy = -delta;
                        rightNeighbor.Width = new DataGridLength(rightNeighbor.ActualWidth + expandBy);
                        remainingDelta = 0;
                    } else {
                        // No columns to the right - expand nearest column to the LEFT
                        var columnsToLeft = GetVisibleColumnsBefore(grid, changedIndex);
                        if (columnsToLeft.Count > 0) {
                            var leftNeighbor = columnsToLeft[0];
                            double expandBy = -delta;
                            leftNeighbor.Width = new DataGridLength(leftNeighbor.ActualWidth + expandBy);
                            remainingDelta = 0;
                        }
                    }
                }

                // If couldn't distribute all delta, limit the change
                if (Math.Abs(remainingDelta) >= 1 && delta > 0) {
                    changedColumn.Width = new DataGridLength(oldWidth + (delta - remainingDelta));
                }

                UpdateStoredWidths(grid);
                if (grid == TexturesDataGrid) {
                    UpdateColumnHeadersBasedOnWidth(grid);
                }
                SaveColumnWidthsDebounced(grid);
            } finally {
                _isAdjustingColumns = false;
            }
        }

        private double ShrinkColumns(List<DataGridColumn> columns, double deltaToDistribute) {
            double remaining = deltaToDistribute;
            foreach (var col in columns) {
                if (remaining < 1) break;

                double colMin = col.MinWidth > 0 ? col.MinWidth : 30;
                double colCurrent = col.ActualWidth;
                double available = colCurrent - colMin;

                if (available > 0) {
                    double shrinkBy = Math.Min(remaining, available);
                    col.Width = new DataGridLength(colCurrent - shrinkBy);
                    remaining -= shrinkBy;
                }
            }
            return remaining;
        }

        private List<DataGridColumn> GetVisibleColumnsBefore(int index) => GetVisibleColumnsBefore(TexturesDataGrid, index);

        private List<DataGridColumn> GetVisibleColumnsBefore(DataGrid grid, int index) {
            var changedColumn = grid.Columns[index];
            int changedDisplayIndex = changedColumn.DisplayIndex;

            // Get visible columns to the left, ordered from nearest to farthest
            return grid.Columns
                .Where(c => c.Visibility == Visibility.Visible && c.DisplayIndex < changedDisplayIndex)
                .OrderByDescending(c => c.DisplayIndex) // Nearest first (highest DisplayIndex that's still < changed)
                .ToList();
        }

        private List<DataGridColumn> GetVisibleColumnsAfter(int index) => GetVisibleColumnsAfter(TexturesDataGrid, index);

        private List<DataGridColumn> GetVisibleColumnsAfter(DataGrid grid, int index) {
            var result = new List<DataGridColumn>();
            var sortedColumns = grid.Columns
                .Where(c => c.Visibility == Visibility.Visible)
                .OrderBy(c => c.DisplayIndex)
                .ToList();

            var changedColumn = grid.Columns[index];
            int changedDisplayIndex = changedColumn.DisplayIndex;

            foreach (var col in sortedColumns) {
                if (col.DisplayIndex > changedDisplayIndex) {
                    result.Add(col);
                }
            }
            return result;
        }

        private DataGridColumn? GetLastVisibleColumn(DataGrid grid) {
            return grid.Columns
                .Where(c => c.Visibility == Visibility.Visible)
                .OrderByDescending(c => c.DisplayIndex)
                .FirstOrDefault();
        }

        private void FillRemainingSpace(DataGrid grid) {
            if (grid == null || grid.Columns.Count == 0 || _isAdjustingColumns) return;

            _isAdjustingColumns = true;
            try {
                double availableWidth = grid.ActualWidth - SystemParameters.VerticalScrollBarWidth - 2;
                if (availableWidth <= 0) return;

                // Get visible columns ordered by DisplayIndex (rightmost first for shrinking)
                var visibleColumns = grid.Columns
                    .Where(c => c.Visibility == Visibility.Visible)
                    .OrderByDescending(c => c.DisplayIndex)
                    .ToList();

                if (visibleColumns.Count == 0) return;

                double totalWidth = visibleColumns.Sum(c => c.ActualWidth);
                double delta = availableWidth - totalWidth;

                if (Math.Abs(delta) < 1) {
                    UpdateStoredWidths(grid);
                    return;
                }

                // Check if this grid has user-saved widths loaded
                bool hasSavedWidths = _hasSavedWidthsFromSettings.Contains(grid);
                var lastVisible = visibleColumns[0]; // Already sorted, first is rightmost

                if (delta > 0) {
                    // Table expanded - add space to last visible column
                    lastVisible.Width = new DataGridLength(lastVisible.ActualWidth + delta);
                } else {
                    if (hasSavedWidths) {
                        // Grid has saved widths - only shrink the last column to preserve user settings
                        double colMin = lastVisible.MinWidth > 0 ? lastVisible.MinWidth : 30;
                        double available = lastVisible.ActualWidth - colMin;
                        if (available > 0) {
                            double shrink = Math.Min(-delta, available);
                            lastVisible.Width = new DataGridLength(lastVisible.ActualWidth - shrink);
                        }
                    } else {
                        // No saved widths - cascade shrink from right to left
                        double remainingShrink = -delta;

                        for (int i = 0; i < visibleColumns.Count && remainingShrink > 0; i++) {
                            var col = visibleColumns[i];
                            double colMin = col.MinWidth > 0 ? col.MinWidth : 30;
                            double available = col.ActualWidth - colMin;

                            if (available > 0) {
                                double shrink = Math.Min(remainingShrink, available);
                                col.Width = new DataGridLength(col.ActualWidth - shrink);
                                remainingShrink -= shrink;
                            }
                        }
                    }
                }

                UpdateStoredWidths(grid);
                // Only save if not auto-adjusting after loading saved widths
                if (!hasSavedWidths) {
                    SaveColumnWidthsDebounced(grid);
                }
            } finally {
                _isAdjustingColumns = false;
            }
        }

        private void UpdateStoredWidths() => UpdateStoredWidths(TexturesDataGrid);

        private void UpdateStoredWidths(DataGrid grid) {
            if (grid == null || !_previousColumnWidths.TryGetValue(grid, out var prevWidths)) return;
            for (int i = 0; i < grid.Columns.Count && i < prevWidths.Length; i++) {
                prevWidths[i] = grid.Columns[i].ActualWidth;
            }
        }

        // Legacy method name for compatibility
        private void AdjustLastColumnToFill(DataGrid grid) => FillRemainingSpace(grid);

        private void UpdateColumnHeadersBasedOnWidth(DataGrid grid) {
            if (grid == null || grid.Columns.Count <= 1) return;

            // Start from column index 1 to skip Export checkbox column
            // TextureColumnHeaders[i] maps to grid.Columns[i + 1]
            for (int i = 0; i < TextureColumnHeaders.Length && i + 1 < grid.Columns.Count; i++) {
                var column = grid.Columns[i + 1];  // +1 to skip Export column
                double actualWidth = column.ActualWidth;

                // Skip if width not yet calculated
                if (actualWidth <= 0) continue;

                // Check if column width is less than minimum needed for full name
                bool needShort = actualWidth < TextureColumnHeaders[i].MinWidthForFull;

                // Only update if state changed
                if (needShort != _columnUsingShortHeader[i]) {
                    _columnUsingShortHeader[i] = needShort;
                    column.Header = needShort ? TextureColumnHeaders[i].Short : TextureColumnHeaders[i].Full;
                }
            }
        }

        // Debounce timers for saving column widths per DataGrid
        private readonly Dictionary<DataGrid, DispatcherTimer> _saveColumnWidthsTimers = new();
        private readonly Dictionary<DataGrid, EventHandler> _saveColumnWidthsHandlers = new();
        private readonly HashSet<DataGrid> _columnWidthsLoadedGrids = new();
        private readonly Dictionary<DataGrid, DateTime> _columnWidthsLoadedTime = new();
        private readonly HashSet<DataGrid> _hasSavedWidthsFromSettings = new(); // Grids with user-saved widths

        private void SaveColumnWidthsDebounced() => SaveColumnWidthsDebounced(TexturesDataGrid);

        private void SaveColumnWidthsDebounced(DataGrid grid) {
            if (!_columnWidthsLoadedGrids.Contains(grid)) {
                return; // Don't save during initial load
            }

            // Don't save within 2 seconds after loading (prevents immediate overwrite from FillRemainingSpace)
            if (_columnWidthsLoadedTime.TryGetValue(grid, out var loadedTime) &&
                (DateTime.Now - loadedTime).TotalSeconds < 2) {
                return;
            }

            if (!_saveColumnWidthsTimers.TryGetValue(grid, out var timer)) {
                timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _saveColumnWidthsTimers[grid] = timer;

                // Create and store handler to allow proper unsubscription
                EventHandler handler = (s, e) => {
                    timer.Stop();
                    SaveColumnWidths(grid);
                };
                _saveColumnWidthsHandlers[grid] = handler;
                timer.Tick += handler;
            }

            timer.Stop();
            timer.Start();
        }

        private void SaveColumnWidths(DataGrid grid) {
            if (grid == null || grid.Columns.Count == 0) return;

            var widths = new List<string>();
            foreach (var column in grid.Columns) {
                widths.Add(column.ActualWidth.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
            }

            string widthsStr = string.Join(",", widths);
            string settingName = GetColumnWidthsSettingName(grid);

            var currentValue = (string?)typeof(AppSettings).GetProperty(settingName)?.GetValue(AppSettings.Default);
            if (currentValue != widthsStr) {
                typeof(AppSettings).GetProperty(settingName)?.SetValue(AppSettings.Default, widthsStr);
                AppSettings.Default.Save();
            }
        }

        private void LoadColumnWidths(DataGrid grid) {
            string settingName = GetColumnWidthsSettingName(grid);
            string? widthsStr = (string?)typeof(AppSettings).GetProperty(settingName)?.GetValue(AppSettings.Default);

            if (!string.IsNullOrEmpty(widthsStr)) {
                string[] parts = widthsStr.Split(',');
                for (int i = 0; i < parts.Length && i < grid.Columns.Count; i++) {
                    if (double.TryParse(parts[i], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double width) && width > 0) {
                        grid.Columns[i].Width = new DataGridLength(width);
                    }
                }
                _hasSavedWidthsFromSettings.Add(grid); // Mark that this grid has user-saved widths
            }

            _columnWidthsLoadedGrids.Add(grid);
            _columnWidthsLoadedTime[grid] = DateTime.Now;
        }

        private void SaveColumnOrder(DataGrid grid) {
            if (grid == null || grid.Columns.Count == 0) return;

            var order = string.Join(",", grid.Columns.Select(c => c.DisplayIndex));
            string settingName = GetColumnOrderSettingName(grid);
            typeof(AppSettings).GetProperty(settingName)?.SetValue(AppSettings.Default, order);
            AppSettings.Default.Save();
        }

        private void LoadColumnOrder(DataGrid grid) {
            string settingName = GetColumnOrderSettingName(grid);
            string? orderStr = (string?)typeof(AppSettings).GetProperty(settingName)?.GetValue(AppSettings.Default);

            if (string.IsNullOrEmpty(orderStr)) return;

            string[] parts = orderStr.Split(',');
            for (int i = 0; i < parts.Length && i < grid.Columns.Count; i++) {
                if (int.TryParse(parts[i], out int displayIndex) && displayIndex >= 0 && displayIndex < grid.Columns.Count) {
                    grid.Columns[i].DisplayIndex = displayIndex;
                }
            }
        }

        private string GetColumnWidthsSettingName(DataGrid grid) {
            if (grid == TexturesDataGrid) return nameof(AppSettings.TexturesColumnWidths);
            if (grid == ModelsDataGrid) return nameof(AppSettings.ModelsColumnWidths);
            if (grid == MaterialsDataGrid) return nameof(AppSettings.MaterialsColumnWidths);
            return nameof(AppSettings.TexturesColumnWidths);
        }

        private string GetColumnOrderSettingName(DataGrid grid) {
            if (grid == TexturesDataGrid) return nameof(AppSettings.TexturesColumnOrder);
            if (grid == ModelsDataGrid) return nameof(AppSettings.ModelsColumnOrder);
            if (grid == MaterialsDataGrid) return nameof(AppSettings.MaterialsColumnOrder);
            return nameof(AppSettings.TexturesColumnOrder);
        }





private void ToggleViewerButton_Click(object? sender, RoutedEventArgs e) {
            if (isViewerVisible == true) {
                // Save current width before hiding
                if (PreviewColumn.Width.Value > 0) {
                    AppSettings.Default.RightPanelPreviousWidth = PreviewColumn.Width.Value;
                }
                AppSettings.Default.RightPanelWidth = 0; // Mark as hidden
                ToggleViewButton.Content = "►";
                PreviewColumn.Width = new GridLength(0);
                PreviewColumn.MinWidth = 0;
            } else {
                // Restore saved width
                double restoreWidth = AppSettings.Default.RightPanelPreviousWidth;
                if (restoreWidth < 256) restoreWidth = 300; // Use default if too small
                ToggleViewButton.Content = "◄";
                PreviewColumn.MinWidth = 256;
                PreviewColumn.Width = new GridLength(restoreWidth);
                AppSettings.Default.RightPanelWidth = restoreWidth;
            }
            isViewerVisible = !isViewerVisible;
        }

        private void RightPanelSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) {
            // Save the new width when user finishes dragging the splitter
            if (PreviewColumn.Width.Value > 0) {
                AppSettings.Default.RightPanelWidth = PreviewColumn.Width.Value;
                AppSettings.Default.RightPanelPreviousWidth = PreviewColumn.Width.Value;
            }
        }

        /// <summary>
        /// Оптимизированная сортировка коллекции для TexturesDataGrid
        /// </summary>
        


private void TexturesDataGrid_Sorting(object? sender, DataGridSortingEventArgs e) {
            OptimizeDataGridSorting(TexturesDataGrid, e);
        }

        /// <summary>
        /// ���������������� ���������� ���������� ��� ModelsDataGrid
        /// </summary>
        


        private void ModelsDataGrid_Sorting(object? sender, DataGridSortingEventArgs e) {
            OptimizeDataGridSorting(ModelsDataGrid, e);
        }

        private void MaterialsDataGrid_Sorting(object? sender, DataGridSortingEventArgs e) {
            OptimizeDataGridSorting(MaterialsDataGrid, e);
        }

        private void OptimizeDataGridSorting(DataGrid dataGrid, DataGridSortingEventArgs e) {
            if (e.Column == null) return;

            e.Handled = true;
            var column = e.Column;

            // Get sort property
            string sortPath = column.SortMemberPath;
            if (string.IsNullOrEmpty(sortPath)) {
                if (column is DataGridBoundColumn boundCol && boundCol.Binding is Binding binding) {
                    sortPath = binding.Path.Path;
                }
            }
            if (string.IsNullOrEmpty(sortPath)) {
                sortPath = column.Header?.ToString() ?? "";
            }
            if (string.IsNullOrEmpty(sortPath)) return;

            // Read current state
            var currentDir = column.SortDirection;

            // Toggle
            var newDir = (currentDir == ListSortDirection.Ascending)
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;

            // Clear other columns
            foreach (var col in dataGrid.Columns) {
                if (col != column)
                    col.SortDirection = null;
            }

            // Apply CustomSort (SortDescriptions doesn't work with our types)
            if (CollectionViewSource.GetDefaultView(dataGrid.ItemsSource) is ListCollectionView listView) {
                listView.CustomSort = new ResourceComparer(sortPath, newDir);
            }

            // Set arrow via Dispatcher to ensure it happens after WPF processing
            _ = Dispatcher.BeginInvoke(new Action(() => {
                column.SortDirection = newDir;
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

#endregion

        #region Column Visibility Management

        private void GroupTexturesCheckBox_Changed(object sender, RoutedEventArgs e) {
            if (GroupTexturesCheckBox.IsChecked == true) {
                ICollectionView view = CollectionViewSource.GetDefaultView(TexturesDataGrid.ItemsSource);
                if (view != null && view.CanGroup) {
                    view.GroupDescriptions.Clear();
                    view.GroupDescriptions.Add(new PropertyGroupDescription("GroupName"));
                    // Второй уровень группировки для ORM подгрупп (ao/gloss/metallic/height под og/ogm/ogmh)
                    view.GroupDescriptions.Add(new PropertyGroupDescription("SubGroupName"));
                }
            } else {
                ICollectionView view = CollectionViewSource.GetDefaultView(TexturesDataGrid.ItemsSource);
                if (view != null) {
                    view.GroupDescriptions.Clear();
                }
            }
        }

        /// <summary>
        /// Applies texture grouping if the GroupTextures checkbox is checked.
        /// Called after loading assets to apply default grouping.
        /// </summary>
        private void ApplyTextureGroupingIfEnabled() {
            if (GroupTexturesCheckBox.IsChecked == true) {
                ICollectionView view = CollectionViewSource.GetDefaultView(TexturesDataGrid.ItemsSource);
                if (view != null && view.CanGroup) {
                    view.GroupDescriptions.Clear();
                    view.GroupDescriptions.Add(new PropertyGroupDescription("GroupName"));
                    view.GroupDescriptions.Add(new PropertyGroupDescription("SubGroupName"));
                }
            }
        }

        /// <summary>
        /// Обработчик клика на заголовок ORM подгруппы - показывает настройки ORM
        /// </summary>
        private void ORMSubGroupHeader_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            // Останавливаем событие чтобы Expander не сворачивался
            e.Handled = true;

            if (sender is FrameworkElement element && element.DataContext is CollectionViewGroup group) {
                // Получаем имя подгруппы (это имя ORM текстуры)
                string? subGroupName = group.Name?.ToString();
                if (string.IsNullOrEmpty(subGroupName)) return;

                // Ищем любую текстуру из этой подгруппы чтобы получить ParentORMTexture
                var textureInGroup = viewModel.Textures.FirstOrDefault(t => t.SubGroupName == subGroupName);
                if (textureInGroup?.ParentORMTexture != null) {
                    var ormTexture = textureInGroup.ParentORMTexture;
                    logService.LogInfo($"ORM subgroup clicked: {ormTexture.Name} ({ormTexture.PackingMode})");

                    // Устанавливаем выбранную подгруппу для визуального выделения
                    SelectedORMSubGroupName = subGroupName;

                    // Снимаем выделение с обычных строк DataGrid
                    TexturesDataGrid.SelectedItem = null;

                    // Показываем ORM панель настроек (как при выборе ORM в DataGrid)
                    if (ConversionSettingsExpander != null) {
                        ConversionSettingsExpander.Visibility = Visibility.Collapsed;
                    }

                    if (ORMPanel != null) {
                        ORMPanel.Visibility = Visibility.Visible;

                        // Инициализируем ORM панель с доступными текстурами (исключаем ORM текстуры)
                        var availableTextures = viewModel.Textures.Where(t => !(t is ORMTextureResource)).ToList();
                        ORMPanel.Initialize(this, availableTextures);
                        ORMPanel.SetORMTexture(ormTexture);
                    }

                    // Обновляем информацию о текстуре в preview панели
                    TextureNameTextBlock.Text = "Texture Name: " + ormTexture.Name;
                    TextureColorSpaceTextBlock.Text = "Color Space: Linear (ORM)";

                    // Если ORM уже упакована - загружаем preview
                    if (!string.IsNullOrEmpty(ormTexture.Path) && File.Exists(ormTexture.Path)) {
                        TextureResolutionTextBlock.Text = ormTexture.Resolution != null && ormTexture.Resolution.Length >= 2
                            ? $"Resolution: {ormTexture.Resolution[0]}x{ormTexture.Resolution[1]}"
                            : "Resolution: Unknown";
                        TextureFormatTextBlock.Text = "Format: KTX2 (packed)";

                        // Загружаем preview асинхронно
                        _ = LoadORMPreviewAsync(ormTexture);
                    } else {
                        TextureResolutionTextBlock.Text = "Resolution: Not packed yet";
                        TextureFormatTextBlock.Text = "Format: Not packed";
                        ResetPreviewState();
                        ClearD3D11Viewer();
                    }

                    // Обновляем ViewModel.SelectedTexture
                    viewModel.SelectedTexture = ormTexture;
                }
            }
        }

        /// <summary>
        /// Загружает preview для упакованной ORM текстуры
        /// </summary>
        private async Task LoadORMPreviewAsync(ORMTextureResource ormTexture) {
            // Cancel any pending texture load
            textureLoadCancellation?.Cancel();
            textureLoadCancellation = new CancellationTokenSource();
            var cancellationToken = textureLoadCancellation.Token;

            try {
                bool ktxLoaded = false;

                if (texturePreviewService.IsUsingD3D11Renderer) {
                    // D3D11 MODE: Try native KTX2 loading
                    logger.Info($"[LoadORMPreviewAsync] Loading packed ORM to D3D11: {ormTexture.Name}");
                    ktxLoaded = await TryLoadKtx2ToD3D11Async(ormTexture, cancellationToken);

                    if (ktxLoaded && !cancellationToken.IsCancellationRequested) {
                        // Extract mip0 bitmap for histogram calculation (fire-and-forget with timeout)
                        string? ormPath = ormTexture.Path;
                        string ormName = ormTexture.Name;
                        logger.Info($"[LoadORMPreviewAsync] Starting histogram extraction for: {ormName}, path: {ormPath}");

                        // DIAGNOSTIC: Add delay to let LoadTexture complete first
                        // This tests if the freeze is caused by concurrent execution
                        _ = Task.Run(async () => {
                            try {
                                // Wait for LoadTexture to complete (queued via BeginInvoke)
                                await Task.Delay(200, cancellationToken).ConfigureAwait(false);

                                if (string.IsNullOrEmpty(ormPath)) {
                                    logger.Warn($"[LoadORMPreviewAsync] ORM path is empty for: {ormName}");
                                    return;
                                }

                                logger.Info($"[LoadORMPreviewAsync] Extracting mipmaps from: {ormPath}");

                                // Add timeout to prevent hanging
                                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                                var mipmaps = await texturePreviewService.LoadKtx2MipmapsAsync(ormPath, linkedCts.Token).ConfigureAwait(false);
                                logger.Info($"[LoadORMPreviewAsync] Extracted {mipmaps.Count} mipmaps for: {ormName}");

                                if (mipmaps.Count > 0 && !linkedCts.Token.IsCancellationRequested) {
                                    var mip0Bitmap = mipmaps[0].Bitmap;
                                    logger.Info($"[LoadORMPreviewAsync] Got mip0 bitmap {mip0Bitmap.PixelWidth}x{mip0Bitmap.PixelHeight} for: {ormName}");

                                    // Use BeginInvoke to avoid deadlock
                                    _ = Dispatcher.BeginInvoke(new Action(() => {
                                        if (!cancellationToken.IsCancellationRequested) {
                                            texturePreviewService.OriginalFileBitmapSource = mip0Bitmap;
                                            UpdateHistogram(mip0Bitmap);
                                            logger.Info($"[LoadORMPreviewAsync] Histogram updated for ORM: {ormName}");
                                        } else {
                                            logger.Info($"[LoadORMPreviewAsync] Cancelled before histogram update: {ormName}");
                                        }
                                    }));
                                } else {
                                    logger.Warn($"[LoadORMPreviewAsync] No mipmaps or cancelled for: {ormName}");
                                }
                            } catch (OperationCanceledException) {
                                logger.Info($"[LoadORMPreviewAsync] Histogram extraction cancelled/timeout for: {ormName}");
                            } catch (Exception ex) {
                                logger.Warn(ex, $"[LoadORMPreviewAsync] Failed to extract bitmap for histogram: {ormName}");
                            }
                        });
                    } else {
                        logger.Info($"[LoadORMPreviewAsync] Histogram skipped - ktxLoaded={ktxLoaded}, cancelled={cancellationToken.IsCancellationRequested}");
                    }

                    if (!ktxLoaded) {
                        // Fallback: Try extracting PNG from KTX2
                        logger.Info($"[LoadORMPreviewAsync] D3D11 native loading failed, trying PNG extraction: {ormTexture.Name}");
                        ktxLoaded = await TryLoadKtx2PreviewAsync(ormTexture, cancellationToken);
                    }
                } else {
                    // WPF MODE: Extract PNG from KTX2
                    ktxLoaded = await TryLoadKtx2PreviewAsync(ormTexture, cancellationToken);
                }

                if (!ktxLoaded && !cancellationToken.IsCancellationRequested) {
                    await Dispatcher.InvokeAsync(() => {
                        texturePreviewService.IsKtxPreviewAvailable = false;
                        TextureFormatTextBlock.Text = "Format: KTX2 (preview unavailable)";
                        logService.LogWarn($"Failed to load preview for packed ORM texture: {ormTexture.Name}");
                    });
                }
            } catch (OperationCanceledException) {
                logService.LogInfo($"[LoadORMPreviewAsync] Cancelled for ORM: {ormTexture.Name}");
            } catch (Exception ex) {
                logService.LogError($"Error loading packed ORM texture {ormTexture.Name}: {ex.Message}");
                ResetPreviewState();
                ClearD3D11Viewer();
            }
        }

        /// <summary>
        /// Scale slider changed - force star columns to recalculate
        /// </summary>
        private void TableScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            RefreshStarColumns(TexturesDataGrid);
            RefreshStarColumns(ModelsDataGrid);
            RefreshStarColumns(MaterialsDataGrid);
        }

        /// <summary>
        /// Force star-width columns to recalculate by toggling their width
        /// </summary>
        private static void RefreshStarColumns(DataGrid? dataGrid) {
            if (dataGrid == null || !dataGrid.IsLoaded) return;

            foreach (var col in dataGrid.Columns) {
                if (col.Width.IsStar) {
                    var starValue = col.Width.Value;
                    col.Width = new DataGridLength(0, DataGridLengthUnitType.Auto);
                    col.Width = new DataGridLength(starValue, DataGridLengthUnitType.Star);
                }
            }
        }

        private void TextureColumnVisibility_Click(object sender, RoutedEventArgs e) {
            if (sender is MenuItem menuItem && menuItem.Tag is string columnTag) {
                // Column indices: 0=№, 1=ID, 2=TextureName, 3=Extension, 4=Size, 5=Compressed,
                // 6=Resolution, 7=ResizeResolution, 8=Compression(Format), 9=Mipmaps, 10=Preset, 11=Status
                int columnIndex = columnTag switch {
                    "ID" => 1,
                    "TextureName" => 2,
                    "Extension" => 3,
                    "Size" => 4,
                    "Compressed" => 5,
                    "Resolution" => 6,
                    "ResizeResolution" => 7,
                    "Compression" => 8,
                    "Mipmaps" => 9,
                    "Preset" => 10,
                    "Status" => 11,
                    _ => -1
                };

                if (columnIndex >= 0 && columnIndex < TexturesDataGrid.Columns.Count) {
                    TexturesDataGrid.Columns[columnIndex].Visibility = menuItem.IsChecked ? Visibility.Visible : Visibility.Collapsed;
                    SubscribeToColumnWidthChanges();
                    AdjustLastColumnToFill(TexturesDataGrid);
                    SaveColumnVisibility(TexturesDataGrid, nameof(AppSettings.TexturesColumnVisibility));
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
                    FillRemainingSpaceForGrid(MaterialsDataGrid);
                    SaveColumnVisibility(MaterialsDataGrid, nameof(AppSettings.MaterialsColumnVisibility));
                }
            }
        }

        private void ModelColumnVisibility_Click(object sender, RoutedEventArgs e) {
            if (sender is MenuItem menuItem && menuItem.Tag is string columnTag) {
                int columnIndex = columnTag switch {
                    "ID" => 1,
                    "Name" => 2,
                    "Size" => 3,
                    "UVChannels" => 4,
                    "Extension" => 5,
                    "Status" => 6,
                    _ => -1
                };

                if (columnIndex >= 0 && columnIndex < ModelsDataGrid.Columns.Count) {
                    ModelsDataGrid.Columns[columnIndex].Visibility = menuItem.IsChecked ? Visibility.Visible : Visibility.Collapsed;
                    FillRemainingSpaceForGrid(ModelsDataGrid);
                    SaveColumnVisibility(ModelsDataGrid, nameof(AppSettings.ModelsColumnVisibility));
                }
            }
        }

        // Save/Load column visibility
        private void SaveColumnVisibility(DataGrid grid, string settingName) {
            var visibility = string.Join(",", grid.Columns.Select(c => c.Visibility == Visibility.Visible ? "1" : "0"));
            typeof(AppSettings).GetProperty(settingName)?.SetValue(AppSettings.Default, visibility);
            AppSettings.Default.Save();
        }

        private void LoadColumnVisibility(DataGrid grid, string settingName, ContextMenu? headerContextMenu = null) {
            var visibility = (string?)typeof(AppSettings).GetProperty(settingName)?.GetValue(AppSettings.Default);
            if (string.IsNullOrEmpty(visibility)) return;

            var parts = visibility.Split(',');
            for (int i = 0; i < parts.Length && i < grid.Columns.Count; i++) {
                grid.Columns[i].Visibility = parts[i] == "1" ? Visibility.Visible : Visibility.Collapsed;
            }

            // Update context menu checkboxes
            if (headerContextMenu != null) {
                UpdateColumnVisibilityMenuItems(grid, headerContextMenu);
            }
        }

        private void UpdateColumnVisibilityMenuItems(DataGrid grid, ContextMenu contextMenu) {
            if (contextMenu.Items[0] is MenuItem columnsMenu && columnsMenu.Header?.ToString() == "Columns Visibility") {
                foreach (MenuItem item in columnsMenu.Items) {
                    if (item.Tag is string tag) {
                        int colIndex = GetColumnIndexByTag(grid, tag);
                        if (colIndex >= 0 && colIndex < grid.Columns.Count) {
                            item.IsChecked = grid.Columns[colIndex].Visibility == Visibility.Visible;
                        }
                    }
                }
            }
        }

        private int GetColumnIndexByTag(DataGrid grid, string tag) {
            if (grid == TexturesDataGrid) {
                return tag switch {
                    "ID" => 1, "TextureName" => 2, "Extension" => 3, "Size" => 4,
                    "Compressed" => 5, "Resolution" => 6, "ResizeResolution" => 7,
                    "Compression" => 8, "Mipmaps" => 9, "Preset" => 10, "Status" => 11,
                    _ => -1
                };
            } else if (grid == ModelsDataGrid) {
                return tag switch {
                    "ID" => 1, "Name" => 2, "Size" => 3, "UVChannels" => 4, "Extension" => 5, "Status" => 6,
                    _ => -1
                };
            } else if (grid == MaterialsDataGrid) {
                return tag switch {
                    "ID" => 1, "Name" => 2, "Status" => 3,
                    _ => -1
                };
            }
            return -1;
        }

        // Generic handlers for Models and Materials DataGrids
        private void ModelsDataGrid_SizeChanged(object sender, SizeChangedEventArgs e) {
            if (sender is not DataGrid grid) return;
            InitializeGridColumnsIfNeeded(grid);
            SubscribeDataGridColumnResizing(grid); // Subscribe to left gripper when grid becomes visible
            FillRemainingSpaceForGrid(grid);
        }

        private void ModelsDataGrid_ColumnDisplayIndexChanged(object? sender, DataGridColumnEventArgs e) {
            _ = Dispatcher.BeginInvoke(new Action(() => {
                SubscribeToColumnWidthChanges(ModelsDataGrid);
                SubscribeDataGridColumnResizing(ModelsDataGrid);
                FillRemainingSpaceForGrid(ModelsDataGrid);
                SaveColumnOrder(ModelsDataGrid);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void MaterialsDataGrid_SizeChanged(object sender, SizeChangedEventArgs e) {
            if (sender is not DataGrid grid) return;
            InitializeGridColumnsIfNeeded(grid);
            SubscribeDataGridColumnResizing(grid); // Subscribe to left gripper when grid becomes visible
            FillRemainingSpaceForGrid(grid);
        }

        private void MaterialsDataGrid_ColumnDisplayIndexChanged(object? sender, DataGridColumnEventArgs e) {
            _ = Dispatcher.BeginInvoke(new Action(() => {
                SubscribeToColumnWidthChanges(MaterialsDataGrid);
                SubscribeDataGridColumnResizing(MaterialsDataGrid);
                FillRemainingSpaceForGrid(MaterialsDataGrid);
                SaveColumnOrder(MaterialsDataGrid);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void InitializeGridColumnsIfNeeded(DataGrid grid) {
            if (grid == null || grid.Columns.Count == 0) return;
            if (_columnWidthsLoadedGrids.Contains(grid)) return;

            // Load saved visibility, widths, and order
            LoadColumnVisibility(grid, GetColumnVisibilitySettingName(grid));
            LoadColumnWidths(grid);
            LoadColumnOrder(grid);
            SubscribeToColumnWidthChanges(grid);
        }

        private string GetColumnVisibilitySettingName(DataGrid grid) {
            if (grid == TexturesDataGrid) return nameof(AppSettings.TexturesColumnVisibility);
            if (grid == ModelsDataGrid) return nameof(AppSettings.ModelsColumnVisibility);
            if (grid == MaterialsDataGrid) return nameof(AppSettings.MaterialsColumnVisibility);
            return nameof(AppSettings.TexturesColumnVisibility);
        }

        private void FillRemainingSpaceForGrid(DataGrid grid) {
            if (grid == null || grid.Columns.Count == 0 || _isAdjustingColumns) return;

            _isAdjustingColumns = true;
            try {
                double availableWidth = grid.ActualWidth - SystemParameters.VerticalScrollBarWidth - 2;
                if (availableWidth <= 0) return;

                // Get visible columns ordered by DisplayIndex (rightmost first for shrinking)
                var visibleColumns = grid.Columns
                    .Where(c => c.Visibility == Visibility.Visible)
                    .OrderByDescending(c => c.DisplayIndex)
                    .ToList();

                if (visibleColumns.Count == 0) return;

                double totalWidth = visibleColumns.Sum(c => c.ActualWidth);
                double delta = availableWidth - totalWidth;

                if (Math.Abs(delta) < 1) return;

                // Check if this grid has user-saved widths loaded
                bool hasSavedWidths = _hasSavedWidthsFromSettings.Contains(grid);
                var lastVisible = visibleColumns[0]; // Already sorted, first is rightmost

                if (delta > 0) {
                    // Table expanded - add space to last visible column
                    lastVisible.Width = new DataGridLength(lastVisible.ActualWidth + delta);
                } else {
                    if (hasSavedWidths) {
                        // Grid has saved widths - only shrink the last column to preserve user settings
                        double colMin = lastVisible.MinWidth > 0 ? lastVisible.MinWidth : 30;
                        double available = lastVisible.ActualWidth - colMin;
                        if (available > 0) {
                            double shrink = Math.Min(-delta, available);
                            lastVisible.Width = new DataGridLength(lastVisible.ActualWidth - shrink);
                        }
                    } else {
                        // No saved widths - cascade shrink from right to left
                        double remainingShrink = -delta;

                        for (int i = 0; i < visibleColumns.Count && remainingShrink > 0; i++) {
                            var col = visibleColumns[i];
                            double colMin = col.MinWidth > 0 ? col.MinWidth : 30;
                            double available = col.ActualWidth - colMin;

                            if (available > 0) {
                                double shrink = Math.Min(remainingShrink, available);
                                col.Width = new DataGridLength(col.ActualWidth - shrink);
                                remainingShrink -= shrink;
                            }
                        }
                    }
                }

                // Only save if not auto-adjusting after loading saved widths
                if (!hasSavedWidths) {
                    SaveColumnWidthsDebounced(grid);
                }
            } finally {
                _isAdjustingColumns = false;
            }
        }

        #endregion

        #region Materials

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



                // ��������� ��������� ��������� � ComboBox ��� Color Channel � UV Channel
                MaterialDiffuseColorChannelComboBox.SelectedItem = parameters.DiffuseColorChannel?.ToString();
                MaterialSpecularColorChannelComboBox.SelectedItem = parameters.SpecularColorChannel?.ToString();
                MaterialMetalnessColorChannelComboBox.SelectedItem = parameters.MetalnessColorChannel?.ToString();
                MaterialGlossinessColorChannelComboBox.SelectedItem = parameters.GlossinessColorChannel?.ToString();
                MaterialAOColorChannelComboBox.SelectedItem = parameters.AOChannel?.ToString();

                // Load ORM Settings
                LoadORMSettingsToUI(parameters.ORMSettings);
            });
        }

        private bool _isLoadingORMSettings = false;

        private void LoadORMSettingsToUI(Resources.MaterialORMSettings settings) {
            _isLoadingORMSettings = true;
            try {
                // Load preset list
                RefreshORMPresetComboBox();

                // Select current preset
                var presetName = settings.PresetName ?? "Standard";
                for (int i = 0; i < ORMPresetComboBox.Items.Count; i++) {
                    if (ORMPresetComboBox.Items[i] is TextureConversion.Core.ORMSettings preset &&
                        preset.Name == presetName) {
                        ORMPresetComboBox.SelectedIndex = i;
                        break;
                    }
                }

                // Load values from effective settings
                var effectiveSettings = settings.GetEffectiveSettings();

                ORMEnabledCheckBox.IsChecked = settings.Enabled;
                ORMApplyToksvigCheckBox.IsChecked = effectiveSettings.ToksvigEnabled;
                ORMAOBiasSlider.Value = effectiveSettings.AOBias;

                // Packing Mode
                ORMPackingModeComboBox.SelectedIndex = effectiveSettings.PackingMode switch {
                    TextureConversion.Core.ChannelPackingMode.Auto => 0,
                    TextureConversion.Core.ChannelPackingMode.None => 1,
                    TextureConversion.Core.ChannelPackingMode.OG => 2,
                    TextureConversion.Core.ChannelPackingMode.OGM => 3,
                    TextureConversion.Core.ChannelPackingMode.OGMH => 4,
                    _ => 0
                };

                // AO Processing Mode
                ORMAOProcessingComboBox.SelectedIndex = effectiveSettings.AOProcessing switch {
                    TextureConversion.Core.AOProcessingMode.None => 0,
                    TextureConversion.Core.AOProcessingMode.BiasedDarkening => 1,
                    TextureConversion.Core.AOProcessingMode.Percentile => 2,
                    _ => 1
                };

                // Update preset description
                UpdateORMPresetDescription(effectiveSettings);
            } finally {
                _isLoadingORMSettings = false;
            }
        }

        private void RefreshORMPresetComboBox() {
            var currentSelection = ORMPresetComboBox.SelectedItem as TextureConversion.Core.ORMSettings;
            ORMPresetComboBox.Items.Clear();

            var presets = TextureConversion.Settings.ORMPresetManager.Instance.GetAllPresets();
            foreach (var preset in presets) {
                ORMPresetComboBox.Items.Add(preset);
            }

            ORMPresetComboBox.DisplayMemberPath = "Name";

            // Restore selection
            if (currentSelection != null) {
                for (int i = 0; i < ORMPresetComboBox.Items.Count; i++) {
                    if (ORMPresetComboBox.Items[i] is TextureConversion.Core.ORMSettings preset &&
                        preset.Name == currentSelection.Name) {
                        ORMPresetComboBox.SelectedIndex = i;
                        break;
                    }
                }
            }
        }

        private void UpdateORMPresetDescription(TextureConversion.Core.ORMSettings settings) {
            ORMPresetDescriptionText.Text = settings.Description;
        }

        private void ORMPreset_Changed(object sender, SelectionChangedEventArgs e) {
            if (_isLoadingORMSettings) return;
            if (ORMPresetComboBox.SelectedItem is not TextureConversion.Core.ORMSettings selectedPreset) return;
            if (MaterialsDataGrid.SelectedItem is not MaterialResource selectedMaterial) return;

            _isLoadingORMSettings = true;
            try {
                // Apply preset
                selectedMaterial.ORMSettings.ApplyPreset(selectedPreset.Name);

                // Update UI
                var settings = selectedMaterial.ORMSettings.GetEffectiveSettings();
                ORMApplyToksvigCheckBox.IsChecked = settings.ToksvigEnabled;
                ORMAOBiasSlider.Value = settings.AOBias;

                ORMPackingModeComboBox.SelectedIndex = settings.PackingMode switch {
                    TextureConversion.Core.ChannelPackingMode.Auto => 0,
                    TextureConversion.Core.ChannelPackingMode.None => 1,
                    TextureConversion.Core.ChannelPackingMode.OG => 2,
                    TextureConversion.Core.ChannelPackingMode.OGM => 3,
                    TextureConversion.Core.ChannelPackingMode.OGMH => 4,
                    _ => 0
                };

                ORMAOProcessingComboBox.SelectedIndex = settings.AOProcessing switch {
                    TextureConversion.Core.AOProcessingMode.None => 0,
                    TextureConversion.Core.AOProcessingMode.BiasedDarkening => 1,
                    TextureConversion.Core.AOProcessingMode.Percentile => 2,
                    _ => 1
                };

                UpdateORMPresetDescription(settings);
            } finally {
                _isLoadingORMSettings = false;
            }
        }

        private void ORMEditPreset_Click(object sender, RoutedEventArgs e) {
            if (ORMPresetComboBox.SelectedItem is not ORMSettings selectedPreset) return;

            // Clone preset for editing
            var presetToEdit = selectedPreset.Clone();

            var editorWindow = new ORMPresetEditorWindow(presetToEdit) {
                Owner = this
            };

            if (editorWindow.ShowDialog() == true && editorWindow.EditedPreset != null) {
                var editedPreset = editorWindow.EditedPreset;

                if (selectedPreset.IsBuiltIn) {
                    // Built-in presets can't be modified, save as new
                    if (ORMPresetManager.Instance.AddPreset(editedPreset)) {
                        RefreshORMPresetComboBox();
                        ORMPresetComboBox.SelectedItem = ORMPresetManager.Instance.GetPreset(editedPreset.Name);
                    }
                } else {
                    // Update existing preset
                    if (ORMPresetManager.Instance.UpdatePreset(selectedPreset.Name, editedPreset)) {
                        RefreshORMPresetComboBox();
                        ORMPresetComboBox.SelectedItem = ORMPresetManager.Instance.GetPreset(editedPreset.Name);
                    }
                }
            }
        }

        private void ORMSettings_Changed(object sender, RoutedEventArgs e) {
            if (_isLoadingORMSettings) return;
            SaveORMSettingsFromUI();
        }

        private void ORMSettings_Changed(object sender, SelectionChangedEventArgs e) {
            if (_isLoadingORMSettings) return;
            // Skip if this is the preset combo box
            if (sender == ORMPresetComboBox) return;
            SaveORMSettingsFromUI();
        }

        private void ORMSettings_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) {
            if (_isLoadingORMSettings) return;
            SaveORMSettingsFromUI();
        }

        private void SaveORMSettingsFromUI() {
            if (MaterialsDataGrid.SelectedItem is not MaterialResource selectedMaterial) return;

            selectedMaterial.ORMSettings.Enabled = ORMEnabledCheckBox.IsChecked ?? true;
            selectedMaterial.ORMSettings.Settings.ToksvigEnabled = ORMApplyToksvigCheckBox.IsChecked ?? true;
            selectedMaterial.ORMSettings.Settings.AOBias = (float)ORMAOBiasSlider.Value;

            // Packing Mode
            selectedMaterial.ORMSettings.Settings.PackingMode = ORMPackingModeComboBox.SelectedIndex switch {
                0 => TextureConversion.Core.ChannelPackingMode.Auto,
                1 => TextureConversion.Core.ChannelPackingMode.None,
                2 => TextureConversion.Core.ChannelPackingMode.OG,
                3 => TextureConversion.Core.ChannelPackingMode.OGM,
                4 => TextureConversion.Core.ChannelPackingMode.OGMH,
                _ => TextureConversion.Core.ChannelPackingMode.Auto
            };

            // AO Processing Mode
            selectedMaterial.ORMSettings.Settings.AOProcessing = ORMAOProcessingComboBox.SelectedIndex switch {
                0 => TextureConversion.Core.AOProcessingMode.None,
                1 => TextureConversion.Core.AOProcessingMode.BiasedDarkening,
                2 => TextureConversion.Core.AOProcessingMode.Percentile,
                _ => TextureConversion.Core.AOProcessingMode.BiasedDarkening
            };

            // Mark as custom (modified from preset)
            selectedMaterial.ORMSettings.PresetName = null;
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

                // ��������� ���������� ����� � ColorPicker
                colorPicker.SelectedColor = color;
            } else {
                colorRect.Background = new SolidColorBrush(Colors.Transparent);
                colorRect.Text = "No Tint";
                colorPicker.SelectedColor = null;
            }
        }

        private void UpdateHyperlinkAndVisibility(Hyperlink hyperlink, Expander expander, int? mapId, string mapName, MaterialResource material) {
            if (hyperlink != null && expander != null) {
                // ������������� DataContext ��� hyperlink, ����� �� ���� � ������ ��������� ���������
                hyperlink.DataContext = material;

                if (mapId.HasValue) {
                    TextureResource? texture = viewModel.Textures.FirstOrDefault(t => t.ID == mapId.Value);
                    if (texture != null && !string.IsNullOrEmpty(texture.Name)) {
                        // ��������� ID � NavigateUri � ���������������� ������ ��� ������������ ����������
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
                logger.Debug("����������� ������. NavigateUri: {NavigateUri}; ������� �����: {HyperlinkText}",
                             hyperlink.NavigateUri,
                             string.Concat(hyperlink.Inlines.OfType<Run>().Select(r => r.Text)));
            }
            else
            {
                logger.Warn("NavigateToTextureFromHyperlink ������ ������������ ���� {SenderType}, ��������� Hyperlink.", sender.GetType().FullName);
            }

            logger.Debug("������ ����� �� �����������. ��� �����������: {SenderType}; ��� DataContext: {DataContextType}; ��� ��������� � �������: {SelectedType}",
                         sender.GetType().FullName,
                         (sender as FrameworkContentElement)?.DataContext?.GetType().FullName ?? "<null>",
                         MaterialsDataGrid.SelectedItem?.GetType().FullName ?? "<null>");

            // 1) �������� ����� ID �������� �� Hyperlink.NavigateUri (�� ��������� ��� ��� ���������)
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

            // 2) ���� � NavigateUri ��� ��������, ������� ����� �� ���������
            if (!mapId.HasValue)
            {
                if (material == null) {
                    logger.Warn("�� ������� ���������� �������� ��� ����������� {MapType}.", mapType);
                    return;
                }

                mapId = mapIdSelector(material);
            }
            material ??= new MaterialResource { Name = "<unknown>", ID = -1 };
            if (!mapId.HasValue) {
                logger.Info("��� ��������� {MaterialName} ({MaterialId}) ����������� ������������� �������� {MapType}.", material.Name, material.ID, mapType);
                return;
            }

            logger.Info("������ �� ������� � �������� {MapType} � ID {TextureId} �� ��������� {MaterialName} ({MaterialId}).",
                        mapType,
                        mapId.Value,
                        material.Name,
                        material.ID);

            _ = Dispatcher.BeginInvoke(new Action(() => {
                if (TexturesTabItem != null) {
                    tabControl.SelectedItem = TexturesTabItem;
                    logger.Debug("������� ������� ������������ ����� TabControl.");
                }

                TextureResource? texture = viewModel.Textures.FirstOrDefault(t => t.ID == mapId.Value);
                if (texture != null) {
                    ICollectionView? view = CollectionViewSource.GetDefaultView(TexturesDataGrid.ItemsSource);
                    view?.MoveCurrentTo(texture);

                    TexturesDataGrid.SelectedItem = texture;
                    TexturesDataGrid.UpdateLayout();
                    TexturesDataGrid.ScrollIntoView(texture);
                    TexturesDataGrid.Focus();

                    logger.Info("�������� {TextureName} (ID {TextureId}) �������� � ���������� � ������� �������.", texture.Name, texture.ID);
                } else {
                    logger.Error("�������� � ID {TextureId} �� ������� � ���������. ����� �������: {TextureCount}.", mapId.Value, viewModel.Textures.Count);
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
                logger.Warn("TexturePreview_MouseLeftButtonUp ������ ������������ ���� {SenderType}, �������� Image.", sender.GetType().FullName);
                return;
            }

            MaterialResource? material = MaterialsDataGrid.SelectedItem as MaterialResource;
            if (material == null) {
                logger.Warn("�� ������� ���������� �������� ��� ������������� ��������.");
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
                logger.Info("��� ��������� {MaterialName} ({MaterialId}) ����������� ������������� �������� ���� {TextureType}.",
                    material.Name, material.ID, textureType);
                return;
            }

            logger.Info("���� �� ������ �������� {TextureType} � ID {TextureId} �� ��������� {MaterialName} ({MaterialId}).",
                textureType, textureId.Value, material.Name, material.ID);

            _ = Dispatcher.BeginInvoke(new Action(() => {
                if (TexturesTabItem != null) {
                    tabControl.SelectedItem = TexturesTabItem;
                    logger.Debug("������� ������� ������������ ����� TabControl.");
                }

                TextureResource? texture = viewModel.Textures.FirstOrDefault(t => t.ID == textureId.Value);
                if (texture != null) {
                    ICollectionView? view = CollectionViewSource.GetDefaultView(TexturesDataGrid.ItemsSource);
                    view?.MoveCurrentTo(texture);

                    TexturesDataGrid.SelectedItem = texture;
                    TexturesDataGrid.UpdateLayout();
                    TexturesDataGrid.ScrollIntoView(texture);
                    TexturesDataGrid.Focus();

                    logger.Info("�������� {TextureName} (ID {TextureId}) �������� � ���������� � ������� �������.", texture.Name, texture.ID);
                } else {
                    logger.Error("�������� � ID {TextureId} �� ������� � ���������. ����� �������: {TextureCount}.", textureId.Value, viewModel.Textures.Count);
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private async void MaterialsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (MaterialsDataGrid.SelectedItem is MaterialResource selectedMaterial) {
                // ��������� ��������� �������� � ViewModel ��� ���������� �������
                if (DataContext is MainViewModel viewModel) {
                    viewModel.SelectedMaterial = selectedMaterial;
                }

                if (!string.IsNullOrEmpty(selectedMaterial.Path) && File.Exists(selectedMaterial.Path)) {
                    MaterialResource? materialParameters = await assetResourceService.LoadMaterialFromFileAsync(
                        selectedMaterial.Path,
                        CancellationToken.None);
                    if (materialParameters != null) {
                        selectedMaterial = materialParameters;
                        DisplayMaterialParameters(selectedMaterial); // �������� ���� ������ MaterialResource
                    }
                }

                // ������������� ������������� �� ������� ������� � �������� ��������� ��������
                // SwitchToTexturesTabAndSelectTexture(selectedMaterial); // ���������: �� ������������� ������������� ��� ������ ���������
            }
        }

        private void SwitchToTexturesTabAndSelectTexture(MaterialResource material) {
            if (material == null) return;

            // ������������� �� ������� �������
            if (TexturesTabItem != null) {
                tabControl.SelectedItem = TexturesTabItem;
            }

            // ���� ������ ��������� ��������, ��������� � ����������
            TextureResource? textureToSelect = null;

            // ��������� ��������� ���� ������� � ������� ����������
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
                    var texture = viewModel.Textures.FirstOrDefault(t => t.ID == textureId.Value);
                    if (texture != null) {
                        textureToSelect = texture;
                        break;
                    }
                }
            }

            // ���� ������� ��������� ��������, �������� �
            if (textureToSelect != null) {
                _ = Dispatcher.BeginInvoke(new Action(() => {
                    ICollectionView? view = CollectionViewSource.GetDefaultView(TexturesDataGrid.ItemsSource);
                    view?.MoveCurrentTo(textureToSelect);

                    TexturesDataGrid.SelectedItem = textureToSelect;
                    TexturesDataGrid.UpdateLayout();
                    TexturesDataGrid.ScrollIntoView(textureToSelect);
                    TexturesDataGrid.Focus();

                    logger.Info($"������������� ������� �������� {textureToSelect.Name} (ID {textureToSelect.ID}) ��� ��������� {material.Name} (ID {material.ID})");
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            } else {
                logger.Info($"��� ��������� {material.Name} (ID {material.ID}) �� ������� ��������� �������");
            }
        }

        private void SetTextureImage(System.Windows.Controls.Image imageControl, int? textureId) {
            if (textureId.HasValue) {
                TextureResource? texture = viewModel.Textures.FirstOrDefault(t => t.ID == textureId.Value);
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

                // ���������� ������ ���������
                if (MaterialsDataGrid.SelectedItem is MaterialResource selectedMaterial) {
                    selectedMaterial.SpecularTint = true;
                    selectedMaterial.Specular = [newColor.R, newColor.G, newColor.B];
                }
            }
        }


        #endregion

        


        


        #region Helper Methods

        private string GetResourcePath(string? fileName, int? parentId = null) {
            if (string.IsNullOrEmpty(ProjectFolderPath)) {
                throw new Exception("Project folder path is null or empty");
            }

            if (string.IsNullOrEmpty(ProjectName)) {
                throw new Exception("Project name is null or empty");
            }

            return projectAssetService.GetResourcePath(AppSettings.Default.ProjectsFolderPath, ProjectName, folderPaths, fileName, parentId);
        }

        private void RecalculateIndices() {
            // ���������� ���������� �������� ��� ��������� race condition � DataGrid
            int index = 1;
            foreach (TextureResource texture in viewModel.Textures) {
                texture.Index = index++;
                // INotifyPropertyChanged ������������� ������� ������ � DataGrid
            }

            index = 1;
            foreach (ModelResource model in viewModel.Models) {
                model.Index = index++;
                // INotifyPropertyChanged ������������� ������� ������ � DataGrid
            }

            index = 1;
            foreach (MaterialResource material in viewModel.Materials) {
                material.Index = index++;
                // INotifyPropertyChanged ������������� ������� ������ � DataGrid
            }

            // Items.Refresh() ����� - INotifyPropertyChanged �� Index ������������� ��������� UI
            // ��� ��������� ������ ����������� DataGrid � ����������� �������� ����������

            // UpdateLayout() ���������� ������ ���� ��� � ����� ����� DeferUpdateLayout()
            // ����� �������� ������������� ����������� ��� ���������������� ������� RecalculateIndices()
        }

        private bool _layoutUpdatePending = false;

        /// <summary>
        /// ���������� ���������� layout DataGrid ��� �������������� ������������� �����������
        /// </summary>
        private void DeferUpdateLayout() {
            if (_layoutUpdatePending) {
                return; // ��� ������������� ����������
            }

            _layoutUpdatePending = true;
            Dispatcher.InvokeAsync(() => {
                TexturesDataGrid?.UpdateLayout();
                ModelsDataGrid?.UpdateLayout();
                MaterialsDataGrid?.UpdateLayout();
                _layoutUpdatePending = false;
            }, DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Scans all textures for existing KTX2 files and populates CompressionFormat, MipmapCount, and CompressedSize.
        /// Runs in background to avoid blocking UI.
        /// </summary>
        private void ScanKtx2InfoForAllTextures() {
            // Take a snapshot of textures to process
            var texturesToScan = viewModel.Textures.Where(t =>
                !string.IsNullOrEmpty(t.Path) &&
                t.CompressedSize == 0 &&
                !(t is ORMTextureResource)).ToList();

            if (texturesToScan.Count == 0) {
                return;
            }

            logger.Info($"ScanKtx2InfoForAllTextures: Scanning {texturesToScan.Count} textures for KTX2 info");

            Task.Run(() => {
                int foundCount = 0;

                foreach (var texture in texturesToScan) {
                    try {
                        if (string.IsNullOrEmpty(texture.Path)) continue;

                        var sourceDir = Path.GetDirectoryName(texture.Path);
                        var sourceFileName = Path.GetFileNameWithoutExtension(texture.Path);

                        if (string.IsNullOrEmpty(sourceDir) || string.IsNullOrEmpty(sourceFileName)) continue;

                        // Check for .ktx2 file
                        var ktx2Path = Path.Combine(sourceDir, sourceFileName + ".ktx2");
                        if (File.Exists(ktx2Path)) {
                            var fileInfo = new FileInfo(ktx2Path);
                            int mipLevels = 0;
                            string? compressionFormat = null;

                            try {
                                using var stream = File.OpenRead(ktx2Path);
                                using var reader = new BinaryReader(stream);
                                // KTX2 header structure:
                                // Bytes 12-15: vkFormat (uint32) - 0 means Basis Universal
                                // Bytes 40-43: levelCount (uint32)
                                // Bytes 44-47: supercompressionScheme (uint32)
                                reader.BaseStream.Seek(12, SeekOrigin.Begin);
                                uint vkFormat = reader.ReadUInt32();

                                reader.BaseStream.Seek(40, SeekOrigin.Begin);
                                mipLevels = (int)reader.ReadUInt32();
                                uint supercompression = reader.ReadUInt32();

                                // Only set compression format for Basis Universal textures (vkFormat = 0)
                                if (vkFormat == 0) {
                                    // supercompressionScheme: 1=BasisLZ(ETC1S), 0/2=UASTC(None/Zstd)
                                    compressionFormat = supercompression == 1 ? "ETC1S" : "UASTC";
                                }
                                // vkFormat != 0 means raw texture format, no Basis compression
                            } catch {
                                // Ignore header read errors
                            }

                            // Update texture on UI thread
                            Dispatcher.InvokeAsync(() => {
                                texture.CompressedSize = fileInfo.Length;
                                if (mipLevels > 0) {
                                    texture.MipmapCount = mipLevels;
                                }
                                if (compressionFormat != null) {
                                    texture.CompressionFormat = compressionFormat;
                                }
                            });

                            foundCount++;
                        }
                    } catch {
                        // Ignore errors for individual textures
                    }
                }

                if (foundCount > 0) {
                    logger.Info($"ScanKtx2InfoForAllTextures: Found KTX2 info for {foundCount} textures");
                }
            });
        }

        private bool IsSupportedTextureFormat(string extension) {
            return supportedFormats.Contains(extension) && !excludedFormats.Contains(extension);
        }

        private bool IsSupportedModelFormat(string extension) {
            return supportedModelFormats.Contains(extension) && !excludedFormats.Contains(extension); // ����������
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
        /// Обновляет кнопку и состояние подключения через сервис
        /// </summary>
        private void UpdateConnectionButton(ConnectionState newState) {
            connectionStateService.SetState(newState);
            ApplyConnectionButtonState();
        }

        /// <summary>
        /// Применяет визуальное состояние кнопки на основе данных из сервиса
        /// </summary>
        private void ApplyConnectionButtonState() {
            Dispatcher.Invoke(() => {
                bool hasSelection = ProjectsComboBox.SelectedItem != null && BranchesComboBox.SelectedItem != null;

                if (DynamicConnectionButton == null) {
                    logger.Warn("ApplyConnectionButtonState: DynamicConnectionButton is null!");
                    return;
                }

                var buttonInfo = connectionStateService.GetButtonInfo(hasSelection);
                DynamicConnectionButton.Content = buttonInfo.Content;
                DynamicConnectionButton.ToolTip = buttonInfo.ToolTip;
                DynamicConnectionButton.IsEnabled = buttonInfo.IsEnabled;
                DynamicConnectionButton.Background = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(buttonInfo.ColorR, buttonInfo.ColorG, buttonInfo.ColorB));

                logger.Info($"ApplyConnectionButtonState: Set to {buttonInfo.Content} (enabled={buttonInfo.IsEnabled})");
            });
        }

        /// <summary>
        /// Обработчик клика по динамической кнопке подключения
        /// </summary>
        private async void DynamicConnectionButton_Click(object sender, RoutedEventArgs e) {
            var currentState = connectionStateService.CurrentState;
            logger.Info($"DynamicConnectionButton_Click: Button clicked, current state: {currentState}");

            try {
                switch (currentState) {
                    case ConnectionState.Disconnected:
                        logger.Info("DynamicConnectionButton_Click: Calling ConnectToPlayCanvas");
                        ConnectToPlayCanvas();
                        break;

                    case ConnectionState.UpToDate:
                        logger.Info("DynamicConnectionButton_Click: Calling RefreshFromServer");
                        await RefreshFromServer();
                        break;

                    case ConnectionState.NeedsDownload:
                        logger.Info("DynamicConnectionButton_Click: Calling DownloadFromServer");
                        await DownloadFromServer();
                        break;
                }
            } catch (Exception ex) {
                logger.Error(ex, "Error in DynamicConnectionButton_Click");
                logService.LogError($"Error in DynamicConnectionButton_Click: {ex}");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// ����������� � PlayCanvas - ��������� ������ �������� � �����
        /// </summary>
        private void ConnectToPlayCanvas() {
            // �������� ������������ ����� Connect
            Connect(null, null);
        }

        /// <summary>
        /// ��������� ������� ���������� �� ������� (Refresh button)
        /// ���������� hash ���������� assets_list.json � ���������
        /// </summary>
        private async Task RefreshFromServer() {
            try {
                DynamicConnectionButton.IsEnabled = false;

                // Re-scan file statuses to detect deleted files
                RescanFileStatuses();

                bool hasUpdates = await CheckForUpdates();
                bool hasMissingFiles = HasMissingFiles();

                if (hasUpdates || hasMissingFiles) {
                    UpdateConnectionButton(ConnectionState.NeedsDownload);
                    string message = hasUpdates && hasMissingFiles
                        ? "Updates available and missing files found! Click Download to get them."
                        : hasUpdates
                            ? "Updates available! Click Download to get them."
                            : $"Missing files found! Click Download to get them.";
                    MessageBox.Show(message, "Download Required", MessageBoxButton.OK, MessageBoxImage.Information);
                } else {
                    MessageBox.Show("Project is up to date!", "No Updates", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            } catch (Exception ex) {
                MessageBox.Show($"Error checking for updates: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                logService.LogError($"Error in RefreshFromServer: {ex}");
            } finally {
                DynamicConnectionButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// Downloads missing files (Download button)
        /// Only syncs from server if there are actual server updates (hash changed)
        /// </summary>
        private async Task DownloadFromServer() {
            try {
                logger.Info("DownloadFromServer: Starting download");
                logService.LogInfo("DownloadFromServer: Starting download from server");
                CancelButton.IsEnabled = true;
                DynamicConnectionButton.IsEnabled = false;

                if (cancellationTokenSource != null) {
                    // Check if there are actual server updates (not just missing local files)
                    bool hasServerUpdates = await CheckForUpdates();

                    if (hasServerUpdates) {
                        // Server has new/changed assets - need to sync the list first
                        logger.Info("DownloadFromServer: Server has updates, syncing assets list");
                        logService.LogInfo("DownloadFromServer: Server has updates, syncing assets list");
                        await TryConnect(cancellationTokenSource.Token);
                    } else {
                        logger.Info("DownloadFromServer: No server updates, downloading missing files only");
                        logService.LogInfo("DownloadFromServer: No server updates, downloading missing files only");
                    }

                    // Download files (textures, models, materials)
                    logger.Info("DownloadFromServer: Starting file downloads");
                    logService.LogInfo("DownloadFromServer: Starting file downloads");
                    await Download(null, null);
                    logger.Info("DownloadFromServer: File downloads completed");
                    logService.LogInfo("DownloadFromServer: File downloads completed");

                    // Check final state
                    bool stillHasUpdates = await CheckForUpdates();
                    bool stillHasMissingFiles = HasMissingFiles();

                    if (stillHasUpdates || stillHasMissingFiles) {
                        logger.Info("DownloadFromServer: Still has updates or missing files - keeping Download button");
                        UpdateConnectionButton(ConnectionState.NeedsDownload);
                    } else {
                        logger.Info("DownloadFromServer: Project is up to date - setting button to Refresh");
                        logService.LogInfo("DownloadFromServer: Project is up to date - setting button to Refresh");
                        UpdateConnectionButton(ConnectionState.UpToDate);
                    }
                    logger.Info("DownloadFromServer: Button state updated");
                } else {
                    logger.Warn("DownloadFromServer: cancellationTokenSource is null!");
                    logService.LogError("DownloadFromServer: cancellationTokenSource is null!");
                }
            } catch (Exception ex) {
                logger.Error(ex, "Error in DownloadFromServer");
                MessageBox.Show($"Error downloading: {ex.Message}", "Download Error", MessageBoxButton.OK, MessageBoxImage.Error);
                logService.LogError($"Error in DownloadFromServer: {ex}");
            } finally {
                CancelButton.IsEnabled = false;
                DynamicConnectionButton.IsEnabled = true;
                logger.Info("DownloadFromServer: Completed");
            }
        }

        private async void Connect(object? sender, RoutedEventArgs? e) {
            CancellationToken cancellationToken = cancellationTokenSource.Token;

            if (string.IsNullOrEmpty(AppSettings.Default.PlaycanvasApiKey) || string.IsNullOrEmpty(AppSettings.Default.UserName)) {
                MessageBox.Show("Please set your Playcanvas API key, and Username in the settings window.");
                SettingsWindow settingsWindow = new();
                settingsWindow.OnPreviewRendererChanged += HandlePreviewRendererChanged;
                settingsWindow.ShowDialog();
                settingsWindow.OnPreviewRendererChanged -= HandlePreviewRendererChanged;
                return;
            }

            try {
                string? apiKey = GetDecryptedApiKey();
                if (string.IsNullOrEmpty(apiKey)) {
                    throw new Exception("Failed to decrypt API key");
                }

                ProjectSelectionResult projectsResult = await projectSelectionService.LoadProjectsAsync(AppSettings.Default.UserName, apiKey, AppSettings.Default.LastSelectedProjectId, cancellationToken);
                if (string.IsNullOrEmpty(projectsResult.UserId)) {
                    throw new Exception("User ID is null or empty");
                }

                await Dispatcher.InvokeAsync(() => UpdateConnectionStatus(true, $"by userID: {projectsResult.UserId}"));

                if (projectsResult.Projects.Count == 0) {
                    throw new Exception("Project list is empty");
                }

                viewModel.Projects.Clear();
                foreach (KeyValuePair<string, string> project in projectsResult.Projects) {
                    viewModel.Projects.Add(project);
                }

                projectSelectionService.SetProjectInitializationInProgress(true);
                try {
                    if (!string.IsNullOrEmpty(projectsResult.SelectedProjectId)) {
                        ProjectsComboBox.SelectedValue = projectsResult.SelectedProjectId;
                    } else {
                        ProjectsComboBox.SelectedIndex = 0;
                    }
                } finally {
                    projectSelectionService.SetProjectInitializationInProgress(false);
                }

                if (ProjectsComboBox.SelectedItem is KeyValuePair<string, string> selectedProject) {
                    await LoadBranchesAsync(selectedProject.Key, cancellationToken, apiKey);
                    UpdateProjectPath();
                }

                await CheckProjectState();
            } catch (Exception ex) {
                MessageBox.Show($"Error: {ex.Message}");
                UpdateConnectionButton(ConnectionState.Disconnected);
            }
        }

        private async Task CheckProjectState() {
            try {
                logger.Info("CheckProjectState: Starting");
                logService.LogInfo("CheckProjectState: Starting project state check");

                if (string.IsNullOrEmpty(ProjectFolderPath) || string.IsNullOrEmpty(ProjectName)) {
                    logger.Warn("CheckProjectState: projectFolderPath or projectName is empty");
            logService.LogInfo("CheckProjectState: projectFolderPath or projectName is empty - setting to NeedsDownload");
            UpdateConnectionButton(ConnectionState.NeedsDownload);
            return;
        }

        string assetsListPath = Path.Combine(ProjectFolderPath!, "assets_list.json");
        logger.Info($"CheckProjectState: Checking for assets_list.json at {assetsListPath}");

                if (!File.Exists(assetsListPath)) {
                    logger.Info("CheckProjectState: Project not downloaded yet - assets_list.json not found");
                    logService.LogInfo("Project not downloaded yet - assets_list.json not found");
                    UpdateConnectionButton(ConnectionState.NeedsDownload);
                    return;
                }

                logService.LogInfo("Project found, loading local assets...");
                logger.Info("CheckProjectState: Loading local assets...");
                await LoadAssetsFromJsonFileAsync();

                logService.LogInfo("Checking for updates...");
                logger.Info("CheckProjectState: Checking for updates on server");
                bool hasUpdates = await CheckForUpdates();

                // Also check for missing files locally (status "On Server" means file doesn't exist)
                bool hasMissingFiles = HasMissingFiles();

                if (hasUpdates || hasMissingFiles) {
                    string reason = hasUpdates && hasMissingFiles
                        ? "updates available and missing files"
                        : hasUpdates ? "updates available on server" : "missing files locally";
                    logger.Info($"CheckProjectState: {reason} - setting button to Download");
                    logService.LogInfo($"CheckProjectState: {reason}");
                    UpdateConnectionButton(ConnectionState.NeedsDownload);
                } else {
                    logger.Info("CheckProjectState: Project is up to date - setting button to Refresh");
                    logService.LogInfo("CheckProjectState: Project is up to date - setting button to Refresh");
                    UpdateConnectionButton(ConnectionState.UpToDate);
                }
            } catch (Exception ex) {
                logger.Error(ex, "Error in CheckProjectState");
                logService.LogError($"Error checking project state: {ex.Message}");
                UpdateConnectionButton(ConnectionState.NeedsDownload);
            }
        }

        /// <summary>
        /// Re-scans all assets to check if local files exist and updates their status.
        /// Call this before HasMissingFiles() to detect deleted files.
        /// </summary>
        private void RescanFileStatuses() {
            var result = fileStatusScannerService.ScanAll(viewModel.Textures, viewModel.Models, viewModel.Materials);

            // Refresh UI to ensure statuses are displayed correctly
            if (result.UpdatedCount > 0) {
                TexturesDataGrid?.Items.Refresh();
                ModelsDataGrid?.Items.Refresh();
                MaterialsDataGrid?.Items.Refresh();
            }
        }

        /// <summary>
        /// Starts watching the project folder for file deletions.
        /// Call when project is loaded/connected.
        /// </summary>
        private void StartFileWatcher() {
            StopFileWatcher(); // Stop existing watcher if any

            if (string.IsNullOrEmpty(ProjectFolderPath) || !Directory.Exists(ProjectFolderPath)) {
                return;
            }

            try {
                projectFileWatcher = new FileSystemWatcher(ProjectFolderPath) {
                    IncludeSubdirectories = true,
                    // Monitor both file and directory names
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    EnableRaisingEvents = true
                };

                projectFileWatcher.Deleted += OnProjectFileDeleted;
                projectFileWatcher.Renamed += OnProjectFileRenamed;

                logger.Info($"FileWatcher started for: {ProjectFolderPath}");
            } catch (Exception ex) {
                logger.Error(ex, "Failed to start FileSystemWatcher");
            }
        }

        /// <summary>
        /// Stops the file watcher. Call when project is unloaded or window closes.
        /// </summary>
        private void StopFileWatcher() {
            if (projectFileWatcher != null) {
                projectFileWatcher.EnableRaisingEvents = false;
                projectFileWatcher.Deleted -= OnProjectFileDeleted;
                projectFileWatcher.Renamed -= OnProjectFileRenamed;
                projectFileWatcher.Dispose();
                projectFileWatcher = null;
                logger.Info("FileWatcher stopped");
            }
        }

        /// <summary>
        /// Handles file/directory deletion events from FileSystemWatcher.
        /// For directories, triggers a full rescan. For files, queues the path.
        /// </summary>
        private void OnProjectFileDeleted(object sender, FileSystemEventArgs e) {
            // Check if this might be a directory deletion
            // FileSystemWatcher doesn't distinguish file vs directory in the event
            // If the path has no extension, it's likely a directory
            bool isLikelyDirectory = string.IsNullOrEmpty(Path.GetExtension(e.FullPath));

            // Ignore build directories (created/deleted during model conversion)
            if (e.FullPath.Contains("\\build\\") || e.FullPath.EndsWith("\\build")) {
                return;
            }

            if (isLikelyDirectory) {
                logger.Info($"Directory likely deleted: {e.FullPath}, scheduling full rescan");
                ScheduleFullRescan();
            } else {
                pendingDeletedPaths.Enqueue(e.FullPath);
                ScheduleFileWatcherRefresh();
            }
        }

        private int fullRescanPending;

        /// <summary>
        /// Schedules a full rescan of all asset statuses (for directory deletions).
        /// </summary>
        private void ScheduleFullRescan() {
            if (Interlocked.CompareExchange(ref fullRescanPending, 1, 0) == 0) {
                Task.Delay(500).ContinueWith(_ => {
                    Dispatcher.InvokeAsync(() => {
                        Interlocked.Exchange(ref fullRescanPending, 0);
                        logger.Info("Performing full rescan due to directory deletion");
                        RescanFileStatuses();

                        if (HasMissingFiles()) {
                            UpdateConnectionButton(ConnectionState.NeedsDownload);
                        }
                    });
                });
            }
        }

        /// <summary>
        /// Handles file rename events (moving to trash also triggers this).
        /// </summary>
        private void OnProjectFileRenamed(object sender, RenamedEventArgs e) {
            pendingDeletedPaths.Enqueue(e.OldFullPath);
            ScheduleFileWatcherRefresh();
        }

        /// <summary>
        /// Schedules a debounced refresh after file system changes.
        /// Only one refresh will occur even if many files are deleted.
        /// </summary>
        private void ScheduleFileWatcherRefresh() {
            // Use Interlocked to ensure only one refresh is scheduled
            if (Interlocked.CompareExchange(ref fileWatcherRefreshPending, 1, 0) == 0) {
                // Schedule refresh after 500ms delay
                Task.Delay(500).ContinueWith(_ => {
                    Dispatcher.InvokeAsync(() => {
                        ProcessPendingDeletedFiles();
                    });
                });
            }
        }

        /// <summary>
        /// Processes all pending deleted files and updates asset statuses.
        /// Called once after debounce delay.
        /// </summary>
        private void ProcessPendingDeletedFiles() {
            // Reset the pending flag
            Interlocked.Exchange(ref fileWatcherRefreshPending, 0);

            // Drain the queue
            var deletedPaths = new List<string>();
            while (pendingDeletedPaths.TryDequeue(out string? deletedPath)) {
                if (!string.IsNullOrEmpty(deletedPath)) {
                    deletedPaths.Add(deletedPath);
                }
            }

            int updatedCount = fileStatusScannerService.ProcessDeletedPaths(
                deletedPaths, viewModel.Textures, viewModel.Models, viewModel.Materials);

            if (updatedCount > 0) {
                // Single refresh after all updates
                TexturesDataGrid?.Items.Refresh();
                ModelsDataGrid?.Items.Refresh();
                MaterialsDataGrid?.Items.Refresh();

                // Update connection button state
                if (HasMissingFiles()) {
                    UpdateConnectionButton(ConnectionState.NeedsDownload);
                }
            }
        }

        /// <summary>
        /// Checks if any assets have status indicating they need to be downloaded.
        /// This catches cases where files were deleted locally but assets_list.json hasn't changed.
        /// </summary>
        private bool HasMissingFiles() {
            // Исключаем ORM текстуры - они виртуальные и не требуют загрузки
            var regularTextures = viewModel.Textures.Where(t => t is not ORMTextureResource);
            return connectionStateService.HasMissingFiles(regularTextures, viewModel.Models, viewModel.Materials);
        }

        private async Task<bool> CheckForUpdates() {
            if (ProjectsComboBox.SelectedItem == null || BranchesComboBox.SelectedItem == null || string.IsNullOrEmpty(ProjectFolderPath)) {
                return false;
            }

            string selectedProjectId = ((KeyValuePair<string, string>)ProjectsComboBox.SelectedItem).Key;
            string selectedBranchId = ((Branch)BranchesComboBox.SelectedItem).Id;
            string? apiKey = GetDecryptedApiKey();

            if (string.IsNullOrEmpty(apiKey)) {
                logService.LogError("API key is missing while checking for updates");
                return false;
            }

            return await connectionStateService.CheckForUpdatesAsync(
                ProjectFolderPath!, selectedProjectId, selectedBranchId, apiKey, CancellationToken.None);
        }

        private async Task LoadBranchesAsync(string projectId, CancellationToken cancellationToken, string? apiKey = null) {
            try {
                string resolvedApiKey = apiKey ?? GetDecryptedApiKey() ?? string.Empty;
                BranchSelectionResult result = await projectSelectionService.LoadBranchesAsync(projectId, resolvedApiKey, AppSettings.Default.LastSelectedBranchName, cancellationToken);

                viewModel.Branches.Clear();
                foreach (Branch branch in result.Branches) {
                    viewModel.Branches.Add(branch);
                }

                if (result.Branches.Count > 0) {
                    if (!string.IsNullOrEmpty(result.SelectedBranchId)) {
                        BranchesComboBox.SelectedValue = result.SelectedBranchId;
                    } else {
                        BranchesComboBox.SelectedIndex = 0;
                    }
                } else {
                    BranchesComboBox.SelectedIndex = -1;
                }
            } catch (Exception ex) {
                MessageBox.Show($"Error loading branches: {ex.Message}");
            }
        }

        private async void CreateBranchButton_Click(object sender, RoutedEventArgs e) {
            try {
                // Проверяем, что выбран проект
                if (ProjectsComboBox.SelectedItem == null) {
                    MessageBox.Show("Пожалуйста, выберите проект перед созданием ветки.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string selectedProjectId = ((KeyValuePair<string, string>)ProjectsComboBox.SelectedItem).Key;
                string? apiKey = GetDecryptedApiKey();

                if (string.IsNullOrEmpty(apiKey)) {
                    MessageBox.Show("API ключ не найден. Пожалуйста, настройте API ключ в настройках.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Показываем диалог для ввода имени ветки
                var inputDialog = new InputDialog("Создать новую ветку", "Введите имя новой ветки:", "");
                if (inputDialog.ShowDialog() != true || string.IsNullOrWhiteSpace(inputDialog.ResponseText)) {
                    return; // Пользователь отменил или не ввел имя
                }

                string branchName = inputDialog.ResponseText.Trim();

                // Создаем ветку через API
                logger.Info($"Creating branch '{branchName}' for project ID '{selectedProjectId}'");
                Branch newBranch = await playCanvasService.CreateBranchAsync(selectedProjectId, branchName, apiKey, CancellationToken.None);

                logger.Info($"Branch created successfully: {newBranch.Name} (ID: {newBranch.Id})");

                // Обновляем список веток
                await LoadBranchesAsync(selectedProjectId, CancellationToken.None, apiKey);

                // Выбираем новую ветку
                BranchesComboBox.SelectedValue = newBranch.Id;

                MessageBox.Show($"Ветка '{branchName}' успешно создана.", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            } catch (PlayCanvasApiException ex) {
                logger.Error(ex, "Failed to create branch");
                MessageBox.Show($"Ошибка при создании ветки: {ex.Message}", "Ошибка API", MessageBoxButton.OK, MessageBoxImage.Error);
            } catch (NetworkException ex) {
                logger.Error(ex, "Network error while creating branch");
                MessageBox.Show($"Сетевая ошибка при создании ветки: {ex.Message}", "Сетевая ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            } catch (Exception ex) {
                logger.Error(ex, "Unexpected error while creating branch");
                MessageBox.Show($"Неожиданная ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateProjectPath() {
            if (ProjectsComboBox.SelectedItem is KeyValuePair<string, string> selectedProject) {
                projectSelectionService.UpdateProjectPath(AppSettings.Default.ProjectsFolderPath, selectedProject);
            }
        }

        private void SaveCurrentSettings() {
            if (!string.IsNullOrEmpty(viewModel.SelectedProjectId)) {
                AppSettings.Default.LastSelectedProjectId = viewModel.SelectedProjectId;
            }

            if (!string.IsNullOrEmpty(viewModel.SelectedBranchId)) {
                Branch? selectedBranch = viewModel.Branches.FirstOrDefault(b => b.Id == viewModel.SelectedBranchId);
                if (selectedBranch != null) {
                    AppSettings.Default.LastSelectedBranchName = selectedBranch.Name;
                }
            }

            AppSettings.Default.Save();
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs? e) {
            try {
                // Stop file watcher
                StopFileWatcher();

                // ������������� billboard ����������
                StopBillboardUpdate();

                cancellationTokenSource?.Cancel();
                textureLoadCancellation?.Cancel();

                System.Threading.Thread.Sleep(100);

                cancellationTokenSource?.Dispose();
                textureLoadCancellation?.Dispose();

                playCanvasService?.Dispose();
            } catch (Exception ex) {
                logger.Error(ex, "Error canceling operations during window closing");
            }

            viewModel.ProjectSelectionChanged -= ViewModel_ProjectSelectionChanged;
            viewModel.BranchSelectionChanged -= ViewModel_BranchSelectionChanged;
            SaveCurrentSettings();
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e) {
            cancellationTokenSource?.Cancel();
            cancellationTokenSource = new CancellationTokenSource();
        }

        private void Setting(object? sender, RoutedEventArgs e) {
            SettingsWindow settingsWindow = new();
            settingsWindow.OnPreviewRendererChanged += HandlePreviewRendererChanged;
            settingsWindow.ShowDialog();
            settingsWindow.OnPreviewRendererChanged -= HandlePreviewRendererChanged;
        }

        /// <summary>
        /// ������������� ConversionSettings ��������� � UI
        /// </summary>
        private void InitializeConversionSettings() {
            try {
                // ��������� ���������� ���������
                globalTextureSettings ??= TextureConversionSettingsManager.LoadSettings();

                // ������� �������� �������� �����������
                conversionSettingsManager = new ConversionSettingsManager(globalTextureSettings);

                // ��������� UI �������� ��� ConversionSettings
                PopulateConversionSettingsUI();

                logger.Info("ConversionSettings initialized successfully");
            } catch (Exception ex) {
                logger.Error(ex, "Error initializing ConversionSettings");
                MessageBox.Show($"Error initializing conversion settings: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// ��������� UI �������� ConversionSettings (������� � ��������� ��������)
        /// </summary>
        private void PopulateConversionSettingsUI() {
            if (conversionSettingsManager == null) {
                logger.Warn("ConversionSettingsManager not initialized");
                return;
            }

            try {
                // �������� ConversionSettingsManager � ������ �������� �����������
                // ��������: ������ ���� �������� ������� �� ConversionSettingsSchema
                // ������ SetConversionSettingsManager() - �� ��������� ��� �����!
                if (ConversionSettingsPanel != null) {
                    ConversionSettingsPanel.SetConversionSettingsManager(conversionSettingsManager);

                    // �������� ��� ��������
                    logger.Info($"ConversionSettingsManager passed to panel. PresetComboBox items count: {ConversionSettingsPanel.PresetComboBox.Items.Count}");
                }

            } catch (Exception ex) {
                logger.Error(ex, "Error populating ConversionSettings UI");
                throw;
            }
        }

        /// <summary>
        /// ������������� ��� ������� ��������� - ������������ � ������� � ��������� �������
        /// ���� hash ���������� JSON ��������� � ��������� - ��������� ��������
        /// </summary>
        private async Task InitializeOnStartup() {
            try {
                logger.Info("=== InitializeOnStartup: Starting ===");
                logService.LogInfo("=== Initializing on startup ===");

                // ��������� ������� API ����� � username
                if (string.IsNullOrEmpty(AppSettings.Default.PlaycanvasApiKey) ||
                    string.IsNullOrEmpty(AppSettings.Default.UserName)) {
                    logger.Info("InitializeOnStartup: No API key or username - showing Connect button");
                    logService.LogInfo("No API key or username - showing Connect button");
                    UpdateConnectionButton(ConnectionState.Disconnected);
                    return;
                }

                // ������������ � ������� � ��������� ������ ��������
                logger.Info("InitializeOnStartup: Connecting to PlayCanvas server...");
                logService.LogInfo("Connecting to PlayCanvas server...");
                await LoadLastSettings();
                logger.Info("InitializeOnStartup: LoadLastSettings completed");

            } catch (Exception ex) {
                logger.Error(ex, "Error during startup initialization");
                logService.LogError($"Error during startup initialization: {ex.Message}");
                UpdateConnectionButton(ConnectionState.Disconnected);
            }
        }

        /// <summary>
        /// ����� �������� �������: ��������� hash � ��������� �������� ���� ���������
        /// ���� hash ���������� - ��������� � �������
        /// </summary>
        private async Task SmartLoadAssets() {
            try {
                logger.Info("=== SmartLoadAssets: Starting ===");
                logService.LogInfo("=== SmartLoadAssets: Checking for updates ===");

                if (ProjectsComboBox.SelectedItem == null || BranchesComboBox.SelectedItem == null) {
                    logger.Warn($"SmartLoadAssets: No project or branch selected (Project={ProjectsComboBox.SelectedItem != null}, Branch={BranchesComboBox.SelectedItem != null})");
                    logService.LogInfo("No project or branch selected");
                    UpdateConnectionButton(ConnectionState.Disconnected);
                    return;
                }

                string selectedProjectId = ((KeyValuePair<string, string>)ProjectsComboBox.SelectedItem).Key;
                string selectedBranchId = ((Branch)BranchesComboBox.SelectedItem).Id;

                if (string.IsNullOrEmpty(ProjectFolderPath)) {
                    logService.LogInfo("Project folder path is empty during SmartLoadAssets");
                    UpdateConnectionButton(ConnectionState.NeedsDownload);
                    return;
                }

                JArray? localAssets = await projectAssetService.LoadAssetsFromJsonAsync(ProjectFolderPath, CancellationToken.None);

                if (localAssets != null) {
                    logService.LogInfo($"Local assets_list.json found for project {ProjectName}");

                    logger.Info("SmartLoadAssets: Loading local assets...");
                    logService.LogInfo("Loading local assets...");
                    await LoadAssetsFromJsonFileAsync();

                    string? apiKey = GetDecryptedApiKey();
                    if (string.IsNullOrEmpty(apiKey)) {
                        logService.LogError("API key missing during SmartLoadAssets");
                        UpdateConnectionButton(ConnectionState.NeedsDownload);
                        return;
                    }

                    ProjectUpdateContext context = new(ProjectFolderPath, selectedProjectId, selectedBranchId, apiKey);
                    bool hasUpdates = await projectAssetService.HasUpdatesAsync(context, CancellationToken.None);

                    if (hasUpdates) {
                        logger.Info("SmartLoadAssets: Updates available on server.");
                        logService.LogInfo("Hashes differ! Updates available on server.");
                        UpdateConnectionButton(ConnectionState.NeedsDownload);
                    } else {
                        logger.Info("SmartLoadAssets: Project is up to date.");
                        logService.LogInfo("Hashes match! Project is up to date.");
                        UpdateConnectionButton(ConnectionState.UpToDate);
                    }
                    logger.Info("SmartLoadAssets: Assets loaded successfully");
                } else {
                    // ���������� ����� ��� - ����� ��������
                    logger.Info("SmartLoadAssets: No local assets_list.json found - need to download");
                    logService.LogInfo("No local assets_list.json found - need to download");
                    UpdateConnectionButton(ConnectionState.NeedsDownload);
                }

            } catch (Exception ex) {
                logger.Error(ex, "Error in SmartLoadAssets");
                logService.LogError($"Error in SmartLoadAssets: {ex.Message}");
                UpdateConnectionButton(ConnectionState.NeedsDownload);
            }
        }
                private async Task LoadLastSettings() {
            try {
                logger.Info("LoadLastSettings: Starting");

                string? apiKey = GetDecryptedApiKey();
                if (string.IsNullOrEmpty(apiKey)) {
                    throw new Exception("API key is null or empty after decryption");
                }

                CancellationToken cancellationToken = new();

                ProjectSelectionResult projectsResult = await projectSelectionService.LoadProjectsAsync(AppSettings.Default.UserName, apiKey, AppSettings.Default.LastSelectedProjectId, cancellationToken);
                if (string.IsNullOrEmpty(projectsResult.UserId)) {
                    throw new Exception("User ID is null or empty");
                }

                UpdateConnectionStatus(true, $"by userID: {projectsResult.UserId}");

                if (projectsResult.Projects.Count > 0) {
                    viewModel.Projects.Clear();
                    foreach (KeyValuePair<string, string> project in projectsResult.Projects) {
                        viewModel.Projects.Add(project);
                    }

                    projectSelectionService.SetProjectInitializationInProgress(true);
                    try {
                        if (!string.IsNullOrEmpty(projectsResult.SelectedProjectId)) {
                            ProjectsComboBox.SelectedValue = projectsResult.SelectedProjectId;
                            logger.Info($"LoadLastSettings: Selected project: {projectsResult.SelectedProjectId}");
                        } else {
                            ProjectsComboBox.SelectedIndex = 0;
                            logger.Info("LoadLastSettings: Selected first project");
                        }
                    } finally {
                        projectSelectionService.SetProjectInitializationInProgress(false);
                    }

                    if (ProjectsComboBox.SelectedItem is KeyValuePair<string, string> selectedProject) {
                        BranchSelectionResult branchesResult = await projectSelectionService.LoadBranchesAsync(selectedProject.Key, apiKey, AppSettings.Default.LastSelectedBranchName, cancellationToken);
                        viewModel.Branches.Clear();
                        foreach (Branch branch in branchesResult.Branches) {
                            viewModel.Branches.Add(branch);
                        }

                        if (!string.IsNullOrEmpty(branchesResult.SelectedBranchId)) {
                            BranchesComboBox.SelectedValue = branchesResult.SelectedBranchId;
                            logger.Info($"LoadLastSettings: Selected branch: {branchesResult.SelectedBranchId}");
                        } else if (branchesResult.Branches.Count > 0) {
                            BranchesComboBox.SelectedIndex = 0;
                            logger.Info("LoadLastSettings: Selected first branch");
                        } else {
                            BranchesComboBox.SelectedIndex = -1;
                        }

                        projectSelectionService.UpdateProjectPath(AppSettings.Default.ProjectsFolderPath, selectedProject);
                        logger.Info($"LoadLastSettings: Project folder path set to: {ProjectFolderPath}");

                        // ????? ??????: ????? hash ? ??????? ?????? ?? ?????
                        await SmartLoadAssets();
                    }
                }
            } catch (Exception ex) {
                logger.Error(ex, "Failed to load last settings");
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
            logService.LogInfo($"[ConversionSettingsPanel_SettingsChanged] Event triggered");

            if (TexturesDataGrid.SelectedItem is TextureResource selectedTexture) {
                logService.LogInfo($"[ConversionSettingsPanel_SettingsChanged] Updating settings for texture: {selectedTexture.Name}");
                UpdateTextureConversionSettings(selectedTexture);
                logService.LogInfo($"[ConversionSettingsPanel_SettingsChanged] Settings updated for texture: {selectedTexture.Name}");
            } else {
                logService.LogInfo($"[ConversionSettingsPanel_SettingsChanged] No texture selected, skipping update");
            }

            logService.LogInfo($"[ConversionSettingsPanel_SettingsChanged] Event handler completed");
        }

        private void UpdateTextureConversionSettings(TextureResource texture) {
            try {
                var compression = ConversionSettingsPanel.GetCompressionSettings();
                var mipProfile = ConversionSettingsPanel.GetMipProfileSettings();

                // Don't update CompressionFormat here - it shows actual compression from KTX2 file,
                // not the intended settings from the UI panel
                texture.PresetName = ConversionSettingsPanel.PresetName ?? "(Custom)";

                logService.LogInfo($"Updated conversion settings for {texture.Name}");
            } catch (Exception ex) {
                logService.LogError($"Error updating conversion settings: {ex.Message}");
            }
        }

        private void LoadTextureConversionSettings(TextureResource texture) {
            logService.LogInfo($"[LoadTextureConversionSettings] START for: {texture.Name}");

            // ��������: ������������� ���� ������� �������� ��� auto-detect normal map!
            ConversionSettingsPanel.SetCurrentTexturePath(texture.Path);

            // ��������: ������� NormalMapPath ����� auto-detect ������� ��� ����� ��������!
            ConversionSettingsPanel.ClearNormalMapPath();

            // ��������: ������ auto-detect preset �� ����� ����� ����� ��������� ��������!
            // ��� ��������� ������������� �������� ���������� preset ��� ������ ��������
            var presetManager = new TextureConversion.Settings.PresetManager();
            var matchedPreset = presetManager.FindPresetByFileName(texture.Name ?? "");
            logService.LogInfo($"[LoadTextureConversionSettings] PresetManager.FindPresetByFileName returned: {matchedPreset?.Name ?? "null"}");

            if (matchedPreset != null) {
                // ����� preset �� ����� ����� (�������� "gloss" > "Gloss (Linear + Toksvig)")
                texture.PresetName = matchedPreset.Name;
                logService.LogInfo($"Auto-detected preset '{matchedPreset.Name}' for texture {texture.Name}");

                // ��������� ������� preset � dropdown
                var dropdownItems = ConversionSettingsPanel.PresetComboBox.Items.Cast<string>().ToList();
                logService.LogInfo($"[LoadTextureConversionSettings] Dropdown contains {dropdownItems.Count} items: {string.Join(", ", dropdownItems)}");

                bool presetExistsInDropdown = dropdownItems.Contains(matchedPreset.Name);
                logService.LogInfo($"[LoadTextureConversionSettings] Preset '{matchedPreset.Name}' exists in dropdown: {presetExistsInDropdown}");

                // ��������: ������������� preset ��� �������� ������� ����� �� ����������� �������� ��������!
                if (presetExistsInDropdown) {
                    logService.LogInfo($"[LoadTextureConversionSettings] Setting dropdown SILENTLY to preset: {matchedPreset.Name}");
                    ConversionSettingsPanel.SetPresetSilently(matchedPreset.Name);
                } else {
                    logService.LogInfo($"[LoadTextureConversionSettings] Preset '{matchedPreset.Name}' not in dropdown, setting to Custom SILENTLY");
                    ConversionSettingsPanel.SetPresetSilently("Custom");
                }
            } else {
                // Preset �� ������ �� ����� ����� - ���������� "Custom"
                texture.PresetName = "";
                logService.LogInfo($"No preset matched for '{texture.Name}', using Custom SILENTLY");
                ConversionSettingsPanel.SetPresetSilently("Custom");
            }

            logService.LogInfo($"[LoadTextureConversionSettings] END for: {texture.Name}");

            // ��������� default ��������� ��� ���� �������� (���� Custom)
            if (string.IsNullOrEmpty(texture.PresetName)) {
                var textureType = TextureResource.DetermineTextureType(texture.Name ?? "");
                var profile = TextureConversion.Core.MipGenerationProfile.CreateDefault(
                    MapTextureTypeToCore(textureType));

                var compression = TextureConversion.Core.CompressionSettings.CreateETC1SDefault();
                var compressionData = TextureConversion.Settings.CompressionSettingsData.FromCompressionSettings(compression);
                var mipProfileData = TextureConversion.Settings.MipProfileSettings.FromMipGenerationProfile(profile);

                ConversionSettingsPanel.LoadSettings(compressionData, mipProfileData, true, false);
                // Don't override CompressionFormat if already set from KTX2 scan
                // CompressionFormat shows actual compression, not intended settings
            }
        }

        // Initialize compression format and preset for texture without updating UI panel
        // ��������������: ���������� ������������ PresetManager � ����������� �������� ������
        private void InitializeTextureConversionSettings(TextureResource texture) {
            // ������� ������������� ��� �������� ������ - ��� �������� ����������� �������
            var textureType = TextureResource.DetermineTextureType(texture.Name ?? "");
            var profile = TextureConversion.Core.MipGenerationProfile.CreateDefault(
                MapTextureTypeToCore(textureType));
            var compression = TextureConversion.Core.CompressionSettings.CreateETC1SDefault();

            // CompressionFormat is only set when texture is actually compressed
            // (from KTX2 metadata or after compression process)

            // Auto-detect preset by filename if not already set
            // ���������� ������������ PresetManager ��� ��������� �������� ������ ��� ������ �������������
            if (string.IsNullOrEmpty(texture.PresetName)) {
                var matchedPreset = cachedPresetManager.FindPresetByFileName(texture.Name ?? "");
                texture.PresetName = matchedPreset?.Name ?? "";
            }

            // �������� ������ ����������� - ��� ����������� ���������� � �� ��������� UI
            // ��� �������� ��� ������������������ ��� ����������� �������
            if (!string.IsNullOrEmpty(texture.Path) && texture.CompressedSize == 0) {
                // ���������� TryAdd ��� ��������� �������� � ��������� �����
                // ��� ������������� race condition ��� ������������� ������� ������ ��� ����� ��������
                var lockObject = new object();
                if (texturesBeingChecked.TryAdd(texture.Path, lockObject)) {
                    // ��������� ����� ������ ���� CompressedSize ��� �� ����������
                    // ���������� ����������� �������� ����� �� ����������� UI
                    Task.Run(() => {
                        try {
                            if (File.Exists(texture.Path)) {
                                var sourceDir = Path.GetDirectoryName(texture.Path);
                                var sourceFileName = Path.GetFileNameWithoutExtension(texture.Path);

                                if (!string.IsNullOrEmpty(sourceDir) && !string.IsNullOrEmpty(sourceFileName)) {
                                    // Check for .ktx2 file first
                                    var ktx2Path = Path.Combine(sourceDir, sourceFileName + ".ktx2");
                                    if (File.Exists(ktx2Path)) {
                                        var fileInfo = new FileInfo(ktx2Path);
                                        // Read KTX2 header to get mip levels and compression format
                                        int mipLevels = 0;
                                        string? compressionFormat = null;
                                        try {
                                            using var stream = File.OpenRead(ktx2Path);
                                            using var reader = new BinaryReader(stream);
                                            // KTX2 header structure:
                                            // Bytes 12-15: vkFormat (uint32) - 0 means Basis Universal
                                            // Bytes 40-43: levelCount (uint32)
                                            // Bytes 44-47: supercompressionScheme (uint32)
                                            reader.BaseStream.Seek(12, SeekOrigin.Begin);
                                            uint vkFormat = reader.ReadUInt32();

                                            reader.BaseStream.Seek(40, SeekOrigin.Begin);
                                            mipLevels = (int)reader.ReadUInt32();
                                            uint supercompression = reader.ReadUInt32();

                                            // Only set compression format for Basis Universal textures (vkFormat = 0)
                                            if (vkFormat == 0) {
                                                // supercompressionScheme: 1=BasisLZ(ETC1S), 0/2=UASTC(None/Zstd)
                                                compressionFormat = supercompression == 1 ? "ETC1S" : "UASTC";
                                            }
                                            // vkFormat != 0 means raw texture format, no Basis compression
                                        } catch {
                                            // Ignore header read errors
                                        }
                                        Dispatcher.InvokeAsync(() => {
                                            texture.CompressedSize = fileInfo.Length;
                                            if (mipLevels > 0) {
                                                texture.MipmapCount = mipLevels;
                                            }
                                            if (compressionFormat != null) {
                                                texture.CompressionFormat = compressionFormat;
                                            }
                                        });
                                    } else {
                                        // Check for .basis file as fallback
                                        var basisPath = Path.Combine(sourceDir, sourceFileName + ".basis");
                                        if (File.Exists(basisPath)) {
                                            var fileInfo = new FileInfo(basisPath);
                                            Dispatcher.InvokeAsync(() => {
                                                texture.CompressedSize = fileInfo.Length;
                                            });
                                        }
                                    }
                                }
                            }
                        } catch {
                            // ���������� ������ ��� �������� ������ - ��� �� �������� ��� �����������
                        } finally {
                            // ������� �������� �� ������� ����� ���������� ��������
                            texturesBeingChecked.TryRemove(texture.Path, out _);
                        }
                    });
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

        private async void ViewModel_TextureProcessingCompleted(object? sender, TextureProcessingCompletedEventArgs e) {
            try {
                await Dispatcher.InvokeAsync(() => {
                    TexturesDataGrid.Items.Refresh();
                    ProgressBar.Value = 0;
                    ProgressBar.Maximum = e.Result.SuccessCount + e.Result.ErrorCount;
                    ProgressTextBlock.Text = $"Completed: {e.Result.SuccessCount} success, {e.Result.ErrorCount} errors";
                });

                string resultMessage = BuildProcessingSummaryMessage(e.Result);
                MessageBoxImage icon = e.Result.ErrorCount == 0 && e.Result.SuccessCount > 0
                    ? MessageBoxImage.Information
                    : MessageBoxImage.Warning;

                // ���������� MessageBox � UI ������
                await Dispatcher.InvokeAsync(() => {
                    MessageBox.Show(resultMessage, "Processing Complete", MessageBoxButton.OK, icon);
                });

                // ��������� ������ ����� ������ MessageBox, ����� �� ����������� UI
                if (e.Result.PreviewTexture != null && viewModel.LoadKtxPreviewCommand is IAsyncRelayCommand<TextureResource?> command) {
                    try {
                        // ��������� ������� �������� (��� ��� async � �� ��������� UI)
                        await command.ExecuteAsync(e.Result.PreviewTexture);
                        
                        // ��������� UI � UI ������ ����� ��������
                        // Preview loading event will switch the viewer to KTX2 mode after the texture is loaded.
                    } catch (Exception ex) {
                        logger.Warn(ex, "������ ��� �������� ������ KTX2");
                    }
                }
            } catch (Exception ex) {
                logger.Error(ex, "������ ��� ��������� ����������� �����������");
            }
        }

        private int _isLoadingTexture = 0; // 0 = false, 1 = true (���������� int ��� Interlocked)

        private async void ViewModel_TexturePreviewLoaded(object? sender, TexturePreviewLoadedEventArgs e) {
            // ��������� �������� � ��������� ����� ��� ������ �� ��������� ��������
            // ���������� CompareExchange ��� ��������� �������� � ���������, ����� �������� TOCTOU
            // �������� ���������� ���� � 1, ���� �� ��� 0 (��������� ��������)
            int wasLoading = Interlocked.CompareExchange(ref _isLoadingTexture, 1, 0);
            if (wasLoading != 0) {
                logger.Warn("Texture loading already in progress, skipping duplicate load");
                // �����: �� ���������� ����, ��� ��� ������ ����� ��� ��������� � ������ ��������
                // � ����� finally �����. ����� �����, ������� �� �� �������, �������� �������� ����������.
                return;
            }

            try {

                // ��������� UI �������� � UI ������ � ������� �����������
                bool rendererAvailable = false;
                await Dispatcher.InvokeAsync(() => {
                    texturePreviewService.CurrentLoadedTexturePath = e.Texture.Path;
                    texturePreviewService.CurrentLoadedKtx2Path = e.Preview.KtxPath;
                    texturePreviewService.IsKtxPreviewAvailable = true;
                    texturePreviewService.IsKtxPreviewActive = true;
                    texturePreviewService.CurrentKtxMipmaps?.Clear();
                    texturePreviewService.CurrentMipLevel = 0;

                    if (!texturePreviewService.IsUserPreviewSelection || texturePreviewService.CurrentPreviewSourceMode == TexturePreviewSourceMode.Ktx2) {
                        SetPreviewSourceMode(TexturePreviewSourceMode.Ktx2, initiatedByUser: false);
                    } else {
                        UpdatePreviewSourceControls();
                    }

                    // ��������� ����������� renderer � UI ������
                    rendererAvailable = D3D11TextureViewer?.Renderer != null;
                    if (!rendererAvailable) {
                        logger.Warn("D3D11 viewer ��� renderer �����������");
                    }
                });

                if (!rendererAvailable) {
                    return;
                }

                // ���� UI ������ ����������� ���������� ������ ��������� ����� �������� ����������
                await Task.Yield();

                // ��������� LoadTexture � UI ������, �� � ����� ������ �����������
                // ����� �� ����������� ������ UI ��������
                await Dispatcher.InvokeAsync(() => {
                    try {
                        if (D3D11TextureViewer?.Renderer == null) {
                            logger.Warn("D3D11 renderer ���� null �� ����� ��������");
                            return;
                        }
                        D3D11TextureViewer.Renderer.LoadTexture(e.Preview.TextureData);
                    } catch (Exception ex) {
                        logger.Error(ex, "������ ��� �������� �������� � D3D11");
                        return;
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);

                // ��� ��� ���� UI ������ ����������� ���������� ������ ���������
                await Task.Yield();

                // ��������� UI � ��������� Render
                await Dispatcher.InvokeAsync(() => {
                    try {
                        if (D3D11TextureViewer?.Renderer == null) {
                            logger.Warn("D3D11 renderer ���� null �� ����� ���������� UI");
                            return;
                        }

                        UpdateHistogramCorrectionButtonState();

                        bool hasHistogram = D3D11TextureViewer.Renderer.HasHistogramMetadata();
                        if (TextureFormatTextBlock != null) {
                            string compressionFormat = e.Preview.TextureData.CompressionFormat ?? "Unknown";
                            string srgbInfo = compressionFormat.IndexOf("SRGB", StringComparison.OrdinalIgnoreCase) >= 0
                                ? " (sRGB)"
                                : compressionFormat.IndexOf("UNORM", StringComparison.OrdinalIgnoreCase) >= 0
                                    ? " (Linear)"
                                    : string.Empty;
                            string histInfo = hasHistogram ? " + Histogram" : string.Empty;
                            TextureFormatTextBlock.Text = $"Format: KTX2/{compressionFormat}{srgbInfo}{histInfo}";
                        }

                        D3D11TextureViewer.Renderer.Render();

                        if (e.Preview.ShouldEnableNormalReconstruction && D3D11TextureViewer.Renderer != null) {
                            texturePreviewService.CurrentActiveChannelMask = "Normal";
                            D3D11TextureViewer.Renderer.SetChannelMask(0x20);
                            D3D11TextureViewer.Renderer.Render();
                            UpdateChannelButtonsState();
                            if (!string.IsNullOrWhiteSpace(e.Preview.AutoEnableReason)) {
                                logService.LogInfo($"Auto-enabled Normal reconstruction mode for {e.Preview.AutoEnableReason}");
                            }
                        }
                    } catch (Exception ex) {
                        logger.Error(ex, "������ ��� ���������� UI ����� �������� ��������");
                    }
                });
            } catch (Exception ex) {
                logger.Error(ex, "������ ��� ���������� ������ KTX2");
            } finally {
                // �������� ���������� ����
                Interlocked.Exchange(ref _isLoadingTexture, 0);
            }
        }

        private static string BuildProcessingSummaryMessage(TextureProcessingResult result) {
            var resultMessage = $"Processing completed!\n\nSuccess: {result.SuccessCount}\nErrors: {result.ErrorCount}";

            if (result.ErrorCount > 0 && result.ErrorMessages.Count > 0) {
                resultMessage += "\n\nError details:";
                var errorsToShow = result.ErrorMessages.Take(10).ToList();
                foreach (var error in errorsToShow) {
                    resultMessage += $"\n� {error}";
                }
                if (result.ErrorMessages.Count > 10) {
                    resultMessage += $"\n... and {result.ErrorMessages.Count - 10} more errors (see log file for details)";
                }
            } else if (result.SuccessCount > 0) {
                resultMessage += "\n\nConverted files saved next to source images.";
            }

            return resultMessage;
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

        }

        private void CreateORMButton_Click(object sender, RoutedEventArgs e) {
            try {
                var ormTexture = ormTextureService.CreateEmptyORM(viewModel.Textures);
                viewModel.Textures.Add(ormTexture);

                TexturesDataGrid.SelectedItem = ormTexture;
                TexturesDataGrid.ScrollIntoView(ormTexture);

                logService.LogInfo($"Created new ORM texture: {ormTexture.Name}");
            } catch (Exception ex) {
                logService.LogError($"Error creating ORM texture: {ex.Message}");
                MessageBox.Show($"Failed to create ORM texture: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ORM from Material handlers
        private void CreateORMFromMaterial_Click(object sender, RoutedEventArgs e) {
            MaterialResource? material = null;

            // Get material from DataGrid selection or button context
            if (MaterialsDataGrid.SelectedItem is MaterialResource selectedMaterial) {
                material = selectedMaterial;
            }

            if (material == null) {
                MessageBox.Show("Please select a material first.",
                    "No Material Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try {
                // Debug: Log material map IDs
                logService.LogInfo($"Material '{material.Name}': AOMapId={material.AOMapId?.ToString() ?? "null"}, GlossMapId={material.GlossMapId?.ToString() ?? "null"}, " +
                    $"MetalnessMapId={material.MetalnessMapId?.ToString() ?? "null"}, SpecularMapId={material.SpecularMapId?.ToString() ?? "null"}, UseMetalness={material.UseMetalness}");

                // Find viewModel.Textures by map IDs
                TextureResource? aoTexture = ormTextureService.FindTextureById(material.AOMapId, viewModel.Textures);
                TextureResource? glossTexture = ormTextureService.FindTextureById(material.GlossMapId, viewModel.Textures);
                TextureResource? metalnessTexture = null;

                // Debug: Log found textures
                logService.LogInfo($"Found textures: AO={aoTexture?.Name ?? "null"}, Gloss={glossTexture?.Name ?? "null"}");

                // Smart workflow detection: prefer actual texture presence over UseMetalness flag
                string workflowInfo = "";
                string mapType = ""; // Track which map type we're actually using

                // First try to find Metalness texture (modern PBR workflow)
                TextureResource? metalnessCandidate = ormTextureService.FindTextureById(material.MetalnessMapId, viewModel.Textures);
                TextureResource? specularCandidate = ormTextureService.FindTextureById(material.SpecularMapId, viewModel.Textures);

                logService.LogInfo($"Texture candidates: Metalness={metalnessCandidate?.Name ?? "null"}, Specular={specularCandidate?.Name ?? "null"}");

                if (metalnessCandidate != null) {
                    // Metalness texture exists - use PBR workflow
                    metalnessTexture = metalnessCandidate;
                    workflowInfo = "Workflow: Metalness (PBR)";
                    mapType = "Metallic";
                    logService.LogInfo($"Metalness workflow detected: Metallic={metalnessTexture.Name}");
                } else if (specularCandidate != null) {
                    // Only Specular texture exists - use legacy workflow
                    metalnessTexture = specularCandidate;
                    workflowInfo = "Workflow: Specular (Legacy)\nNote: Specular map will be used as Metallic";
                    mapType = "Specular";
                    logService.LogInfo($"Specular workflow detected: Specular={metalnessTexture.Name}");
                } else {
                    // No metallic/specular texture found
                    logService.LogWarn($"No metallic or specular texture found for material '{material.Name}' (MetalnessMapId={material.MetalnessMapId}, SpecularMapId={material.SpecularMapId})");
                    workflowInfo = material.UseMetalness ? "Workflow: Metalness (PBR)" : "Workflow: Specular (Legacy)";
                    mapType = material.UseMetalness ? "Metallic" : "Specular";
                }

                // Auto-detect packing mode
                ChannelPackingMode mode = ormTextureService.DetectPackingMode(aoTexture, glossTexture, metalnessTexture);

                // If insufficient textures for ORM - don't create
                if (mode == ChannelPackingMode.None) {
                    // mapType is already set by workflow detection above
                    var aoStatus = aoTexture != null ? $"Found: {aoTexture.Name}" : "Missing";
                    var glossStatus = glossTexture != null ? $"Found: {glossTexture.Name}" : "Missing";
                    var metallicStatus = metalnessTexture != null ? $"Found: {metalnessTexture.Name}" : "Missing";

                    MessageBox.Show($"Cannot create ORM texture - insufficient textures.\n\n" +
                                  $"{workflowInfo}\n\n" +
                                  $"AO: {aoStatus}\n" +
                                  $"Gloss: {glossStatus}\n" +
                                  $"{mapType}: {metallicStatus}\n\n" +
                                  $"Required combinations:\n" +
                                  $"  - OGM: AO + Gloss + Metallic\n" +
                                  $"  - OG: AO + Gloss",
                        "Insufficient Textures", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Get base material name without _mat suffix
                string baseMaterialName = (material.Name?.EndsWith("_mat", StringComparison.OrdinalIgnoreCase) == true)
                    ? material.Name.Substring(0, material.Name.Length - 4)
                    : (material.Name ?? "unknown");

                // Generate ORM texture name based on packing mode
                string modeSuffix = mode switch {
                    ChannelPackingMode.OG => "_og",
                    ChannelPackingMode.OGM => "_ogm",
                    ChannelPackingMode.OGMH => "_ogmh",
                    _ => "_ogm"
                };
                string ormTextureName = baseMaterialName + modeSuffix;

                // Check if ORM texture already exists for this material
                var existingORM = viewModel.Textures.OfType<ORMTextureResource>()
                    .FirstOrDefault(t => t.Name == ormTextureName);

                if (existingORM != null) {
                    var result = MessageBox.Show($"ORM texture '{ormTextureName}' already exists.\n\nDo you want to update it?",
                        "ORM Already Exists", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (result == MessageBoxResult.No) {
                        return;
                    }
                    // Remove existing
                    viewModel.Textures.Remove(existingORM);
                }

                // Create ORM texture
                var ormTexture = new ORMTextureResource {
                    Name = ormTextureName,
                    TextureType = "ORM (Virtual)",
                    PackingMode = mode,
                    AOSource = aoTexture,
                    GlossSource = glossTexture,
                    MetallicSource = metalnessTexture,
                    Status = "Ready to pack"
                };

                // Add to viewModel.Textures collection
                viewModel.Textures.Add(ormTexture);

                // Select the newly created ORM texture and switch to viewModel.Textures tab
                tabControl.SelectedItem = TexturesTabItem;
                TexturesDataGrid.SelectedItem = ormTexture;
                TexturesDataGrid.ScrollIntoView(ormTexture);

                logService.LogInfo($"Created ORM texture '{ormTexture.Name}' with mode {mode}");
                MessageBox.Show($"Created ORM texture:\n\n" +
                              $"Name: {ormTexture.Name}\n" +
                              $"Mode: {mode}\n" +
                              $"AO: {aoTexture?.Name ?? "None"}\n" +
                              $"Gloss: {glossTexture?.Name ?? "None"}\n" +
                              $"Metallic: {metalnessTexture?.Name ?? "None"}",
                    "ORM Texture Created", MessageBoxButton.OK, MessageBoxImage.Information);
            } catch (Exception ex) {
                logService.LogError($"Error creating ORM from material: {ex.Message}");
                MessageBox.Show($"Failed to create ORM texture: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CreateORMForAllMaterials_Click(object sender, RoutedEventArgs e) {
            try {
                int created = 0;
                int skipped = 0;
                var errors = new List<string>();

                foreach (var material in viewModel.Materials) {
                    try {
                        // Find textures
                        TextureResource? aoTexture = ormTextureService.FindTextureById(material.AOMapId, viewModel.Textures);
                        TextureResource? glossTexture = ormTextureService.FindTextureById(material.GlossMapId, viewModel.Textures);
                        TextureResource? metalnessTexture = null;

                        // Smart workflow detection: prefer actual texture presence over UseMetalness flag
                        TextureResource? metalnessCandidate = ormTextureService.FindTextureById(material.MetalnessMapId, viewModel.Textures);
                        TextureResource? specularCandidate = ormTextureService.FindTextureById(material.SpecularMapId, viewModel.Textures);

                        if (metalnessCandidate != null) {
                            metalnessTexture = metalnessCandidate;
                        } else if (specularCandidate != null) {
                            metalnessTexture = specularCandidate;
                        }

                        // Auto-detect mode
                        ChannelPackingMode mode = ormTextureService.DetectPackingMode(aoTexture, glossTexture, metalnessTexture);

                        if (mode == ChannelPackingMode.None) {
                            skipped++;
                            continue;
                        }

                        // Get base material name without _mat suffix
                        string baseMaterialName = (material.Name?.EndsWith("_mat", StringComparison.OrdinalIgnoreCase) == true)
                            ? material.Name.Substring(0, material.Name.Length - 4)
                            : (material.Name ?? "unknown");

                        // Generate ORM texture name based on packing mode
                        string modeSuffix = mode switch {
                            ChannelPackingMode.OG => "_og",
                            ChannelPackingMode.OGM => "_ogm",
                            ChannelPackingMode.OGMH => "_ogmh",
                            _ => "_ogm"
                        };
                        string ormTextureName = baseMaterialName + modeSuffix;

                        // Check if already exists
                        var existingORM = viewModel.Textures.OfType<ORMTextureResource>()
                            .FirstOrDefault(t => t.Name == ormTextureName);

                        if (existingORM != null) {
                            skipped++;
                            continue;
                        }

                        // Create ORM texture
                        var ormTexture = new ORMTextureResource {
                            Name = ormTextureName,
                            TextureType = "ORM (Virtual)",
                            PackingMode = mode,
                            AOSource = aoTexture,
                            GlossSource = glossTexture,
                            MetallicSource = metalnessTexture,
                            Status = "Ready to pack"
                        };

                        viewModel.Textures.Add(ormTexture);
                        created++;
                    } catch (Exception ex) {
                        errors.Add($"{material.Name}: {ex.Message}");
                    }
                }

                var message = $"Batch ORM Creation Results:\n\n" +
                             $"? Created: {created}\n" +
                             $"? Skipped: {skipped}\n" +
                             $"? Errors: {errors.Count}";

                if (errors.Count > 0) {
                    message += $"\n\nErrors:\n{string.Join("\n", errors.Take(5))}";
                    if (errors.Count > 5) {
                        message += $"\n... and {errors.Count - 5} more";
                    }
                }

                logService.LogInfo($"Batch ORM creation: {created} created, {skipped} skipped, {errors.Count} errors");
                MessageBox.Show(message, "Batch ORM Creation Complete",
                    MessageBoxButton.OK, errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            } catch (Exception ex) {
                logService.LogError($"Error in batch ORM creation: {ex.Message}");
                MessageBox.Show($"Failed to create ORM textures: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteORMTexture_Click(object sender, RoutedEventArgs e) {
            // Get selected material to find associated ORM
            if (MaterialsDataGrid.SelectedItem is not MaterialResource material) {
                return;
            }

            // Get base material name without _mat suffix
            string baseMaterialName = (material.Name?.EndsWith("_mat", StringComparison.OrdinalIgnoreCase) == true)
                ? material.Name.Substring(0, material.Name.Length - 4)
                : (material.Name ?? "unknown");

            // Try all possible ORM suffixes
            var ormTexture = viewModel.Textures.OfType<ORMTextureResource>()
                .FirstOrDefault(t => t.Name == baseMaterialName + "_og" ||
                                     t.Name == baseMaterialName + "_ogm" ||
                                     t.Name == baseMaterialName + "_ogmh");

            if (ormTexture == null) {
                MessageBox.Show($"No ORM texture found for material '{material.Name}'.\n\nExpected: {baseMaterialName}_og, {baseMaterialName}_ogm, or {baseMaterialName}_ogmh",
                    "Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show($"Delete ORM texture '{ormTexture.Name}'?\n\nThis will only remove the virtual container, not the source textures.",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes) {
                viewModel.Textures.Remove(ormTexture);
                logService.LogInfo($"Deleted ORM texture: {ormTexture.Name}");
            }
        }

        private void DeleteORMFromList_Click(object sender, RoutedEventArgs e) {
            // Get selected texture from TexturesDataGrid
            if (TexturesDataGrid.SelectedItem is not ORMTextureResource ormTexture) {
                MessageBox.Show("Please select an ORM texture to delete.\n\nThis option only works for ORM textures (textureName_og, textureName_ogm, textureName_ogmh).",
                    "Not an ORM Texture", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show($"Delete ORM texture '{ormTexture.Name}'?\n\nThis will only remove the virtual container, not the source textures.",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes) {
                viewModel.Textures.Remove(ormTexture);
                logService.LogInfo($"Deleted ORM texture '{ormTexture.Name}' from texture list");
            }
        }

        // Reads KTX2 file header to extract metadata (width, height, mip levels)
        private async Task<(int Width, int Height, int MipLevels, string CompressionFormat)> GetKtx2InfoAsync(string ktx2Path) {
            return await Task.Run(() => {
                using var stream = File.OpenRead(ktx2Path);
                using var reader = new BinaryReader(stream);

                // KTX2 header structure:
                // Bytes 0-11: identifier (12 bytes) - skip
                // Bytes 12-15: vkFormat (uint32) - 0 means Basis Universal
                // Bytes 16-19: typeSize (uint32) - skip
                // Bytes 20-23: pixelWidth (uint32)
                // Bytes 24-27: pixelHeight (uint32)
                // Bytes 28-31: pixelDepth (uint32) - skip
                // Bytes 32-35: layerCount (uint32) - skip
                // Bytes 36-39: faceCount (uint32) - skip
                // Bytes 40-43: levelCount (uint32)
                // Bytes 44-47: supercompressionScheme (uint32)

                reader.BaseStream.Seek(12, SeekOrigin.Begin);
                uint vkFormat = reader.ReadUInt32();

                reader.BaseStream.Seek(20, SeekOrigin.Begin);
                int width = (int)reader.ReadUInt32();
                int height = (int)reader.ReadUInt32();

                reader.BaseStream.Seek(40, SeekOrigin.Begin);
                int mipLevels = (int)reader.ReadUInt32();
                uint supercompression = reader.ReadUInt32();

                // Only set compression format for Basis Universal textures (vkFormat = 0)
                string compressionFormat = "";
                if (vkFormat == 0) {
                    // supercompressionScheme: 1=BasisLZ(ETC1S), 0/2=UASTC(None/Zstd)
                    compressionFormat = supercompression == 1 ? "ETC1S" : "UASTC";
                }
                // vkFormat != 0 means raw texture format, no Basis compression

                return (width, height, mipLevels, compressionFormat);
            });
        }

        // Context menu handlers for texture rows
        private async void ProcessSelectedTextures_Click(object sender, RoutedEventArgs e) {
            if (viewModel.ProcessTexturesCommand is IAsyncRelayCommand<IList?> command) {
                await command.ExecuteAsync(TexturesDataGrid.SelectedItems);
            }
        }

        private void UploadTexture_Click(object sender, RoutedEventArgs e) {
            UploadTexturesButton_Click(sender, e);
        }

        private void OpenFileLocation_Click(object sender, RoutedEventArgs e) {
            if (TexturesDataGrid.SelectedItem is TextureResource texture && !string.IsNullOrEmpty(texture.Path)) {
                OpenFileInExplorer(texture.Path);
            }
        }

        /// <summary>
        /// Opens Windows Explorer and selects the specified file.
        /// </summary>
        private void OpenFileInExplorer(string filePath) {
            try {
                if (File.Exists(filePath)) {
                    // Use /select to highlight the file in Explorer
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
                } else {
                    // File doesn't exist, try to open the directory
                    var directory = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory)) {
                        System.Diagnostics.Process.Start("explorer.exe", $"\"{directory}\"");
                    } else {
                        MessageBox.Show("File and directory not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            } catch (Exception ex) {
                MessageBox.Show($"Failed to open file location: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

        // Context menu handlers for model rows
        private async void ProcessSelectedModel_Click(object sender, RoutedEventArgs e) {
            System.IO.File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [CONVERT] ProcessSelectedModel_Click START\n");
            try {
                var selectedModel = ModelsDataGrid.SelectedItem as ModelResource;
                if (selectedModel == null) {
                    MessageBox.Show("No model selected for processing.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                System.IO.File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [CONVERT] Model: {selectedModel.Name}\n");

                if (string.IsNullOrEmpty(selectedModel.Path)) {
                    MessageBox.Show("Model file path is empty.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Get settings from ModelConversionSettingsPanel
                // ������� ���� � ����� ��� ��������������� ����������� ���� ��������� (FBX/GLB)
                var settings = ModelConversionSettingsPanel.GetSettings(selectedModel.Path);

                // Create output directory
                var modelName = Path.GetFileNameWithoutExtension(selectedModel.Path);
                var sourceDir = Path.GetDirectoryName(selectedModel.Path) ?? Environment.CurrentDirectory;
                var outputDir = Path.Combine(sourceDir, "glb");
                Directory.CreateDirectory(outputDir);

                logService.LogInfo($"Processing model: {selectedModel.Name}");
                logService.LogInfo($"  Source: {selectedModel.Path}");
                logService.LogInfo($"  Output: {outputDir}");

                // Load FBX2glTF and gltfpack paths from global settings
                var modelConversionSettings = ModelConversion.Settings.ModelConversionSettingsManager.LoadSettings();
                var fbx2glTFPath = string.IsNullOrWhiteSpace(modelConversionSettings.FBX2glTFExecutablePath)
                    ? "FBX2glTF-windows-x64.exe"
                    : modelConversionSettings.FBX2glTFExecutablePath;
                var gltfPackPath = string.IsNullOrWhiteSpace(modelConversionSettings.GltfPackExecutablePath)
                    ? "gltfpack.exe"
                    : modelConversionSettings.GltfPackExecutablePath;

                logService.LogInfo($"  FBX2glTF: {fbx2glTFPath}");
                logService.LogInfo($"  gltfpack: {gltfPackPath}");

                ProgressTextBlock.Text = $"Processing {selectedModel.Name}...";

                // Create the model conversion pipeline
                var pipeline = new ModelConversion.Pipeline.ModelConversionPipeline(fbx2glTFPath, gltfPackPath);

                System.IO.File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [CONVERT] Calling ConvertAsync\n");
                var result = await pipeline.ConvertAsync(selectedModel.Path, outputDir, settings);
                System.IO.File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [CONVERT] ConvertAsync returned, Success={result.Success}\n");

                if (result.Success) {
                    logService.LogInfo($"Model processed successfully");
                    logService.LogInfo($"  LOD files: {result.LodFiles.Count}");
                    logService.LogInfo($"  Manifest: {result.ManifestPath}");

                    // Автоматически обновляем viewport с новыми GLB LOD файлами
                    logService.LogInfo("Refreshing viewport with converted GLB LOD files...");
                    System.IO.File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [CONVERT] Before TryLoadGlbLodAsync\n");
                    await TryLoadGlbLodAsync(selectedModel.Path);
                    System.IO.File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [CONVERT] After TryLoadGlbLodAsync\n");

                    System.IO.File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [CONVERT] About to show MessageBox\n");
                    MessageBox.Show($"Model processed successfully!\n\nLOD files: {result.LodFiles.Count}\nOutput: {outputDir}",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    System.IO.File.AppendAllText("glblod_debug.txt", $"{DateTime.Now}: [CONVERT] MessageBox closed\n");
                } else {
                    var errors = string.Join("\n", result.Errors);
                    logService.LogError($"? Model processing failed:\n{errors}");
                    MessageBox.Show($"Model processing failed:\n\n{errors}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                ProgressTextBlock.Text = "Ready";
            } catch (Exception ex) {
                logService.LogError($"Error processing model: {ex.Message}");
                MessageBox.Show($"Error processing model: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ProgressTextBlock.Text = "Ready";
            }
        }

        private void OpenModelFileLocation_Click(object sender, RoutedEventArgs e) {
            if (ModelsDataGrid.SelectedItem is ModelResource model && !string.IsNullOrEmpty(model.Path)) {
                OpenFileInExplorer(model.Path);
            }
        }

        private void CopyModelPath_Click(object sender, RoutedEventArgs e) {
            if (ModelsDataGrid.SelectedItem is ModelResource model) {
                if (!string.IsNullOrEmpty(model.Path)) {
                    try {
                        Clipboard.SetText(model.Path);
                        MessageBox.Show($"Path copied to clipboard:\n{model.Path}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    } catch (Exception ex) {
                        MessageBox.Show($"Failed to copy path: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void RefreshModelPreview_Click(object sender, RoutedEventArgs e) {
            if (ModelsDataGrid.SelectedItem is ModelResource model) {
                // Trigger a refresh by re-selecting the model
                ModelsDataGrid_SelectionChanged(ModelsDataGrid, null!);
            }
        }

        private void ModelConversionSettingsPanel_ProcessRequested(object sender, EventArgs e) {
            // When user clicks "Process Selected Model" button in the settings panel
            ProcessSelectedModel_Click(sender, new RoutedEventArgs());
        }

        private void ModelPreviewGridSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) {
            // ��������� ������� ������ ModelPreviewRow � ���������
            if (ModelPreviewRow != null) {
                double currentHeight = ModelPreviewRow.ActualHeight;
                if (currentHeight > 0 && currentHeight >= 200 && currentHeight <= 800) {
                    AppSettings.Default.ModelPreviewRowHeight = currentHeight;
                    AppSettings.Default.Save();
                }
            }
        }

        /// <summary>
        /// ���������� ������� ������ ��� ����� ����
        /// </summary>
        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) {
            // F - Fit model to viewport (ZoomExtents)
            if (e.Key == System.Windows.Input.Key.F) {
                viewPort3d?.ZoomExtents();
                e.Handled = true;
            }
        }

        #endregion
    }
}