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
using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureConversion.Settings;
using AssetProcessor.TextureViewer;

namespace AssetProcessor {
    public partial class MainWindow : Window, INotifyPropertyChanged {
        // Коллекции теперь в MainViewModel - удалены дублирующиеся объявления
        // viewModel.Textures, viewModel.Models, viewModel.Materials, Assets теперь доступны через viewModel

        private readonly SemaphoreSlim getAssetsSemaphore;
        private readonly SemaphoreSlim downloadSemaphore;
        private string? projectFolderPath = string.Empty;
        private string? userName = string.Empty;
        private string? userID = string.Empty;
        private string? projectName = string.Empty;
        private bool? isViewerVisible = true;
        private BitmapSource? originalBitmapSource;
        private bool isUpdatingChannelButtons = false; // Flag to prevent recursive button updates
        private readonly List<string> supportedFormats = [".png", ".jpg", ".jpeg"];
        private readonly List<string> excludedFormats = [".hdr", ".avif"];
        private readonly List<string> supportedModelFormats = [".fbx", ".obj"];//, ".glb"];
        private CancellationTokenSource cancellationTokenSource = new();
        private readonly IPlayCanvasService playCanvasService;
        private readonly IHistogramService histogramService;
        private readonly ITextureChannelService textureChannelService;
        private readonly ILogService logService;
        private readonly ILocalCacheService localCacheService;
        private Dictionary<int, string> folderPaths = new();
        private readonly Dictionary<string, BitmapImage> imageCache = new(); // Кеш для загруженных изображений
        private CancellationTokenSource? textureLoadCancellation; // Токен отмены для загрузки текстур
        private GlobalTextureConversionSettings? globalTextureSettings; // Глобальные настройки конвертации текстур
        private ConversionSettingsManager? conversionSettingsManager; // Менеджер параметров конвертации
        private ConnectionState currentConnectionState = ConnectionState.Disconnected; // Текущее состояние подключения
        private const int MaxPreviewSize = 2048; // Максимальный размер изображения для превью (высокое качество для детального просмотра)
        private const int ThumbnailSize = 512; // Размер для быстрого превью (увеличено для лучшей читаемости)
        private const double MinPreviewColumnWidth = 256.0;
        private const double MaxPreviewColumnWidth = 512.0;
        private const double MinPreviewContentHeight = 128.0;
        private const double MaxPreviewContentHeight = 512.0;
        private const double DefaultPreviewContentHeight = 300.0;
        private bool isKtxPreviewActive;
        private int currentMipLevel;
        private bool isUpdatingMipLevel;
        private bool isSorting = false; // Флаг для отслеживания процесса сортировки
        private static readonly TextureConversion.Settings.PresetManager cachedPresetManager = new(); // Кэшированный PresetManager для избежания создания нового при каждой инициализации
        private readonly ConcurrentDictionary<string, object> texturesBeingChecked = new(StringComparer.OrdinalIgnoreCase); // Отслеживание текстур, для которых уже запущена проверка CompressedSize
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
        // Current loaded texture paths for preview renderer switching
        private string? currentLoadedTexturePath;
        private string? currentLoadedKtx2Path;
        private TextureResource? currentSelectedTexture; // Currently selected texture for sRGB detection
        private string? currentActiveChannelMask; // Current active RGBA mask (null = no mask, "R"/"G"/"B"/"A" = active mask)
        // Legacy fields removed - zoom/pan now handled natively by D3D11TextureViewerControl
        private BitmapSource? originalFileBitmapSource;
        private double previewReferenceWidth;
        private double previewReferenceHeight;

        // D3D11 Texture Viewer state (zoom/pan now handled in control itself)
        private bool isD3D11RenderLoopEnabled = true;

        // KTX2 preview mode: always use D3D11 native when D3D11 renderer is active
        // Legacy PNG extraction removed
        // Track which preview renderer is currently active
        private bool isUsingD3D11Renderer = true;
        private static readonly Regex MipLevelRegex = new(@"(?:_level|_mip|_)(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly HashSet<string> ignoredAssetTypes = new(StringComparer.OrdinalIgnoreCase) { "script", "wasm", "cubemap" };
        private readonly HashSet<string> reportedIgnoredAssetTypes = new(StringComparer.OrdinalIgnoreCase);
        private readonly object ignoredAssetTypesLock = new();
        private bool isBranchInitializationInProgress;
        private bool isProjectInitializationInProgress;

        // Projects и Branches теперь в MainViewModel - удалены дублирующиеся объявления

        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Получает расшифрованный PlayCanvas API ключ из настроек.
        /// Используется для всех вызовов PlayCanvas API для корректной работы с зашифрованным хранилищем.
        /// </summary>
        private static string? GetDecryptedApiKey() {
            if (!AppSettings.Default.TryGetDecryptedPlaycanvasApiKey(out string? apiKey)) {
                logger.Error("Не удалось расшифровать PlayCanvas API ключ из настроек");
                return null;
            }
            return apiKey;
        }

        private readonly MainViewModel viewModel;

        public MainWindow(
            IPlayCanvasService playCanvasService,
            IHistogramService histogramService,
            ITextureChannelService textureChannelService,
            ILogService logService,
            ILocalCacheService localCacheService,
            MainViewModel viewModel) {
            this.playCanvasService = playCanvasService ?? throw new ArgumentNullException(nameof(playCanvasService));
            this.histogramService = histogramService ?? throw new ArgumentNullException(nameof(histogramService));
            this.textureChannelService = textureChannelService ?? throw new ArgumentNullException(nameof(textureChannelService));
            this.logService = logService ?? throw new ArgumentNullException(nameof(logService));
            this.localCacheService = localCacheService ?? throw new ArgumentNullException(nameof(localCacheService));
            this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

            InitializeComponent();
            UpdatePreviewContentHeight(DefaultPreviewContentHeight);
            ResetPreviewState();
            _ = InitializeOnStartup();

            // Инициализация ConversionSettings
            InitializeConversionSettings();

            viewModel.ConversionSettingsProvider = ConversionSettingsPanel;
            viewModel.TextureProcessingCompleted += ViewModel_TextureProcessingCompleted;
            viewModel.TexturePreviewLoaded += ViewModel_TexturePreviewLoaded;

            // Подписка на события панели настроек конвертации
            ConversionSettingsPanel.AutoDetectRequested += ConversionSettingsPanel_AutoDetectRequested;
            ConversionSettingsPanel.ConvertRequested += ConversionSettingsPanel_ConvertRequested;

            // Отображение версии приложения с информацией о бранче и коммите
            VersionTextBlock.Text = $"v{VersionHelper.GetVersionString()}";

            // Заполнение ComboBox для Color Channel
            ComboBoxHelper.PopulateComboBox<ColorChannel>(MaterialAOColorChannelComboBox);
            ComboBoxHelper.PopulateComboBox<ColorChannel>(MaterialDiffuseColorChannelComboBox);
            ComboBoxHelper.PopulateComboBox<ColorChannel>(MaterialSpecularColorChannelComboBox);
            ComboBoxHelper.PopulateComboBox<ColorChannel>(MaterialMetalnessColorChannelComboBox);
            ComboBoxHelper.PopulateComboBox<ColorChannel>(MaterialGlossinessColorChannelComboBox);

            // Заполнение ComboBox для UV Channel
            ComboBoxHelper.PopulateComboBox<UVChannel>(MaterialDiffuseUVChannelComboBox);
            ComboBoxHelper.PopulateComboBox<UVChannel>(MaterialSpecularUVChannelComboBox);
            ComboBoxHelper.PopulateComboBox<UVChannel>(MaterialNormalUVChannelComboBox);
            ComboBoxHelper.PopulateComboBox<UVChannel>(MaterialAOUVChannelComboBox);

            LoadModel(path: MainWindowHelpers.MODEL_PATH);

            getAssetsSemaphore = new SemaphoreSlim(AppSettings.Default.GetTexturesSemaphoreLimit);
            downloadSemaphore = new SemaphoreSlim(AppSettings.Default.DownloadSemaphoreLimit);

            projectFolderPath = AppSettings.Default.ProjectsFolderPath;
            UpdateConnectionStatus(false);

            TexturesDataGrid.ItemsSource = viewModel.Textures;
            ModelsDataGrid.ItemsSource = viewModel.Models;
            MaterialsDataGrid.ItemsSource = viewModel.Materials;

            TexturesDataGrid.LoadingRow += TexturesDataGrid_LoadingRow;
            TexturesDataGrid.Sorting += TexturesDataGrid_Sorting;

            DataContext = viewModel;

            this.Closing += MainWindow_Closing;
            //LoadLastSettings();

            RenderOptions.SetBitmapScalingMode(UVImage, BitmapScalingMode.HighQuality);
            RenderOptions.SetBitmapScalingMode(UVImage2, BitmapScalingMode.HighQuality);

            // Setup D3D11 render loop
            this.Loaded += MainWindow_Loaded;
            CompositionTarget.Rendering += OnD3D11Rendering;

            // Примечание: InitializeOnStartup() уже вызывается выше (строка 144)
            // и корректно обрабатывает загрузку локальных файлов без показа MessageBox
            // Пресеты инициализируются в TextureConversionSettingsPanel
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
            logService.LogInfo($"=== ProjectsComboBox_SelectionChanged CALLED, isProjectInitializationInProgress={isProjectInitializationInProgress} ===");

            if (isProjectInitializationInProgress) {
                logService.LogInfo("Skipping ProjectsComboBox_SelectionChanged - initialization in progress");
                return;
            }

            if (ProjectsComboBox.SelectedItem is KeyValuePair<string, string> selectedProject) {
                projectName = MainWindowHelpers.CleanProjectName(selectedProject.Value);
                projectFolderPath = Path.Combine(AppSettings.Default.ProjectsFolderPath, projectName);
                logService.LogInfo($"Updated Project Folder Path: {projectFolderPath}");

                // Проверяем наличие JSON-файла
                logService.LogInfo("Calling LoadAssetsFromJsonFileAsync from ProjectsComboBox_SelectionChanged");
                bool jsonLoaded = await LoadAssetsFromJsonFileAsync();
                if (!jsonLoaded) {
                    // Если JSON-файл не найден, просто логируем (без MessageBox)
                    logService.LogInfo($"No local data found for project '{projectName}'. User can connect to server to download.");
                }

                // Обновляем ветки для выбранного проекта
                isBranchInitializationInProgress = true;
                try {
                    string? apiKey = GetDecryptedApiKey();
                    List<Branch> branches = await playCanvasService.GetBranchesAsync(selectedProject.Key, apiKey ?? "", [], CancellationToken.None);
                    if (branches != null && branches.Count > 0) {
                        viewModel.Branches.Clear();
                        foreach (Branch branch in branches) {
                            viewModel.Branches.Add(branch);
                        }
                        BranchesComboBox.SelectedIndex = 0;
                    } else {
                        viewModel.Branches.Clear();
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
            logService.LogInfo($"[TexturesDataGrid_SelectionChanged] EVENT FIRED");

            // Update selection count in central control box
            UpdateSelectedTexturesCount();
            viewModel.SelectedTexture = TexturesDataGrid.SelectedItem as TextureResource;
            viewModel.ProcessTexturesCommand.NotifyCanExecuteChanged();

            // Отменяем предыдущую загрузку, если она еще выполняется
            textureLoadCancellation?.Cancel();
            textureLoadCancellation = new CancellationTokenSource();
            CancellationToken cancellationToken = textureLoadCancellation.Token;

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

                    // Update texture info
                    TextureNameTextBlock.Text = "Texture Name: " + ormTexture.Name;
                    TextureColorSpaceTextBlock.Text = "Color Space: Linear (ORM)";

                    // Load the packed KTX2 file for preview and histogram
                    try {
                        // Debounce
                        await Task.Delay(50, cancellationToken);

                        bool ktxLoaded = false;

                        if (isUsingD3D11Renderer) {
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

                        if (!ktxLoaded) {
                            await Dispatcher.InvokeAsync(() => {
                                if (cancellationToken.IsCancellationRequested) return;

                                isKtxPreviewAvailable = false;
                                TextureFormatTextBlock.Text = "Format: KTX2 (preview unavailable)";

                                // Show error message
                                logService.LogWarn($"Failed to load preview for packed ORM texture: {ormTexture.Name}");
                            });
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
                        // Обновляем информацию о текстуре сразу
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

                        // Debounce: wait a bit to see if user is still scrolling
                        await Task.Delay(50, cancellationToken);
                        logService.LogInfo($"[TextureSelection] Debounce completed for: {selectedTexture.Name}");

                        // Load conversion settings for this texture
                        logService.LogInfo($"[TextureSelection] Loading conversion settings for: {selectedTexture.Name}");
                        LoadTextureConversionSettings(selectedTexture);
                        logService.LogInfo($"[TextureSelection] Conversion settings loaded for: {selectedTexture.Name}");

                        // Check cancellation before starting heavy operations
                        cancellationToken.ThrowIfCancellationRequested();
                        logService.LogInfo($"[TextureSelection] Starting texture load for: {selectedTexture.Name}");

                        // FIXED: Separate D3D11 native KTX2 loading from PNG extraction
                        // to prevent conflicts
                        bool ktxLoaded = false;

                        if (isUsingD3D11Renderer) {
                            // D3D11 MODE: Try D3D11 native KTX2 loading (always use native when D3D11 is active)
                            logService.LogInfo($"[TextureSelection] Attempting KTX2 load for: {selectedTexture.Name}");
                            ktxLoaded = await TryLoadKtx2ToD3D11Async(selectedTexture, cancellationToken);
                            logService.LogInfo($"[TextureSelection] KTX2 load result for {selectedTexture.Name}: {ktxLoaded}");

                            if (ktxLoaded) {
                                // KTX2 loaded successfully to D3D11, still load source for histogram/info
                                // If user is in Source mode, show the PNG; otherwise just load for histogram
                                bool showInViewer = (currentPreviewSourceMode == TexturePreviewSourceMode.Source);
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
                        logService.LogInfo($"[TextureSelection] Cancelled for: {selectedTexture.Name}");
                        // Загрузка была отменена - это нормально
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
            string? ktxPath = GetExistingKtx2Path(selectedTexture.Path);
            if (ktxPath == null) {
                logger.Info($"KTX2 file not found for: {selectedTexture.Path}");
                return false;
            }

            logger.Info($"Found KTX2 file: {ktxPath}");

            try {
                // Load KTX2 directly to D3D11 (no PNG extraction)
                bool loaded = await LoadKtx2ToD3D11ViewerAsync(ktxPath);
                if (!loaded || cancellationToken.IsCancellationRequested) {
                    logger.Warn($"Failed to load KTX2 to D3D11 viewer: {ktxPath}");
                    return false;
                }

                logger.Info($"Loaded KTX2 directly to D3D11 viewer: {ktxPath}");

                await Dispatcher.InvokeAsync(() => {
                    if (cancellationToken.IsCancellationRequested) {
                        return;
                    }

                    // Save current loaded texture paths for preview renderer switching
                    currentLoadedTexturePath = selectedTexture.Path;
                    currentLoadedKtx2Path = ktxPath;

                    // Mark KTX2 preview as available
                    isKtxPreviewAvailable = true;
                    isKtxPreviewActive = true;

                    // Clear old mipmap data (we're using D3D11 native mipmaps now)
                    currentKtxMipmaps?.Clear();
                    currentMipLevel = 0;

                    // Update UI to show KTX2 is active
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
                logger.Warn(ex, $"Failed to load KTX2 preview: {ktxPath}");
                return false;
            }
        }

        /// <summary>
        /// OLD METHOD: Extract KTX2 mipmaps to PNG files and load them.
        /// Used when useD3D11NativeKtx2 = false.
        /// </summary>
        private async Task<bool> TryLoadKtx2PreviewAsync(TextureResource selectedTexture, CancellationToken cancellationToken) {
            string? ktxPath = GetExistingKtx2Path(selectedTexture.Path);
            if (ktxPath == null) {
                logger.Info($"KTX2 file not found for: {selectedTexture.Path}");
                return false;
            }

            logger.Info($"Found KTX2 file: {ktxPath}");

            try {
                // OLD METHOD: Extract to PNG files
                List<KtxMipLevel> mipmaps = await LoadKtx2MipmapsAsync(ktxPath, cancellationToken);
                if (mipmaps.Count == 0 || cancellationToken.IsCancellationRequested) {
                    logger.Warn($"Failed to extract mipmaps from KTX2: {ktxPath}");
                    return false;
                }

                logger.Info($"Extracted {mipmaps.Count} mipmaps from KTX2");

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
                logger.Warn(ex, $"Failed to load KTX2 preview: {ktxPath}");
                return false;
            }
        }

        private async Task LoadSourcePreviewAsync(TextureResource selectedTexture, CancellationToken cancellationToken, bool loadToViewer = true) {
            // Reset channel masks when loading new texture
            // BUT: Don't reset if Normal mode was auto-enabled for normal maps
            if (currentActiveChannelMask != "Normal") {
                currentActiveChannelMask = null;
                Dispatcher.Invoke(() => {
                    UpdateChannelButtonsState();
                    // Reset D3D11 renderer mask
                    if (isUsingD3D11Renderer && D3D11TextureViewer?.Renderer != null) {
                        D3D11TextureViewer.Renderer.SetChannelMask(0xFFFFFFFF);
                        D3D11TextureViewer.Renderer.RestoreOriginalGamma();
                    }
                });
            } else {
                logger.Info("LoadSourcePreviewAsync: Skipping mask reset - Normal mode is active for normal map");
            }

            // Store currently selected texture for sRGB detection
            currentSelectedTexture = selectedTexture;

            string? texturePath = selectedTexture.Path;
            if (string.IsNullOrEmpty(texturePath)) {
                return;
            }

            if (imageCache.TryGetValue(texturePath, out BitmapImage? cachedImage)) {
                await Dispatcher.InvokeAsync(() => {
                    if (cancellationToken.IsCancellationRequested) {
                        return;
                    }

                    // Save current loaded texture path for preview renderer switching
                    currentLoadedTexturePath = texturePath;

                    originalFileBitmapSource = cachedImage;
                    isSourcePreviewAvailable = true;

                    // Only show in viewer if loadToViewer=true (not when KTX2 is already loaded)
                    if (loadToViewer && currentPreviewSourceMode == TexturePreviewSourceMode.Source) {
                        originalBitmapSource = cachedImage;
                        ShowOriginalImage();
                    }

                    // Always update histogram when source image is loaded (even if showing KTX2)
                    // Use the source bitmap for histogram calculation
                    _ = UpdateHistogramAsync(cachedImage);

                    UpdatePreviewSourceControls();
                });

                return;
            }

            BitmapImage? thumbnailImage = LoadOptimizedImage(texturePath, ThumbnailSize);
            if (thumbnailImage == null) {
                logService.LogInfo($"Error loading thumbnail for texture: {selectedTexture.Name}");
                return;
            }

            await Dispatcher.InvokeAsync(() => {
                if (cancellationToken.IsCancellationRequested) {
                    return;
                }

                // Save current loaded texture path for preview renderer switching
                currentLoadedTexturePath = texturePath;

                originalFileBitmapSource = thumbnailImage;
                isSourcePreviewAvailable = true;

                // Only show in viewer if loadToViewer=true (not when KTX2 is already loaded)
                if (loadToViewer && currentPreviewSourceMode == TexturePreviewSourceMode.Source) {
                    originalBitmapSource = thumbnailImage;
                    ShowOriginalImage();
                }

                // Always update histogram when source image is loaded (even if showing KTX2)
                // Use the source bitmap for histogram calculation
                _ = UpdateHistogramAsync(thumbnailImage);

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

                        // Only show in viewer if loadToViewer=true (not when KTX2 is already loaded)
                        if (loadToViewer && currentPreviewSourceMode == TexturePreviewSourceMode.Source) {
                            originalBitmapSource = bitmapImage;
                            ShowOriginalImage();
                            // НЕ применяем fitZoom для full resolution - это обновление кэша, зум уже установлен
                        }

                        // Always update histogram when full-resolution image is loaded (even if showing KTX2)
                        // This replaces the thumbnail-based histogram with accurate full-image data
                        _ = UpdateHistogramAsync(bitmapImage);

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

            // КРИТИЧНО: Применяем SanitizePath к входному пути!
            // Без этого File.Exists() не найдёт файл если путь содержит \n
            sourcePath = PathSanitizer.SanitizePath(sourcePath);

            string? directory = Path.GetDirectoryName(sourcePath);
            if (string.IsNullOrEmpty(directory)) {
                return null;
            }

            string baseName = Path.GetFileNameWithoutExtension(sourcePath);
            string normalizedBaseName = TextureResource.ExtractBaseTextureName(baseName);

            // Ищем .ktx2 и .ktx файлы
            foreach (var extension in new[] { ".ktx2", ".ktx" }) {
                string directPath = Path.Combine(directory, baseName + extension);
                if (File.Exists(directPath)) {
                    return directPath;
                }

                if (!string.IsNullOrWhiteSpace(normalizedBaseName) &&
                    !normalizedBaseName.Equals(baseName, StringComparison.OrdinalIgnoreCase)) {
                    string normalizedDirectPath = Path.Combine(directory, normalizedBaseName + extension);
                    if (File.Exists(normalizedDirectPath)) {
                        return normalizedDirectPath;
                    }
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

            try {
                // Ищем как .ktx2 так и .ktx файлы
                foreach (var pattern in new[] { "*.ktx2", "*.ktx" }) {
                    foreach (string file in Directory.EnumerateFiles(directory, pattern, searchOption)) {
                        DateTime writeTime = File.GetLastWriteTimeUtc(file);

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

            // Возвращаем ТОЛЬКО точное совпадение, НЕ возвращаем самый новый файл!
            // Если нет совпадения - вернём null, а не случайный ktx2 файл
            return bestMatch;
        }

        private static int GetKtxMatchScore(string candidateName, string baseName, string normalizedBaseName) {
            if (string.IsNullOrWhiteSpace(candidateName)) {
                return -1;
            }

            // ТОЛЬКО точные совпадения полного имени файла (без расширения)
            // texture_gloss.ktx2 должен матчить ТОЛЬКО texture_gloss.png
            // НЕ должен матчить texture_normal.ktx2 или texture.ktx2

            // 1. Точное совпадение полного имени - наивысший приоритет
            if (candidateName.Equals(baseName, StringComparison.OrdinalIgnoreCase)) {
                return 500;
            }

            // 2. Точное совпадение нормализованного имени (без суффикса типа)
            // Например: texture_gloss.png → нормализуется в "texture"
            // Матчит texture.ktx2, но НЕ матчит texture_normal.ktx2
            if (!string.IsNullOrWhiteSpace(normalizedBaseName) &&
                candidateName.Equals(normalizedBaseName, StringComparison.OrdinalIgnoreCase)) {
                return 450;
            }

            // УБРАНЫ все fallback на частичные совпадения!
            // Больше НЕ ищем файлы которые просто "содержат" baseName
            // Это предотвращает ложные совпадения типа texture_normal при поиске texture_gloss

            return -1;
        }

        private async Task<List<KtxMipLevel>> LoadKtx2MipmapsAsync(string ktxPath, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();

            FileInfo fileInfo = new(ktxPath);
            DateTime lastWriteTimeUtc = fileInfo.LastWriteTimeUtc;

            if (ktxPreviewCache.TryGetValue(ktxPath, out KtxPreviewCacheEntry? cacheEntry) && cacheEntry.LastWriteTimeUtc == lastWriteTimeUtc) {
                return cacheEntry.Mipmaps;
            }

            return await ExtractKtxMipmapsAsync(ktxPath, lastWriteTimeUtc, cancellationToken);
        }

        private async Task<List<KtxMipLevel>> ExtractKtxMipmapsAsync(string ktxPath, DateTime lastWriteTimeUtc, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();

            // Используем ktx (из KTX-Software) вместо basisu для извлечения мипмапов из KTX2
            string ktxToolPath = GetKtxToolExecutablePath();
            string tempDirectory = Path.Combine(Path.GetTempPath(), "PlaycanvasAssetProcessor", "Preview", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDirectory);

            try {
                if (!string.IsNullOrEmpty(Path.GetDirectoryName(ktxToolPath)) && !File.Exists(ktxToolPath)) {
                    throw new FileNotFoundException($"Не удалось найти исполняемый файл ktx по пути '{ktxToolPath}'. Убедитесь что KTX-Software установлен.", ktxToolPath);
                }

                // ktx v4.0 синтаксис: ktx extract [options] <input-file> <output>
                // Когда используется --level all, output интерпретируется как ДИРЕКТОРИЯ
                // ktx создаст файлы: output/output_level0.png, output/output_level1.png, ...
                // Поэтому передаём tempDirectory напрямую как output path
                string outputBaseName = Path.Combine(tempDirectory, "mip");

                ProcessStartInfo startInfo = new() {
                    FileName = ktxToolPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                // ktx extract --level all --transcode rgba8 input.ktx2 output_base_name
                // --level all: извлекает все уровни мипмапов
                // --transcode rgba8: декодирует Basis/UASTC в RGBA8 перед извлечением
                // Создаст файлы: output_base_name_level0.png, output_base_name_level1.png, ...
                startInfo.ArgumentList.Add("extract");
                startInfo.ArgumentList.Add("--level");
                startInfo.ArgumentList.Add("all");
                startInfo.ArgumentList.Add("--transcode");
                startInfo.ArgumentList.Add("rgba8");
                startInfo.ArgumentList.Add(ktxPath);
                startInfo.ArgumentList.Add(outputBaseName);

                // Логируем точную команду для диагностики
                string commandLine = $"{ktxToolPath} {string.Join(" ", startInfo.ArgumentList.Select(arg => arg.Contains(' ') ? $"\"{arg}\"" : arg))}";
                logger.Info($"Executing command: {commandLine}");
                logger.Info($"Working directory: {tempDirectory}");
                logger.Info($"Input file exists: {File.Exists(ktxPath)}");
                logger.Info($"Input file size: {new FileInfo(ktxPath).Length} bytes");
                logger.Info($"Output base path: {outputBaseName}");
                logger.Info($"Output directory exists: {Directory.Exists(tempDirectory)}");

                using Process process = new() { StartInfo = startInfo };
                try {
                    if (!process.Start()) {
                        throw new InvalidOperationException("Не удалось запустить ktx для извлечения предпросмотра KTX2.");
                    }
                } catch (Win32Exception ex) {
                    throw new InvalidOperationException("Не удалось запустить ktx для извлечения предпросмотра KTX2. Проверьте что KTX-Software установлен и доступен в PATH.", ex);
                } catch (Exception ex) {
                    throw new InvalidOperationException("Не удалось запустить ktx для извлечения предпросмотра KTX2.", ex);
                }

                string standardOutput = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                string standardError = await process.StandardError.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);

                // Логируем результат даже при успехе для диагностики
                logger.Info($"ktx extract completed. ExitCode={process.ExitCode}, StdOut={standardOutput}, StdErr={standardError}");

                if (process.ExitCode != 0) {
                    logger.Warn($"ktx exited with code {process.ExitCode} while processing {ktxPath}");
                    throw new InvalidOperationException($"ktx exited with code {process.ExitCode} while preparing preview.");
                }

                cancellationToken.ThrowIfCancellationRequested();

                // ktx extract создаёт поддиректорию с именем outputBaseName
                // Файлы будут в формате: mip/mip_level0.png, mip/mip_level1.png, ...
                string extractedDirectory = outputBaseName;

                // Логируем какие файлы были созданы
                string[] allFiles = Directory.Exists(extractedDirectory)
                    ? Directory.GetFiles(extractedDirectory, "*.*", SearchOption.TopDirectoryOnly)
                    : Array.Empty<string>();
                logger.Info($"Files in {extractedDirectory}: {string.Join(", ", allFiles.Select(Path.GetFileName))}");

                string[] pngFiles = Directory.Exists(extractedDirectory)
                    ? Directory.GetFiles(extractedDirectory, "*.png", SearchOption.TopDirectoryOnly)
                    : Array.Empty<string>();
                if (pngFiles.Length == 0) {
                    logger.Warn($"ktx did not create PNG files. Total files in directory: {allFiles.Length}");
                    logger.Warn($"Directory exists: {Directory.Exists(extractedDirectory)}");
                    throw new InvalidOperationException("ktx did not generate PNG files for KTX2 preview.");
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
            // KTX2 preview отключён - мы используем только toktx для создания KTX2, а не для preview
            // basisu больше не используется в проекте
            return "basisu"; // Возвращаем значение по умолчанию, но метод больше не должен вызываться
        }

        private string GetKtxToolExecutablePath() {
            // Используем утилиту ktx из KTX-Software для извлечения мипмапов из KTX2
            // Загружаем путь из настроек, если не указан - используем "ktx" из PATH
            var settings = TextureConversionSettingsManager.LoadSettings();
            return string.IsNullOrWhiteSpace(settings.KtxExecutablePath) ? "ktx" : settings.KtxExecutablePath;
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
                logService.LogError($"Error loading optimized image from {path}: {ex.Message}");
                return null;
            }
        }

        // Кэшированный конвертер для избежания создания нового экземпляра при каждой перерисовке строки
        private static readonly TextureTypeToBackgroundConverter textureTypeConverter = new();

        private void TexturesDataGrid_LoadingRow(object? sender, DataGridRowEventArgs? e) {
            // Пропускаем инициализацию во время сортировки для ускорения
            if (isSorting) {
                return;
            }

            if (e?.Row?.DataContext is TextureResource texture) {
                // Initialize conversion settings for the texture if not already set
                // Проверяем только один раз, чтобы не вызывать тяжелые операции при каждой перерисовке
                if (string.IsNullOrEmpty(texture.CompressionFormat)) {
                    InitializeTextureConversionSettings(texture);
                }

                // НЕ устанавливаем цвет фона здесь - он уже установлен через Style в XAML
                // Это предотвращает лишние операции при каждой перерисовке строки во время сортировки
                // Цвет фона управляется через DataTrigger в DataGrid.RowStyle
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

        /// <summary>
        /// Оптимизированный обработчик сортировки для TexturesDataGrid
        /// </summary>
        private void TexturesDataGrid_Sorting(object? sender, DataGridSortingEventArgs e) {
            OptimizeDataGridSorting(TexturesDataGrid, e);
        }

        /// <summary>
        /// Оптимизированный обработчик сортировки для ModelsDataGrid
        /// </summary>
        private void ModelsDataGrid_Sorting(object? sender, DataGridSortingEventArgs e) {
            OptimizeDataGridSorting(ModelsDataGrid, e);
        }

        /// <summary>
        /// Оптимизированный обработчик сортировки для MaterialsDataGrid
        /// </summary>
        private void MaterialsDataGrid_Sorting(object? sender, DataGridSortingEventArgs e) {
            OptimizeDataGridSorting(MaterialsDataGrid, e);
        }

        /// <summary>
        /// Универсальный оптимизированный метод сортировки для DataGrid
        /// Использует DeferRefresh для отложенного обновления UI во время сортировки
        /// </summary>
        private void OptimizeDataGridSorting(DataGrid dataGrid, DataGridSortingEventArgs e) {
            try {
                if (dataGrid == null || e == null || e.Column == null) {
                    return;
                }

                e.Handled = true;
                
                if (dataGrid.ItemsSource == null) {
                    return;
                }

                ICollectionView dataView = CollectionViewSource.GetDefaultView(dataGrid.ItemsSource);
                if (dataView == null) {
                    return;
                }
                
                ListSortDirection direction = e.Column.SortDirection == ListSortDirection.Ascending 
                    ? ListSortDirection.Descending 
                    : ListSortDirection.Ascending;
                
                string sortMemberPath = e.Column.SortMemberPath;
                if (string.IsNullOrEmpty(sortMemberPath)) {
                    // Если SortMemberPath не указан, пытаемся извлечь имя свойства из Binding
                    if (e.Column is DataGridBoundColumn boundColumn && boundColumn.Binding is Binding binding) {
                        sortMemberPath = binding.Path?.Path ?? "";
                    }
                    
                    // Если все еще пусто, не сортируем эту колонку
                    if (string.IsNullOrEmpty(sortMemberPath)) {
                        e.Handled = false; // Разрешаем стандартную сортировку DataGrid
                        return;
                    }
                }
                
                // Устанавливаем флаг сортировки, чтобы LoadingRow не выполнял тяжелые операции
                isSorting = true;
                
                // Сохраняем оригинальные стили и LayoutTransform для TexturesDataGrid
                // Это нужно для временного отключения DataTrigger и LayoutTransform во время сортировки
                Style? originalRowStyle = null;
                System.Windows.Media.Transform? originalLayoutTransform = null;
                bool isTexturesGrid = dataGrid == TexturesDataGrid;
                
                if (isTexturesGrid) {
                    originalRowStyle = dataGrid.RowStyle;
                    originalLayoutTransform = dataGrid.LayoutTransform;
                }
                
                try {
                    // Отключаем обновления визуального дерева для максимальной производительности
                    dataGrid.BeginInit();
                    
                    // Для TexturesDataGrid временно упрощаем стили и отключаем LayoutTransform
                    // Это критично для производительности - предотвращает вычисление DataTrigger для каждой строки
                    if (isTexturesGrid && originalRowStyle != null) {
                        // Создаем упрощенный стиль без DataTrigger (только ContextMenu)
                        var simpleRowStyle = new Style(typeof(DataGridRow));
                        var contextMenuSetter = originalRowStyle.Setters.OfType<Setter>()
                            .FirstOrDefault(s => s.Property == DataGridRow.ContextMenuProperty);
                        if (contextMenuSetter != null) {
                            // Если ContextMenu задан через Setter, используем его значение
                            simpleRowStyle.Setters.Add(new Setter(DataGridRow.ContextMenuProperty, contextMenuSetter.Value));
                        } else {
                            // Если ContextMenu не найден в Setters, пытаемся получить из ресурсов
                            var contextMenuResource = dataGrid.TryFindResource("TextureRowContextMenu");
                            if (contextMenuResource != null) {
                                simpleRowStyle.Setters.Add(new Setter(DataGridRow.ContextMenuProperty, contextMenuResource));
                            }
                        }
                        dataGrid.RowStyle = simpleRowStyle;
                        
                        // Отключаем LayoutTransform для предотвращения пересчета трансформаций
                        dataGrid.LayoutTransform = null;
                    }
                    
                    try {
                        // Используем DeferRefresh для отложенного обновления - это критично
                        // DeferRefresh предотвращает множественные обновления UI во время сортировки
                        using (dataView.DeferRefresh()) {
                            dataView.SortDescriptions.Clear();
                            dataView.SortDescriptions.Add(new SortDescription(sortMemberPath, direction));
                        }
                        
                        e.Column.SortDirection = direction;
                    } finally {
                        // Восстанавливаем оригинальные стили и LayoutTransform СИНХРОННО перед EndInit
                        // Это предотвращает мерцание, так как все изменения применяются за один раз
                        if (isTexturesGrid) {
                            if (originalRowStyle != null) {
                                dataGrid.RowStyle = originalRowStyle;
                            }
                            if (originalLayoutTransform != null) {
                                dataGrid.LayoutTransform = originalLayoutTransform;
                            }
                        }
                        
                        dataGrid.EndInit();
                        
                        // Сбрасываем флаг асинхронно с низким приоритетом
                        Dispatcher.BeginInvoke(() => {
                            isSorting = false;
                        }, System.Windows.Threading.DispatcherPriority.Background);
                    }
                } finally {
                    // Дополнительная защита на случай ошибки
                    if (isSorting) {
                        Dispatcher.BeginInvoke(() => {
                            isSorting = false;
                        }, System.Windows.Threading.DispatcherPriority.Background);
                    }
                }
            } catch (Exception ex) {
                logger.Error(ex, "Error in OptimizeDataGridSorting");
                logService.LogError($"Error in OptimizeDataGridSorting: {ex.Message}");
                // Не обрабатываем событие, позволяем DataGrid использовать стандартную сортировку
                e.Handled = false;
                isSorting = false;
            }
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs? e) {
            // Отменяем все активные операции перед закрытием
            try {
                cancellationTokenSource?.Cancel();
                textureLoadCancellation?.Cancel();

                // Даём задачам немного времени на корректную отмену
                System.Threading.Thread.Sleep(100);

                cancellationTokenSource?.Dispose();
                textureLoadCancellation?.Dispose();

                // Освобождаем PlayCanvasService
                playCanvasService?.Dispose();
            } catch (Exception ex) {
                logger.Error(ex, "Error canceling operations during window closing");
            }

            SaveCurrentSettings();
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e) {
            cancellationTokenSource?.Cancel();
            cancellationTokenSource = new CancellationTokenSource();
        }

        private void Setting(object? sender, RoutedEventArgs e) {
            SettingsWindow settingsWindow = new();
            // Subscribe to preview renderer changes
            settingsWindow.OnPreviewRendererChanged += HandlePreviewRendererChanged;
            settingsWindow.ShowDialog();
            // Unsubscribe after window closes
            settingsWindow.OnPreviewRendererChanged -= HandlePreviewRendererChanged;
        }

        private void HandlePreviewRendererChanged(bool useD3D11) {
            SwitchPreviewRenderer(useD3D11);
        }

        private async void SwitchPreviewRenderer(bool useD3D11) {
            if (useD3D11) {
                // Switch to D3D11 renderer
                isUsingD3D11Renderer = true;
                D3D11TextureViewer.Visibility = Visibility.Visible;
                WpfTexturePreviewImage.Visibility = Visibility.Collapsed;
                logger.Info("Switched to D3D11 preview renderer");

                // Show mipmap controls if KTX2 is available
                if (isKtxPreviewAvailable && currentPreviewSourceMode == TexturePreviewSourceMode.Ktx2) {
                    ShowMipmapControls();
                }

                // Reload current texture to D3D11 based on current preview source mode
                if (currentPreviewSourceMode == TexturePreviewSourceMode.Ktx2 && !string.IsNullOrEmpty(currentLoadedKtx2Path)) {
                    // Reload KTX2
                    try {
                        await LoadKtx2ToD3D11ViewerAsync(currentLoadedKtx2Path);
                        logger.Info($"Reloaded KTX2 to D3D11 viewer: {currentLoadedKtx2Path}");
                    } catch (Exception ex) {
                        logger.Error(ex, "Failed to reload KTX2 to D3D11 viewer");
                    }
                } else if (currentPreviewSourceMode == TexturePreviewSourceMode.Source && originalFileBitmapSource != null) {
                    // Reload Source (PNG) to D3D11
                    try {
                        bool isSRGB = IsSRGBTexture(currentSelectedTexture);
                        LoadTextureToD3D11Viewer(originalFileBitmapSource, isSRGB);
                        logger.Info($"Reloaded Source PNG to D3D11 viewer, sRGB={isSRGB}");
                    } catch (Exception ex) {
                        logger.Error(ex, "Failed to reload Source PNG to D3D11 viewer");
                    }
                }
            } else {
                // Switch to WPF Image renderer (legacy mode)
                isUsingD3D11Renderer = false;
                D3D11TextureViewer.Visibility = Visibility.Collapsed;
                WpfTexturePreviewImage.Visibility = Visibility.Visible;
                logger.Info("Switched to WPF preview renderer");

                // Show mipmap controls if KTX2 is available (WPF uses PNG extraction)
                if (isKtxPreviewAvailable) {
                    ShowMipmapControls();
                }

                // Load current texture to WPF Image
                if (!string.IsNullOrEmpty(currentLoadedTexturePath) && File.Exists(currentLoadedTexturePath)) {
                    try {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.UriSource = new Uri(currentLoadedTexturePath, UriKind.Absolute);
                        bitmap.EndInit();
                        bitmap.Freeze();

                        WpfTexturePreviewImage.Source = PrepareForWPFDisplay(bitmap);
                        logger.Info($"Loaded source texture to WPF Image: {currentLoadedTexturePath}");
                    } catch (Exception ex) {
                        logger.Error(ex, "Failed to load texture to WPF Image");
                        WpfTexturePreviewImage.Source = null;
                    }
                } else {
                    logger.Warn("No source texture path available for WPF preview");
                    WpfTexturePreviewImage.Source = null;
                }
            }
        }

        private async void GetListAssets(object sender, RoutedEventArgs e) {
            try {
                CancelButton.IsEnabled = true;
                if (cancellationTokenSource != null) {
                    await TryConnect(cancellationTokenSource.Token);
                }
            } catch (Exception ex) {
                MessageBox.Show($"Error in Get ListAssets: {ex.Message}");
                logService.LogError($"Error in Get List Assets: {ex}");
            } finally {
                CancelButton.IsEnabled = false;
            }
        }

        private async void Connect(object? sender, RoutedEventArgs? e) {
            CancellationToken cancellationToken = cancellationTokenSource.Token;

            if (string.IsNullOrEmpty(AppSettings.Default.PlaycanvasApiKey) || string.IsNullOrEmpty(AppSettings.Default.UserName)) {
                MessageBox.Show("Please set your Playcanvas API key, and Username in the settings window.");
                SettingsWindow settingsWindow = new();
                // Subscribe to preview renderer changes
                settingsWindow.OnPreviewRendererChanged += HandlePreviewRendererChanged;
                settingsWindow.ShowDialog();
                // Unsubscribe after window closes
                settingsWindow.OnPreviewRendererChanged -= HandlePreviewRendererChanged;
                return; // Прерываем выполнение Connect, если данные не заполнены
            } else {
                try {
                    string? apiKey = GetDecryptedApiKey();
                    if (string.IsNullOrEmpty(apiKey)) {
                        throw new Exception("Failed to decrypt API key");
                    }

                    userName = AppSettings.Default.UserName.ToLower();
                    userID = await playCanvasService.GetUserIdAsync(userName, apiKey, cancellationToken);
                    if (string.IsNullOrEmpty(userID)) {
                        throw new Exception("User ID is null or empty");
                    } else {
                        await Dispatcher.InvokeAsync(() => UpdateConnectionStatus(true, $"by userID: {userID}"));
                    }

                    Dictionary<string, string> projectsDict = await playCanvasService.GetProjectsAsync(userID, apiKey, [], cancellationToken);
                    if (projectsDict != null && projectsDict.Count > 0) {
                        string lastSelectedProjectId = AppSettings.Default.LastSelectedProjectId;

                        viewModel.Projects.Clear();
                        foreach (KeyValuePair<string, string> project in projectsDict) {
                            viewModel.Projects.Add(project);
                        }

                        // Устанавливаем флаг чтобы избежать двойной загрузки через SelectionChanged
                        isProjectInitializationInProgress = true;
                        try {
                            if (!string.IsNullOrEmpty(lastSelectedProjectId) && projectsDict.ContainsKey(lastSelectedProjectId)) {
                                ProjectsComboBox.SelectedValue = lastSelectedProjectId;
                            } else {
                                ProjectsComboBox.SelectedIndex = 0;
                            }
                        } finally {
                            isProjectInitializationInProgress = false;
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
                logger.Info("CheckProjectState: Starting");
                logService.LogInfo("CheckProjectState: Starting project state check");
                
                if (string.IsNullOrEmpty(projectFolderPath) || string.IsNullOrEmpty(projectName)) {
                    logger.Warn("CheckProjectState: projectFolderPath or projectName is empty");
                    logService.LogInfo("CheckProjectState: projectFolderPath or projectName is empty - setting to NeedsDownload");
                    UpdateConnectionButton(ConnectionState.NeedsDownload);
                    return;
                }

                // Проверяем наличие assets_list.json
                string assetsListPath = Path.Combine(projectFolderPath, "assets_list.json");
                logger.Info($"CheckProjectState: Checking for assets_list.json at {assetsListPath}");

                if (!File.Exists(assetsListPath)) {
                    // Проект не скачан - нужна загрузка
                    logger.Info("CheckProjectState: Project not downloaded yet - assets_list.json not found");
                    logService.LogInfo("Project not downloaded yet - assets_list.json not found");
                    UpdateConnectionButton(ConnectionState.NeedsDownload);
                    return;
                }

                // Проект скачан, загружаем локальные данные
                logService.LogInfo("Project found, loading local assets...");
                logger.Info("CheckProjectState: Loading local assets...");
                await LoadAssetsFromJsonFileAsync();

                // Проверяем hash для определения обновлений
                logService.LogInfo("Checking for updates...");
                logger.Info("CheckProjectState: Checking for updates on server");
                bool hasUpdates = await CheckForUpdates();

                if (hasUpdates) {
                    // Есть обновления на сервере
                    logger.Info("CheckProjectState: Updates available on server - setting button to Download");
                    logService.LogInfo("CheckProjectState: Updates available on server");
                    UpdateConnectionButton(ConnectionState.NeedsDownload);
                } else {
                    // Проект актуален
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
                List<PlayCanvasAssetSummary> serverSummaries = [];
                string? apiKey = GetDecryptedApiKey();
                await foreach (PlayCanvasAssetSummary asset in playCanvasService.GetAssetsAsync(selectedProjectId, selectedBranchId, apiKey ?? "", CancellationToken.None)) {
                    serverSummaries.Add(asset);
                }
                JArray serverData = new();
                foreach (PlayCanvasAssetSummary asset in serverSummaries) {
                    serverData.Add(JToken.Parse(asset.ToJsonString()));
                }

                // Сравниваем hash или количество ассетов
                string localHash = ComputeHash(localJson);
                string serverHash = ComputeHash(serverData.ToString());

                bool hasChanges = localHash != serverHash;

                if (hasChanges) {
                    logService.LogInfo($"Project has updates: local hash {localHash.Substring(0, 8)}... != server hash {serverHash.Substring(0, 8)}...");
                } else {
                    logService.LogInfo("Project is up to date");
                }

                return hasChanges;
            } catch (Exception ex) {
                logService.LogError($"Error checking for updates: {ex.Message}");
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

                string? apiKey = GetDecryptedApiKey();
                List<Branch> branchesList = await playCanvasService.GetBranchesAsync(projectId, apiKey ?? "", [], cancellationToken);
                if (branchesList != null && branchesList.Count > 0) {
                    viewModel.Branches.Clear();
                    foreach (Branch branch in branchesList) {
                        viewModel.Branches.Add(branch);
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
                    viewModel.Branches.Clear();
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

                logService.LogInfo($"Updated Project Folder Path: {projectFolderPath}");
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

        /// <summary>
        /// Обработчик изменения масштаба таблиц - принудительно обновляет layout для корректного растяжения колонок
        /// </summary>
        private void TableScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            // Принудительно обновляем layout DataGrid-ов для пересчёта Width="*" колонок при изменении ScaleTransform
            ForceDataGridReflow(TexturesDataGrid);
            ForceDataGridReflow(ModelsDataGrid);
            ForceDataGridReflow(MaterialsDataGrid);
        }

        /// <summary>
        /// Пересчитывает layout DataGrid с учётом ScaleTransform, принудительно обновляя "звёздочные" колонки.
        /// </summary>
        private static void ForceDataGridReflow(DataGrid? dataGrid) {
            if (dataGrid == null || !dataGrid.IsLoaded) {
                return;
            }

            dataGrid.Dispatcher.InvokeAsync(() => {
                // Сбрасываем измерения, чтобы новая ScaleTransform корректно распределила доступное пространство
                dataGrid.InvalidateMeasure();
                dataGrid.InvalidateArrange();
                dataGrid.UpdateLayout();

                var starColumns = dataGrid.Columns
                    .Where(c => c.Visibility == Visibility.Visible && c.Width.IsStar)
                    .Select(c => new {
                        Column = c,
                        StarValue = c.Width.Value,
                        DisplayIndex = c.DisplayIndex
                    })
                    .ToList();

                if (starColumns.Count == 0) {
                    return;
                }

                // Полностью скрываем "звёздочные" колонки и возвращаем их обратно —
                // это повторяет ручной workaround (скрыть/показать колонку),
                // который гарантированно заставляет DataGrid пересчитать размеры шапки и строк.
                foreach (var entry in starColumns) {
                    entry.Column.Visibility = Visibility.Collapsed;
                }

                dataGrid.UpdateLayout();

                foreach (var entry in starColumns) {
                    entry.Column.Visibility = Visibility.Visible;
                    entry.Column.DisplayIndex = entry.DisplayIndex;
                    entry.Column.Width = new DataGridLength(entry.StarValue, DataGridLengthUnitType.Star);
                }

                dataGrid.UpdateLayout();
            }, DispatcherPriority.Background);
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

                    // Debug: Log texture map tokens from JSON
                    var materialName = json["name"]?.ToString() ?? "Unknown";
                    logger.Debug($"Parsing material '{materialName}' from JSON:");
                    logger.Debug($"  aoMap token: {data["aoMap"]?.ToString() ?? "null"}");
                    logger.Debug($"  glossMap token: {data["glossMap"]?.ToString() ?? "null"}");
                    logger.Debug($"  metalnessMap token: {data["metalnessMap"]?.ToString() ?? "null"}");
                    logger.Debug($"  specularMap token: {data["specularMap"]?.ToString() ?? "null"}");
                    logger.Debug($"  useMetalness: {data["useMetalness"]?.ToString() ?? "null"}");

                    var materialResource = new MaterialResource {
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

                    // Debug: Log parsed MapIds
                    logger.Debug($"  Parsed MapIds for '{materialName}':");
                    logger.Debug($"    AOMapId: {materialResource.AOMapId?.ToString() ?? "null"}");
                    logger.Debug($"    GlossMapId: {materialResource.GlossMapId?.ToString() ?? "null"}");
                    logger.Debug($"    MetalnessMapId: {materialResource.MetalnessMapId?.ToString() ?? "null"}");
                    logger.Debug($"    SpecularMapId: {materialResource.SpecularMapId?.ToString() ?? "null"}");

                    return materialResource;
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
                    TextureResource? texture = viewModel.Textures.FirstOrDefault(t => t.ID == mapId.Value);
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

                TextureResource? texture = viewModel.Textures.FirstOrDefault(t => t.ID == mapId.Value);
                if (texture != null) {
                    ICollectionView? view = CollectionViewSource.GetDefaultView(TexturesDataGrid.ItemsSource);
                    view?.MoveCurrentTo(texture);

                    TexturesDataGrid.SelectedItem = texture;
                    TexturesDataGrid.UpdateLayout();
                    TexturesDataGrid.ScrollIntoView(texture);
                    TexturesDataGrid.Focus();

                    logger.Info("Текстура {TextureName} (ID {TextureId}) выделена и прокручена в таблице текстур.", texture.Name, texture.ID);
                } else {
                    logger.Error("Текстура с ID {TextureId} не найдена в коллекции. Всего текстур: {TextureCount}.", mapId.Value, viewModel.Textures.Count);
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

                TextureResource? texture = viewModel.Textures.FirstOrDefault(t => t.ID == textureId.Value);
                if (texture != null) {
                    ICollectionView? view = CollectionViewSource.GetDefaultView(TexturesDataGrid.ItemsSource);
                    view?.MoveCurrentTo(texture);

                    TexturesDataGrid.SelectedItem = texture;
                    TexturesDataGrid.UpdateLayout();
                    TexturesDataGrid.ScrollIntoView(texture);
                    TexturesDataGrid.Focus();

                    logger.Info("Текстура {TextureName} (ID {TextureId}) выделена и прокручена в таблице текстур.", texture.Name, texture.ID);
                } else {
                    logger.Error("Текстура с ID {TextureId} не найдена в коллекции. Всего текстур: {TextureCount}.", textureId.Value, viewModel.Textures.Count);
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
                    var texture = viewModel.Textures.FirstOrDefault(t => t.ID == textureId.Value);
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

                // Обновление данных материала
                if (MaterialsDataGrid.SelectedItem is MaterialResource selectedMaterial) {
                    selectedMaterial.SpecularTint = true;
                    selectedMaterial.Specular = [newColor.R, newColor.G, newColor.B];
                }
            }
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

            string sanitizedFileName = localCacheService.SanitizePath(fileName);
            string fullPath = localCacheService.GetResourcePath(AppSettings.Default.ProjectsFolderPath, projectName, folderPaths, sanitizedFileName, parentId);
            logService.LogInfo($"Generated resource path: {fullPath}");
            return fullPath;
        }

        private void RecalculateIndices() {
            // Синхронное обновление индексов для избежания race condition с DataGrid
            int index = 1;
            foreach (TextureResource texture in viewModel.Textures) {
                texture.Index = index++;
                // INotifyPropertyChanged автоматически обновит строку в DataGrid
            }

            index = 1;
            foreach (ModelResource model in viewModel.Models) {
                model.Index = index++;
                // INotifyPropertyChanged автоматически обновит строку в DataGrid
            }

            index = 1;
            foreach (MaterialResource material in viewModel.Materials) {
                material.Index = index++;
                // INotifyPropertyChanged автоматически обновит строку в DataGrid
            }

            // Items.Refresh() убран - INotifyPropertyChanged на Index автоматически обновляет UI
            // Это устраняет полную перерисовку DataGrid и значительно ускоряет обновление

            // UpdateLayout() вызывается только один раз в конце через DeferUpdateLayout()
            // чтобы избежать множественных перерисовок при последовательных вызовах RecalculateIndices()
        }

        private bool _layoutUpdatePending = false;

        /// <summary>
        /// Отложенное обновление layout DataGrid для предотвращения множественных перерисовок
        /// </summary>
        private void DeferUpdateLayout() {
            if (_layoutUpdatePending) {
                return; // Уже запланировано обновление
            }

            _layoutUpdatePending = true;
            Dispatcher.InvokeAsync(() => {
                TexturesDataGrid?.UpdateLayout();
                ModelsDataGrid?.UpdateLayout();
                MaterialsDataGrid?.UpdateLayout();
                _layoutUpdatePending = false;
            }, DispatcherPriority.Loaded);
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
            logger.Info($"UpdateConnectionButton: Changing state from {currentConnectionState} to {newState}");
            logService.LogInfo($"UpdateConnectionButton: Changing state to {newState}");
            currentConnectionState = newState;

            Dispatcher.Invoke(() => {
                bool hasSelection = ProjectsComboBox.SelectedItem != null && BranchesComboBox.SelectedItem != null;
                logger.Info($"UpdateConnectionButton: hasSelection={hasSelection}, DynamicConnectionButton is null: {DynamicConnectionButton == null}");

                if (DynamicConnectionButton == null) {
                    logger.Warn("UpdateConnectionButton: DynamicConnectionButton is null!");
                    return;
                }

                switch (currentConnectionState) {
                    case ConnectionState.Disconnected:
                        DynamicConnectionButton.Content = "Connect";
                        DynamicConnectionButton.ToolTip = "Connect to PlayCanvas and load projects";
                        DynamicConnectionButton.IsEnabled = true;
                        DynamicConnectionButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 240, 240)); // Grey
                        logger.Info("UpdateConnectionButton: Set to Connect");
                        break;

                    case ConnectionState.UpToDate:
                        DynamicConnectionButton.Content = "Refresh";
                        DynamicConnectionButton.ToolTip = "Check for updates from PlayCanvas server";
                        DynamicConnectionButton.IsEnabled = hasSelection;
                        DynamicConnectionButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(173, 216, 230)); // Light blue
                        logger.Info($"UpdateConnectionButton: Set to Refresh (enabled={hasSelection})");
                        logService.LogInfo($"UpdateConnectionButton: Button set to Refresh, enabled={hasSelection}");
                        break;

                    case ConnectionState.NeedsDownload:
                        DynamicConnectionButton.Content = "Download";
                        DynamicConnectionButton.ToolTip = "Download assets from PlayCanvas (list + files)";
                        DynamicConnectionButton.IsEnabled = hasSelection;
                        DynamicConnectionButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(144, 238, 144)); // Light green
                        logger.Info($"UpdateConnectionButton: Set to Download (enabled={hasSelection})");
                        logService.LogInfo($"UpdateConnectionButton: Button set to Download, enabled={hasSelection}");
                        break;
                }
            });
        }

        /// <summary>
        /// Обработчик клика по динамической кнопке подключения
        /// </summary>
        private async void DynamicConnectionButton_Click(object sender, RoutedEventArgs e) {
            logger.Info($"DynamicConnectionButton_Click: Button clicked, current state: {currentConnectionState}");
            logService.LogInfo($"DynamicConnectionButton_Click: Button clicked, current state: {currentConnectionState}");
            
            try {
                switch (currentConnectionState) {
                    case ConnectionState.Disconnected:
                        // Подключаемся к PlayCanvas и загружаем список проектов
                        logger.Info("DynamicConnectionButton_Click: Calling ConnectToPlayCanvas");
                        logService.LogInfo("DynamicConnectionButton_Click: Calling ConnectToPlayCanvas");
                        ConnectToPlayCanvas();
                        break;

                    case ConnectionState.UpToDate:
                        // Проверяем наличие обновлений на сервере
                        logger.Info("DynamicConnectionButton_Click: Calling RefreshFromServer");
                        logService.LogInfo("DynamicConnectionButton_Click: Calling RefreshFromServer");
                        await RefreshFromServer();
                        break;

                    case ConnectionState.NeedsDownload:
                        // Скачиваем список ассетов + файлы
                        logger.Info("DynamicConnectionButton_Click: Calling DownloadFromServer");
                        logService.LogInfo("DynamicConnectionButton_Click: Calling DownloadFromServer");
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
                logService.LogError($"Error in RefreshFromServer: {ex}");
            } finally {
                DynamicConnectionButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// Скачивает список ассетов с сервера + загружает все файлы (Download button)
        /// </summary>
        private async Task DownloadFromServer() {
            try {
                logger.Info("DownloadFromServer: Starting download");
                logService.LogInfo("DownloadFromServer: Starting download from server");
                CancelButton.IsEnabled = true;
                DynamicConnectionButton.IsEnabled = false;

                if (cancellationTokenSource != null) {
                    // Загружаем список ассетов (assets_list.json) с сервера
                    logger.Info("DownloadFromServer: Loading assets list from server");
                    logService.LogInfo("DownloadFromServer: Loading assets list from server");
                    await TryConnect(cancellationTokenSource.Token);

                    // Теперь скачиваем файлы (текстуры, модели, материалы)
                    logger.Info("DownloadFromServer: Starting file downloads");
                    logService.LogInfo("DownloadFromServer: Starting file downloads");
                    await Download(null, null);
                    logger.Info("DownloadFromServer: File downloads completed");
                    logService.LogInfo("DownloadFromServer: File downloads completed");

                    // После успешной загрузки обновляем статус кнопки без перезагрузки данных
                    // НЕ вызываем CheckProjectState(), так как он перезагрузит данные из JSON и сбросит статусы
                    // Вместо этого просто проверяем наличие обновлений на сервере
                    logger.Info("DownloadFromServer: Checking for updates on server after download");
                    logService.LogInfo("DownloadFromServer: Checking for updates on server after download");
                    bool hasUpdates = await CheckForUpdates();
                    
                    if (hasUpdates) {
                        logger.Info("DownloadFromServer: Updates still available - setting button to Download");
                        UpdateConnectionButton(ConnectionState.NeedsDownload);
                    } else {
                        logger.Info("DownloadFromServer: Project is up to date - setting button to Refresh");
                        logService.LogInfo("DownloadFromServer: Project is up to date - setting button to Refresh");
                        UpdateConnectionButton(ConnectionState.UpToDate);
                    }
                    logger.Info("DownloadFromServer: Button state updated");
                    logService.LogInfo("DownloadFromServer: Button state updated");
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
        /// Инициализация ConversionSettings менеджера и UI
        /// </summary>
        private void InitializeConversionSettings() {
            try {
                // Загружаем глобальные настройки
                globalTextureSettings ??= TextureConversionSettingsManager.LoadSettings();

                // Создаем менеджер настроек конвертации
                conversionSettingsManager = new ConversionSettingsManager(globalTextureSettings);

                // Загружаем UI элементы для ConversionSettings
                PopulateConversionSettingsUI();

                logger.Info("ConversionSettings initialized successfully");
            } catch (Exception ex) {
                logger.Error(ex, "Error initializing ConversionSettings");
                MessageBox.Show($"Error initializing conversion settings: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Заполняет UI элементы ConversionSettings (пресеты и начальные значения)
        /// </summary>
        private void PopulateConversionSettingsUI() {
            if (conversionSettingsManager == null) {
                logger.Warn("ConversionSettingsManager not initialized");
                return;
            }

            try {
                // Передаем ConversionSettingsManager в панель настроек конвертации
                // КРИТИЧНО: Панель сама загрузит пресеты из ConversionSettingsSchema
                // внутри SetConversionSettingsManager() - не дублируем код здесь!
                if (ConversionSettingsPanel != null) {
                    ConversionSettingsPanel.SetConversionSettingsManager(conversionSettingsManager);

                    // Логируем для проверки
                    logger.Info($"ConversionSettingsManager passed to panel. PresetComboBox items count: {ConversionSettingsPanel.PresetComboBox.Items.Count}");
                }

            } catch (Exception ex) {
                logger.Error(ex, "Error populating ConversionSettings UI");
                throw;
            }
        }

        /// <summary>
        /// Инициализация при запуске программы - подключается к серверу и загружает проекты
        /// Если hash локального JSON совпадает с серверным - загружает локально
        /// </summary>
        private async Task InitializeOnStartup() {
            try {
                logger.Info("=== InitializeOnStartup: Starting ===");
                logService.LogInfo("=== Initializing on startup ===");

                // Проверяем наличие API ключа и username
                if (string.IsNullOrEmpty(AppSettings.Default.PlaycanvasApiKey) ||
                    string.IsNullOrEmpty(AppSettings.Default.UserName)) {
                    logger.Info("InitializeOnStartup: No API key or username - showing Connect button");
                    logService.LogInfo("No API key or username - showing Connect button");
                    UpdateConnectionButton(ConnectionState.Disconnected);
                    return;
                }

                // Подключаемся к серверу и загружаем список проектов
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
        /// Умная загрузка ассетов: проверяет hash и загружает локально если актуально
        /// Если hash отличается - загружает с сервера
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
                string assetsListPath = Path.Combine(projectFolderPath ?? "", "assets_list.json");

                // Проверяем наличие локального JSON
                bool localFileExists = File.Exists(assetsListPath);

                if (localFileExists) {
                    logService.LogInfo($"Local assets_list.json found: {assetsListPath}");

                    // Получаем hash локального JSON
                    string localJson = await File.ReadAllTextAsync(assetsListPath);
                    string localHash = ComputeHash(localJson);
                    logService.LogInfo($"Local hash: {localHash.Substring(0, 16)}...");

                    // Получаем данные с сервера для сравнения hash
                    logService.LogInfo("Fetching assets from server to check hash...");
                    List<PlayCanvasAssetSummary> serverSummaries = [];
                    string? apiKey = GetDecryptedApiKey();
                await foreach (PlayCanvasAssetSummary asset in playCanvasService.GetAssetsAsync(selectedProjectId, selectedBranchId, apiKey ?? "", CancellationToken.None)) {
                        serverSummaries.Add(asset);
                    }
                    JArray serverData = new();
                    foreach (PlayCanvasAssetSummary asset in serverSummaries) {
                        serverData.Add(JToken.Parse(asset.ToJsonString()));
                    }
                    string serverHash = ComputeHash(serverData.ToString());
                    logService.LogInfo($"Server hash: {serverHash.Substring(0, 16)}...");

                    // Загружаем локальные данные в любом случае
                    logger.Info("SmartLoadAssets: Loading local assets...");
                    logService.LogInfo("Loading local assets...");
                    await LoadAssetsFromJsonFileAsync();

                    if (localHash == serverHash) {
                        // Hash совпадают - проект актуален
                        logger.Info("SmartLoadAssets: Hashes match! Project is up to date.");
                        logService.LogInfo("Hashes match! Project is up to date.");
                        UpdateConnectionButton(ConnectionState.UpToDate);
                    } else {
                        // Hash отличаются - есть обновления на сервере
                        logger.Info("SmartLoadAssets: Hashes differ! Updates available on server.");
                        logService.LogInfo("Hashes differ! Updates available on server.");
                        UpdateConnectionButton(ConnectionState.NeedsDownload);
                    }
                    logger.Info("SmartLoadAssets: Assets loaded successfully");
                } else {
                    // Локального файла нет - нужна загрузка
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

        /// <summary>
        /// Загружает последние настройки и подключается к серверу (старый метод)
        /// </summary>
        private async Task LoadLastSettings() {
            try {
                logger.Info("LoadLastSettings: Starting");
                userName = AppSettings.Default.UserName.ToLower();
                if (string.IsNullOrEmpty(userName)) {
                    throw new Exception("Username is null or empty");
                }

                string? apiKey = GetDecryptedApiKey();
                if (string.IsNullOrEmpty(apiKey)) {
                    throw new Exception("API key is null or empty after decryption");
                }

                CancellationToken cancellationToken = new();

                logger.Info($"LoadLastSettings: Getting user ID for {userName}");
                userID = await playCanvasService.GetUserIdAsync(userName, apiKey, cancellationToken);
                if (string.IsNullOrEmpty(userID)) {
                    throw new Exception("User ID is null or empty");
                } else {
                    UpdateConnectionStatus(true, $"by userID: {userID}");
                }
                logger.Info($"LoadLastSettings: Getting projects for user {userID}");
                Dictionary<string, string> projectsDict = await playCanvasService.GetProjectsAsync(userID, apiKey, [], cancellationToken);

                if (projectsDict != null && projectsDict.Count > 0) {
                    logger.Info($"LoadLastSettings: Found {projectsDict.Count} projects");
                    viewModel.Projects.Clear();
                    foreach (KeyValuePair<string, string> project in projectsDict) {
                        viewModel.Projects.Add(project);
                    }

                    isProjectInitializationInProgress = true;
                    try {
                        if (!string.IsNullOrEmpty(AppSettings.Default.LastSelectedProjectId) && projectsDict.ContainsKey(AppSettings.Default.LastSelectedProjectId)) {
                            ProjectsComboBox.SelectedValue = AppSettings.Default.LastSelectedProjectId;
                            logger.Info($"LoadLastSettings: Selected last project: {AppSettings.Default.LastSelectedProjectId}");
                        } else {
                            ProjectsComboBox.SelectedIndex = 0;
                            logger.Info("LoadLastSettings: Selected first project");
                        }
                    } finally {
                        isProjectInitializationInProgress = false;
                    }

                    if (ProjectsComboBox.SelectedItem != null) {
                        string projectId = ((KeyValuePair<string, string>)ProjectsComboBox.SelectedItem).Key;
                        logger.Info($"LoadLastSettings: Getting branches for project {projectId}");
                        List<Branch> branchesList = await playCanvasService.GetBranchesAsync(projectId, apiKey, [], cancellationToken);

                        if (branchesList != null && branchesList.Count > 0) {
                            logger.Info($"LoadLastSettings: Found {branchesList.Count} branches");
                            viewModel.Branches.Clear();
                            foreach (Branch branch in branchesList) {
                                viewModel.Branches.Add(branch);
                            }

                            isBranchInitializationInProgress = true;
                            try {
                                if (!string.IsNullOrEmpty(AppSettings.Default.LastSelectedBranchName)) {
                                    Branch? selectedBranch = branchesList.FirstOrDefault(b => b.Name == AppSettings.Default.LastSelectedBranchName);
                                    if (selectedBranch != null) {
                                        BranchesComboBox.SelectedValue = selectedBranch.Id;
                                        logger.Info($"LoadLastSettings: Selected last branch: {selectedBranch.Name}");
                                    } else {
                                        BranchesComboBox.SelectedIndex = 0;
                                        logger.Info("LoadLastSettings: Last branch not found, selected first branch");
                                    }
                                } else {
                                    BranchesComboBox.SelectedIndex = 0;
                                    logger.Info("LoadLastSettings: No last branch, selected first branch");
                                }
                            } finally {
                                isBranchInitializationInProgress = false;
                            }
                        }

                        // Загружаем данные с проверкой hash
                        projectName = MainWindowHelpers.CleanProjectName(((KeyValuePair<string, string>)ProjectsComboBox.SelectedItem).Value);
                        projectFolderPath = Path.Combine(AppSettings.Default.ProjectsFolderPath, projectName);
                        logger.Info($"LoadLastSettings: Project folder path set to: {projectFolderPath}");

                        // Умная загрузка: проверяем hash и загружаем локально если актуально
                        await SmartLoadAssets();
                    }
                }
            } catch (Exception ex) {
                logger.Error(ex, "Error loading last settings");
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

                texture.CompressionFormat = compression.CompressionFormat.ToString();
                texture.PresetName = ConversionSettingsPanel.PresetName ?? "(Custom)";

                logService.LogInfo($"Updated conversion settings for {texture.Name}");
            } catch (Exception ex) {
                logService.LogError($"Error updating conversion settings: {ex.Message}");
            }
        }

        private void LoadTextureConversionSettings(TextureResource texture) {
            logService.LogInfo($"[LoadTextureConversionSettings] START for: {texture.Name}");

            // КРИТИЧНО: Устанавливаем путь текущей текстуры для auto-detect normal map!
            ConversionSettingsPanel.SetCurrentTexturePath(texture.Path);

            // КРИТИЧНО: Очищаем NormalMapPath чтобы auto-detect работал для НОВОЙ текстуры!
            ConversionSettingsPanel.ClearNormalMapPath();

            // КРИТИЧНО: ВСЕГДА auto-detect preset по имени файла ПЕРЕД загрузкой настроек!
            // Это позволяет автоматически выбирать правильный preset для каждой текстуры
            var presetManager = new TextureConversion.Settings.PresetManager();
            var matchedPreset = presetManager.FindPresetByFileName(texture.Name ?? "");
            logService.LogInfo($"[LoadTextureConversionSettings] PresetManager.FindPresetByFileName returned: {matchedPreset?.Name ?? "null"}");

            if (matchedPreset != null) {
                // Нашли preset по имени файла (например "gloss" → "Gloss (Linear + Toksvig)")
                texture.PresetName = matchedPreset.Name;
                logService.LogInfo($"Auto-detected preset '{matchedPreset.Name}' for texture {texture.Name}");

                // Проверяем наличие preset в dropdown
                var dropdownItems = ConversionSettingsPanel.PresetComboBox.Items.Cast<string>().ToList();
                logService.LogInfo($"[LoadTextureConversionSettings] Dropdown contains {dropdownItems.Count} items: {string.Join(", ", dropdownItems)}");

                bool presetExistsInDropdown = dropdownItems.Contains(matchedPreset.Name);
                logService.LogInfo($"[LoadTextureConversionSettings] Preset '{matchedPreset.Name}' exists in dropdown: {presetExistsInDropdown}");

                // КРИТИЧНО: Устанавливаем preset БЕЗ триггера событий чтобы не блокировать загрузку текстуры!
                if (presetExistsInDropdown) {
                    logService.LogInfo($"[LoadTextureConversionSettings] Setting dropdown SILENTLY to preset: {matchedPreset.Name}");
                    ConversionSettingsPanel.SetPresetSilently(matchedPreset.Name);
                } else {
                    logService.LogInfo($"[LoadTextureConversionSettings] Preset '{matchedPreset.Name}' not in dropdown, setting to Custom SILENTLY");
                    ConversionSettingsPanel.SetPresetSilently("Custom");
                }
            } else {
                // Preset не найден по имени файла - используем "Custom"
                texture.PresetName = "";
                logService.LogInfo($"No preset matched for '{texture.Name}', using Custom SILENTLY");
                ConversionSettingsPanel.SetPresetSilently("Custom");
            }

            logService.LogInfo($"[LoadTextureConversionSettings] END for: {texture.Name}");

            // Загружаем default настройки для типа текстуры (если Custom)
            if (string.IsNullOrEmpty(texture.PresetName)) {
                var textureType = TextureResource.DetermineTextureType(texture.Name ?? "");
                var profile = TextureConversion.Core.MipGenerationProfile.CreateDefault(
                    MapTextureTypeToCore(textureType));

                var compression = TextureConversion.Core.CompressionSettings.CreateETC1SDefault();
                var compressionData = TextureConversion.Settings.CompressionSettingsData.FromCompressionSettings(compression);
                var mipProfileData = TextureConversion.Settings.MipProfileSettings.FromMipGenerationProfile(profile);

                ConversionSettingsPanel.LoadSettings(compressionData, mipProfileData, true, false);
                texture.CompressionFormat = compression.CompressionFormat.ToString();
            }
        }

        // Initialize compression format and preset for texture without updating UI panel
        // Оптимизировано: использует кэшированный PresetManager и откладывает проверку файлов
        private void InitializeTextureConversionSettings(TextureResource texture) {
            // Быстрая инициализация без проверки файлов - это ускоряет перерисовку таблицы
            var textureType = TextureResource.DetermineTextureType(texture.Name ?? "");
            var profile = TextureConversion.Core.MipGenerationProfile.CreateDefault(
                MapTextureTypeToCore(textureType));
            var compression = TextureConversion.Core.CompressionSettings.CreateETC1SDefault();

            texture.CompressionFormat = compression.CompressionFormat.ToString();

            // Auto-detect preset by filename if not already set
            // Используем кэшированный PresetManager для избежания создания нового при каждой инициализации
            if (string.IsNullOrEmpty(texture.PresetName)) {
                var matchedPreset = cachedPresetManager.FindPresetByFileName(texture.Name ?? "");
                texture.PresetName = matchedPreset?.Name ?? "";
            }

            // Проверку файлов откладываем - она выполняется асинхронно и не блокирует UI
            // Это критично для производительности при перерисовке таблицы
            if (!string.IsNullOrEmpty(texture.Path) && texture.CompressedSize == 0) {
                // Используем TryAdd для атомарной проверки и установки флага
                // Это предотвращает race condition при множественных вызовах метода для одной текстуры
                var lockObject = new object();
                if (texturesBeingChecked.TryAdd(texture.Path, lockObject)) {
                    // Проверяем файлы только если CompressedSize еще не установлен
                    // Используем асинхронную проверку чтобы не блокировать UI
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
                                        Dispatcher.InvokeAsync(() => {
                                            texture.CompressedSize = fileInfo.Length;
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
                            // Игнорируем ошибки при проверке файлов - это не критично для отображения
                        } finally {
                            // Удаляем текстуру из словаря после завершения проверки
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

                // Показываем MessageBox в UI потоке
                await Dispatcher.InvokeAsync(() => {
                    MessageBox.Show(resultMessage, "Processing Complete", MessageBoxButton.OK, icon);
                });

                // Загружаем превью после показа MessageBox, чтобы не блокировать UI
                if (e.Result.PreviewTexture != null && viewModel.LoadKtxPreviewCommand is IAsyncRelayCommand<TextureResource?> command) {
                    try {
                        // Выполняем команду напрямую (она уже async и не блокирует UI)
                        await command.ExecuteAsync(e.Result.PreviewTexture);
                        
                        // Обновляем UI в UI потоке после загрузки
                        await Dispatcher.InvokeAsync(() => {
                            SetPreviewSourceMode(TexturePreviewSourceMode.Ktx2, initiatedByUser: false);
                        });
                    } catch (Exception ex) {
                        logger.Warn(ex, "Ошибка при загрузке превью KTX2");
                    }
                }
            } catch (Exception ex) {
                logger.Error(ex, "Ошибка при обработке результатов конвертации");
            }
        }

        private async void ViewModel_TexturePreviewLoaded(object? sender, TexturePreviewLoadedEventArgs e) {
            try {
                await Dispatcher.InvokeAsync(() => {
                    currentLoadedTexturePath = e.Texture.Path;
                    currentLoadedKtx2Path = e.Preview.KtxPath;
                    isKtxPreviewAvailable = true;
                    isKtxPreviewActive = true;
                    currentKtxMipmaps?.Clear();
                    currentMipLevel = 0;

                    if (!isUserPreviewSelection || currentPreviewSourceMode == TexturePreviewSourceMode.Ktx2) {
                        SetPreviewSourceMode(TexturePreviewSourceMode.Ktx2, initiatedByUser: false);
                    } else {
                        UpdatePreviewSourceControls();
                    }

                    if (D3D11TextureViewer?.Renderer == null) {
                        logger.Warn("D3D11 viewer или renderer отсутствует");
                        return;
                    }

                    D3D11TextureViewer.Renderer.LoadTexture(e.Preview.TextureData);
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
                        currentActiveChannelMask = "Normal";
                        D3D11TextureViewer.Renderer.SetChannelMask(0x20);
                        D3D11TextureViewer.Renderer.Render();
                        UpdateChannelButtonsState();
                        if (!string.IsNullOrWhiteSpace(e.Preview.AutoEnableReason)) {
                            logService.LogInfo($"Auto-enabled Normal reconstruction mode for {e.Preview.AutoEnableReason}");
                        }
                    }
                });
            } catch (Exception ex) {
                logger.Error(ex, "Ошибка при обновлении превью KTX2");
            }
        }

        private static string BuildProcessingSummaryMessage(TextureProcessingResult result) {
            var resultMessage = $"Processing completed!\n\nSuccess: {result.SuccessCount}\nErrors: {result.ErrorCount}";

            if (result.ErrorCount > 0 && result.ErrorMessages.Count > 0) {
                resultMessage += "\n\nError details:";
                var errorsToShow = result.ErrorMessages.Take(10).ToList();
                foreach (var error in errorsToShow) {
                    resultMessage += $"\n• {error}";
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
                // Count existing ORM textures to generate unique name
                int ormCount = viewModel.Textures.Count(t => t is ORMTextureResource) + 1;

                // Create virtual ORM texture
                var ormTexture = new ORMTextureResource {
                    Name = $"[ORM Texture {ormCount}]",
                    TextureType = "ORM (Virtual)",
                    PackingMode = ChannelPackingMode.OGM, // Default to standard OGM mode
                    Status = "Ready to configure"
                };

                // Add to viewModel.Textures collection
                viewModel.Textures.Add(ormTexture);

                // Select the newly created ORM texture
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
                TextureResource? aoTexture = FindTextureById(material.AOMapId);
                TextureResource? glossTexture = FindTextureById(material.GlossMapId);
                TextureResource? metalnessTexture = null;

                // Debug: Log found textures
                logService.LogInfo($"Found textures: AO={aoTexture?.Name ?? "null"}, Gloss={glossTexture?.Name ?? "null"}");

                // Smart workflow detection: prefer actual texture presence over UseMetalness flag
                string workflowInfo = "";
                string mapType = ""; // Track which map type we're actually using

                // First try to find Metalness texture (modern PBR workflow)
                TextureResource? metalnessCandidate = FindTextureById(material.MetalnessMapId);
                TextureResource? specularCandidate = FindTextureById(material.SpecularMapId);

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
                ChannelPackingMode mode = DetectPackingMode(aoTexture, glossTexture, metalnessTexture);

                // If only one texture or none - don't create ORM
                if (mode == ChannelPackingMode.None) {
                    // mapType is already set by workflow detection above
                    MessageBox.Show($"Material ... textures for ORM packing.\n\n" +
                                  $"{workflowInfo}\n\n" +
                                  $"AO: {(aoTexture != null ? "✓" : "✗")}\n" +
                                  $"Gloss: {(glossTexture != null ? "✓" : "✗")}\n" +
                                  $"{mapType}: {(metalnessTexture != null ? "✓" : "✗")}\n\n" +
                                  $"At least 2 textures are required.",
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
                        TextureResource? aoTexture = FindTextureById(material.AOMapId);
                        TextureResource? glossTexture = FindTextureById(material.GlossMapId);
                        TextureResource? metalnessTexture = null;

                        // Smart workflow detection: prefer actual texture presence over UseMetalness flag
                        TextureResource? metalnessCandidate = FindTextureById(material.MetalnessMapId);
                        TextureResource? specularCandidate = FindTextureById(material.SpecularMapId);

                        if (metalnessCandidate != null) {
                            metalnessTexture = metalnessCandidate;
                        } else if (specularCandidate != null) {
                            metalnessTexture = specularCandidate;
                        }

                        // Auto-detect mode
                        ChannelPackingMode mode = DetectPackingMode(aoTexture, glossTexture, metalnessTexture);

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
                             $"✓ Created: {created}\n" +
                             $"⊘ Skipped: {skipped}\n" +
                             $"✗ Errors: {errors.Count}";

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

        // Helper methods for ORM creation
        // Finds texture by material map ID
        private TextureResource? FindTextureById(int? mapId) {
            if (mapId == null) return null;

            // Debug: Log search
            var found = viewModel.Textures.FirstOrDefault(t => t.ID == mapId.Value);
            if (found == null) {
                logService.LogWarn($"Texture with ID {mapId.Value} not found. Total textures in collection: {viewModel.Textures.Count}");
                // Log first few texture IDs for debugging
                var sampleIds = viewModel.Textures.Take(5).Select(t => $"{t.ID}({t.Name}");
                logService.LogInfo($"Sample texture IDs: {sampleIds}");
            }
            return found;
        }

        // Reads KTX2 file header to extract metadata (width, height, mip levels)
        private async Task<(int Width, int Height, int MipLevels)> GetKtx2InfoAsync(string ktx2Path) {
            return await Task.Run(() => {
                using var stream = File.OpenRead(ktx2Path);
                using var reader = new BinaryReader(stream);

                // KTX2 header structure:
                // Bytes 0-11: identifier (12 bytes) - skip
                // Bytes 12-15: vkFormat (uint32) - skip
                // Bytes 16-19: typeSize (uint32) - skip
                // Bytes 20-23: pixelWidth (uint32)
                // Bytes 24-27: pixelHeight (uint32)
                // Bytes 28-31: pixelDepth (uint32) - skip
                // Bytes 32-35: layerCount (uint32) - skip
                // Bytes 36-39: faceCount (uint32) - skip
                // Bytes 40-43: levelCount (uint32)

                reader.BaseStream.Seek(20, SeekOrigin.Begin);
                int width = (int)reader.ReadUInt32();
                int height = (int)reader.ReadUInt32();

                reader.BaseStream.Seek(40, SeekOrigin.Begin);
                int mipLevels = (int)reader.ReadUInt32();

                return (width, height, mipLevels);
            });
        }

        private ChannelPackingMode DetectPackingMode(TextureResource? ao, TextureResource? gloss, TextureResource? metallic) {
            int count = 0;
            if (ao != null) count++;
            if (gloss != null) count++;
            if (metallic != null) count++;

            // Need at least 2 textures
            if (count < 2) return ChannelPackingMode.None;

            // Determine mode
            if (ao != null && gloss != null && metallic != null) {
                return ChannelPackingMode.OGM; // R=AO, G=Gloss, B=Metallic
            } else if (ao != null && gloss != null) {
                return ChannelPackingMode.OG;  // RGB=AO, A=Gloss
            } else {
                // Other combinations - default to OGM with missing channels
                return ChannelPackingMode.OGM;
            }
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

        // Context menu handlers for model rows
        private async void ProcessSelectedModel_Click(object sender, RoutedEventArgs e) {
            try {
                var selectedModel = ModelsDataGrid.SelectedItem as ModelResource;
                if (selectedModel == null) {
                    MessageBox.Show("No model selected for processing.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (string.IsNullOrEmpty(selectedModel.Path)) {
                    MessageBox.Show("Model file path is empty.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Get settings from ModelConversionSettingsPanel
                var settings = ModelConversionSettingsPanel.GetSettings();

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

                var pipeline = new ModelConversion.Pipeline.ModelConversionPipeline(fbx2glTFPath, gltfPackPath);

                ProgressTextBlock.Text = $"Processing {selectedModel.Name}...";

                var result = await pipeline.ConvertAsync(selectedModel.Path, outputDir, settings);

                if (result.Success) {
                    logService.LogInfo($"✓ Model processed successfully");
                    logService.LogInfo($"  LOD files: {result.LodFiles.Count}");
                    logService.LogInfo($"  Manifest: {result.ManifestPath}");
                    MessageBox.Show($"Model processed successfully!\n\nLOD files: {result.LodFiles.Count}\nOutput: {outputDir}",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                } else {
                    var errors = string.Join("\n", result.Errors);
                    logService.LogError($"✗ Model processing failed:\n{errors}");
                    MessageBox.Show($"Model processing failed:\n\n{errors}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                ProgressTextBlock.Text = "Ready";
            } catch (Exception ex) {
                logService.LogError($"Error processing model: {ex.Message}");
                MessageBox.Show($"Error processing model: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ProgressTextBlock.Text = "Ready";
            }
        }

        private void UploadModel_Click(object sender, RoutedEventArgs e) {
            // TODO: Implement model upload
            MessageBox.Show("Model upload not yet implemented.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OpenModelFileLocation_Click(object sender, RoutedEventArgs e) {
            if (ModelsDataGrid.SelectedItem is ModelResource model) {
                if (!string.IsNullOrEmpty(model.Path) && File.Exists(model.Path)) {
                    var directory = Path.GetDirectoryName(model.Path);
                    if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory)) {
                        System.Diagnostics.Process.Start("explorer.exe", directory);
                    }
                } else {
                    MessageBox.Show("Model file path is invalid or file does not exist.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
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
            // Сохраняем текущую высоту ModelPreviewRow в настройки
            if (ModelPreviewRow != null) {
                double currentHeight = ModelPreviewRow.ActualHeight;
                if (currentHeight > 0 && currentHeight >= 200 && currentHeight <= 800) {
                    AppSettings.Default.ModelPreviewRowHeight = currentHeight;
                    AppSettings.Default.Save();
                }
            }
        }

        #endregion
    }
}
