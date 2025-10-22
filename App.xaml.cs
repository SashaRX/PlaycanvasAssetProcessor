using AssetProcessor.Services;
using AssetProcessor.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;

namespace AssetProcessor {
    public partial class App : Application {
        private readonly IHost host;

        public App() {
            host = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) => {
                    // Register Services
                    services.AddSingleton<IPlayCanvasService, PlayCanvasService>();

                    // Register ViewModels
                    services.AddTransient<MainViewModel>();

                    // Register Windows
                    services.AddTransient<MainWindow>();
                })
                .Build();
        }

        protected override async void OnStartup(StartupEventArgs e) {
            await host.StartAsync();

            var mainWindow = host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();

            base.OnStartup(e);
        }

        protected override async void OnExit(ExitEventArgs e) {
            await host.StopAsync();
            host.Dispose();

            base.OnExit(e);
        }
    }
}
