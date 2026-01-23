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
        private readonly IProjectConnectionService projectConnectionService;
        private readonly IAssetLoadCoordinator assetLoadCoordinator;
        private readonly IProjectFileWatcherService projectFileWatcherService;
        private readonly IKtx2InfoService ktx2InfoService;
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
            IProjectConnectionService projectConnectionService,
            IAssetLoadCoordinator assetLoadCoordinator,
            IProjectFileWatcherService projectFileWatcherService,
            IKtx2InfoService ktx2InfoService,
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
            this.projectConnectionService = projectConnectionService ?? throw new ArgumentNullException(nameof(projectConnectionService));
            this.assetLoadCoordinator = assetLoadCoordinator ?? throw new ArgumentNullException(nameof(assetLoadCoordinator));
            this.projectFileWatcherService = projectFileWatcherService ?? throw new ArgumentNullException(nameof(projectFileWatcherService));
            this.ktx2InfoService = ktx2InfoService ?? throw new ArgumentNullException(nameof(ktx2InfoService));
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
            VersionTextBlock.Text = $"v{VersionHelper.GetVersionString()}";


#if DEBUG
            // Dev-only: load test model at startup for quick debugging
            if (File.Exists(MainWindowHelpers.MODEL_PATH)) {
                LoadModel(path: MainWindowHelpers.MODEL_PATH);
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
                if (ProjectsComboBox?.SelectedItem is KeyValuePair<string, string> selectedProject) {
                    if (int.TryParse(selectedProject.Key, out int projectId)) {
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
            ServerFileInfoScroll.Visibility = Visibility.Collapsed;
            ChunkSlotsScroll.Visibility = Visibility.Collapsed;
        }

        private void ShowModelViewer() {
            TextureViewerScroll.Visibility = Visibility.Collapsed;
            ModelViewerScroll.Visibility = Visibility.Visible;
            MaterialViewerScroll.Visibility = Visibility.Collapsed;
            ServerFileInfoScroll.Visibility = Visibility.Collapsed;
            ChunkSlotsScroll.Visibility = Visibility.Collapsed;
        }

        private void ShowMaterialViewer() {
            TextureViewerScroll.Visibility = Visibility.Collapsed;
            ModelViewerScroll.Visibility = Visibility.Collapsed;
            MaterialViewerScroll.Visibility = Visibility.Visible;
            ServerFileInfoScroll.Visibility = Visibility.Collapsed;
            ChunkSlotsScroll.Visibility = Visibility.Collapsed;
        }

        private void HideAllViewers() {
            TextureViewerScroll.Visibility = Visibility.Collapsed;
            ModelViewerScroll.Visibility = Visibility.Collapsed;
            MaterialViewerScroll.Visibility = Visibility.Collapsed;
            ServerFileInfoScroll.Visibility = Visibility.Collapsed;
            ChunkSlotsScroll.Visibility = Visibility.Collapsed;
        }

        private void ShowServerFileInfo() {
            TextureViewerScroll.Visibility = Visibility.Collapsed;
            ModelViewerScroll.Visibility = Visibility.Collapsed;
            MaterialViewerScroll.Visibility = Visibility.Collapsed;
            ServerFileInfoScroll.Visibility = Visibility.Visible;
            ChunkSlotsScroll.Visibility = Visibility.Collapsed;
        }

        private void ShowChunkSlotsViewer() {
            TextureViewerScroll.Visibility = Visibility.Collapsed;
            ModelViewerScroll.Visibility = Visibility.Collapsed;
            MaterialViewerScroll.Visibility = Visibility.Collapsed;
            ServerFileInfoScroll.Visibility = Visibility.Collapsed;
            ChunkSlotsScroll.Visibility = Visibility.Visible;
        }

        private ViewModels.ServerAssetViewModel? _selectedServerAsset;

        /// <summary>
        /// Updates the server file info panel with the selected asset
        /// </summary>
        public void UpdateServerFileInfo(ViewModels.ServerAssetViewModel? asset) {
            _selectedServerAsset = asset;

            // Safety check - panel controls may not be ready
            if (ServerFileNameText == null) return;

            if (asset == null) {
                ServerFileNameText.Text = "-";
                ServerFileTypeText.Text = "-";
                ServerFileSizeText.Text = "-";
                ServerFileSyncStatusText.Text = "-";
                ServerFileSyncStatusText.Foreground = System.Windows.Media.Brushes.Gray;
                ServerFileUploadedText.Text = "-";
                ServerFileSha1Text.Text = "-";
                ServerFileRemotePathText.Text = "-";
                ServerFileCdnUrlText.Text = "-";
                ServerFileLocalPathText.Text = "-";
                return;
            }

            ServerFileNameText.Text = asset.FileName;
            ServerFileTypeText.Text = asset.FileType;
            ServerFileSizeText.Text = asset.SizeDisplay;
            ServerFileSyncStatusText.Text = asset.SyncStatus;
            ServerFileSyncStatusText.Foreground = asset.SyncStatusColor;
            ServerFileUploadedText.Text = asset.UploadedAtDisplay;
            ServerFileSha1Text.Text = asset.ContentSha1;
            ServerFileRemotePathText.Text = asset.RemotePath;
            ServerFileCdnUrlText.Text = asset.CdnUrl ?? "-";
            ServerFileLocalPathText.Text = asset.LocalPath ?? "Not found locally";
        }

        private void CopyServerUrlButton_Click(object sender, RoutedEventArgs e) {
            if (_selectedServerAsset != null && !string.IsNullOrEmpty(_selectedServerAsset.CdnUrl)) {
                if (Helpers.ClipboardHelper.SetText(_selectedServerAsset.CdnUrl)) {
                    logService.LogInfo($"Copied CDN URL: {_selectedServerAsset.CdnUrl}");
                }
            }
        }

        private void OnNavigateToResourceRequested(object? sender, string fileName) {
            string baseName = System.IO.Path.GetFileNameWithoutExtension(fileName);

            // Strip ORM suffix patterns for better matching (_og, _ogm, _ogmh)
            string baseNameWithoutSuffix = baseName;
            bool isOrmFile = false;
            if (baseName.EndsWith("_og", StringComparison.OrdinalIgnoreCase)) {
                baseNameWithoutSuffix = baseName[..^3];
                isOrmFile = true;
            } else if (baseName.EndsWith("_ogm", StringComparison.OrdinalIgnoreCase)) {
                baseNameWithoutSuffix = baseName[..^4];
                isOrmFile = true;
            } else if (baseName.EndsWith("_ogmh", StringComparison.OrdinalIgnoreCase)) {
                baseNameWithoutSuffix = baseName[..^5];
                isOrmFile = true;
            }

            // Try textures (including ORM textures)
            var texture = viewModel.Textures.FirstOrDefault(t => {
                // Direct name match (most common case - check first)
                if (t.Name?.Equals(baseName, StringComparison.OrdinalIgnoreCase) == true)
                    return true;
                // Path ends with filename
                if (t.Path != null && t.Path.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                    return true;
                // For ORM textures, try additional matching
                if (t is Resources.ORMTextureResource orm) {
                    // Match against SettingsKey
                    if (!string.IsNullOrEmpty(orm.SettingsKey)) {
                        var settingsKeyBase = orm.SettingsKey.StartsWith("orm_", StringComparison.OrdinalIgnoreCase)
                            ? orm.SettingsKey[4..]
                            : orm.SettingsKey;
                        if (baseName.Equals(orm.SettingsKey, StringComparison.OrdinalIgnoreCase) ||
                            baseName.Equals(settingsKeyBase, StringComparison.OrdinalIgnoreCase) ||
                            baseNameWithoutSuffix.Equals(settingsKeyBase, StringComparison.OrdinalIgnoreCase) ||
                            settingsKeyBase.Contains(baseNameWithoutSuffix, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    // Match file name from Path property (packed ORM)
                    if (!string.IsNullOrEmpty(orm.Path)) {
                        var ormPathBaseName = System.IO.Path.GetFileNameWithoutExtension(orm.Path);
                        if (baseName.Equals(ormPathBaseName, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    // Match ORM texture name patterns
                    var cleanName = t.Name?.Replace("[ORM Texture - Not Packed]", "").Trim();
                    if (!string.IsNullOrEmpty(cleanName)) {
                        if (cleanName.Equals(baseName, StringComparison.OrdinalIgnoreCase) ||
                            cleanName.Equals(baseNameWithoutSuffix, StringComparison.OrdinalIgnoreCase) ||
                            baseNameWithoutSuffix.Contains(cleanName, StringComparison.OrdinalIgnoreCase) ||
                            cleanName.Contains(baseNameWithoutSuffix, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    // Match against source texture names
                    if (orm.AOSource?.Name != null && baseNameWithoutSuffix.Contains(orm.AOSource.Name.Replace("_ao", "").Replace("_AO", ""), StringComparison.OrdinalIgnoreCase))
                        return true;
                    if (orm.GlossSource?.Name != null && baseNameWithoutSuffix.Contains(orm.GlossSource.Name.Replace("_gloss", "").Replace("_Gloss", "").Replace("_roughness", "").Replace("_Roughness", ""), StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            });
            if (texture != null) {
                tabControl.SelectedItem = TexturesTabItem;
                TexturesDataGrid.SelectedItem = texture;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, () => {
                    TexturesDataGrid.ScrollIntoView(texture);
                });
                return;
            }

            // For ORM files, highlight the ORM group header and show ORM panel
            if (isOrmFile) {
                tabControl.SelectedItem = TexturesTabItem;
                TexturesDataGrid.SelectedItems.Clear();

                // Ищем текстуру по GroupName = baseNameWithoutSuffix (например "oldMailBox")
                var textureInGroup = viewModel.Textures.FirstOrDefault(t =>
                    !string.IsNullOrEmpty(t.GroupName) &&
                    t.GroupName.Equals(baseNameWithoutSuffix, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(t.SubGroupName));

                if (textureInGroup != null) {
                    SelectedORMSubGroupName = textureInGroup.SubGroupName;

                    // Скролл к текстуре в группе
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, () => {
                        TexturesDataGrid.ScrollIntoView(textureInGroup);
                    });

                    // Показываем ORM панель если есть ParentORMTexture
                    if (textureInGroup.ParentORMTexture != null) {
                        var ormTexture = textureInGroup.ParentORMTexture;

                        if (ConversionSettingsExpander != null) {
                            ConversionSettingsExpander.Visibility = Visibility.Collapsed;
                        }

                        if (ORMPanel != null) {
                            ORMPanel.Visibility = Visibility.Visible;
                            var availableTextures = viewModel.Textures.Where(t => !(t is ORMTextureResource)).ToList();
                            ORMPanel.Initialize(this, availableTextures);
                            ORMPanel.SetORMTexture(ormTexture);
                        }

                        TextureNameTextBlock.Text = "Texture Name: " + ormTexture.Name;
                        TextureColorSpaceTextBlock.Text = "Color Space: Linear (ORM)";

                        if (!string.IsNullOrEmpty(ormTexture.Path) && File.Exists(ormTexture.Path)) {
                            TextureResolutionTextBlock.Text = ormTexture.Resolution != null && ormTexture.Resolution.Length >= 2
                                ? $"Resolution: {ormTexture.Resolution[0]}x{ormTexture.Resolution[1]}"
                                : "Resolution: Unknown";
                            TextureFormatTextBlock.Text = "Format: KTX2 (packed)";
                            _ = LoadORMPreviewAsync(ormTexture);
                        } else {
                            TextureResolutionTextBlock.Text = "Resolution: Not packed yet";
                            TextureFormatTextBlock.Text = "Format: Not packed";
                        }

                        viewModel.SelectedTexture = ormTexture;
                    }
                }
                return;
            }

            // Try models
            var model = viewModel.Models.FirstOrDefault(m =>
                m.Name?.Equals(baseName, StringComparison.OrdinalIgnoreCase) == true ||
                (m.Path != null && m.Path.EndsWith(fileName, StringComparison.OrdinalIgnoreCase)));
            if (model != null) {
                logger.Debug($"[Navigation] Found model: {model.Name}");
                tabControl.SelectedItem = ModelsTabItem;
                ModelsDataGrid.SelectedItem = model;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, () => {
                    ModelsDataGrid.ScrollIntoView(model);
                });
                return;
            }

            // Try materials
            var material = viewModel.Materials.FirstOrDefault(m =>
                m.Name?.Equals(baseName, StringComparison.OrdinalIgnoreCase) == true ||
                (m.Path != null && m.Path.EndsWith(fileName, StringComparison.OrdinalIgnoreCase)));
            if (material != null) {
                logger.Debug($"[Navigation] Found material: {material.Name}");
                tabControl.SelectedItem = MaterialsTabItem;
                MaterialsDataGrid.SelectedItem = material;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, () => {
                    MaterialsDataGrid.ScrollIntoView(material);
                });
                return;
            }

            logger.Debug($"[Navigation] Resource not found: {fileName}");
        }

        /// <summary>
        /// Обработчик события удаления файлов с сервера - сбрасывает статусы ресурсов
        /// </summary>
        private void OnServerFilesDeleted(List<string> deletedPaths) {
            if (deletedPaths == null || deletedPaths.Count == 0) return;

            // Нормализуем пути для сравнения
            var normalizedPaths = deletedPaths
                .Select(p => p.Replace('\\', '/').ToLowerInvariant())
                .ToHashSet();

            // Сбрасываем статусы текстур
            foreach (var texture in viewModel.Textures) {
                if (!string.IsNullOrEmpty(texture.RemoteUrl)) {
                    // Извлекаем относительный путь из RemoteUrl (убираем базовый CDN URL)
                    var remotePath = ExtractRelativePathFromUrl(texture.RemoteUrl);
                    if (remotePath != null && normalizedPaths.Contains(remotePath.ToLowerInvariant())) {
                        texture.UploadStatus = null;
                        texture.UploadedHash = null;
                        texture.RemoteUrl = null;
                        texture.LastUploadedAt = null;
                    }
                }
            }

            // Сбрасываем статусы материалов
            foreach (var material in viewModel.Materials) {
                if (!string.IsNullOrEmpty(material.RemoteUrl)) {
                    var remotePath = ExtractRelativePathFromUrl(material.RemoteUrl);
                    if (remotePath != null && normalizedPaths.Contains(remotePath.ToLowerInvariant())) {
                        material.UploadStatus = null;
                        material.UploadedHash = null;
                        material.RemoteUrl = null;
                        material.LastUploadedAt = null;
                    }
                }
            }

            // Сбрасываем статусы моделей
            foreach (var model in viewModel.Models) {
                if (!string.IsNullOrEmpty(model.RemoteUrl)) {
                    var remotePath = ExtractRelativePathFromUrl(model.RemoteUrl);
                    if (remotePath != null && normalizedPaths.Contains(remotePath.ToLowerInvariant())) {
                        model.UploadStatus = null;
                        model.UploadedHash = null;
                        model.RemoteUrl = null;
                        model.LastUploadedAt = null;
                    }
                }
            }

            logger.Info($"Reset upload status for resources matching {deletedPaths.Count} deleted server paths");
        }

        /// <summary>
        /// Извлекает относительный путь из полного CDN URL
        /// </summary>
        private string? ExtractRelativePathFromUrl(string url) {
            if (string.IsNullOrEmpty(url)) return null;

            // CDN URL обычно имеет формат: https://cdn.example.com/bucket/path/to/file.ext
            // или просто путь: content/path/to/file.ext
            try {
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) {
                    // Убираем начальный слэш из пути
                    return uri.AbsolutePath.TrimStart('/');
                }
                // Если это уже относительный путь
                return url.Replace('\\', '/').TrimStart('/');
            } catch {
                return url.Replace('\\', '/').TrimStart('/');
            }
        }

        /// <summary>
        /// Обработчик события обновления списка файлов на сервере - верифицирует статусы ресурсов
        /// </summary>
        private void OnServerAssetsRefreshed(HashSet<string> serverPaths) {
            if (serverPaths == null || serverPaths.Count == 0) {
                // Сервер пустой - сбрасываем все upload статусы
                ResetAllUploadStatuses();
                return;
            }

            int verified = 0;
            int notFound = 0;

            // Проверяем текстуры
            foreach (var texture in viewModel.Textures) {
                if (texture.UploadStatus == "Uploaded" && !string.IsNullOrEmpty(texture.RemoteUrl)) {
                    var remotePath = ExtractRelativePathFromUrl(texture.RemoteUrl);
                    if (remotePath != null && serverPaths.Contains(remotePath)) {
                        verified++;
                    } else {
                        texture.UploadStatus = null;
                        texture.UploadedHash = null;
                        texture.RemoteUrl = null;
                        texture.LastUploadedAt = null;
                        notFound++;
                    }
                }
            }

            // Проверяем материалы
            foreach (var material in viewModel.Materials) {
                if (material.UploadStatus == "Uploaded" && !string.IsNullOrEmpty(material.RemoteUrl)) {
                    var remotePath = ExtractRelativePathFromUrl(material.RemoteUrl);
                    if (remotePath != null && serverPaths.Contains(remotePath)) {
                        verified++;
                    } else {
                        material.UploadStatus = null;
                        material.UploadedHash = null;
                        material.RemoteUrl = null;
                        material.LastUploadedAt = null;
                        notFound++;
                    }
                }
            }

            // Проверяем модели
            foreach (var model in viewModel.Models) {
                if (model.UploadStatus == "Uploaded" && !string.IsNullOrEmpty(model.RemoteUrl)) {
                    var remotePath = ExtractRelativePathFromUrl(model.RemoteUrl);
                    if (remotePath != null && serverPaths.Contains(remotePath)) {
                        verified++;
                    } else {
                        model.UploadStatus = null;
                        model.UploadedHash = null;
                        model.RemoteUrl = null;
                        model.LastUploadedAt = null;
                        notFound++;
                    }
                }
            }

            if (notFound > 0) {
                logger.Info($"Server status verification: {verified} verified, {notFound} not found (status reset)");
            } else if (verified > 0) {
                logger.Info($"Server status verification: {verified} files verified");
            }
        }

        /// <summary>
        /// Сбрасывает все upload статусы (когда сервер пустой)
        /// </summary>
        private void ResetAllUploadStatuses() {
            int reset = 0;

            foreach (var texture in viewModel.Textures) {
                if (!string.IsNullOrEmpty(texture.UploadStatus)) {
                    texture.UploadStatus = null;
                    texture.UploadedHash = null;
                    texture.RemoteUrl = null;
                    texture.LastUploadedAt = null;
                    reset++;
                }
            }

            foreach (var material in viewModel.Materials) {
                if (!string.IsNullOrEmpty(material.UploadStatus)) {
                    material.UploadStatus = null;
                    material.UploadedHash = null;
                    material.RemoteUrl = null;
                    material.LastUploadedAt = null;
                    reset++;
                }
            }

            foreach (var model in viewModel.Models) {
                if (!string.IsNullOrEmpty(model.UploadStatus)) {
                    model.UploadStatus = null;
                    model.UploadedHash = null;
                    model.RemoteUrl = null;
                    model.LastUploadedAt = null;
                    reset++;
                }
            }

            if (reset > 0) {
                logger.Info($"Server empty - reset {reset} upload statuses");
            }
        }

        private async void DeleteServerFileButton_Click(object sender, RoutedEventArgs e) {
            if (_selectedServerAsset == null) return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete '{_selectedServerAsset.FileName}' from the server?\n\nThis action cannot be undone.",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try {
                using var b2Service = new Upload.B2UploadService();

                if (!Settings.AppSettings.Default.TryGetDecryptedB2ApplicationKey(out var appKey) || string.IsNullOrEmpty(appKey)) {
                    logService.LogError("Failed to decrypt B2 application key.");
                    return;
                }

                var settings = new Upload.B2UploadSettings {
                    KeyId = Settings.AppSettings.Default.B2KeyId,
                    ApplicationKey = appKey,
                    BucketName = Settings.AppSettings.Default.B2BucketName,
                    BucketId = Settings.AppSettings.Default.B2BucketId
                };

                await b2Service.AuthorizeAsync(settings);
                var success = await b2Service.DeleteFileAsync(_selectedServerAsset.RemotePath);

                if (success) {
                    logService.LogInfo($"Deleted: {_selectedServerAsset.RemotePath}");
                    UpdateServerFileInfo(null);
                    // Refresh the server assets panel
                    await ServerAssetsPanel.RefreshServerAssetsAsync();
                } else {
                    logService.LogError($"Failed to delete: {_selectedServerAsset.RemotePath}");
                }
            } catch (Exception ex) {
                logService.LogError($"Error deleting file: {ex.Message}");
            }
        }

        private void SetRightPanelVisibility(bool visible) {
            if (!visible) {
                HideAllViewers();
            }
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            // Guard against early calls during initialization
            if (!this.IsLoaded || UnifiedExportGroupBox == null) return;

            if (tabControl.SelectedItem is TabItem selectedTab) {
                switch (selectedTab.Header.ToString()) {
                    case "Textures":
                        SetRightPanelVisibility(true);
                        ShowTextureViewer();
                        UpdateExportCounts();
                        TextureToolsPanel.Visibility = Visibility.Visible;
                        break;
                    case "Models":
                        SetRightPanelVisibility(true);
                        ShowModelViewer();
                        UpdateExportCounts();
                        TextureToolsPanel.Visibility = Visibility.Collapsed;
                        break;
                    case "Materials":
                        SetRightPanelVisibility(true);
                        ShowMaterialViewer();
                        UpdateExportCounts();
                        TextureToolsPanel.Visibility = Visibility.Collapsed;
                        break;
                    case "Master Materials":
                        SetRightPanelVisibility(true);
                        ShowChunkSlotsViewer();
                        TextureToolsPanel.Visibility = Visibility.Collapsed;
                        break;
                    case "Server":
                        ShowServerFileInfo();
                        TextureToolsPanel.Visibility = Visibility.Collapsed;
                        break;
                    case "Logs":
                        SetRightPanelVisibility(false);
                        TextureToolsPanel.Visibility = Visibility.Collapsed;
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

        #region TextureSelectionViewModel Event Handlers

        /// <summary>
        /// Handles panel visibility changes requested by TextureSelectionViewModel
        /// </summary>
        private void OnPanelVisibilityRequested(object? sender, PanelVisibilityRequestEventArgs e) {
            if (ConversionSettingsExpander != null) {
                ConversionSettingsExpander.Visibility = e.ShowConversionSettingsPanel ? Visibility.Visible : Visibility.Collapsed;
            }
            if (ORMPanel != null) {
                ORMPanel.Visibility = e.ShowORMPanel ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Handles ORM texture selection - initializes ORM panel with available textures
        /// </summary>
        private void OnORMTextureSelected(object? sender, ORMTextureSelectedEventArgs e) {
            logService.LogInfo($"[OnORMTextureSelected] Initializing ORM panel for: {e.ORMTexture.Name}");

            if (ORMPanel != null) {
                // Initialize ORM panel with available textures (exclude other ORM textures)
                var availableTextures = viewModel.Textures.Where(t => !(t is ORMTextureResource)).ToList();
                logService.LogInfo($"[OnORMTextureSelected] availableTextures count: {availableTextures.Count}");
                ORMPanel.Initialize(this, availableTextures);
                ORMPanel.SetORMTexture(e.ORMTexture);
                logService.LogInfo($"[OnORMTextureSelected] ORMPanel initialized and texture set");
            } else {
                logService.LogInfo($"[OnORMTextureSelected] ERROR: ORMPanel is NULL!");
            }
        }

        /// <summary>
        /// Handles debounced texture selection - performs actual preview loading
        /// </summary>
        private async void OnTextureSelectionReady(object? sender, TextureSelectionReadyEventArgs e) {
            var ct = e.CancellationToken;

            try {
                if (e.IsORM) {
                    await LoadORMTexturePreviewAsync((ORMTextureResource)e.Texture, e.IsPacked, ct);
                } else {
                    await LoadTexturePreviewAsync(e.Texture, ct);
                }

                viewModel.TextureSelection.OnPreviewLoadCompleted(true);
            } catch (OperationCanceledException) {
                logService.LogInfo($"[OnTextureSelectionReady] Cancelled for: {e.Texture.Name}");
            } catch (Exception ex) {
                logService.LogError($"[OnTextureSelectionReady] Error loading texture {e.Texture.Name}: {ex.Message}");
                viewModel.TextureSelection.OnPreviewLoadCompleted(false, ex.Message);
            }
        }

        /// <summary>
        /// Loads preview for ORM texture (packed or unpacked)
        /// </summary>
        private async Task LoadORMTexturePreviewAsync(ORMTextureResource ormTexture, bool isPacked, CancellationToken ct) {
            logService.LogInfo($"[LoadORMTexturePreview] Loading preview for ORM: {ormTexture.Name}, isPacked: {isPacked}");

            // Reset preview state
            ResetPreviewState();
            ClearD3D11Viewer();

            // Update texture info
            TextureNameTextBlock.Text = "Texture Name: " + ormTexture.Name;
            TextureColorSpaceTextBlock.Text = "Color Space: Linear (ORM)";

            if (!isPacked || string.IsNullOrEmpty(ormTexture.Path)) {
                // Not packed yet - show info
                TextureResolutionTextBlock.Text = "Resolution: Not packed yet";
                TextureSizeTextBlock.Text = "Size: N/A";
                TextureFormatTextBlock.Text = "Format: Not packed";
                return;
            }

            // Load the packed KTX2 file for preview and histogram
            bool ktxLoaded = false;

            if (texturePreviewService.IsUsingD3D11Renderer) {
                // D3D11 MODE: Try native KTX2 loading
                logService.LogInfo($"[LoadORMTexturePreview] Loading packed ORM to D3D11: {ormTexture.Name}");
                ktxLoaded = await TryLoadKtx2ToD3D11Async(ormTexture, ct);

                if (!ktxLoaded) {
                    // Fallback: Try extracting PNG from KTX2
                    logService.LogInfo($"[LoadORMTexturePreview] D3D11 native loading failed, trying PNG extraction");
                    ktxLoaded = await TryLoadKtx2PreviewAsync(ormTexture, ct);
                }
            } else {
                // WPF MODE: Extract PNG from KTX2
                ktxLoaded = await TryLoadKtx2PreviewAsync(ormTexture, ct);
            }

            // Extract histogram for packed ORM textures
            if (ktxLoaded && !ct.IsCancellationRequested) {
                string? ormPath = ormTexture.Path;
                string ormName = ormTexture.Name ?? "unknown";
                logger.Info($"[ORM Histogram] Starting extraction for: {ormName}, path: {ormPath}");

                _ = Task.Run(async () => {
                    try {
                        if (string.IsNullOrEmpty(ormPath)) {
                            logger.Warn($"[ORM Histogram] Path is empty for: {ormName}");
                            return;
                        }

                        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                        var mipmaps = await texturePreviewService.LoadKtx2MipmapsAsync(ormPath, linkedCts.Token).ConfigureAwait(false);
                        logger.Info($"[ORM Histogram] Extracted {mipmaps.Count} mipmaps for: {ormName}");

                        if (mipmaps.Count > 0 && !linkedCts.Token.IsCancellationRequested) {
                            var mip0Bitmap = mipmaps[0].Bitmap;
                            logger.Info($"[ORM Histogram] Got mip0 bitmap {mip0Bitmap.PixelWidth}x{mip0Bitmap.PixelHeight}");
                            _ = Dispatcher.BeginInvoke(new Action(() => {
                                if (!ct.IsCancellationRequested) {
                                    texturePreviewService.OriginalFileBitmapSource = mip0Bitmap;
                                    UpdateHistogram(mip0Bitmap);
                                    logger.Info($"[ORM Histogram] Histogram updated for: {ormName}");
                                }
                            }));
                        }
                    } catch (OperationCanceledException) {
                        logger.Info($"[ORM Histogram] Extraction cancelled/timeout for: {ormName}");
                    } catch (Exception ex) {
                        logger.Warn(ex, $"[ORM Histogram] Failed to extract for: {ormName}");
                    }
                });
            }

            if (!ktxLoaded) {
                _ = Dispatcher.BeginInvoke(new Action(() => {
                    if (ct.IsCancellationRequested) return;
                    texturePreviewService.IsKtxPreviewAvailable = false;
                    TextureFormatTextBlock.Text = "Format: KTX2 (preview unavailable)";
                    logService.LogWarn($"Failed to load preview for packed ORM texture: {ormTexture.Name}");
                }));
            }
        }

        /// <summary>
        /// Loads preview for regular texture (PNG/JPG source)
        /// </summary>
        private async Task LoadTexturePreviewAsync(TextureResource texture, CancellationToken ct) {
            logService.LogInfo($"[LoadTexturePreview] Loading preview for: {texture.Name}, Path: {texture.Path ?? "NULL"}");

            ResetPreviewState();
            ClearD3D11Viewer();

            if (string.IsNullOrEmpty(texture.Path)) {
                return;
            }

            // Update texture info
            TextureNameTextBlock.Text = "Texture Name: " + texture.Name;
            TextureResolutionTextBlock.Text = "Resolution: " + string.Join("x", texture.Resolution);
            AssetProcessor.Helpers.SizeConverter sizeConverter = new();
            object size = AssetProcessor.Helpers.SizeConverter.Convert(texture.Size) ?? "Unknown size";
            TextureSizeTextBlock.Text = "Size: " + size;

            // Add color space info
            bool isSRGB = IsSRGBTexture(texture);
            string colorSpace = isSRGB ? "sRGB" : "Linear";
            string textureType = texture.TextureType ?? "Unknown";
            TextureColorSpaceTextBlock.Text = $"Color Space: {colorSpace} ({textureType})";
            TextureFormatTextBlock.Text = "Format: Loading...";

            // Load conversion settings for this texture
            logService.LogInfo($"[LoadTexturePreview] Loading conversion settings for: {texture.Name}");
            LoadTextureConversionSettings(texture);

            ct.ThrowIfCancellationRequested();

            bool ktxLoaded = false;

            if (texturePreviewService.IsUsingD3D11Renderer) {
                // D3D11 MODE: Try D3D11 native KTX2 loading
                logService.LogInfo($"[LoadTexturePreview] Attempting KTX2 load for: {texture.Name}");
                ktxLoaded = await TryLoadKtx2ToD3D11Async(texture, ct);
                logService.LogInfo($"[LoadTexturePreview] KTX2 load result: {ktxLoaded}");

                if (ktxLoaded) {
                    // KTX2 loaded successfully, still load source for histogram
                    bool showInViewer = (texturePreviewService.CurrentPreviewSourceMode == TexturePreviewSourceMode.Source);
                    logService.LogInfo($"[LoadTexturePreview] Loading source for histogram, showInViewer: {showInViewer}");
                    await LoadSourcePreviewAsync(texture, ct, loadToViewer: showInViewer);
                } else {
                    // No KTX2 or failed, fallback to source preview
                    logService.LogInfo($"[LoadTexturePreview] No KTX2, loading source preview");
                    await LoadSourcePreviewAsync(texture, ct, loadToViewer: true);
                }
            } else {
                // WPF MODE: Use PNG extraction for mipmaps
                Task<bool> ktxPreviewTask = TryLoadKtx2PreviewAsync(texture, ct);
                await LoadSourcePreviewAsync(texture, ct, loadToViewer: true);
                ktxLoaded = await ktxPreviewTask;
            }

            if (!ktxLoaded) {
                _ = Dispatcher.BeginInvoke(new Action(() => {
                    if (ct.IsCancellationRequested) return;

                    texturePreviewService.IsKtxPreviewAvailable = false;

                    if (!texturePreviewService.IsUserPreviewSelection && texturePreviewService.CurrentPreviewSourceMode == TexturePreviewSourceMode.Ktx2) {
                        SetPreviewSourceMode(TexturePreviewSourceMode.Source, initiatedByUser: false);
                    } else {
                        UpdatePreviewSourceControls();
                    }
                }));
            }
        }

        #endregion

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

                    // Histogram is updated when full-res image loads (skip for cached to reduce CPU)
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

                // Histogram is updated when full-res image loads (skip for thumbnail to reduce CPU)
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
        private readonly Dictionary<DataGrid, double> _lastGridWidth = new();
        private bool _isAdjustingColumns = false;
        private DispatcherTimer? _resizeDebounceTimer;

        private void TexturesDataGrid_SizeChanged(object sender, SizeChangedEventArgs e) {
            if (sender is not DataGrid grid) return;
            InitializeGridColumnsIfNeeded(grid);
            SubscribeDataGridColumnResizing(grid);
            DebouncedResizeUpdate(grid);
        }

        private void DebouncedResizeUpdate(DataGrid grid) {
            _resizeDebounceTimer?.Stop();
            _resizeDebounceTimer = new DispatcherTimer {
                Interval = TimeSpan.FromMilliseconds(16) // ~1 frame
            };
            _resizeDebounceTimer.Tick += (s, e) => {
                _resizeDebounceTimer?.Stop();
                UpdateColumnHeadersBasedOnWidth(grid);
                FillRemainingSpace(grid);
            };
            _resizeDebounceTimer.Start();
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

            // Skip if already subscribed - prevents expensive FindVisualChildren on every resize
            if (_leftGripperSubscribedGrids.Contains(grid)) return;

            // Subscribe to column headers for left edge mouse handling
            var columnHeaders = FindVisualChildren<DataGridColumnHeader>(grid).ToList();

            // Skip if no headers found yet (grid not visible)
            if (columnHeaders.Count == 0) return;

            foreach (var header in columnHeaders) {
                if (header.Column == null) continue;
                header.PreviewMouseLeftButtonDown += OnHeaderMouseDown;
                header.PreviewMouseMove += OnHeaderMouseMoveForCursor;
            }

            // Subscribe to move/up events on DataGrid level for dragging
            grid.PreviewMouseMove += OnDataGridMouseMove;
            grid.PreviewMouseLeftButtonUp += OnDataGridMouseUp;
            grid.LostMouseCapture += OnDataGridLostCapture;
            _leftGripperSubscribedGrids.Add(grid);
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

            // Skip if grid width hasn't changed significantly (avoids LINQ on every resize event)
            double currentWidth = grid.ActualWidth;
            if (_lastGridWidth.TryGetValue(grid, out double lastWidth) && Math.Abs(currentWidth - lastWidth) < 1) {
                return;
            }
            _lastGridWidth[grid] = currentWidth;

            _isAdjustingColumns = true;
            try {
                double availableWidth = currentWidth - SystemParameters.VerticalScrollBarWidth - 2;
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
            AppSettings.Default.Save();
            if (TexturesDataGrid?.ItemsSource == null || viewModel?.Textures == null) return;
            ApplyTextureGroupingIfEnabled();
        }

        private void CollapseGroupsCheckBox_Changed(object sender, RoutedEventArgs e) {
            // Save the preference when changed
            AppSettings.Default.Save();
        }

        /// <summary>
        /// Applies texture grouping if the GroupTextures checkbox is checked.
        /// Called after loading assets to apply default grouping.
        /// </summary>
        private void ApplyTextureGroupingIfEnabled() {
            logger.Info("[ApplyTextureGroupingIfEnabled] Starting...");

            var view = CollectionViewSource.GetDefaultView(TexturesDataGrid.ItemsSource);
            if (view == null || !view.CanGroup) {
                logger.Info("[ApplyTextureGroupingIfEnabled] View is null or cannot group");
                return;
            }

            if (GroupTexturesCheckBox.IsChecked == true) {
                logger.Info("[ApplyTextureGroupingIfEnabled] Applying grouping...");
                using (view.DeferRefresh()) {
                    view.GroupDescriptions.Clear();
                    view.GroupDescriptions.Add(new PropertyGroupDescription("GroupName"));
                    view.GroupDescriptions.Add(new PropertyGroupDescription("SubGroupName"));
                }
                logger.Info("[ApplyTextureGroupingIfEnabled] Grouping applied");
            } else {
                if (view.GroupDescriptions.Count > 0) {
                    view.GroupDescriptions.Clear();
                }
            }
            logger.Info("[ApplyTextureGroupingIfEnabled] Complete");
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
                        string ormName = ormTexture.Name ?? "unknown";
                        logger.Info($"[LoadORMPreviewAsync] Starting histogram extraction for: {ormName}, path: {ormPath}");

                        // Small delay to let LoadTexture complete first (prevents concurrent execution issues)
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
                // Column indices: 0=Export, 1=№, 2=ID, 3=TextureName, 4=Extension, 5=Size, 6=Compressed,
                // 7=Resolution, 8=ResizeResolution, 9=Compression(Format), 10=Mipmaps, 11=Preset, 12=Status, 13=Upload
                int columnIndex = columnTag switch {
                    "Export" => 0,
                    "Index" => 1,
                    "ID" => 2,
                    "TextureName" => 3,
                    "Extension" => 4,
                    "Size" => 5,
                    "Compressed" => 6,
                    "Resolution" => 7,
                    "ResizeResolution" => 8,
                    "Compression" => 9,
                    "Mipmaps" => 10,
                    "Preset" => 11,
                    "Status" => 12,
                    "Upload" => 13,
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
                // Column indices: 0=Export, 1=№, 2=ID, 3=Name, 4=Master, 5=Status
                int columnIndex = columnTag switch {
                    "Export" => 0,
                    "Index" => 1,
                    "ID" => 2,
                    "Name" => 3,
                    "Master" => 4,
                    "Status" => 5,
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
                // Column indices: 0=Export, 1=№, 2=ID, 3=Name, 4=Size, 5=UVChannels, 6=Extension, 7=Status
                int columnIndex = columnTag switch {
                    "Export" => 0,
                    "Index" => 1,
                    "ID" => 2,
                    "Name" => 3,
                    "Size" => 4,
                    "UVChannels" => 5,
                    "Extension" => 6,
                    "Status" => 7,
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
                // 0=Export, 1=№, 2=ID, 3=TextureName, 4=Extension, 5=Size, 6=Compressed,
                // 7=Resolution, 8=ResizeResolution, 9=Compression, 10=Mipmaps, 11=Preset, 12=Status, 13=Upload
                return tag switch {
                    "Export" => 0, "Index" => 1, "ID" => 2, "TextureName" => 3, "Extension" => 4,
                    "Size" => 5, "Compressed" => 6, "Resolution" => 7, "ResizeResolution" => 8,
                    "Compression" => 9, "Mipmaps" => 10, "Preset" => 11, "Status" => 12, "Upload" => 13,
                    _ => -1
                };
            } else if (grid == ModelsDataGrid) {
                // 0=Export, 1=№, 2=ID, 3=Name, 4=Size, 5=UVChannels, 6=Extension, 7=Status
                return tag switch {
                    "Export" => 0, "Index" => 1, "ID" => 2, "Name" => 3, "Size" => 4,
                    "UVChannels" => 5, "Extension" => 6, "Status" => 7,
                    _ => -1
                };
            } else if (grid == MaterialsDataGrid) {
                // 0=Export, 1=№, 2=ID, 3=Name, 4=Master, 5=Status
                return tag switch {
                    "Export" => 0, "Index" => 1, "ID" => 2, "Name" => 3, "Master" => 4, "Status" => 5,
                    _ => -1
                };
            }
            return -1;
        }

        // Generic handlers for Models and Materials DataGrids
        private void ModelsDataGrid_SizeChanged(object sender, SizeChangedEventArgs e) {
            if (sender is not DataGrid grid) return;
            InitializeGridColumnsIfNeeded(grid);
            SubscribeDataGridColumnResizing(grid);
            DebouncedResizeUpdateForGrid(grid);
        }

        private void DebouncedResizeUpdateForGrid(DataGrid grid) {
            _resizeDebounceTimer?.Stop();
            _resizeDebounceTimer = new DispatcherTimer {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _resizeDebounceTimer.Tick += (s, e) => {
                _resizeDebounceTimer?.Stop();
                FillRemainingSpaceForGrid(grid);
            };
            _resizeDebounceTimer.Start();
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
            SubscribeDataGridColumnResizing(grid);
            DebouncedResizeUpdateForGrid(grid);
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

            // Skip if grid width hasn't changed significantly (avoids LINQ on every resize event)
            double currentWidth = grid.ActualWidth;
            if (_lastGridWidth.TryGetValue(grid, out double lastWidth) && Math.Abs(currentWidth - lastWidth) < 1) {
                return;
            }
            _lastGridWidth[grid] = currentWidth;

            _isAdjustingColumns = true;
            try {
                double availableWidth = currentWidth - SystemParameters.VerticalScrollBarWidth - 2;
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
                // Header
                MaterialIDTextBlock.Text = $"ID: {parameters.ID}";
                MaterialNameTextBlock.Text = parameters.Name ?? "Unnamed";

                // NOTE: Do NOT update MaterialMasterComboBox here!
                // The MasterMaterialName is stored in config.json, not in material JSON files.
                // MaterialsDataGrid_SelectionChanged already handles ComboBox update using
                // the material from DataGrid which has the correct MasterMaterialName.

                // Texture hyperlinks and previews
                UpdateTextureHyperlink(MaterialDiffuseMapHyperlink, parameters.DiffuseMapId, parameters);
                UpdateTextureHyperlink(MaterialNormalMapHyperlink, parameters.NormalMapId, parameters);
                UpdateTextureHyperlink(MaterialAOMapHyperlink, parameters.AOMapId, parameters);
                UpdateTextureHyperlink(MaterialGlossMapHyperlink, parameters.GlossMapId, parameters);
                UpdateTextureHyperlink(MaterialMetalnessMapHyperlink, parameters.MetalnessMapId, parameters);
                UpdateTextureHyperlink(MaterialEmissiveMapHyperlink, parameters.EmissiveMapId, parameters);
                UpdateTextureHyperlink(MaterialOpacityMapHyperlink, parameters.OpacityMapId, parameters);

                // Texture previews
                SetTextureImage(TextureDiffusePreviewImage, parameters.DiffuseMapId);
                SetTextureImage(TextureNormalPreviewImage, parameters.NormalMapId);
                SetTextureImage(TextureAOPreviewImage, parameters.AOMapId);
                SetTextureImage(TextureGlossPreviewImage, parameters.GlossMapId);
                SetTextureImage(TextureMetalnessPreviewImage, parameters.MetalnessMapId);
                SetTextureImage(TextureEmissivePreviewImage, parameters.EmissiveMapId);
                SetTextureImage(TextureOpacityPreviewImage, parameters.OpacityMapId);

                // Overrides sliders
                MaterialBumpinessTextBox.Text = (parameters.BumpMapFactor ?? 1.0f).ToString("F2");
                MaterialBumpinessIntensitySlider.Value = parameters.BumpMapFactor ?? 1.0;

                MaterialMetalnessTextBox.Text = (parameters.Metalness ?? 0.0f).ToString("F2");
                MaterialMetalnessIntensitySlider.Value = parameters.Metalness ?? 0.0;

                MaterialGlossinessTextBox.Text = (parameters.Glossiness ?? parameters.Shininess ?? 0.25f).ToString("F2");
                MaterialGlossinessIntensitySlider.Value = parameters.Glossiness ?? parameters.Shininess ?? 0.25;

                // Tint colors
                SetTintColor(MaterialDiffuseTintCheckBox, MaterialTintColorRect, TintColorPicker, parameters.DiffuseTint, parameters.Diffuse);
                SetTintColor(MaterialAOTintCheckBox, MaterialAOTintColorRect, AOTintColorPicker, parameters.AOTint, parameters.AOColor);
                SetTintColor(MaterialSpecularTintCheckBox, MaterialSpecularTintColorRect, TintSpecularColorPicker, parameters.SpecularTint, parameters.Specular);
            });
        }

        private void UpdateTextureHyperlink(Hyperlink hyperlink, int? mapId, MaterialResource material) {
            if (hyperlink == null) return;

            hyperlink.DataContext = material;

            if (mapId.HasValue) {
                TextureResource? texture = viewModel.Textures.FirstOrDefault(t => t.ID == mapId.Value);
                hyperlink.NavigateUri = new Uri($"texture://{mapId.Value}");
                hyperlink.Inlines.Clear();
                hyperlink.Inlines.Add(texture?.Name ?? $"ID:{mapId.Value}");
            } else {
                hyperlink.NavigateUri = null;
                hyperlink.Inlines.Clear();
                hyperlink.Inlines.Add("-");
            }
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

        private void MaterialEmissiveMapHyperlink_Click(object sender, RoutedEventArgs e) {
            NavigateToTextureFromHyperlink(sender, "Emissive Map", material => material.EmissiveMapId);
        }

        private void MaterialOpacityMapHyperlink_Click(object sender, RoutedEventArgs e) {
            NavigateToTextureFromHyperlink(sender, "Opacity Map", material => material.OpacityMapId);
        }

        private void NavigateToDiffuseTexture_Click(object sender, RoutedEventArgs e) {
            NavigateToTextureById(GetSelectedMaterial()?.DiffuseMapId);
        }

        private void NavigateToNormalTexture_Click(object sender, RoutedEventArgs e) {
            NavigateToTextureById(GetSelectedMaterial()?.NormalMapId);
        }

        private void NavigateToAOTexture_Click(object sender, RoutedEventArgs e) {
            NavigateToTextureById(GetSelectedMaterial()?.AOMapId);
        }

        private void NavigateToGlossTexture_Click(object sender, RoutedEventArgs e) {
            NavigateToTextureById(GetSelectedMaterial()?.GlossMapId);
        }

        private void NavigateToMetalnessTexture_Click(object sender, RoutedEventArgs e) {
            NavigateToTextureById(GetSelectedMaterial()?.MetalnessMapId);
        }

        private void NavigateToEmissiveTexture_Click(object sender, RoutedEventArgs e) {
            NavigateToTextureById(GetSelectedMaterial()?.EmissiveMapId);
        }

        private void NavigateToOpacityTexture_Click(object sender, RoutedEventArgs e) {
            NavigateToTextureById(GetSelectedMaterial()?.OpacityMapId);
        }

        private MaterialResource? GetSelectedMaterial() {
            return MaterialsDataGrid.SelectedItem as MaterialResource;
        }

        private void NavigateToTextureById(int? textureId) {
            if (!textureId.HasValue) return;

            _ = Dispatcher.BeginInvoke(new Action(() => {
                if (TexturesTabItem != null) {
                    tabControl.SelectedItem = TexturesTabItem;
                }

                TextureResource? texture = viewModel.Textures.FirstOrDefault(t => t.ID == textureId.Value);
                if (texture != null) {
                    ICollectionView? view = CollectionViewSource.GetDefaultView(TexturesDataGrid.ItemsSource);
                    view?.MoveCurrentTo(texture);

                    TexturesDataGrid.SelectedItem = texture;
                    TexturesDataGrid.UpdateLayout();
                    TexturesDataGrid.ScrollIntoView(texture);
                    TexturesDataGrid.Focus();
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Centralized helper to update the MaterialMasterComboBox with proper ItemsSource and selection.
        /// Ensures ItemsSource is set before selection to prevent WPF binding issues.
        /// </summary>
        private void UpdateMasterMaterialComboBox(string? masterName) {
            _isUpdatingMasterComboBox = true;
            try {
                // Always populate ItemsSource first with master names as strings
                var masters = viewModel.MasterMaterialsViewModel.MasterMaterials;
                var masterNames = masters.Select(m => m.Name).ToList();

                logger.Info($"UpdateMasterMaterialComboBox: masterNames count={masterNames.Count}, selecting='{masterName}'");

                // Set ItemsSource (will clear selection)
                MaterialMasterComboBox.ItemsSource = masterNames;

                // Now set selection
                if (!string.IsNullOrEmpty(masterName) && masterNames.Contains(masterName)) {
                    MaterialMasterComboBox.SelectedItem = masterName;
                    logger.Info($"UpdateMasterMaterialComboBox: SelectedItem set to '{masterName}', SelectedIndex={MaterialMasterComboBox.SelectedIndex}");
                } else {
                    MaterialMasterComboBox.SelectedIndex = -1;
                    logger.Info($"UpdateMasterMaterialComboBox: Cleared selection (masterName='{masterName}' not found)");
                }

                // Force layout update to ensure visual refresh
                MaterialMasterComboBox.UpdateLayout();
            } finally {
                _isUpdatingMasterComboBox = false;
            }
        }

        private void MaterialMasterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            // Skip if we're programmatically updating the ComboBox
            if (_isUpdatingMasterComboBox) return;
            if (MaterialMasterComboBox.SelectedItem is not string masterName) return;

            // Apply to ALL selected materials (group assignment)
            var selectedMaterials = MaterialsDataGrid.SelectedItems.Cast<MaterialResource>().ToList();
            if (selectedMaterials.Count == 0) return;

            logger.Info($"MaterialMasterComboBox_SelectionChanged: Applying master '{masterName}' to {selectedMaterials.Count} materials");

            foreach (var material in selectedMaterials) {
                material.MasterMaterialName = masterName;
                viewModel.MasterMaterialsViewModel.SetMasterForMaterial(material.ID, masterName);
            }

            // Refresh DataGrid to show updated values
            MaterialsDataGrid.Items.Refresh();
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
                "Emissive" => material.EmissiveMapId,
                "Opacity" => material.OpacityMapId,
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
            logger.Info($"MaterialsDataGrid_SelectionChanged CALLED, SelectedItem={MaterialsDataGrid.SelectedItem?.GetType().Name ?? "null"}");

            if (MaterialsDataGrid.SelectedItem is MaterialResource selectedMaterial) {
                logger.Info($"MaterialsDataGrid_SelectionChanged: processing material={selectedMaterial.Name}, MasterMaterialName='{selectedMaterial.MasterMaterialName}'");

                // Update MainViewModel's selected material for filtering
                viewModel.SelectedMaterial = selectedMaterial;

                // Update right panel Master ComboBox using centralized helper
                UpdateMasterMaterialComboBox(selectedMaterial.MasterMaterialName);

                // Delegate to MaterialSelectionViewModel for parameter loading
                await viewModel.MaterialSelection.SelectMaterialCommand.ExecuteAsync(selectedMaterial);
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
                material.AOMapId,
                material.GlossMapId,
                material.MetalnessMapId,
                material.EmissiveMapId,
                material.OpacityMapId
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
            viewModel.RecalculateIndices();
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
            var texturesToScan = viewModel.Textures.ToList();

            if (texturesToScan.Count == 0) {
                return;
            }

            // Run scanning in background, apply results on UI thread
            Task.Run(() => {
                var results = ktx2InfoService.ScanTextures(texturesToScan);

                foreach (var result in results) {
                    Dispatcher.InvokeAsync(() => {
                        result.Texture.CompressedSize = result.Info.FileSize;
                        if (result.Info.MipmapCount > 0) {
                            result.Texture.MipmapCount = result.Info.MipmapCount;
                        }
                        if (result.Info.CompressionFormat != null) {
                            result.Texture.CompressionFormat = result.Info.CompressionFormat;
                        }
                    });
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

                // Initialize Master Materials context (sync happens in OnAssetsLoaded when materials are populated)
                if (!string.IsNullOrEmpty(ProjectFolderPath))
                {
                    await viewModel.MasterMaterialsViewModel.SetProjectContextAsync(ProjectFolderPath);
                }

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

            if (string.IsNullOrEmpty(ProjectFolderPath)) {
                return;
            }

            projectFileWatcherService.Start(ProjectFolderPath);
        }

        /// <summary>
        /// Stops the file watcher. Call when project is unloaded or window closes.
        /// </summary>
        private void StopFileWatcher() {
            projectFileWatcherService.Stop();
        }

        /// <summary>
        /// Handles file deletion events from ProjectFileWatcherService.
        /// </summary>
        private void OnFilesDeletionDetected(object? sender, Services.Models.FilesDeletionDetectedEventArgs e) {
            // Must dispatch to UI thread since event comes from background
            Dispatcher.InvokeAsync(() => {
                if (e.RequiresFullRescan) {
                    logger.Info("Performing full rescan due to directory deletion");
                    RescanFileStatuses();

                    if (HasMissingFiles()) {
                        UpdateConnectionButton(ConnectionState.NeedsDownload);
                    }
                } else if (e.DeletedPaths.Count > 0) {
                    int updatedCount = fileStatusScannerService.ProcessDeletedPaths(
                        e.DeletedPaths.ToList(), viewModel.Textures, viewModel.Models, viewModel.Materials);

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
            });
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
                // Stop file watcher and unsubscribe
                projectFileWatcherService.FilesDeletionDetected -= OnFilesDeletionDetected;
                StopFileWatcher();

                // ������������� billboard ����������
                StopBillboardUpdate();

                cancellationTokenSource?.Cancel();
                textureLoadCancellation?.Cancel();

                // Thread.Sleep removed - CancellationToken.Cancel() is instant

                cancellationTokenSource?.Dispose();
                textureLoadCancellation?.Dispose();

                playCanvasService?.Dispose();
            } catch (Exception ex) {
                logger.Error(ex, "Error canceling operations during window closing");
            }

            // Cleanup DispatcherTimers to prevent memory leaks
            foreach (var kvp in _saveColumnWidthsTimers)
            {
                kvp.Value.Stop();
                if (_saveColumnWidthsHandlers.TryGetValue(kvp.Key, out var handler))
                    kvp.Value.Tick -= handler;
            }
            _saveColumnWidthsTimers.Clear();
            _saveColumnWidthsHandlers.Clear();
            _columnWidthsLoadedGrids.Clear();
            _columnWidthsLoadedTime.Clear();
            _hasSavedWidthsFromSettings.Clear();
            _sortDirections.Clear();
            _previousColumnWidths.Clear();

            // Cleanup GLB viewer resources
            CleanupGlbViewer();

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
        /// Smart asset loading: loads local assets and checks for server updates.
        /// If hashes differ - shows download button.
        /// </summary>
        private async Task SmartLoadAssets() {
            try {
                logger.Info("=== SmartLoadAssets: Starting ===");

                if (ProjectsComboBox.SelectedItem == null || BranchesComboBox.SelectedItem == null) {
                    logger.Warn("SmartLoadAssets: No project or branch selected");
                    UpdateConnectionButton(ConnectionState.Disconnected);
                    return;
                }

                string selectedProjectId = ((KeyValuePair<string, string>)ProjectsComboBox.SelectedItem).Key;
                string selectedBranchId = ((Branch)BranchesComboBox.SelectedItem).Id;

                if (string.IsNullOrEmpty(ProjectFolderPath)) {
                    logService.LogInfo("Project folder path is empty");
                    UpdateConnectionButton(ConnectionState.NeedsDownload);
                    return;
                }

                // Initialize Master Materials context BEFORE loading assets
                // so config is ready when OnAssetsLoaded calls SyncMaterialMasterMappings
                if (!string.IsNullOrEmpty(ProjectFolderPath)) {
                    logger.Info("SmartLoadAssets: Initializing Master Materials context");
                    await viewModel.MasterMaterialsViewModel.SetProjectContextAsync(ProjectFolderPath);
                    logger.Info("SmartLoadAssets: Master Materials context initialized");
                }

                // Try to load local assets
                bool assetsLoaded = await LoadAssetsFromJsonFileAsync();

                if (assetsLoaded) {
                    // Local assets loaded, now check for server updates
                    string? apiKey = GetDecryptedApiKey();
                    if (string.IsNullOrEmpty(apiKey)) {
                        logService.LogError("API key missing");
                        UpdateConnectionButton(ConnectionState.NeedsDownload);
                        return;
                    }

                    // Use service to check for updates
                    var checkResult = await projectConnectionService.CheckForUpdatesAsync(
                        ProjectFolderPath,
                        selectedProjectId,
                        selectedBranchId,
                        apiKey,
                        CancellationToken.None);

                    if (!checkResult.Success) {
                        logger.Warn($"SmartLoadAssets: Failed to check for updates: {checkResult.Error}");
                        UpdateConnectionButton(ConnectionState.NeedsDownload);
                        return;
                    }

                    if (checkResult.HasUpdates) {
                        logger.Info("SmartLoadAssets: Updates available on server");
                        UpdateConnectionButton(ConnectionState.NeedsDownload);
                    } else {
                        logger.Info("SmartLoadAssets: Project is up to date");
                        UpdateConnectionButton(ConnectionState.UpToDate);
                    }
                } else {
                    // No local assets - need to download
                    logger.Info("SmartLoadAssets: No local assets found - need to download");
                    UpdateConnectionButton(ConnectionState.NeedsDownload);
                }

            } catch (Exception ex) {
                logger.Error(ex, "Error in SmartLoadAssets");
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
                // Get project ID
                int projectId = 0;
                if (!string.IsNullOrEmpty(viewModel.SelectedProjectId)) {
                    int.TryParse(viewModel.SelectedProjectId, out projectId);
                }
                if (projectId <= 0) return;

                // Build TextureSettings from UI panel
                var compression = ConversionSettingsPanel.GetCompressionSettings();
                var mipProfile = ConversionSettingsPanel.GetMipProfileSettings();
                var histogramSettings = ConversionSettingsPanel.GetHistogramSettings();

                // Don't update CompressionFormat here - it shows actual compression from KTX2 file,
                // not the intended settings from the UI panel
                texture.PresetName = ConversionSettingsPanel.PresetName ?? "(Custom)";

                var settings = new Services.TextureSettings {
                    PresetName = ConversionSettingsPanel.PresetName,

                    // Compression
                    CompressionFormat = compression.CompressionFormat.ToString(),
                    ColorSpace = compression.ColorSpace.ToString(),
                    CompressionLevel = compression.CompressionLevel,
                    QualityLevel = compression.QualityLevel,
                    UASTCQuality = compression.UASTCQuality,
                    UseUASTCRDO = compression.UseUASTCRDO,
                    UASTCRDOQuality = compression.UASTCRDOQuality,
                    UseETC1SRDO = compression.UseETC1SRDO,
                    ETC1SRDOLambda = 1.0f,
                    KTX2Supercompression = compression.KTX2Supercompression.ToString(),
                    KTX2ZstdLevel = compression.KTX2ZstdLevel,

                    // Mipmaps
                    GenerateMipmaps = compression.GenerateMipmaps,
                    UseCustomMipmaps = compression.UseCustomMipmaps,
                    FilterType = mipProfile?.Filter.ToString() ?? "Kaiser",
                    ApplyGammaCorrection = mipProfile?.ApplyGammaCorrection ?? true,
                    Gamma = mipProfile?.Gamma ?? 2.2f,
                    NormalizeNormals = mipProfile?.NormalizeNormals ?? false,

                    // Normal map
                    ConvertToNormalMap = compression.ConvertToNormalMap,
                    NormalizeVectors = compression.NormalizeVectors,

                    // Advanced
                    PerceptualMode = compression.PerceptualMode,
                    SeparateAlpha = compression.SeparateAlpha,
                    ForceAlphaChannel = compression.ForceAlphaChannel,
                    RemoveAlphaChannel = compression.RemoveAlphaChannel,
                    WrapMode = compression.WrapMode.ToString(),

                    // Histogram
                    HistogramEnabled = histogramSettings != null && histogramSettings.Mode != TextureConversion.Core.HistogramMode.Off,
                    HistogramMode = histogramSettings?.Mode.ToString() ?? "Off",
                    HistogramQuality = histogramSettings?.Quality.ToString() ?? "HighQuality",
                    HistogramChannelMode = histogramSettings?.ChannelMode.ToString() ?? "PerChannel",
                    HistogramPercentileLow = histogramSettings?.PercentileLow ?? 5.0f,
                    HistogramPercentileHigh = histogramSettings?.PercentileHigh ?? 95.0f,
                    HistogramKneeWidth = histogramSettings?.KneeWidth ?? 0.02f
                };

                // Delegate to ViewModel for saving
                viewModel.ConversionSettings.SaveSettingsCommand.Execute(new SettingsSaveRequest {
                    Texture = texture,
                    Settings = settings,
                    ProjectId = projectId
                });

                logService.LogInfo($"Updated conversion settings for {texture.Name}");
            } catch (Exception ex) {
                logService.LogError($"Error updating conversion settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Загружает сохранённые настройки текстуры в UI панель
        /// </summary>
        private void LoadSavedSettingsToUI(Services.TextureSettings saved) {
            try {
                // Устанавливаем флаг загрузки чтобы не триггерить SettingsChanged
                ConversionSettingsPanel.BeginLoadingSettings();

                // Preset
                if (!string.IsNullOrEmpty(saved.PresetName)) {
                    ConversionSettingsPanel.SetPresetSilently(saved.PresetName);
                } else {
                    ConversionSettingsPanel.SetPresetSilently("Custom");
                }

                // Compression format
                if (Enum.TryParse<TextureConversion.Core.CompressionFormat>(saved.CompressionFormat, true, out var format)) {
                    ConversionSettingsPanel.CompressionFormatComboBox.SelectedItem = format;
                }

                // Color space
                if (Enum.TryParse<TextureConversion.Core.ColorSpace>(saved.ColorSpace, true, out var colorSpace)) {
                    ConversionSettingsPanel.ColorSpaceComboBox.SelectedItem = colorSpace;
                }

                // ETC1S settings
                ConversionSettingsPanel.CompressionLevelSlider.Value = saved.CompressionLevel;
                ConversionSettingsPanel.ETC1SQualitySlider.Value = saved.QualityLevel;
                ConversionSettingsPanel.UseETC1SRDOCheckBox.IsChecked = saved.UseETC1SRDO;

                // UASTC settings
                ConversionSettingsPanel.UASTCQualitySlider.Value = saved.UASTCQuality;
                ConversionSettingsPanel.UseUASTCRDOCheckBox.IsChecked = saved.UseUASTCRDO;
                ConversionSettingsPanel.UASTCRDOLambdaSlider.Value = saved.UASTCRDOQuality;

                // Supercompression
                if (Enum.TryParse<TextureConversion.Core.KTX2SupercompressionType>(saved.KTX2Supercompression, true, out var supercomp)) {
                    ConversionSettingsPanel.KTX2SupercompressionComboBox.SelectedItem = supercomp;
                }
                ConversionSettingsPanel.ZstdLevelSlider.Value = saved.KTX2ZstdLevel;

                // Mipmaps
                ConversionSettingsPanel.GenerateMipmapsCheckBox.IsChecked = saved.GenerateMipmaps;
                ConversionSettingsPanel.CustomMipmapsCheckBox.IsChecked = saved.UseCustomMipmaps;

                if (Enum.TryParse<TextureConversion.Core.FilterType>(saved.FilterType, true, out var filter)) {
                    ConversionSettingsPanel.MipFilterComboBox.SelectedItem = filter;
                }

                ConversionSettingsPanel.ApplyGammaCorrectionCheckBox.IsChecked = saved.ApplyGammaCorrection;
                ConversionSettingsPanel.NormalizeNormalsCheckBox.IsChecked = saved.NormalizeNormals;

                // Normal map
                ConversionSettingsPanel.ConvertToNormalMapCheckBox.IsChecked = saved.ConvertToNormalMap;
                ConversionSettingsPanel.NormalizeVectorsCheckBox.IsChecked = saved.NormalizeVectors;

                // Advanced
                ConversionSettingsPanel.PerceptualModeCheckBox.IsChecked = saved.PerceptualMode;
                ConversionSettingsPanel.ForceAlphaCheckBox.IsChecked = saved.ForceAlphaChannel;
                ConversionSettingsPanel.RemoveAlphaCheckBox.IsChecked = saved.RemoveAlphaChannel;

                if (Enum.TryParse<TextureConversion.Core.WrapMode>(saved.WrapMode, true, out var wrapMode)) {
                    ConversionSettingsPanel.WrapModeComboBox.SelectedItem = wrapMode;
                }

                // Histogram
                ConversionSettingsPanel.EnableHistogramCheckBox.IsChecked = saved.HistogramEnabled;
                if (saved.HistogramEnabled) {
                    if (Enum.TryParse<TextureConversion.Core.HistogramQuality>(saved.HistogramQuality, true, out var hquality)) {
                        ConversionSettingsPanel.HistogramQualityComboBox.SelectedItem = hquality;
                    }
                    if (Enum.TryParse<TextureConversion.Core.HistogramChannelMode>(saved.HistogramChannelMode, true, out var hchannel)) {
                        ConversionSettingsPanel.HistogramChannelModeComboBox.SelectedItem = hchannel;
                    }
                    ConversionSettingsPanel.HistogramPercentileLowSlider.Value = saved.HistogramPercentileLow;
                    ConversionSettingsPanel.HistogramPercentileHighSlider.Value = saved.HistogramPercentileHigh;
                }

                logService.LogInfo($"Loaded saved settings to UI: Format={saved.CompressionFormat}, Quality={saved.QualityLevel}");
            } catch (Exception ex) {
                logService.LogError($"Error loading saved settings to UI: {ex.Message}");
            } finally {
                ConversionSettingsPanel.EndLoadingSettings();
            }
        }

        private void LoadTextureConversionSettings(TextureResource texture) {
            // Delegate to ViewModel - it will raise SettingsLoaded event
            // which is handled by OnConversionSettingsLoaded
            int projectId = 0;
            if (!string.IsNullOrEmpty(viewModel.SelectedProjectId)) {
                int.TryParse(viewModel.SelectedProjectId, out projectId);
            }

            viewModel.ConversionSettings.LoadSettingsForTextureCommand.Execute(new SettingsLoadRequest {
                Texture = texture,
                ProjectId = projectId
            });
        }

        // Initialize compression format and preset for texture without updating UI panel
        // ��������������: ���������� ������������ PresetManager � ����������� �������� ������
        private void InitializeTextureConversionSettings(TextureResource texture) {
            // Mark as initialized first to prevent re-entry on scroll
            texture.IsConversionSettingsInitialized = true;

            // Базовая инициализация для текстуры - без тяжелых операций чтения
            var textureType = TextureResource.DetermineTextureType(texture.Name ?? "");
            var profile = TextureConversion.Core.MipGenerationProfile.CreateDefault(
                MapTextureTypeToCore(textureType));
            var compression = TextureConversion.Core.CompressionSettings.CreateETC1SDefault();

            // CompressionFormat is only set when texture is actually compressed
            // (from KTX2 metadata or after compression process)

            // Auto-detect preset by filename if not already set
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

        private async void UploadTexturesButton_Click(object sender, RoutedEventArgs e) {
            var projectName = ProjectName ?? "UnknownProject";
            var outputPath = Settings.AppSettings.Default.ProjectsFolderPath;

            if (string.IsNullOrEmpty(outputPath)) {
                MessageBox.Show(
                    "Не указана папка проектов. Откройте настройки и укажите Projects Folder Path.",
                    "Upload Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Проверяем настройки B2
            if (string.IsNullOrEmpty(Settings.AppSettings.Default.B2KeyId) ||
                string.IsNullOrEmpty(Settings.AppSettings.Default.B2BucketName)) {
                MessageBox.Show(
                    "Backblaze B2 credentials not configured. Go to Settings -> CDN/Upload to configure.",
                    "Upload Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Получаем текстуры для загрузки:
            // 1. Сначала пробуем выбранные в DataGrid
            // 2. Если ничего не выбрано - берём отмеченные для экспорта (ExportToServer = true)
            IEnumerable<Resources.TextureResource> texturesToUpload = TexturesDataGrid.SelectedItems.Cast<Resources.TextureResource>();

            if (!texturesToUpload.Any()) {
                // Используем текстуры, отмеченные для экспорта
                texturesToUpload = viewModel.Textures.Where(t => t.ExportToServer);
            }

            // Вычисляем путь к KTX2 из исходного Path (заменяем расширение на .ktx2)
            var selectedTextures = texturesToUpload
                .Where(t => !string.IsNullOrEmpty(t.Path))
                .Select(t => {
                    var sourceDir = System.IO.Path.GetDirectoryName(t.Path)!;
                    var fileName = System.IO.Path.GetFileNameWithoutExtension(t.Path);
                    var ktx2Path = System.IO.Path.Combine(sourceDir, fileName + ".ktx2");
                    return (Texture: t, Ktx2Path: ktx2Path);
                })
                .Where(x => System.IO.File.Exists(x.Ktx2Path))
                .ToList();

            if (!selectedTextures.Any()) {
                MessageBox.Show(
                    "No converted textures found.\n\nEither select textures in the list, or mark them for export (Mark Related), then process them to KTX2.",
                    "Upload Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try {
                UploadTexturesButton.IsEnabled = false;
                UploadTexturesButton.Content = "Uploading...";

                using var b2Service = new Upload.B2UploadService();
                using var uploadStateService = new Data.UploadStateService();
                var uploadCoordinator = new Services.AssetUploadCoordinator(b2Service, uploadStateService);

                var initialized = await uploadCoordinator.InitializeAsync();
                if (!initialized) {
                    MessageBox.Show(
                        "Failed to connect to Backblaze B2. Check your credentials in Settings.",
                        "Upload Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                // Подготавливаем список файлов для загрузки
                var files = selectedTextures
                    .Select(x => (LocalPath: x.Ktx2Path, RemotePath: $"{projectName}/textures/{System.IO.Path.GetFileName(x.Ktx2Path)}"))
                    .ToList();

                var result = await b2Service.UploadBatchAsync(
                    files,
                    progress: new Progress<Upload.B2UploadProgress>(p => {
                        Dispatcher.Invoke(() => {
                            ProgressBar.Value = p.PercentComplete;
                        });
                    })
                );

                // Обновляем статусы текстур и сохраняем в БД
                logger.Debug($"Results count: {result.Results.Count}, selectedTextures count: {selectedTextures.Count}");
                foreach (var item in selectedTextures) {
                    logger.Debug($"Looking for path: '{item.Ktx2Path}'");
                    foreach (var r in result.Results) {
                        logger.Debug($"  Result path: '{r.LocalPath}' Success={r.Success} Skipped={r.Skipped}");
                    }
                    var uploadResult = result.Results.FirstOrDefault(r =>
                        string.Equals(r.LocalPath, item.Ktx2Path, StringComparison.OrdinalIgnoreCase));
                    logger.Debug($"uploadResult found: {uploadResult != null}, Success: {uploadResult?.Success}");
                    if (uploadResult?.Success == true) {
                        item.Texture.UploadStatus = "Uploaded";
                        item.Texture.RemoteUrl = uploadResult.CdnUrl;
                        item.Texture.UploadedHash = uploadResult.ContentSha1;
                        item.Texture.LastUploadedAt = DateTime.UtcNow;

                        // Сохраняем в БД для персистентности между сессиями
                        var remotePath = $"{projectName}/textures/{System.IO.Path.GetFileName(item.Ktx2Path)}";
                        await uploadStateService.SaveUploadAsync(new Data.UploadRecord {
                            LocalPath = item.Ktx2Path,
                            RemotePath = remotePath,
                            ContentSha1 = uploadResult.ContentSha1 ?? "",
                            ContentLength = new System.IO.FileInfo(item.Ktx2Path).Length,
                            UploadedAt = DateTime.UtcNow,
                            CdnUrl = uploadResult.CdnUrl ?? "",
                            Status = "Uploaded",
                            FileId = uploadResult.FileId,
                            ProjectName = projectName
                        });
                    }
                }

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
                logger.Error(ex, "Texture upload failed");
                MessageBox.Show($"Upload failed: {ex.Message}", "Upload Error", MessageBoxButton.OK, MessageBoxImage.Error);
            } finally {
                UploadTexturesButton.IsEnabled = true;
                UploadTexturesButton.Content = "Upload";
                ProgressBar.Value = 0;
            }
        }

        /// <summary>
        /// Updates the unified export panel counts for all asset types
        /// </summary>
        private void UpdateExportCounts() {
            int markedModels = viewModel.Models.Count(m => m.ExportToServer);
            int markedMaterials = viewModel.Materials.Count(m => m.ExportToServer);
            int markedTextures = viewModel.Textures.Count(t => t.ExportToServer);

            MarkedModelsCountText.Text = markedModels.ToString();
            MarkedMaterialsCountText.Text = markedMaterials.ToString();
            MarkedTexturesCountText.Text = markedTextures.ToString();
        }

        #region ORMTextureViewModel Event Handlers

        private void OnORMCreated(object? sender, ORMCreatedEventArgs e) {
            // Select and scroll to the newly created ORM texture
            tabControl.SelectedItem = TexturesTabItem;
            TexturesDataGrid.SelectedItem = e.ORMTexture;
            TexturesDataGrid.ScrollIntoView(e.ORMTexture);

            // Show success message with details
            if (e.Mode.HasValue) {
                MessageBox.Show(
                    $"Created ORM texture:\n\n" +
                    $"Name: {e.ORMTexture.Name}\n" +
                    $"Mode: {e.Mode}\n" +
                    $"AO: {e.AOSource?.Name ?? "None"}\n" +
                    $"Gloss: {e.GlossSource?.Name ?? "None"}\n" +
                    $"Metallic: {e.MetallicSource?.Name ?? "None"}",
                    "ORM Texture Created", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void OnORMDeleted(object? sender, ORMDeletedEventArgs e) {
            logService.LogInfo($"ORM texture deleted: {e.ORMTexture.Name}");
        }

        private void OnORMConfirmationRequested(object? sender, ORMConfirmationRequestEventArgs e) {
            var result = MessageBox.Show(e.Message, "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes) {
                e.OnConfirmed?.Invoke();
            }
        }

        private void OnORMErrorOccurred(object? sender, ORMErrorEventArgs e) {
            MessageBox.Show(e.Message, e.Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnORMBatchCreationCompleted(object? sender, ORMBatchCreationCompletedEventArgs e) {
            var message = $"Batch ORM Creation Results:\n\n" +
                         $"Created: {e.Created}\n" +
                         $"Skipped: {e.Skipped}\n" +
                         $"Errors: {e.Errors.Count}";

            if (e.Errors.Count > 0) {
                message += $"\n\nErrors:\n{string.Join("\n", e.Errors.Take(5))}";
                if (e.Errors.Count > 5) {
                    message += $"\n... and {e.Errors.Count - 5} more";
                }
            }

            MessageBox.Show(message, "Batch ORM Creation Complete",
                MessageBoxButton.OK, e.Errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }

        #endregion

        #region TextureConversionSettingsViewModel Event Handlers

        private void OnConversionSettingsLoaded(object? sender, SettingsLoadedEventArgs e) {
            logService.LogInfo($"[OnConversionSettingsLoaded] Settings loaded for {e.Texture.Name}, Source: {e.Source}");

            // Set current texture path for auto-detect normal map
            ConversionSettingsPanel.SetCurrentTexturePath(e.Texture.Path);
            ConversionSettingsPanel.ClearNormalMapPath();

            switch (e.Source) {
                case SettingsSource.Saved:
                    // Load saved settings to UI
                    if (e.Settings != null) {
                        LoadSavedSettingsToUI(e.Settings);
                    }
                    break;

                case SettingsSource.AutoDetectedPreset:
                    // Set preset silently (without triggering change events)
                    if (!string.IsNullOrEmpty(e.PresetName)) {
                        var dropdownItems = ConversionSettingsPanel.PresetComboBox.Items.Cast<string>().ToList();
                        bool presetExistsInDropdown = dropdownItems.Contains(e.PresetName);

                        if (presetExistsInDropdown) {
                            ConversionSettingsPanel.SetPresetSilently(e.PresetName);
                        } else {
                            ConversionSettingsPanel.SetPresetSilently("Custom");
                        }
                    }
                    break;

                case SettingsSource.Default:
                    // Set to Custom preset and apply default settings
                    ConversionSettingsPanel.SetPresetSilently("Custom");

                    // Apply default settings based on texture type
                    var textureType = TextureResource.DetermineTextureType(e.Texture.Name ?? "");
                    var profile = TextureConversion.Core.MipGenerationProfile.CreateDefault(
                        MapTextureTypeToCore(textureType));
                    var compression = TextureConversion.Core.CompressionSettings.CreateETC1SDefault();
                    var compressionData = TextureConversion.Settings.CompressionSettingsData.FromCompressionSettings(compression);
                    var mipProfileData = TextureConversion.Settings.MipProfileSettings.FromMipGenerationProfile(profile);

                    ConversionSettingsPanel.LoadSettings(compressionData, mipProfileData, true, false);
                    break;
            }
        }

        private void OnConversionSettingsSaved(object? sender, SettingsSavedEventArgs e) {
            logService.LogInfo($"[OnConversionSettingsSaved] Settings saved for {e.Texture.Name}");
        }

        private void OnConversionSettingsError(object? sender, SettingsErrorEventArgs e) {
            logService.LogError($"[OnConversionSettingsError] {e.Title}: {e.Message}");
            MessageBox.Show(e.Message, e.Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        #endregion

        #region AssetLoadingViewModel Event Handlers

        // Track window active state to skip render loops when inactive (Alt+Tab fix)
        private volatile bool _isWindowActive = true;

        private void OnAssetsLoaded(object? sender, AssetsLoadedEventArgs e) {
            // Check if window is active - if not, defer loading to prevent UI freeze
            if (!_isWindowActive) {
                logger.Info("[OnAssetsLoaded] Window inactive, deferring asset loading");
                _pendingAssetsData = e;
                return;
            }

            ApplyAssetsToUI(e);
        }

        private void ApplyAssetsToUI(AssetsLoadedEventArgs e) {
            logger.Info($"[ApplyAssetsToUI] Starting: {e.Textures.Count} textures, {e.Models.Count} models");

            Dispatcher.Invoke(() => {
                viewModel.Textures = new ObservableCollection<TextureResource>(e.Textures);
                viewModel.Models = new ObservableCollection<ModelResource>(e.Models);
                viewModel.Materials = new ObservableCollection<MaterialResource>(e.Materials);
                folderPaths = new Dictionary<int, string>(e.FolderPaths);
                RecalculateIndices();
                viewModel.SyncMaterialMasterMappings();

                logger.Info("[ApplyAssetsToUI] Data assigned, binding DataGrids");

                // Bind all DataGrids
                ModelsDataGrid.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Models"));
                MaterialsDataGrid.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Materials"));
                TexturesDataGrid.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Textures"));

                ModelsDataGrid.Visibility = Visibility.Visible;
                MaterialsDataGrid.Visibility = Visibility.Visible;
                TexturesDataGrid.Visibility = Visibility.Visible;

                // Apply grouping after binding
                ApplyTextureGroupingIfEnabled();

                viewModel.ProgressValue = viewModel.ProgressMaximum;
                viewModel.ProgressText = $"Ready ({e.Textures.Count} textures, {e.Models.Count} models, {e.Materials.Count} materials)";
                _ = ServerAssetsPanel.RefreshServerAssetsAsync();
                logger.Info("[ApplyAssetsToUI] Complete");
            });
        }

        private void OnAssetLoadingProgressChanged(object? sender, AssetLoadProgressEventArgs e) {
            // Progress<T> already marshals to UI thread, no need for Dispatcher
            viewModel.ProgressMaximum = e.Total;
            viewModel.ProgressValue = e.Processed;
            // Show asset name being processed
            if (!string.IsNullOrEmpty(e.CurrentAsset)) {
                viewModel.ProgressText = $"{e.CurrentAsset} ({e.Processed}/{e.Total})";
            } else {
                viewModel.ProgressText = e.Total > 0 ? $"Loading... ({e.Processed}/{e.Total})" : "Loading...";
            }
        }

        /// <summary>
        /// Shows DataGrids and applies grouping with yield for heavy Textures grid.
        /// Split into phases to prevent UI freeze:
        /// 1. Show all DataGrids WITHOUT grouping (fast)
        /// 2. Defer grouping application to separate callback
        /// </summary>
        private void ShowDataGridsAndApplyGrouping(int textureCount, int modelCount, int materialCount) {
            logger.Info("[ShowDataGridsAndApplyGrouping] Binding and showing Models/Materials...");
            viewModel.ProgressText = "Loading...";

            // Bind and show Models and Materials immediately (small - fast)
            ModelsDataGrid.SetBinding(System.Windows.Controls.ItemsControl.ItemsSourceProperty,
                new System.Windows.Data.Binding("Models"));
            MaterialsDataGrid.SetBinding(System.Windows.Controls.ItemsControl.ItemsSourceProperty,
                new System.Windows.Data.Binding("Materials"));
            ModelsDataGrid.Visibility = Visibility.Visible;
            MaterialsDataGrid.Visibility = Visibility.Visible;

            // Use DispatcherTimer with delay to load TexturesDataGrid
            // This ensures UI is fully rendered before we touch the heavy DataGrid
            var timer = new System.Windows.Threading.DispatcherTimer {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            timer.Tick += (s, e) => {
                timer.Stop();
                logger.Info("[ShowDataGridsAndApplyGrouping] Timer: Binding TexturesDataGrid...");
                TexturesDataGrid.SetBinding(System.Windows.Controls.ItemsControl.ItemsSourceProperty,
                    new System.Windows.Data.Binding("Textures"));
                TexturesDataGrid.Visibility = Visibility.Visible;
                logger.Info("[ShowDataGridsAndApplyGrouping] Timer: TexturesDataGrid visible");

                // Apply grouping after another small delay
                var groupTimer = new System.Windows.Threading.DispatcherTimer {
                    Interval = TimeSpan.FromMilliseconds(100)
                };
                groupTimer.Tick += (s2, e2) => {
                    groupTimer.Stop();
                    logger.Info("[ShowDataGridsAndApplyGrouping] Timer: Applying grouping...");
                    ApplyTextureGroupingIfEnabled();
                    logger.Info("[ShowDataGridsAndApplyGrouping] Complete");
                    viewModel.ProgressText = $"Ready ({textureCount} textures, {modelCount} models, {materialCount} materials)";
                    viewModel.ProgressValue = viewModel.ProgressMaximum;
                };
                groupTimer.Start();
            };
            timer.Start();
            logger.Info("[ShowDataGridsAndApplyGrouping] Models/Materials shown, timer started for textures");
        }

        private void OnORMTexturesDetected(object? sender, ORMTexturesDetectedEventArgs e) {
            logService.LogInfo($"[OnORMTexturesDetected] Detected {e.DetectedCount} ORM textures, {e.Associations.Count} associations");
            // SubGroupName and ParentORMTexture are already set in AssetLoadingViewModel before AssetsLoaded
            // This event is for informational purposes only
        }

        private void OnVirtualORMTexturesGenerated(object? sender, VirtualORMTexturesGeneratedEventArgs e) {
            logService.LogInfo($"[OnVirtualORMTexturesGenerated] Generated {e.GeneratedCount} virtual ORM textures, {e.Associations.Count} associations");
            // SubGroupName, ParentORMTexture and sources are already set in AssetLoadingViewModel before AssetsLoaded
            // This event is for informational purposes only
        }

        private void OnUploadStatesRestored(object? sender, UploadStatesRestoredEventArgs e) {
            logService.LogInfo($"[OnUploadStatesRestored] Restored {e.RestoredCount} upload states");
            // Upload states are already applied in AssetLoadingViewModel before AssetsLoaded
            // This event is for informational purposes only
        }

        private void OnB2VerificationCompleted(object? sender, B2VerificationCompletedEventArgs e) {
            // Use BeginInvoke instead of Invoke to prevent potential deadlock
            // when this is called from background thread while UI thread is processing ApplyAssetsToUI
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, () => {
                if (!e.Success) {
                    logService.LogWarn($"[B2 Verification] Failed: {e.ErrorMessage}");
                    return;
                }

                if (e.NotFoundOnServer.Count > 0) {
                    logService.LogWarn($"[B2 Verification] {e.NotFoundOnServer.Count} files not found on server (status updated to 'Not on Server')");

                    // Refresh DataGrids to show updated status
                    TexturesDataGrid.Items.Refresh();
                    ModelsDataGrid.Items.Refresh();
                    MaterialsDataGrid.Items.Refresh();
                } else {
                    logService.LogInfo($"[B2 Verification] All {e.VerifiedCount} uploaded files verified on server");
                }
            });
        }

        private void OnAssetLoadingError(object? sender, AssetLoadErrorEventArgs e) {
            logService.LogError($"[OnAssetLoadingError] {e.Title}: {e.Message}");
            MessageBox.Show(e.Message, e.Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        #endregion

        #region MaterialSelectionViewModel Event Handlers

        private void OnMaterialParametersLoaded(object? sender, MaterialParametersLoadedEventArgs e) {
            // Use BeginInvoke to prevent potential deadlock when called from background thread
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, () => {
                DisplayMaterialParameters(e.Material);
            });
        }

        private void OnNavigateToTextureRequested(object? sender, NavigateToTextureEventArgs e) {
            Dispatcher.BeginInvoke(new Action(() => {
                if (TexturesTabItem != null) {
                    tabControl.SelectedItem = TexturesTabItem;
                }

                TextureResource? texture = viewModel.Textures.FirstOrDefault(t => t.ID == e.TextureId);
                if (texture != null) {
                    var view = CollectionViewSource.GetDefaultView(TexturesDataGrid.ItemsSource);
                    view?.MoveCurrentTo(texture);

                    TexturesDataGrid.SelectedItem = texture;
                    TexturesDataGrid.UpdateLayout();
                    TexturesDataGrid.ScrollIntoView(texture);
                    TexturesDataGrid.Focus();

                    logger.Info("Navigated to texture {TextureName} (ID {TextureId}) for {MapType}",
                        texture.Name, texture.ID, e.MapType);
                } else {
                    logger.Warn("Texture with ID {TextureId} not found for navigation", e.TextureId);
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void OnMaterialSelectionError(object? sender, MaterialErrorEventArgs e) {
            logService.LogError($"[OnMaterialSelectionError] {e.Message}");
        }

        #endregion

        private void CreateORMButton_Click(object sender, RoutedEventArgs e) {
            // Delegate to ViewModel
            viewModel.ORMTexture.CreateEmptyORMCommand.Execute(viewModel.Textures);
        }

        // Master Material assignment handlers
        private void SetMasterForSelectedMaterials_Click(object sender, RoutedEventArgs e) {
            if (sender is MenuItem menuItem) {
                string? masterName = menuItem.Tag as string;
                if (string.IsNullOrEmpty(masterName)) masterName = null;

                // Get selected materials
                var selectedMaterials = MaterialsDataGrid.SelectedItems
                    .OfType<MaterialResource>()
                    .ToList();

                if (selectedMaterials.Count == 0) return;

                // Set master for all selected materials
                var materialIds = selectedMaterials.Select(m => m.ID).ToList();
                viewModel.MasterMaterialsViewModel.SetMasterForMaterials(materialIds, masterName);

                // Update UI
                foreach (var material in selectedMaterials) {
                    material.MasterMaterialName = masterName;
                }

                logService.LogInfo($"Set master '{masterName ?? "(none)"}' for {selectedMaterials.Count} materials");
            }
        }

        private void MaterialRowContextMenu_Opened(object sender, RoutedEventArgs e) {
            if (sender is ContextMenu contextMenu) {
                // Find "Set Master Material" menu item
                var setMasterMenuItem = contextMenu.Items
                    .OfType<MenuItem>()
                    .FirstOrDefault(m => m.Header?.ToString() == "Set Master Material");

                if (setMasterMenuItem != null) {
                    setMasterMenuItem.Items.Clear();

                    // Add "(None)" option
                    var noneItem = new MenuItem {
                        Header = "(None)",
                        Tag = ""
                    };
                    noneItem.Click += SetMasterForSelectedMaterials_Click;
                    setMasterMenuItem.Items.Add(noneItem);

                    // Add separator
                    setMasterMenuItem.Items.Add(new Separator());

                    // Add all master materials
                    foreach (var master in viewModel.MasterMaterialsViewModel.MasterMaterials) {
                        var masterItem = new MenuItem {
                            Header = master.Name,
                            Tag = master.Name,
                            FontWeight = master.IsBuiltIn ? System.Windows.FontWeights.Normal : System.Windows.FontWeights.Bold
                        };
                        masterItem.Click += SetMasterForSelectedMaterials_Click;
                        setMasterMenuItem.Items.Add(masterItem);
                    }
                }
            }
        }

        // ORM from Material handlers
        private void CreateORMFromMaterial_Click(object sender, RoutedEventArgs e) {
            // Delegate to ViewModel
            var material = MaterialsDataGrid.SelectedItem as MaterialResource;
            viewModel.ORMTexture.CreateORMFromMaterialCommand.Execute(new ORMFromMaterialRequest {
                Material = material,
                Textures = viewModel.Textures
            });
        }

        private async void CreateORMForAllMaterials_Click(object sender, RoutedEventArgs e) {
            // Delegate to ViewModel
            await viewModel.ORMTexture.CreateAllORMsCommand.ExecuteAsync(new ORMBatchCreationRequest {
                Materials = viewModel.Materials,
                Textures = viewModel.Textures
            });
        }

        private void DeleteORMTexture_Click(object sender, RoutedEventArgs e) {
            // Delegate to ViewModel
            var material = MaterialsDataGrid.SelectedItem as MaterialResource;
            if (material == null) return;

            viewModel.ORMTexture.DeleteORMForMaterialCommand.Execute(new ORMDeleteRequest {
                Material = material,
                Textures = viewModel.Textures
            });
        }

        private void DeleteORMFromList_Click(object sender, RoutedEventArgs e) {
            // Delegate to ViewModel
            var ormTexture = TexturesDataGrid.SelectedItem as ORMTextureResource;
            viewModel.ORMTexture.DeleteORMCommand.Execute(new ORMDirectDeleteRequest {
                ORMTexture = ormTexture,
                Textures = viewModel.Textures
            });
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

        private void OpenProcessedFolder_Click(object sender, RoutedEventArgs e) {
            if (TexturesDataGrid.SelectedItem is TextureResource texture) {
                var processedPath = FindProcessedTexturePath(texture);
                if (!string.IsNullOrEmpty(processedPath)) {
                    OpenFileInExplorer(processedPath);
                } else {
                    MessageBox.Show("Processed file not found. Export the model first.", "Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void OpenModelProcessedFolder_Click(object sender, RoutedEventArgs e) {
            if (ModelsDataGrid.SelectedItem is ModelResource model) {
                var processedPath = FindProcessedModelPath(model);
                if (!string.IsNullOrEmpty(processedPath)) {
                    OpenFileInExplorer(processedPath);
                } else {
                    MessageBox.Show("Processed file not found. Export the model first.", "Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void OpenMaterialSourceFolder_Click(object sender, RoutedEventArgs e) {
            if (MaterialsDataGrid.SelectedItem is MaterialResource material && !string.IsNullOrEmpty(material.Path)) {
                OpenFileInExplorer(material.Path);
            }
        }

        private void OpenMaterialProcessedFolder_Click(object sender, RoutedEventArgs e) {
            if (MaterialsDataGrid.SelectedItem is MaterialResource material) {
                var processedPath = FindProcessedMaterialPath(material);
                if (!string.IsNullOrEmpty(processedPath)) {
                    OpenFileInExplorer(processedPath);
                } else {
                    MessageBox.Show("Processed file not found. Export the model first.", "Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        /// <summary>
        /// Finds the processed KTX2 file path for a texture
        /// </summary>
        private string? FindProcessedTexturePath(TextureResource texture) {
            if (string.IsNullOrEmpty(ProjectFolderPath)) return null;

            var serverContentPath = Path.Combine(ProjectFolderPath, "server", "assets", "content");
            if (!Directory.Exists(serverContentPath)) return null;

            // Try to find KTX2 file by texture name
            var textureName = Path.GetFileNameWithoutExtension(texture.Path ?? texture.Name);
            if (string.IsNullOrEmpty(textureName)) return null;

            // Search for matching KTX2 file
            var ktx2Files = Directory.GetFiles(serverContentPath, $"{textureName}.ktx2", SearchOption.AllDirectories);
            if (ktx2Files.Length > 0) return ktx2Files[0];

            // Also try with _lod0 suffix (for some textures)
            ktx2Files = Directory.GetFiles(serverContentPath, $"{textureName}_*.ktx2", SearchOption.AllDirectories);
            if (ktx2Files.Length > 0) return ktx2Files[0];

            return null;
        }

        /// <summary>
        /// Finds the processed GLB file path for a model
        /// </summary>
        private string? FindProcessedModelPath(ModelResource model) {
            if (string.IsNullOrEmpty(ProjectFolderPath)) return null;

            var serverContentPath = Path.Combine(ProjectFolderPath, "server", "assets", "content");
            if (!Directory.Exists(serverContentPath)) return null;

            var modelName = Path.GetFileNameWithoutExtension(model.Path ?? model.Name);
            if (string.IsNullOrEmpty(modelName)) return null;

            // Search for GLB files
            var glbFiles = Directory.GetFiles(serverContentPath, $"{modelName}*.glb", SearchOption.AllDirectories);
            if (glbFiles.Length > 0) return glbFiles[0];

            return null;
        }

        /// <summary>
        /// Finds the processed JSON file path for a material
        /// </summary>
        private string? FindProcessedMaterialPath(MaterialResource material) {
            if (string.IsNullOrEmpty(ProjectFolderPath)) return null;

            var serverContentPath = Path.Combine(ProjectFolderPath, "server", "assets", "content");
            if (!Directory.Exists(serverContentPath)) return null;

            var materialName = material.Name;
            if (string.IsNullOrEmpty(materialName)) return null;

            // Search for JSON files
            var jsonFiles = Directory.GetFiles(serverContentPath, $"{materialName}.json", SearchOption.AllDirectories);
            if (jsonFiles.Length > 0) return jsonFiles[0];

            return null;
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
                Helpers.ClipboardHelper.SetTextWithFeedback(texture.Path);
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
                    ? "FBX2glTF-windows-x86_64.exe"
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
            if (ModelsDataGrid.SelectedItem is ModelResource model && !string.IsNullOrEmpty(model.Path)) {
                Helpers.ClipboardHelper.SetTextWithFeedback(model.Path);
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

        private DispatcherTimer? _windowStateTimer;

        private void Window_StateChanged(object sender, EventArgs e) {
            // Hide TexturesDataGrid during maximize/restore to prevent freeze with grouping
            if (TexturesDataGrid == null) return;

            // Only apply workaround when grouping is enabled
            if (GroupTexturesCheckBox?.IsChecked != true) return;

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

        #endregion

        #region Master Materials Tab Event Handlers

        private void MasterMaterialsDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            if (MasterMaterialsDataGrid.SelectedItem is MasterMaterials.Models.MasterMaterial master) {
                if (!master.IsBuiltIn) {
                    OpenMasterMaterialEditor(master);
                }
            }
        }

        private void ChunkCheckBox_Click(object sender, RoutedEventArgs e) {
            if (sender is not CheckBox checkBox) return;
            if (checkBox.Tag is not MasterMaterials.Models.ShaderChunk chunk) return;

            var selectedMaster = viewModel.MasterMaterialsViewModel.SelectedMaster;
            if (selectedMaster == null || selectedMaster.IsBuiltIn) return;

            if (checkBox.IsChecked == true) {
                // Add chunk to master
                _ = viewModel.MasterMaterialsViewModel.AddChunkToMasterAsync(selectedMaster, chunk);
                logger.Info($"Added chunk '{chunk.Id}' to master '{selectedMaster.Name}'");
            } else {
                // Remove chunk from master
                _ = viewModel.MasterMaterialsViewModel.RemoveChunkFromMasterAsync(selectedMaster, chunk.Id);
                logger.Info($"Removed chunk '{chunk.Id}' from master '{selectedMaster.Name}'");
            }

            // Force refresh the ListBox to update chunk count display
            AllChunksListBox.Items.Refresh();
        }

        // ChunkLabel_MouseLeftButtonDown removed - selection is now handled by ListBox.SelectedItem binding

        private void MasterMaterial_Clone_Click(object sender, RoutedEventArgs e) {
            if (sender is MenuItem menuItem && menuItem.DataContext is MasterMaterials.Models.MasterMaterial master) {
                viewModel.MasterMaterialsViewModel.CloneMasterCommand.Execute(master);
            }
        }

        private void MasterMaterial_Edit_Click(object sender, RoutedEventArgs e) {
            if (sender is MenuItem menuItem && menuItem.DataContext is MasterMaterials.Models.MasterMaterial master) {
                if (!master.IsBuiltIn) {
                    OpenMasterMaterialEditor(master);
                }
            }
        }

        private void MasterMaterial_Delete_Click(object sender, RoutedEventArgs e) {
            if (sender is MenuItem menuItem && menuItem.DataContext is MasterMaterials.Models.MasterMaterial master) {
                if (master.IsBuiltIn) {
                    MessageBox.Show("Cannot delete built-in master materials.", "Delete Master Material",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var result = MessageBox.Show($"Are you sure you want to delete master material '{master.Name}'?",
                    "Delete Master Material", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes) {
                    viewModel.MasterMaterialsViewModel.DeleteMasterCommand.Execute(master);
                }
            }
        }

        private void Chunk_Edit_Click(object sender, RoutedEventArgs e) {
            if (sender is MenuItem menuItem && menuItem.DataContext is MasterMaterials.Models.ShaderChunk chunk) {
                // Select the chunk, which will load it into the embedded editor via PropertyChanged
                viewModel.MasterMaterialsViewModel.SelectedChunk = chunk;
            }
        }

        private async void Chunk_Delete_Click(object sender, RoutedEventArgs e) {
            if (sender is MenuItem menuItem && menuItem.DataContext is MasterMaterials.Models.ShaderChunk chunk) {
                if (chunk.IsBuiltIn) {
                    MessageBox.Show("Cannot delete built-in chunks.", "Delete Chunk", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var result = MessageBox.Show($"Are you sure you want to delete chunk '{chunk.Id}'?",
                    "Delete Chunk", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes) {
                    await viewModel.MasterMaterialsViewModel.DeleteChunkCommand.ExecuteAsync(chunk);
                }
            }
        }

        private void Chunk_Copy_Click(object sender, RoutedEventArgs e) {
            if (sender is MenuItem menuItem && menuItem.DataContext is MasterMaterials.Models.ShaderChunk chunk) {
                viewModel.MasterMaterialsViewModel.CopyChunkCommand.Execute(chunk);
            }
        }

        private void SlotEditChunk_Click(object sender, RoutedEventArgs e) {
            if (sender is not Button button) return;
            if (button.Tag is not ViewModels.ChunkSlotViewModel slotVm) return;

            var selectedChunk = slotVm.SelectedChunk;
            if (selectedChunk != null) {
                // Select the chunk and load it into the editor
                viewModel.MasterMaterialsViewModel.SelectedChunk = selectedChunk;
                LoadChunkIntoEditor(selectedChunk);
            }
        }

        private void OpenMasterMaterialEditor(MasterMaterials.Models.MasterMaterial master) {
            var availableChunks = viewModel.MasterMaterialsViewModel.Chunks.ToList();
            var editorWindow = new Windows.MasterMaterialEditorWindow(master, availableChunks) {
                Owner = this
            };

            if (editorWindow.ShowDialog() == true && editorWindow.EditedMaster != null) {
                // Update the master in the collection
                var index = viewModel.MasterMaterialsViewModel.MasterMaterials.IndexOf(master);
                if (index >= 0) {
                    viewModel.MasterMaterialsViewModel.MasterMaterials[index] = editorWindow.EditedMaster;
                }
                viewModel.MasterMaterialsViewModel.HasUnsavedChanges = true;
                logger.Info($"Master material '{editorWindow.EditedMaster.Name}' updated");
            }
        }

        private void OpenChunkEditor(MasterMaterials.Models.ShaderChunk chunk) {
            // Instead of opening a separate window, load the chunk into the embedded editor
            LoadChunkIntoEditor(chunk);
        }

        #region Embedded Chunk Editor

        /// <summary>
        /// Initializes the embedded chunk code editor with syntax highlighting
        /// </summary>
        private void InitializeChunkCodeEditor() {
            // Load syntax highlighting definitions
            if (_glslHighlighting == null) {
                _glslHighlighting = LoadHighlightingDefinition("AssetProcessor.SyntaxHighlighting.GLSL.xshd");
            }
            if (_wgslHighlighting == null) {
                _wgslHighlighting = LoadHighlightingDefinition("AssetProcessor.SyntaxHighlighting.WGSL.xshd");
            }

            // Apply highlighting to editors
            if (_glslHighlighting != null) {
                GlslCodeEditor.SyntaxHighlighting = _glslHighlighting;
            }
            if (_wgslHighlighting != null) {
                WgslCodeEditor.SyntaxHighlighting = _wgslHighlighting;
            }

            // Wire up text change events
            GlslCodeEditor.TextChanged += (_, _) => OnChunkCodeChanged();
            WgslCodeEditor.TextChanged += (_, _) => OnChunkCodeChanged();

            // Subscribe to SelectedChunk changes
            viewModel.MasterMaterialsViewModel.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(viewModel.MasterMaterialsViewModel.SelectedChunk)) {
                    var selectedChunk = viewModel.MasterMaterialsViewModel.SelectedChunk;
                    if (selectedChunk != null) {
                        LoadChunkIntoEditor(selectedChunk);
                    }
                }
            };

            // Initial state
            UpdateChunkEditorStatus("Select a chunk to edit");
        }

        /// <summary>
        /// Loads a syntax highlighting definition from an embedded resource
        /// </summary>
        private static IHighlightingDefinition? LoadHighlightingDefinition(string resourceName) {
            try {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream(resourceName);

                if (stream == null) {
                    System.Diagnostics.Debug.WriteLine($"Could not find embedded resource: {resourceName}");
                    return null;
                }

                using var reader = new XmlTextReader(stream);
                return HighlightingLoader.Load(reader, HighlightingManager.Instance);
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"Error loading syntax highlighting: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Loads a chunk into the embedded editor
        /// </summary>
        private void LoadChunkIntoEditor(MasterMaterials.Models.ShaderChunk chunk) {
            // Check for unsaved changes
            if (_chunkEditorHasUnsavedChanges && _currentEditingChunk != null) {
                var result = MessageBox.Show(
                    $"You have unsaved changes in chunk '{_currentEditingChunk.Id}'. Do you want to save them first?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes) {
                    SaveCurrentChunk();
                } else if (result == MessageBoxResult.Cancel) {
                    // Restore selection to current chunk
                    viewModel.MasterMaterialsViewModel.SelectedChunk = _currentEditingChunk;
                    return;
                }
            }

            _currentEditingChunk = chunk;
            _originalGlslCode = chunk.Glsl;
            _originalWgslCode = chunk.Wgsl;

            // Load code into editors
            GlslCodeEditor.Text = chunk.Glsl ?? "";
            WgslCodeEditor.Text = chunk.Wgsl ?? "";

            // Set read-only state for built-in chunks
            GlslCodeEditor.IsReadOnly = chunk.IsBuiltIn;
            WgslCodeEditor.IsReadOnly = chunk.IsBuiltIn;

            // Update UI state
            _chunkEditorHasUnsavedChanges = false;
            UpdateChunkEditorUnsavedIndicator();

            if (chunk.IsBuiltIn) {
                UpdateChunkEditorStatus($"Viewing built-in chunk '{chunk.Id}' (read-only)");
            } else {
                UpdateChunkEditorStatus($"Editing chunk '{chunk.Id}'");
            }

            logger.Info($"Loaded chunk '{chunk.Id}' into editor");
        }

        /// <summary>
        /// Called when code changes in either editor
        /// </summary>
        private void OnChunkCodeChanged() {
            if (_currentEditingChunk == null || _currentEditingChunk.IsBuiltIn) return;

            // Check if there are actual changes
            bool glslChanged = GlslCodeEditor.Text != _originalGlslCode;
            bool wgslChanged = WgslCodeEditor.Text != _originalWgslCode;

            _chunkEditorHasUnsavedChanges = glslChanged || wgslChanged;
            UpdateChunkEditorUnsavedIndicator();
        }

        /// <summary>
        /// Updates the unsaved changes indicator visibility
        /// </summary>
        private void UpdateChunkEditorUnsavedIndicator() {
            ChunkEditorUnsavedIndicator.Visibility = _chunkEditorHasUnsavedChanges
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        /// <summary>
        /// Updates the status text in the chunk editor
        /// </summary>
        private void UpdateChunkEditorStatus(string status) {
            ChunkEditorStatusText.Text = status;
        }

        /// <summary>
        /// Saves the current chunk being edited
        /// </summary>
        private void SaveCurrentChunk() {
            if (_currentEditingChunk == null) return;

            if (_currentEditingChunk.IsBuiltIn) {
                MessageBox.Show("Cannot save built-in chunks. Use 'Copy' to create an editable version.",
                    "Read-Only Chunk", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Update chunk with new code
            _currentEditingChunk.Glsl = GlslCodeEditor.Text;
            _currentEditingChunk.Wgsl = WgslCodeEditor.Text;

            // Update in ViewModel
            viewModel.MasterMaterialsViewModel.UpdateChunk(_currentEditingChunk);

            // Update original values
            _originalGlslCode = GlslCodeEditor.Text;
            _originalWgslCode = WgslCodeEditor.Text;

            _chunkEditorHasUnsavedChanges = false;
            UpdateChunkEditorUnsavedIndicator();
            UpdateChunkEditorStatus($"Chunk '{_currentEditingChunk.Id}' saved");

            logger.Info($"Chunk '{_currentEditingChunk.Id}' saved");
        }

        /// <summary>
        /// Handler for Save Chunk button click
        /// </summary>
        private void ChunkEditorSaveButton_Click(object sender, RoutedEventArgs e) {
            SaveCurrentChunk();
        }

        /// <summary>
        /// Handler for New Chunk button click
        /// </summary>
        private void ChunkEditorNewButton_Click(object sender, RoutedEventArgs e) {
            // Check for unsaved changes
            if (_chunkEditorHasUnsavedChanges && _currentEditingChunk != null) {
                var result = MessageBox.Show(
                    $"You have unsaved changes in chunk '{_currentEditingChunk.Id}'. Do you want to save them first?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes) {
                    SaveCurrentChunk();
                } else if (result == MessageBoxResult.Cancel) {
                    return;
                }
            }

            // Create new chunk
            viewModel.MasterMaterialsViewModel.AddNewChunkCommand.Execute(null);
        }

        #endregion

        #endregion
    }
}