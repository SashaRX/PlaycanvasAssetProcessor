using System.Windows;

namespace AssetProcessor {
    public partial class App : Application {
        protected override void OnStartup(StartupEventArgs e) {
            base.OnStartup(e);

            // Инициализация конфигурации
            var config = InitializeConfig();

            // Передайте конфигурацию в другие модули приложения
            MyApplication.Config = config;
        }
    }
}
