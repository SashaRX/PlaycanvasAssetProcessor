using AssetProcessor.Helpers;
using AssetProcessor.Services;
using AssetProcessor.Settings;
using AssetProcessor.Upload;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace AssetProcessor {
    /// <summary>
    /// Partial class containing upload-to-cloud logic:
    /// - AutoUploadAfterExportAsync (post-export upload)
    /// - UploadToCloudButton_Click (manual upload)
    /// - Shared B2 upload pipeline (BuildUploadFilePairs, PerformB2UploadAsync)
    /// - SaveUploadRecordsAndUpdateStatusesAsync (persistence + UI status)
    /// </summary>
    public partial class MainWindow {

        /// <summary>
        /// Automatic upload after export — uploads only the specified exported files.
        /// </summary>
        private async Task AutoUploadAfterExportAsync(string contentPath, List<string> exportedFiles) {
            if (!ValidateB2Credentials()) return;

            if (exportedFiles.Count == 0) {
                MessageBox.Show("No files to upload.", "Upload", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var serverPath = Path.GetDirectoryName(Path.GetDirectoryName(contentPath));
            await PerformB2UploadAsync(exportedFiles, serverPath);
        }

        /// <summary>
        /// Manual upload button — uploads files from the last export (_lastExportedFiles).
        /// </summary>
        private async void UploadToCloudButton_Click(object sender, RoutedEventArgs e) {
            if (string.IsNullOrEmpty(AppSettings.Default.ProjectsFolderPath)) {
                MessageBox.Show(
                    "Не указана папка проектов. Откройте настройки и укажите Projects Folder Path.",
                    "Upload Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!ValidateB2Credentials()) return;

            if (_lastExportedFiles.Count == 0) {
                MessageBox.Show(
                    "No files to upload.\n\nPlease export assets first, then click Upload.",
                    "Upload Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var projectName = ProjectName ?? "UnknownProject";
            var serverPath = Path.Combine(AppSettings.Default.ProjectsFolderPath, projectName, "server");

            logger.Info($"Upload starting: {_lastExportedFiles.Count} files from last export");
            await PerformB2UploadAsync(_lastExportedFiles, serverPath);
        }

        /// <summary>
        /// Validates that B2 credentials are configured.
        /// </summary>
        private bool ValidateB2Credentials() {
            var validation = uploadWorkflowCoordinator.ValidateB2Configuration(
                AppSettings.Default.B2KeyId,
                AppSettings.Default.B2BucketName);
            if (validation.IsValid) {
                return true;
            }

            MessageBox.Show(
                validation.ErrorMessage ?? "Backblaze B2 credentials are invalid.",
                "Upload Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        /// <summary>
        /// Shared B2 upload pipeline: initialize service, build file pairs, upload batch,
        /// upload mapping.json, save records, update UI statuses.
        /// </summary>
        private async Task PerformB2UploadAsync(List<string> filesToUpload, string? serverPath) {
            var projectName = ProjectName ?? "UnknownProject";

            try {
                viewModel.IsUploadToCloudEnabled = false;
                viewModel.UploadToCloudButtonContent = "Uploading...";

                using var b2Service = new B2UploadService();
                using var uploadStateService = new Data.UploadStateService();
                var uploadCoordinator = new AssetUploadCoordinator(b2Service, uploadStateService);

                var initialized = await uploadCoordinator.InitializeAsync();
                if (!initialized) {
                    MessageBox.Show(
                        "Failed to connect to Backblaze B2. Check your credentials in Settings.",
                        "Upload Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var filePairs = uploadWorkflowCoordinator.BuildUploadFilePairs(filesToUpload, serverPath, missingPath => logger.Warn($"Exported file not found: {missingPath}"));
                logger.Info($"Uploading {filePairs.Count} files (from {filesToUpload.Count} exported)");

                var result = await b2Service.UploadBatchAsync(
                    filePairs,
                    progress: new Progress<B2UploadProgress>(p => {
                        Dispatcher.Invoke(() => {
                            viewModel.ProgressValue = p.PercentComplete * 0.9;
                            var fileName = Path.GetFileName(p.CurrentFile);
                            viewModel.ProgressText = $"Upload: {fileName} ({p.CurrentFileIndex}/{p.TotalFiles})";
                        });
                    })
                );

                // Upload mapping.json separately (lives in server/, not content/)
                int mappingUploaded = 0;
                if (!string.IsNullOrEmpty(serverPath)) {
                    mappingUploaded = await uploadWorkflowCoordinator.TryUploadMappingJsonAsync(
                        b2Service,
                        uploadStateService,
                        serverPath,
                        projectName,
                        onInfo: message => logger.Info(message),
                        onWarn: (ex, message) => logger.Warn(ex, message));

                    var statusUpdates = await uploadWorkflowCoordinator.SaveUploadRecordsAsync(
                        result,
                        serverPath,
                        projectName,
                        uploadStateService,
                        onInfo: message => logger.Info(message),
                        onError: (ex, message) => logger.Error(ex, message));

                    Dispatcher.Invoke(() => {
                        uploadWorkflowCoordinator.ApplyUploadStatuses(statusUpdates.Models, viewModel.Models);
                        uploadWorkflowCoordinator.ApplyUploadStatuses(statusUpdates.Materials, viewModel.Materials);
                        uploadWorkflowCoordinator.ApplyUploadStatuses(statusUpdates.Textures, viewModel.Textures);
                    });
                }

                Dispatcher.Invoke(() => { viewModel.ProgressValue = 100; });

                var uploadMessage = uploadWorkflowCoordinator.BuildUploadResultMessage(result, mappingUploaded);

                MessageBox.Show(
                    uploadMessage,
                    "Upload Result", MessageBoxButton.OK,
                    result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);

            } catch (Exception ex) {
                logger.Error(ex, "Upload failed");
                MessageBox.Show($"Upload failed: {ex.Message}", "Upload Error", MessageBoxButton.OK, MessageBoxImage.Error);
            } finally {
                viewModel.IsUploadToCloudEnabled = true;
                viewModel.UploadToCloudButtonContent = "Upload";
                viewModel.ProgressValue = 0;
                viewModel.ProgressText = "";
            }
        }

    }
}
