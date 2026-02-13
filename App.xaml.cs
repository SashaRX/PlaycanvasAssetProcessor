using AssetProcessor.Helpers;
using AssetProcessor.Services;
using AssetProcessor.Settings;
using AssetProcessor.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using System;
using System.IO.Abstractions;
using System.Net.Http;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace AssetProcessor {
    public partial class App : Application {
        public static IServiceProvider Services { get; private set; } = null!;
        private ServiceProvider? serviceProvider;

        protected override void OnStartup(StartupEventArgs e) {
            base.OnStartup(e);

            // CRITICAL: Disable WPF hardware acceleration to prevent freeze during Alt+Tab from fullscreen games
            // This is the nuclear option - WPF software rendering won't conflict with exclusive GPU access
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

            // Apply theme based on Windows settings
            ThemeHelper.ApplyTheme(this);

            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);

            serviceProvider = serviceCollection.BuildServiceProvider();
            Services = serviceProvider;

            // Initialize ImageHelper with HttpClientFactory
            var httpClientFactory = Services.GetRequiredService<IHttpClientFactory>();
            ImageHelper.Initialize(httpClientFactory);

            var mainWindow = Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e) {
            // Dispose LogService to flush pending writes
            if (Services.GetService<ILogService>() is IDisposable disposableLog) {
                disposableLog.Dispose();
            }

            // Dispose the service provider
            serviceProvider?.Dispose();

            base.OnExit(e);
        }

        private static void ConfigureServices(IServiceCollection services) {
            services.AddSingleton<AppSettings>(_ => AppSettings.Default);

            // Configure HttpClient with SocketsHttpHandler for optimal connection pooling
            services.AddHttpClient<IPlayCanvasService, PlayCanvasService>()
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                    MaxConnectionsPerServer = 50
                });

            // Configure named HttpClient for ImageHelper
            services.AddHttpClient("ImageHelper")
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                    MaxConnectionsPerServer = 50
                });

            // Configure named HttpClient for downloads
            services.AddHttpClient("Downloads")
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                    MaxConnectionsPerServer = 50
                });

            services.AddSingleton<IFileSystem, FileSystem>();
            services.AddSingleton<ILogService, LogService>();
            services.AddSingleton<IHistogramService, HistogramService>();
            services.AddSingleton<IHistogramCoordinator, HistogramCoordinator>();
            services.AddSingleton<ITextureChannelService, TextureChannelService>();
            services.AddSingleton<IDownloadService, DownloadService>();
            services.AddSingleton<ILocalCacheService, LocalCacheService>();
            services.AddSingleton<IProjectAssetService, ProjectAssetService>();
            services.AddSingleton<IAssetResourceService, AssetResourceService>();
            services.AddSingleton<ITextureConversionPipelineFactory, TextureConversionPipelineFactory>();
            services.AddSingleton<ITextureProcessingService>(sp => new TextureProcessingService(
                sp.GetRequiredService<ITextureConversionPipelineFactory>(),
                sp.GetRequiredService<ILogService>()));
            services.AddSingleton<ITexturePreviewService, TexturePreviewService>();
            services.AddSingleton<IPreviewRendererCoordinator, PreviewRendererCoordinator>();
            services.AddSingleton<IProjectSelectionService, ProjectSelectionService>();
            services.AddSingleton<IProjectSyncService, ProjectSyncService>();
            services.AddSingleton<IConnectionStateService, ConnectionStateService>();
            services.AddSingleton<IPlayCanvasCredentialsService, PlayCanvasCredentialsService>();
            services.AddSingleton<IAssetJsonParserService, AssetJsonParserService>();
            services.AddSingleton<IORMTextureService, ORMTextureService>();
            services.AddSingleton<IFileStatusScannerService, FileStatusScannerService>();
            services.AddSingleton<IKtx2InfoService, Ktx2InfoService>();
            services.AddSingleton<IMasterMaterialService, MasterMaterialService>();
            services.AddSingleton<IDataGridLayoutService, DataGridLayoutService>();
            services.AddSingleton<IAssetDownloadCoordinator>(sp => new AssetDownloadCoordinator(
                sp.GetRequiredService<IProjectSyncService>(),
                sp.GetRequiredService<ILocalCacheService>(),
                LogManager.GetLogger(nameof(AssetDownloadCoordinator))));

            // New refactored services for MainWindow
            services.AddSingleton<IProjectConnectionService, ProjectConnectionService>();
            services.AddSingleton<IAssetLoadCoordinator, AssetLoadCoordinator>();
            services.AddSingleton<IProjectFileWatcherService, ProjectFileWatcherService>();

            services.AddSingleton<TextureSelectionViewModel>(sp => {
                var logService = sp.GetRequiredService<ILogService>();
                return new TextureSelectionViewModel(logService);
            });

            services.AddSingleton<ORMTextureViewModel>(sp => {
                var ormTextureService = sp.GetRequiredService<IORMTextureService>();
                var logService = sp.GetRequiredService<ILogService>();
                return new ORMTextureViewModel(ormTextureService, logService);
            });

            services.AddSingleton<TextureConversionSettingsViewModel>(sp => {
                var logService = sp.GetRequiredService<ILogService>();
                return new TextureConversionSettingsViewModel(logService);
            });

            services.AddSingleton<AssetLoadingViewModel>(sp => {
                var logService = sp.GetRequiredService<ILogService>();
                var assetLoadCoordinator = sp.GetRequiredService<IAssetLoadCoordinator>();
                return new AssetLoadingViewModel(logService, assetLoadCoordinator);
            });

            services.AddSingleton<MaterialSelectionViewModel>(sp => {
                var assetResourceService = sp.GetRequiredService<IAssetResourceService>();
                var logService = sp.GetRequiredService<ILogService>();
                return new MaterialSelectionViewModel(assetResourceService, logService);
            });

            services.AddSingleton<MasterMaterialsViewModel>(sp => {
                var masterMaterialService = sp.GetRequiredService<IMasterMaterialService>();
                var logService = sp.GetRequiredService<ILogService>();
                return new MasterMaterialsViewModel(masterMaterialService, logService);
            });

            services.AddSingleton<MainViewModel>(sp => {
                var playCanvasService = sp.GetRequiredService<IPlayCanvasService>();
                var textureProcessingService = sp.GetRequiredService<ITextureProcessingService>();
                var localCacheService = sp.GetRequiredService<ILocalCacheService>();
                var projectSyncService = sp.GetRequiredService<IProjectSyncService>();
                var assetDownloadCoordinator = sp.GetRequiredService<IAssetDownloadCoordinator>();
                var projectSelectionService = sp.GetRequiredService<IProjectSelectionService>();
                var credentialsService = sp.GetRequiredService<IPlayCanvasCredentialsService>();
                var textureSelectionViewModel = sp.GetRequiredService<TextureSelectionViewModel>();
                var ormTextureViewModel = sp.GetRequiredService<ORMTextureViewModel>();
                var conversionSettingsViewModel = sp.GetRequiredService<TextureConversionSettingsViewModel>();
                var assetLoadingViewModel = sp.GetRequiredService<AssetLoadingViewModel>();
                var materialSelectionViewModel = sp.GetRequiredService<MaterialSelectionViewModel>();
                var masterMaterialsViewModel = sp.GetRequiredService<MasterMaterialsViewModel>();
                return new MainViewModel(
                    playCanvasService,
                    textureProcessingService,
                    localCacheService,
                    projectSyncService,
                    assetDownloadCoordinator,
                    projectSelectionService,
                    credentialsService,
                    textureSelectionViewModel,
                    ormTextureViewModel,
                    conversionSettingsViewModel,
                    assetLoadingViewModel,
                    materialSelectionViewModel,
                    masterMaterialsViewModel);
            });
            // Service facades for MainWindow (reduce constructor from 21 to 6 parameters)
            services.AddSingleton<ConnectionServiceFacade>();
            services.AddSingleton<AssetDataServiceFacade>();
            services.AddSingleton<TextureViewerServiceFacade>();

            services.AddTransient<MainWindow>();
        }
    }
}
