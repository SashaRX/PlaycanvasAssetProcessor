using AssetProcessor.Exceptions;
using AssetProcessor.Helpers;
using AssetProcessor.Infrastructure.Enums;
using AssetProcessor.Resources;
using AssetProcessor.Services;
using AssetProcessor.Services.Models;
using AssetProcessor.Settings;
using AssetProcessor.Windows;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace AssetProcessor {
    /// <summary>
    /// Partial class containing PlayCanvas connection and synchronization logic:
    /// - Connection status and button management
    /// - Project/branch connection
    /// - Server sync and download operations
    /// - File watcher for local changes
    /// - Startup initialization
    /// </summary>
    public partial class MainWindow {

        #region Connection Status

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
        /// Updates button and connection state via service
        /// </summary>
        private void UpdateConnectionButton(ConnectionState newState) {
            connectionStateService.SetState(newState);
            ApplyConnectionButtonState();
        }

        /// <summary>
        /// Applies visual button state based on service data
        /// </summary>
        private void ApplyConnectionButtonState() {
            Dispatcher.Invoke(() => {
                bool hasSelection = !string.IsNullOrEmpty(viewModel.SelectedProjectId)
                    && !string.IsNullOrEmpty(viewModel.SelectedBranchId);

                if (DynamicConnectionButton == null) {
                    logger.Warn("ApplyConnectionButtonState: DynamicConnectionButton is null!");
                    return;
                }

                var buttonInfo = connectionStateService.GetButtonInfo(hasSelection);
                DynamicConnectionButton.Content = buttonInfo.Content;
                DynamicConnectionButton.ToolTip = buttonInfo.ToolTip;
                DynamicConnectionButton.IsEnabled = buttonInfo.IsEnabled;
                DynamicConnectionButton.Background = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(buttonInfo.ColorR, buttonInfo.ColorG, buttonInfo.ColorB));

                logger.Info($"ApplyConnectionButtonState: Set to {buttonInfo.Content} (enabled={buttonInfo.IsEnabled})");
            });
        }

        #endregion

        #region Connection Button Click

        /// <summary>
        /// Handler for dynamic connection button click
        /// </summary>
        private async void DynamicConnectionButton_Click(object sender, RoutedEventArgs e) {
            var currentState = connectionStateService.CurrentState;
            logger.Info($"DynamicConnectionButton_Click: current state: {currentState}");

            await UiAsyncHelper.ExecuteAsync(async () => {
                switch (currentState) {
                    case ConnectionState.Disconnected:
                        ConnectToPlayCanvas();
                        break;

                    case ConnectionState.UpToDate:
                        await RefreshFromServer();
                        break;

                    case ConnectionState.NeedsDownload:
                        await DownloadFromServer();
                        break;
                }
            }, nameof(DynamicConnectionButton_Click), showMessageBox: true);
        }

        /// <summary>
        /// Connects to PlayCanvas - loads project and branch lists
        /// </summary>
        private void ConnectToPlayCanvas() {
            Connect(null, null);
        }

        #endregion

        #region Server Operations

        /// <summary>
        /// Refreshes project state from server (Refresh button)
        /// Compares hash of remote assets_list.json with local
        /// </summary>
        private async Task RefreshFromServer() {
            try {
                DynamicConnectionButton.IsEnabled = false;

                // Re-scan file statuses to detect deleted files
                RescanFileStatuses();

                bool hasUpdates = await CheckForUpdates();
                bool hasMissingFiles = HasMissingFiles();

                if (hasUpdates || hasMissingFiles) {
                    UpdateConnectionButton(ConnectionState.NeedsDownload);
                    string message = hasUpdates && hasMissingFiles
                        ? "Updates available and missing files found! Click Download to get them."
                        : hasUpdates
                            ? "Updates available! Click Download to get them."
                            : $"Missing files found! Click Download to get them.";
                    MessageBox.Show(message, "Download Required", MessageBoxButton.OK, MessageBoxImage.Information);
                } else {
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
        /// Downloads missing files (Download button)
        /// Only syncs from server if there are actual server updates (hash changed)
        /// </summary>
        private async Task DownloadFromServer() {
            try {
                logger.Info("DownloadFromServer: Starting download");
                logService.LogInfo("DownloadFromServer: Starting download from server");
                CancelButton.IsEnabled = true;
                DynamicConnectionButton.IsEnabled = false;

                if (cancellationTokenSource != null) {
                    // Check if there are actual server updates (not just missing local files)
                    bool hasServerUpdates = await CheckForUpdates();

                    if (hasServerUpdates) {
                        // Server has new/changed assets - need to sync the list first
                        logger.Info("DownloadFromServer: Server has updates, syncing assets list");
                        logService.LogInfo("DownloadFromServer: Server has updates, syncing assets list");
                        await TryConnect(cancellationTokenSource.Token);
                    } else {
                        logger.Info("DownloadFromServer: No server updates, downloading missing files only");
                        logService.LogInfo("DownloadFromServer: No server updates, downloading missing files only");
                    }

                    // Download files (textures, models, materials)
                    logger.Info("DownloadFromServer: Starting file downloads");
                    logService.LogInfo("DownloadFromServer: Starting file downloads");
                    await Download(null, null);
                    logger.Info("DownloadFromServer: File downloads completed");
                    logService.LogInfo("DownloadFromServer: File downloads completed");

                    // Check final state
                    bool stillHasUpdates = await CheckForUpdates();
                    bool stillHasMissingFiles = HasMissingFiles();

                    if (stillHasUpdates || stillHasMissingFiles) {
                        logger.Info("DownloadFromServer: Still has updates or missing files - keeping Download button");
                        UpdateConnectionButton(ConnectionState.NeedsDownload);
                    } else {
                        logger.Info("DownloadFromServer: Project is up to date - setting button to Refresh");
                        logService.LogInfo("DownloadFromServer: Project is up to date - setting button to Refresh");
                        UpdateConnectionButton(ConnectionState.UpToDate);
                    }
                    logger.Info("DownloadFromServer: Button state updated");
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

        private async void Connect(object? sender, RoutedEventArgs? e) {
            CancellationToken cancellationToken = cancellationTokenSource.Token;

            if (!credentialsService.HasStoredCredentials) {
                MessageBox.Show("Please set your Playcanvas API key, and Username in the settings window.");
                SettingsWindow settingsWindow = new();
                settingsWindow.OnPreviewRendererChanged += HandlePreviewRendererChanged;
                settingsWindow.ShowDialog();
                settingsWindow.OnPreviewRendererChanged -= HandlePreviewRendererChanged;
                return;
            }

            try {
                if (!TryGetApiKey(out string apiKey)) {
                    throw new Exception("Failed to decrypt API key");
                }

                string? username = credentialsService.Username;
                if (string.IsNullOrEmpty(username)) {
                    throw new Exception("Username is null or empty");
                }

                ProjectSelectionResult projectsResult = await projectSelectionService.LoadProjectsAsync(username, apiKey, AppSettings.Default.LastSelectedProjectId, cancellationToken);
                if (string.IsNullOrEmpty(projectsResult.UserId)) {
                    throw new Exception("User ID is null or empty");
                }

                await Dispatcher.InvokeAsync(() => UpdateConnectionStatus(true, $"by userID: {projectsResult.UserId}"));

                if (projectsResult.Projects.Count == 0) {
                    throw new Exception("Project list is empty");
                }

                viewModel.Projects.Clear();
                foreach (KeyValuePair<string, string> project in projectsResult.Projects) {
                    viewModel.Projects.Add(project);
                }

                projectSelectionService.SetProjectInitializationInProgress(true);
                try {
                    if (!string.IsNullOrEmpty(projectsResult.SelectedProjectId)) {
                        viewModel.SelectedProjectId = projectsResult.SelectedProjectId;
                    } else if (viewModel.Projects.Count > 0) {
                        viewModel.SelectedProjectId = viewModel.Projects[0].Key;
                    } else {
                        viewModel.SelectedProjectId = null;
                    }
                } finally {
                    projectSelectionService.SetProjectInitializationInProgress(false);
                }

                if (!string.IsNullOrEmpty(viewModel.SelectedProjectId)) {
                    await viewModel.LoadBranchesCommand.ExecuteAsync(cancellationToken);
                    UpdateProjectPath();
                }

                await CheckProjectState();
            } catch (Exception ex) {
                MessageBox.Show($"Error: {ex.Message}");
                UpdateConnectionButton(ConnectionState.Disconnected);
            }
        }

        private async Task CheckProjectState() {
            try {
                logger.Info("CheckProjectState: Starting");
                logService.LogInfo("CheckProjectState: Starting project state check");

                if (string.IsNullOrEmpty(ProjectFolderPath) || string.IsNullOrEmpty(ProjectName)) {
                    logger.Warn("CheckProjectState: projectFolderPath or projectName is empty");
                    logService.LogInfo("CheckProjectState: projectFolderPath or projectName is empty - setting to NeedsDownload");
                    UpdateConnectionButton(ConnectionState.NeedsDownload);
                    return;
                }

                string assetsListPath = Path.Combine(ProjectFolderPath!, "assets_list.json");
                logger.Info($"CheckProjectState: Checking for assets_list.json at {assetsListPath}");

                if (!File.Exists(assetsListPath)) {
                    logger.Info("CheckProjectState: Project not downloaded yet - assets_list.json not found");
                    logService.LogInfo("Project not downloaded yet - assets_list.json not found");
                    UpdateConnectionButton(ConnectionState.NeedsDownload);
                    return;
                }

                logService.LogInfo("Project found, loading local assets...");
                logger.Info("CheckProjectState: Loading local assets...");
                await LoadAssetsFromJsonFileAsync();

                // Initialize Master Materials context (sync happens in OnAssetsLoaded when materials are populated)
                if (!string.IsNullOrEmpty(ProjectFolderPath)) {
                    await viewModel.MasterMaterialsViewModel.SetProjectContextAsync(ProjectFolderPath);
                }

                logService.LogInfo("Checking for updates...");
                logger.Info("CheckProjectState: Checking for updates on server");
                bool hasUpdates = await CheckForUpdates();

                // Also check for missing files locally (status "On Server" means file doesn't exist)
                bool hasMissingFiles = HasMissingFiles();

                if (hasUpdates || hasMissingFiles) {
                    string reason = hasUpdates && hasMissingFiles
                        ? "updates available and missing files"
                        : hasUpdates ? "updates available on server" : "missing files locally";
                    logger.Info($"CheckProjectState: {reason} - setting button to Download");
                    logService.LogInfo($"CheckProjectState: {reason}");
                    UpdateConnectionButton(ConnectionState.NeedsDownload);
                } else {
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

        #endregion

        #region File Status Scanning

        /// <summary>
        /// Re-scans all assets to check if local files exist and updates their status.
        /// Call this before HasMissingFiles() to detect deleted files.
        /// </summary>
        private void RescanFileStatuses() {
            var result = fileStatusScannerService.ScanAll(viewModel.Textures, viewModel.Models, viewModel.Materials);

            // Refresh UI to ensure statuses are displayed correctly
            if (result.UpdatedCount > 0) {
                TexturesDataGrid?.Items.Refresh();
                ModelsDataGrid?.Items.Refresh();
                MaterialsDataGrid?.Items.Refresh();
            }
        }

        /// <summary>
        /// Checks if any assets have status indicating they need to be downloaded.
        /// This catches cases where files were deleted locally but assets_list.json hasn't changed.
        /// </summary>
        private bool HasMissingFiles() {
            // Exclude ORM textures - they are virtual and don't require downloading
            var regularTextures = viewModel.Textures.Where(t => t is not ORMTextureResource);
            return connectionStateService.HasMissingFiles(regularTextures, viewModel.Models, viewModel.Materials);
        }

        private async Task<bool> CheckForUpdates() {
            if (string.IsNullOrEmpty(ProjectFolderPath)) {
                return false;
            }

            string? selectedProjectId = viewModel.SelectedProjectId;
            string? selectedBranchId = viewModel.SelectedBranchId;
            if (string.IsNullOrEmpty(selectedProjectId) || string.IsNullOrEmpty(selectedBranchId)) {
                return false;
            }
            if (!TryGetApiKey(out string apiKey)) {
                return false;
            }

            return await connectionStateService.CheckForUpdatesAsync(
                ProjectFolderPath!, selectedProjectId, selectedBranchId, apiKey, CancellationToken.None);
        }

        #endregion

        #region File Watcher

        /// <summary>
        /// Starts watching the project folder for file deletions.
        /// Call when project is loaded/connected.
        /// </summary>
        private void StartFileWatcher() {
            StopFileWatcher(); // Stop existing watcher if any

            if (string.IsNullOrEmpty(ProjectFolderPath)) {
                return;
            }

            projectFileWatcherService.Start(ProjectFolderPath);
        }

        /// <summary>
        /// Stops the file watcher. Call when project is unloaded or window closes.
        /// </summary>
        private void StopFileWatcher() {
            projectFileWatcherService.Stop();
        }

        /// <summary>
        /// Handles file deletion events from ProjectFileWatcherService.
        /// </summary>
        private void OnFilesDeletionDetected(object? sender, FilesDeletionDetectedEventArgs e) {
            // Must dispatch to UI thread since event comes from background
            Dispatcher.InvokeAsync(() => {
                if (e.RequiresFullRescan) {
                    logger.Info("Performing full rescan due to directory deletion");
                    RescanFileStatuses();

                    if (HasMissingFiles()) {
                        UpdateConnectionButton(ConnectionState.NeedsDownload);
                    }
                } else if (e.DeletedPaths.Count > 0) {
                    int updatedCount = fileStatusScannerService.ProcessDeletedPaths(
                        e.DeletedPaths.ToList(), viewModel.Textures, viewModel.Models, viewModel.Materials);

                    if (updatedCount > 0) {
                        // Single refresh after all updates
                        TexturesDataGrid?.Items.Refresh();
                        ModelsDataGrid?.Items.Refresh();
                        MaterialsDataGrid?.Items.Refresh();

                        // Update connection button state
                        if (HasMissingFiles()) {
                            UpdateConnectionButton(ConnectionState.NeedsDownload);
                        }
                    }
                }
            });
        }

        #endregion

        #region Branch Management

        private async void CreateBranchButton_Click(object sender, RoutedEventArgs e) {
            try {
                // Check that a project is selected
                if (string.IsNullOrEmpty(viewModel.SelectedProjectId)) {
                    MessageBox.Show("Please select a project before creating a branch.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string selectedProjectId = viewModel.SelectedProjectId;
                if (!TryGetApiKey(out string apiKey)) {
                    MessageBox.Show("API key not found. Please configure API key in settings.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Show dialog for branch name input
                var inputDialog = new InputDialog("Create New Branch", "Enter new branch name:", "");
                if (inputDialog.ShowDialog() != true || string.IsNullOrWhiteSpace(inputDialog.ResponseText)) {
                    return; // User cancelled or didn't enter name
                }

                string branchName = inputDialog.ResponseText.Trim();

                // Create branch via API
                logger.Info($"Creating branch '{branchName}' for project ID '{selectedProjectId}'");
                Branch newBranch = await playCanvasService.CreateBranchAsync(selectedProjectId, branchName, apiKey, CancellationToken.None);

                logger.Info($"Branch created successfully: {newBranch.Name} (ID: {newBranch.Id})");

                // Refresh branch list
                await viewModel.LoadBranchesCommand.ExecuteAsync(CancellationToken.None);

                // Select the new branch
                viewModel.SelectedBranchId = newBranch.Id;

                MessageBox.Show($"Branch '{branchName}' created successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            } catch (PlayCanvasApiException ex) {
                logger.Error(ex, "Failed to create branch");
                MessageBox.Show($"Error creating branch: {ex.Message}", "API Error", MessageBoxButton.OK, MessageBoxImage.Error);
            } catch (NetworkException ex) {
                logger.Error(ex, "Network error while creating branch");
                MessageBox.Show($"Network error creating branch: {ex.Message}", "Network Error", MessageBoxButton.OK, MessageBoxImage.Error);
            } catch (Exception ex) {
                logger.Error(ex, "Unexpected error while creating branch");
                MessageBox.Show($"Unexpected error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateProjectPath() {
            if (string.IsNullOrEmpty(viewModel.SelectedProjectId)) {
                return;
            }

            var selectedProject = viewModel.Projects.FirstOrDefault(project => project.Key == viewModel.SelectedProjectId);
            if (!selectedProject.Equals(default(KeyValuePair<string, string>))) {
                projectSelectionService.UpdateProjectPath(AppSettings.Default.ProjectsFolderPath, selectedProject);
            }
        }

        #endregion

        #region Startup Initialization

        /// <summary>
        /// Initializes on application startup - connects to server and loads last settings
        /// If hash of local JSON matches remote - skips download
        /// </summary>
        private async Task InitializeOnStartup() {
            try {
                logger.Info("=== InitializeOnStartup: Starting ===");
                logService.LogInfo("=== Initializing on startup ===");

                // Check for API key and username
                if (!credentialsService.HasStoredCredentials) {
                    logger.Info("InitializeOnStartup: No API key or username - showing Connect button");
                    logService.LogInfo("No API key or username - showing Connect button");
                    UpdateConnectionButton(ConnectionState.Disconnected);
                    return;
                }

                // Connect to server and load project list
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
        /// Smart asset loading: loads local assets and checks for server updates.
        /// If hashes differ - shows download button.
        /// </summary>
        private async Task SmartLoadAssets() {
            try {
                logger.Info("=== SmartLoadAssets: Starting ===");

                string? selectedProjectId = viewModel.SelectedProjectId;
                string? selectedBranchId = viewModel.SelectedBranchId;
                if (string.IsNullOrEmpty(selectedProjectId) || string.IsNullOrEmpty(selectedBranchId)) {
                    logger.Warn("SmartLoadAssets: No project or branch selected");
                    UpdateConnectionButton(ConnectionState.Disconnected);
                    return;
                }

                if (string.IsNullOrEmpty(ProjectFolderPath)) {
                    logService.LogInfo("Project folder path is empty");
                    UpdateConnectionButton(ConnectionState.NeedsDownload);
                    return;
                }

                // Initialize Master Materials context BEFORE loading assets
                // so config is ready when OnAssetsLoaded calls SyncMaterialMasterMappings
                if (!string.IsNullOrEmpty(ProjectFolderPath)) {
                    logger.Info("SmartLoadAssets: Initializing Master Materials context");
                    await viewModel.MasterMaterialsViewModel.SetProjectContextAsync(ProjectFolderPath);
                    logger.Info("SmartLoadAssets: Master Materials context initialized");
                }

                // Try to load local assets
                bool assetsLoaded = await LoadAssetsFromJsonFileAsync();

                if (assetsLoaded) {
                    // Local assets loaded, now check for server updates
                    if (!TryGetApiKey(out string apiKey)) {
                        UpdateConnectionButton(ConnectionState.NeedsDownload);
                        return;
                    }

                    // Use service to check for updates
                    var checkResult = await projectConnectionService.CheckForUpdatesAsync(
                        ProjectFolderPath,
                        selectedProjectId,
                        selectedBranchId,
                        apiKey,
                        CancellationToken.None);

                    if (!checkResult.Success) {
                        logger.Warn($"SmartLoadAssets: Failed to check for updates: {checkResult.Error}");
                        UpdateConnectionButton(ConnectionState.NeedsDownload);
                        return;
                    }

                    if (checkResult.HasUpdates) {
                        logger.Info("SmartLoadAssets: Updates available on server");
                        UpdateConnectionButton(ConnectionState.NeedsDownload);
                    } else {
                        logger.Info("SmartLoadAssets: Project is up to date");
                        UpdateConnectionButton(ConnectionState.UpToDate);
                    }
                } else {
                    // No local assets - need to download
                    logger.Info("SmartLoadAssets: No local assets found - need to download");
                    UpdateConnectionButton(ConnectionState.NeedsDownload);
                }

            } catch (Exception ex) {
                logger.Error(ex, "Error in SmartLoadAssets");
                UpdateConnectionButton(ConnectionState.NeedsDownload);
            }
        }

        private async Task LoadLastSettings() {
            try {
                logger.Info("LoadLastSettings: Starting");

                if (!TryGetApiKey(out string apiKey)) {
                    throw new Exception("API key is null or empty after decryption");
                }

                CancellationToken cancellationToken = new();

                string? username = credentialsService.Username;
                if (string.IsNullOrEmpty(username)) {
                    throw new Exception("Username is null or empty");
                }

                ProjectSelectionResult projectsResult = await projectSelectionService.LoadProjectsAsync(username, apiKey, AppSettings.Default.LastSelectedProjectId, cancellationToken);
                if (string.IsNullOrEmpty(projectsResult.UserId)) {
                    throw new Exception("User ID is null or empty");
                }

                UpdateConnectionStatus(true, $"by userID: {projectsResult.UserId}");

                if (projectsResult.Projects.Count > 0) {
                    viewModel.Projects.Clear();
                    foreach (KeyValuePair<string, string> project in projectsResult.Projects) {
                        viewModel.Projects.Add(project);
                    }

                    projectSelectionService.SetProjectInitializationInProgress(true);
                    try {
                        if (!string.IsNullOrEmpty(projectsResult.SelectedProjectId)) {
                            viewModel.SelectedProjectId = projectsResult.SelectedProjectId;
                            logger.Info($"LoadLastSettings: Selected project: {projectsResult.SelectedProjectId}");
                        } else if (viewModel.Projects.Count > 0) {
                            viewModel.SelectedProjectId = viewModel.Projects[0].Key;
                            logger.Info("LoadLastSettings: Selected first project");
                        } else {
                            viewModel.SelectedProjectId = null;
                        }
                    } finally {
                        projectSelectionService.SetProjectInitializationInProgress(false);
                    }

                    if (!string.IsNullOrEmpty(viewModel.SelectedProjectId)) {
                        await viewModel.LoadBranchesCommand.ExecuteAsync(cancellationToken);

                        UpdateProjectPath();
                        logger.Info($"LoadLastSettings: Project folder path set to: {ProjectFolderPath}");

                        // Smart loading: compare hash and show buttons accordingly
                        await SmartLoadAssets();
                    }
                }
            } catch (Exception ex) {
                logger.Error(ex, "Failed to load last settings");
                MessageBox.Show($"Error loading last settings: {ex.Message}");
            }
        }

        #endregion
    }
}
