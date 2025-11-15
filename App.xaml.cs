using AssetProcessor.Helpers;
using AssetProcessor.Services;
using AssetProcessor.Settings;
using AssetProcessor.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Http;
using System.Windows;

namespace AssetProcessor {
    public partial class App : Application {
        public static IServiceProvider Services { get; private set; } = null!;
        protected override void OnStartup(StartupEventArgs e) {
            base.OnStartup(e);

            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);

            Services = serviceCollection.BuildServiceProvider();

            // Initialize ImageHelper with HttpClientFactory
            var httpClientFactory = Services.GetRequiredService<IHttpClientFactory>();
            ImageHelper.Initialize(httpClientFactory);

            var mainWindow = Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
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

            services.AddSingleton<ITextureProcessingService, TextureProcessingService>();

            services.AddSingleton<MainViewModel>(sp => {
                var playCanvasService = sp.GetRequiredService<IPlayCanvasService>();
                var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                var textureProcessingService = sp.GetRequiredService<ITextureProcessingService>();
                return new MainViewModel(playCanvasService, httpClientFactory, textureProcessingService);
            });
            services.AddTransient<MainWindow>();
        }
    }
}
