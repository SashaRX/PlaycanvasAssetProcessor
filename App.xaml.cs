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

namespace AssetProcessor {
    public partial class App : Application {
        public static IServiceProvider Services { get; private set; } = null!;
        private ServiceProvider? serviceProvider;

        protected override void OnStartup(StartupEventArgs e) {
            base.OnStartup(e);

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
            services.AddSingleton<IAssetJsonParserService, AssetJsonParserService>();
            services.AddSingleton<IORMTextureService, ORMTextureService>();
            services.AddSingleton<IFileStatusScannerService, FileStatusScannerService>();
            services.AddSingleton<IAssetDownloadCoordinator>(sp => new AssetDownloadCoordinator(
                sp.GetRequiredService<IProjectSyncService>(),
                sp.GetRequiredService<ILocalCacheService>(),
                LogManager.GetLogger(nameof(AssetDownloadCoordinator))));

            services.AddSingleton<MainViewModel>(sp => {
                var playCanvasService = sp.GetRequiredService<IPlayCanvasService>();
                var textureProcessingService = sp.GetRequiredService<ITextureProcessingService>();
                var localCacheService = sp.GetRequiredService<ILocalCacheService>();
                var projectSyncService = sp.GetRequiredService<IProjectSyncService>();
                var assetDownloadCoordinator = sp.GetRequiredService<IAssetDownloadCoordinator>();
                var projectSelectionService = sp.GetRequiredService<IProjectSelectionService>();
                return new MainViewModel(
                    playCanvasService,
                    textureProcessingService,
                    localCacheService,
                    projectSyncService,
                    assetDownloadCoordinator,
                    projectSelectionService);
            });
            services.AddTransient<MainWindow>();
        }
    }
}
