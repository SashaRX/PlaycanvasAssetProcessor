using AssetProcessor.Services;
using AssetProcessor.Settings;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Windows;

namespace AssetProcessor {
    public partial class App : Application {
        public IServiceProvider Services { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e) {
            base.OnStartup(e);

            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);

            Services = serviceCollection.BuildServiceProvider();

            var mainWindow = Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }

        private static void ConfigureServices(IServiceCollection services) {
            services.AddSingleton(AppSettings.Default);
            services.AddSingleton<IPlayCanvasService, PlayCanvasService>();
            services.AddTransient<MainWindow>();
        }
    }
}
