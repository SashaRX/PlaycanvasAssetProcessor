using AssetProcessor.Settings;
using AssetProcessor.ViewModels;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace AssetProcessor {
    public partial class MainWindow {
        private async Task TryConnect(CancellationToken cancellationToken) {
            try {
                await viewModel.SyncProjectCommand.ExecuteAsync(cancellationToken);

                if (!string.IsNullOrEmpty(viewModel.CurrentProjectName)) {
                    projectSelectionService.UpdateProjectPath(
                        AppSettings.Default.ProjectsFolderPath,
                        new KeyValuePair<string, string>(string.Empty, viewModel.CurrentProjectName));
                }

                if (viewModel.FolderPaths != null) {
                    folderPaths = new Dictionary<int, string>(viewModel.FolderPaths);
                }
            } catch (Exception ex) {
                MessageBox.Show($"Error in TryConnect: {ex.Message}");
                logService.LogError($"Error in TryConnect: {ex}");
            }
        }

        private async Task InitializeAsync() {
            // Попробуйте загрузить данные из сохраненного JSON
            bool jsonLoaded = await LoadAssetsFromJsonFileAsync();
            if (!jsonLoaded) {
                MessageBox.Show("No saved data found. Please ensure the JSON file is available.");
            }
        }

        private async Task<bool> LoadAssetsFromJsonFileAsync() {
            if (string.IsNullOrEmpty(ProjectFolderPath) || string.IsNullOrEmpty(ProjectName)) {
                logService.LogError("Project folder path or name is null or empty");
                return false;
            }

            viewModel.ProgressValue = 0;
            viewModel.ProgressMaximum = 0;
            viewModel.ProgressText = "Loading...";

            try {
                // Use ViewModel command which handles threading correctly
                await viewModel.AssetLoading.LoadAssetsCommand.ExecuteAsync(new ViewModels.AssetLoadRequest {
                    ProjectFolderPath = ProjectFolderPath,
                    ProjectName = ProjectName,
                    ProjectsBasePath = AppSettings.Default.ProjectsFolderPath,
                    ProjectId = CurrentProjectId
                });

                // Start file watcher
                StartFileWatcher();

                // Scan KTX2 info
                ScanKtx2InfoForAllTextures();

                logService.LogInfo("[LoadAssetsFromJsonFileAsync] Completed successfully");
                return true;

            } catch (Exception ex) {
                logService.LogError($"Error loading assets: {ex.Message}");
                MessageBox.Show($"Error loading JSON file: {ex.Message}");
                return false;
            }
        }
    }
}


