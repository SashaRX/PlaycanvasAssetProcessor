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
        private readonly IPlayCanvasCredentialsService credentialsService;
        private readonly IDataGridLayoutService dataGridLayoutService;
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
            IPlayCanvasCredentialsService credentialsService,
            IDataGridLayoutService dataGridLayoutService,
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
            this.credentialsService = credentialsService ?? throw new ArgumentNullException(nameof(credentialsService));
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

        private enum ViewerType { None, Texture, Model, Material, ServerFile, ChunkSlots }

        private void ShowViewer(ViewerType viewerType) {
            TextureViewerScroll.Visibility = viewerType == ViewerType.Texture ? Visibility.Visible : Visibility.Collapsed;
            ModelViewerScroll.Visibility = viewerType == ViewerType.Model ? Visibility.Visible : Visibility.Collapsed;
            MaterialViewerScroll.Visibility = viewerType == ViewerType.Material ? Visibility.Visible : Visibility.Collapsed;
            ServerFileInfoScroll.Visibility = viewerType == ViewerType.ServerFile ? Visibility.Visible : Visibility.Collapsed;
            ChunkSlotsScroll.Visibility = viewerType == ViewerType.ChunkSlots ? Visibility.Visible : Visibility.Collapsed;
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

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            // Guard against early calls during initialization
            if (!this.IsLoaded || UnifiedExportGroupBox == null) return;

            if (tabControl.SelectedItem is TabItem selectedTab) {
                switch (selectedTab.Header.ToString()) {
                    case "Textures":
                        ShowViewer(ViewerType.Texture);
                        UpdateExportCounts();
                        TextureToolsPanel.Visibility = Visibility.Visible;
                        break;
                    case "Models":
                        ShowViewer(ViewerType.Model);
                        UpdateExportCounts();
                        TextureToolsPanel.Visibility = Visibility.Collapsed;
                        break;
                    case "Materials":
                        ShowViewer(ViewerType.Material);
                        UpdateExportCounts();
                        TextureToolsPanel.Visibility = Visibility.Collapsed;
                        break;
                    case "Master Materials":
                        ShowViewer(ViewerType.ChunkSlots);
                        TextureToolsPanel.Visibility = Visibility.Collapsed;
                        break;
                    case "Server":
                        ShowViewer(ViewerType.ServerFile);
                        TextureToolsPanel.Visibility = Visibility.Collapsed;
                        break;
                    case "Logs":
                        ShowViewer(ViewerType.None);
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
                viewModel.RecalculateIndices();
                viewModel.SyncMaterialMasterMappings();

                logger.Info("[ApplyAssetsToUI] Data assigned, binding DataGrids");

                ModelsDataGrid.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Models"));
                MaterialsDataGrid.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Materials"));
                TexturesDataGrid.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Textures"));

                ModelsDataGrid.Visibility = Visibility.Visible;
                MaterialsDataGrid.Visibility = Visibility.Visible;
                TexturesDataGrid.Visibility = Visibility.Visible;

                RestoreGridLayout(ModelsDataGrid);
                RestoreGridLayout(MaterialsDataGrid);
                RestoreGridLayout(TexturesDataGrid);

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
            RestoreGridLayout(ModelsDataGrid);
            RestoreGridLayout(MaterialsDataGrid);

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
                RestoreGridLayout(TexturesDataGrid);
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
    }
}
