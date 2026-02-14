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
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System.Xml;

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
        private bool _isUpdatingMasterComboBox = false; // Flag to prevent recursive master combobox updates
        private CancellationTokenSource cancellationTokenSource = new();

        // Service facades (group related services to reduce constructor parameters: 21 → 5+viewModel)
        private readonly ConnectionServiceFacade connectionServices;
        private readonly AssetDataServiceFacade assetDataServices;
        private readonly TextureViewerServiceFacade textureViewerServices;
        private readonly ILogService logService;
        private readonly IDataGridLayoutService dataGridLayoutService;

        // Shortcut properties for backward compatibility with partial files
        private IPlayCanvasService playCanvasService => connectionServices.PlayCanvasService;
        private IConnectionStateService connectionStateService => connectionServices.ConnectionStateService;
        private IPlayCanvasCredentialsService credentialsService => connectionServices.CredentialsService;
        private IProjectSelectionService projectSelectionService => connectionServices.ProjectSelectionService;
        private IProjectConnectionService projectConnectionService => connectionServices.ProjectConnectionService;
        private IProjectFileWatcherService projectFileWatcherService => connectionServices.ProjectFileWatcherService;
        private IAssetLoadCoordinator assetLoadCoordinator => assetDataServices.AssetLoadCoordinator;
        private IAssetResourceService assetResourceService => assetDataServices.AssetResourceService;
        private IAssetJsonParserService assetJsonParserService => assetDataServices.AssetJsonParserService;
        private IFileStatusScannerService fileStatusScannerService => assetDataServices.FileStatusScannerService;
        private ILocalCacheService localCacheService => assetDataServices.LocalCacheService;
        private IProjectAssetService projectAssetService => assetDataServices.ProjectAssetService;
        private ITexturePreviewService texturePreviewService => textureViewerServices.TexturePreviewService;
        private ITextureChannelService textureChannelService => textureViewerServices.TextureChannelService;
        private IPreviewRendererCoordinator previewRendererCoordinator => textureViewerServices.PreviewRendererCoordinator;
        private IHistogramCoordinator histogramCoordinator => textureViewerServices.HistogramCoordinator;
        private IORMTextureService ormTextureService => textureViewerServices.ORMTextureService;
        private IKtx2InfoService ktx2InfoService => textureViewerServices.Ktx2InfoService;
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
        private string? selectedORMSubGroupName; // Имя выбранной ORM подгруппы для визуального выделения
        private List<string> _lastExportedFiles = new(); // Последние экспортированные файлы для upload

        // Chunk Editor state
        private static IHighlightingDefinition? _glslHighlighting;
        private static IHighlightingDefinition? _wgslHighlighting;
        private MasterMaterials.Models.ShaderChunk? _currentEditingChunk;
        private bool _chunkEditorHasUnsavedChanges;
        private string? _originalGlslCode;
        private string? _originalWgslCode;

        private string? ProjectFolderPath => projectSelectionService.ProjectFolderPath;
        private string? ProjectName => projectSelectionService.ProjectName;
        private string? UserId => projectSelectionService.UserId;
        private string? UserName => projectSelectionService.UserName;

        // Projects � Branches ������ � MainViewModel - ������� ������������� ����������

        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private bool TryGetApiKey(out string apiKey) {
            if (credentialsService.TryGetApiKey(out apiKey)) {
                return true;
            }

            logService.LogError("API key is missing or could not be decrypted.");
            return false;
        }

        private readonly MainViewModel viewModel;

        public MainViewModel ViewModel { get; }

        public MainWindow(
            ConnectionServiceFacade connectionServices,
            AssetDataServiceFacade assetDataServices,
            TextureViewerServiceFacade textureViewerServices,
            ILogService logService,
            IDataGridLayoutService dataGridLayoutService,
            MainViewModel viewModel) {
            this.connectionServices = connectionServices ?? throw new ArgumentNullException(nameof(connectionServices));
            this.assetDataServices = assetDataServices ?? throw new ArgumentNullException(nameof(assetDataServices));
            this.textureViewerServices = textureViewerServices ?? throw new ArgumentNullException(nameof(textureViewerServices));
            this.logService = logService ?? throw new ArgumentNullException(nameof(logService));
            this.dataGridLayoutService = dataGridLayoutService ?? throw new ArgumentNullException(nameof(dataGridLayoutService));
            this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            ViewModel = this.viewModel;

            InitializeComponent();

            // Alt+Tab freeze fix - must be called after window handle exists
            this.SourceInitialized += (s, e) => SetupAltTabFix();

            UpdatePreviewContentHeight(DefaultPreviewContentHeight);
            ResetPreviewState();
            _ = InitializeOnStartup();

            // ������������� ConversionSettings
            InitializeConversionSettings();
            InitializeMaterialInfoPanel();
            InitializeServerFileInfoPanel();
            InitializeChunkSlotsPanel();
            InitializeMasterMaterialsEditorPanel();
            InitializeExportToolsPanel();
            InitializeConnectionPanel();

            viewModel.ConversionSettingsProvider = ConversionSettingsPanel;
            viewModel.TextureProcessingCompleted += ViewModel_TextureProcessingCompleted;
            viewModel.TexturePreviewLoaded += ViewModel_TexturePreviewLoaded;

            // Subscribe to TextureSelectionViewModel events
            viewModel.TextureSelection.SelectionReady += OnTextureSelectionReady;
            viewModel.TextureSelection.ORMTextureSelected += OnORMTextureSelected;
            viewModel.TextureSelection.PanelVisibilityRequested += OnPanelVisibilityRequested;

            // Subscribe to ORMTextureViewModel events
            viewModel.ORMTexture.ORMCreated += OnORMCreated;
            viewModel.ORMTexture.ORMDeleted += OnORMDeleted;
            viewModel.ORMTexture.ConfirmationRequested += OnORMConfirmationRequested;
            viewModel.ORMTexture.ErrorOccurred += OnORMErrorOccurred;
            viewModel.ORMTexture.BatchCreationCompleted += OnORMBatchCreationCompleted;

            // Subscribe to TextureConversionSettingsViewModel events
            viewModel.ConversionSettings.SettingsLoaded += OnConversionSettingsLoaded;
            viewModel.ConversionSettings.SettingsSaved += OnConversionSettingsSaved;
            viewModel.ConversionSettings.ErrorOccurred += OnConversionSettingsError;

            // Subscribe to AssetLoadingViewModel events
            viewModel.AssetLoading.AssetsLoaded += OnAssetsLoaded;
            viewModel.AssetLoading.LoadingProgressChanged += OnAssetLoadingProgressChanged;
            viewModel.AssetLoading.ORMTexturesDetected += OnORMTexturesDetected;
            viewModel.AssetLoading.VirtualORMTexturesGenerated += OnVirtualORMTexturesGenerated;
            viewModel.AssetLoading.UploadStatesRestored += OnUploadStatesRestored;
            viewModel.AssetLoading.B2VerificationCompleted += OnB2VerificationCompleted;
            viewModel.AssetLoading.ErrorOccurred += OnAssetLoadingError;

            // Subscribe to MaterialSelectionViewModel events
            viewModel.MaterialSelection.MaterialParametersLoaded += OnMaterialParametersLoaded;
            viewModel.MaterialSelection.NavigateToTextureRequested += OnNavigateToTextureRequested;
            viewModel.MaterialSelection.ErrorOccurred += OnMaterialSelectionError;

            // �������� �� ������� ������ �������� �����������
            ConversionSettingsPanel.AutoDetectRequested += ConversionSettingsPanel_AutoDetectRequested;
            ConversionSettingsPanel.ConvertRequested += ConversionSettingsPanel_ConvertRequested;
            ConversionSettingsPanel.SettingsChanged += ConversionSettingsPanel_SettingsChanged;

            // Server assets panel selection (deferred to Loaded to avoid initialization issues)
            this.Loaded += (s, e) => {
                ServerAssetsPanel.SelectionChanged += (sender, asset) => UpdateServerFileInfo(asset);
                ServerAssetsPanel.NavigateToResourceRequested += OnNavigateToResourceRequested;
                ServerAssetsPanel.FilesDeleted += OnServerFilesDeleted;
                ServerAssetsPanel.ServerAssetsRefreshed += OnServerAssetsRefreshed;

                // Initialize chunk code editor
                InitializeChunkCodeEditor();
            };

            // Subscribe to file watcher events (debounced by service)
            projectFileWatcherService.FilesDeletionDetected += OnFilesDeletionDetected;

            // ����������� ������ ���������� � ����������� � ������ � �������
            viewModel.VersionText = $"v{VersionHelper.GetVersionString()}";


#if DEBUG
            // Dev-only: load test model at startup for quick debugging
            if (File.Exists(MainWindowHelpers.MODEL_PATH)) {
                _ = LoadModelAsync(path: MainWindowHelpers.MODEL_PATH);
            }
#endif

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
        /// ID текущего проекта для хранения настроек ресурсов
        /// </summary>
        public int CurrentProjectId {
            get {
                if (!string.IsNullOrEmpty(viewModel.SelectedProjectId)) {
                    if (int.TryParse(viewModel.SelectedProjectId, out int projectId)) {
                        return projectId;
                    }
                }
                return 0;
            }
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
            await UiAsyncHelper.ExecuteAsync(
                () => HandleProjectSelectionChangedAsync(),
                nameof(ViewModel_ProjectSelectionChanged));
        }

        private async void ViewModel_BranchSelectionChanged(object? sender, BranchSelectionChangedEventArgs e) {
            await UiAsyncHelper.ExecuteAsync(
                () => HandleBranchSelectionChangedAsync(),
                nameof(ViewModel_BranchSelectionChanged));
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

        // Viewer management, Server file info, Navigation, and Server sync handlers are in MainWindow.ServerAssets.cs

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            // Guard against early calls during initialization
            if (!this.IsLoaded || exportToolsPanel == null) return;

            if (tabControl.SelectedItem is TabItem selectedTab) {
                switch (selectedTab.Header.ToString()) {
                    case "Textures":
                        ShowViewer(ViewerType.Texture);
                        UpdateExportCounts();
                        exportToolsPanel.TextureToolsPanel.Visibility = Visibility.Visible;
                        break;
                    case "Models":
                        ShowViewer(ViewerType.Model);
                        UpdateExportCounts();
                        exportToolsPanel.TextureToolsPanel.Visibility = Visibility.Collapsed;
                        break;
                    case "Materials":
                        ShowViewer(ViewerType.Material);
                        UpdateExportCounts();
                        exportToolsPanel.TextureToolsPanel.Visibility = Visibility.Collapsed;
                        break;
                    case "Master Materials":
                        ShowViewer(ViewerType.ChunkSlots);
                        exportToolsPanel.TextureToolsPanel.Visibility = Visibility.Collapsed;
                        break;
                    case "Server":
                        ShowViewer(ViewerType.ServerFile);
                        exportToolsPanel.TextureToolsPanel.Visibility = Visibility.Collapsed;
                        break;
                    case "Logs":
                        ShowViewer(ViewerType.None);
                        exportToolsPanel.TextureToolsPanel.Visibility = Visibility.Collapsed;
                        break;
                }
            }
        }

        private async Task HandleProjectSelectionChangedAsync() {
            // Clear texture check cache on project change to prevent unbounded memory growth
            texturesBeingChecked.Clear();

            if (projectSelectionService.IsProjectInitializationInProgress) {
                logService.LogInfo("Skipping project selection - initialization in progress");
                return;
            }

            if (string.IsNullOrEmpty(viewModel.SelectedProjectId)) {
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
            // Clear texture check cache on branch change to prevent unbounded memory growth
            texturesBeingChecked.Clear();

            SaveCurrentSettings();

            if (projectSelectionService.IsBranchInitializationInProgress) {
                return;
            }

            if (connectionStateService.CurrentState != ConnectionState.Disconnected) {
                await CheckProjectState();
            }
        }

        private void TexturesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            // Update selection count and command state (lightweight, UI-only)
            UpdateExportCounts();
            viewModel.SelectedTexture = TexturesDataGrid.SelectedItem as TextureResource;
            viewModel.ProcessTexturesCommand.NotifyCanExecuteChanged();

            // Delegate to ViewModel for debouncing, cancellation, and preview loading
            var selectedResource = TexturesDataGrid.SelectedItem as BaseResource;
            viewModel.TextureSelection.SelectTextureCommand.Execute(selectedResource);
        }

        /// <summary>
        /// Refreshes the DataGrid and reloads the preview for the currently selected texture.
        /// Called after ORM packing to update the UI (row colors and preview).
        /// </summary>
        public async Task RefreshCurrentTextureAsync() {
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

            // Reload the preview using ViewModel
            if (selectedItem is BaseResource resource) {
                // Small delay to allow DataGrid to rebind
                await Task.Delay(100);

                // Trigger preview reload via ViewModel
                logService.LogInfo($"[RefreshCurrentTexture] Triggering preview reload for: {resource.Name ?? "unknown"}");
                await viewModel.TextureSelection.RefreshPreviewCommand.ExecuteAsync(null);
            }
        }


        // TextureSelectionViewModel Event Handlers and texture preview loading are in MainWindow.TextureSelection.cs

private void AboutMenu(object? sender, RoutedEventArgs e) {
            var aboutWindow = new Windows.AboutWindow {
                Owner = this
            };
            aboutWindow.ShowDialog();
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

        private void SetThemeState(Helpers.ThemeMode mode) {
            viewModel.IsThemeAuto = mode == Helpers.ThemeMode.Auto;
            viewModel.IsThemeLight = mode == Helpers.ThemeMode.Light;
            viewModel.IsThemeDark = mode == Helpers.ThemeMode.Dark;
            ThemeHelper.CurrentMode = mode;
            viewModel.IsDarkThemeChecked = ThemeHelper.IsDarkTheme;
            RefreshHistogramForTheme();
        }

        private void ThemeAuto_Click(object sender, RoutedEventArgs e) {
            SetThemeState(Helpers.ThemeMode.Auto);
        }

        private void ThemeLight_Click(object sender, RoutedEventArgs e) {
            SetThemeState(Helpers.ThemeMode.Light);
        }

        private void ThemeDark_Click(object sender, RoutedEventArgs e) {
            SetThemeState(Helpers.ThemeMode.Dark);
        }

        private void DarkThemeCheckBox_Click(object sender, RoutedEventArgs e) {
            bool isDark = viewModel.IsDarkThemeChecked;
            SetThemeState(isDark ? Helpers.ThemeMode.Dark : Helpers.ThemeMode.Light);
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
            try {
                if (e?.Row?.DataContext is TextureResource texture) {
                    // Initialize conversion settings for the texture if not already done
                    // Use flag to prevent repeated initialization on every scroll
                    if (!texture.IsConversionSettingsInitialized) {
                        InitializeTextureConversionSettings(texture);
                    }
                }
            } catch (Exception ex) {
                logger.Error(ex, "Error in TexturesDataGrid_LoadingRow");
            }
        }






private void ToggleViewerButton_Click(object? sender, RoutedEventArgs e) {
            if (isViewerVisible == true) {
                // Save current width before hiding
                if (PreviewColumn.Width.Value > 0) {
                    AppSettings.Default.RightPanelPreviousWidth = PreviewColumn.Width.Value;
                }
                AppSettings.Default.RightPanelWidth = 0; // Mark as hidden
                viewModel.ToggleViewButtonContent = "►";
                PreviewColumn.Width = new GridLength(0);
                PreviewColumn.MinWidth = 0;
            } else {
                // Restore saved width
                double restoreWidth = AppSettings.Default.RightPanelPreviousWidth;
                if (restoreWidth < 256) restoreWidth = 300; // Use default if too small
                viewModel.ToggleViewButtonContent = "◄";
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

        // Context menu handlers, ORM creation, Master Material menu, and file location helpers are in MainWindow.ContextMenuHandlers.cs

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

        private DispatcherTimer? _windowStateTimer;

        private void Window_StateChanged(object sender, EventArgs e) {
            // Hide TexturesDataGrid during maximize/restore to prevent freeze with grouping
            if (TexturesDataGrid == null) return;

            // Only apply workaround when grouping is enabled
            if (!Settings.AppSettings.Default.GroupTexturesByType) return;

            // Hide DataGrid before layout recalculation
            TexturesDataGrid.Visibility = Visibility.Hidden;

            // Show after layout settles
            _windowStateTimer?.Stop();
            _windowStateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _windowStateTimer.Tick += (s, args) => {
                _windowStateTimer?.Stop();
                _windowStateTimer = null;
                TexturesDataGrid.Visibility = Visibility.Visible;
            };
            _windowStateTimer.Start();
        }
    }
}
