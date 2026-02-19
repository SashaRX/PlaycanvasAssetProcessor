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

        /// <summary>
        /// Wires event handlers for ConnectionPanel controls.
        /// Called from MainWindow constructor after InitializeComponent.
        /// </summary>
        private void InitializeConnectionPanel() {
            connectionPanel.DynamicConnectionButton.Click += DynamicConnectionButton_Click;
            connectionPanel.CreateBranchButton.Click += CreateBranchButton_Click;
        }

        #region Connection Status

        private void UpdateConnectionStatus(bool isConnected, string message = "") {
            Dispatcher.Invoke(() => {
                viewModel.IsConnected = isConnected;
                if (isConnected) {
                    viewModel.ConnectionStatusText = string.IsNullOrEmpty(message) ? "Connected" : $"Connected: {message}";
                } else {
                    viewModel.ConnectionStatusText = string.IsNullOrEmpty(message) ? "Disconnected" : $"Error: {message}";
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

                var buttonInfo = connectionStateService.GetButtonInfo(hasSelection);
                viewModel.ConnectionButtonContent = buttonInfo.Content;
                viewModel.ConnectionButtonToolTip = buttonInfo.ToolTip;
                viewModel.IsConnectionButtonEnabled = buttonInfo.IsEnabled;
                viewModel.ConnectionButtonBackground = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(buttonInfo.ColorR, buttonInfo.ColorG, buttonInfo.ColorB));
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
                viewModel.IsConnectionButtonEnabled = false;

                // Re-scan file statuses to detect deleted files
                RescanFileStatuses();

                var refreshResult = await connectionWorkflowCoordinator.EvaluateRefreshAsync(CheckForUpdates, HasMissingFiles);

                if (refreshResult.State == ConnectionState.NeedsDownload) {
                    UpdateConnectionButton(refreshResult.State);
                    MessageBox.Show(refreshResult.Message, "Download Required", MessageBoxButton.OK, MessageBoxImage.Information);
                } else {
                    MessageBox.Show(refreshResult.Message, "No Updates", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            } catch (Exception ex) {
                MessageBox.Show($"Error checking for updates: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                logService.LogError($"Error in RefreshFromServer: {ex}");
            } finally {
                viewModel.IsConnectionButtonEnabled = true;
            }
        }

        /// <summary>
        /// Downloads missing files (Download button)
        /// Only syncs from server if there are actual server updates (hash changed)
        /// </summary>
        private async Task DownloadFromServer() {
            try {
                logService.LogInfo("Starting download from server");
                viewModel.IsCancelEnabled = true;
                viewModel.IsConnectionButtonEnabled = false;

                if (cancellationTokenSource != null) {
                    bool hasServerUpdates = await CheckForUpdates();

                    if (hasServerUpdates) {
                        logService.LogInfo("Server has updates, syncing assets list");
                        await TryConnect(cancellationTokenSource.Token);
                    } else {
                        logService.LogInfo("No server updates, downloading missing files only");
                    }

                    await Download(null, null);

                    var postDownloadResult = await connectionWorkflowCoordinator.EvaluatePostDownloadAsync(CheckForUpdates, HasMissingFiles);
                    if (postDownloadResult.State == ConnectionState.UpToDate) {
                        logService.LogInfo("Download complete, project is up to date");
                    }
                    UpdateConnectionButton(postDownloadResult.State);
                } else {
                    logger.Warn("DownloadFromServer: cancellationTokenSource is null");
                }
            } catch (Exception ex) {
                logger.Error(ex, "Error in DownloadFromServer");
                MessageBox.Show($"Error downloading: {ex.Message}", "Download Error", MessageBoxButton.OK, MessageBoxImage.Error);
            } finally {
                viewModel.IsCancelEnabled = false;
                viewModel.IsConnectionButtonEnabled = true;
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
                bool hasProjectFolder = !string.IsNullOrEmpty(ProjectFolderPath);
                bool hasProjectName = !string.IsNullOrEmpty(ProjectName);
                bool assetsListExists = hasProjectFolder
                    && File.Exists(Path.Combine(ProjectFolderPath!, "assets_list.json"));

                if (!assetsListExists) {
                    logService.LogInfo("Project not downloaded yet — assets_list.json not found");
                } else {
                    logService.LogInfo("Loading local assets...");
                    await LoadAssetsFromJsonFileAsync();

                    await viewModel.MasterMaterialsViewModel.SetProjectContextAsync(ProjectFolderPath!);
                }

                bool hasUpdates = assetsListExists && await CheckForUpdates();
                bool hasMissingFiles = assetsListExists && HasMissingFiles();
                var stateResult = connectionWorkflowCoordinator.EvaluateProjectState(
                    hasProjectFolder,
                    hasProjectName,
                    assetsListExists,
                    hasUpdates,
                    hasMissingFiles);

                if (stateResult.State == ConnectionState.NeedsDownload) {
                    string reason = stateResult.HasUpdates && stateResult.HasMissingFiles
                        ? "updates available and missing files"
                        : stateResult.HasUpdates
                            ? "updates available"
                            : "missing files or project is not downloaded";
                    logService.LogInfo($"CheckProjectState: {reason}");
                } else {
                    logService.LogInfo("Project is up to date");
                }

                UpdateConnectionButton(stateResult.State);
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
                if (!credentialsService.HasStoredCredentials) {
                    logService.LogInfo("No API key or username — showing Connect button");
                    UpdateConnectionButton(ConnectionState.Disconnected);
                    return;
                }

                logService.LogInfo("Connecting to PlayCanvas server...");
                await LoadLastSettings();
            } catch (Exception ex) {
                logger.Error(ex, "Error during startup initialization");
                logService.LogError($"Startup error: {ex.Message}");
                UpdateConnectionButton(ConnectionState.Disconnected);
            }
        }

        /// <summary>
        /// Smart asset loading: loads local assets and checks for server updates.
        /// If hashes differ - shows download button.
        /// </summary>
        private async Task SmartLoadAssets() {
            try {
                string? selectedProjectId = viewModel.SelectedProjectId;
                string? selectedBranchId = viewModel.SelectedBranchId;
                bool hasSelection = !string.IsNullOrEmpty(selectedProjectId) && !string.IsNullOrEmpty(selectedBranchId);
                if (!hasSelection) {
                    UpdateConnectionButton(connectionWorkflowCoordinator.EvaluateSmartLoadState(
                        hasSelection: false,
                        hasProjectPath: false,
                        assetsLoaded: false,
                        updatesCheckSucceeded: false,
                        hasUpdates: false));
                    return;
                }

                bool hasProjectPath = !string.IsNullOrEmpty(ProjectFolderPath);
                if (!hasProjectPath) {
                    UpdateConnectionButton(connectionWorkflowCoordinator.EvaluateSmartLoadState(
                        hasSelection: true,
                        hasProjectPath: false,
                        assetsLoaded: false,
                        updatesCheckSucceeded: false,
                        hasUpdates: false));
                    return;
                }

                // Initialize Master Materials context BEFORE loading assets
                await viewModel.MasterMaterialsViewModel.SetProjectContextAsync(ProjectFolderPath);

                bool assetsLoaded = await LoadAssetsFromJsonFileAsync();

                if (assetsLoaded) {
                    if (!TryGetApiKey(out string apiKey)) {
                        UpdateConnectionButton(connectionWorkflowCoordinator.EvaluateSmartLoadState(
                            hasSelection: true,
                            hasProjectPath: true,
                            assetsLoaded: true,
                            updatesCheckSucceeded: false,
                            hasUpdates: false));
                        return;
                    }

                    var checkResult = await projectConnectionService.CheckForUpdatesAsync(
                        ProjectFolderPath,
                        selectedProjectId,
                        selectedBranchId,
                        apiKey,
                        CancellationToken.None);

                    if (!checkResult.Success) {
                        logger.Warn($"SmartLoadAssets: Failed to check for updates: {checkResult.Error}");
                        UpdateConnectionButton(connectionWorkflowCoordinator.EvaluateSmartLoadState(
                            hasSelection: true,
                            hasProjectPath: true,
                            assetsLoaded: true,
                            updatesCheckSucceeded: false,
                            hasUpdates: false));
                        return;
                    }

                    UpdateConnectionButton(connectionWorkflowCoordinator.EvaluateSmartLoadState(
                        hasSelection: true,
                        hasProjectPath: true,
                        assetsLoaded: true,
                        updatesCheckSucceeded: true,
                        hasUpdates: checkResult.HasUpdates));
                } else {
                    UpdateConnectionButton(connectionWorkflowCoordinator.EvaluateSmartLoadState(
                        hasSelection: true,
                        hasProjectPath: true,
                        assetsLoaded: false,
                        updatesCheckSucceeded: false,
                        hasUpdates: false));
                }
            } catch (Exception ex) {
                logger.Error(ex, "Error in SmartLoadAssets");
                UpdateConnectionButton(ConnectionState.NeedsDownload);
            }
        }

        private async Task LoadLastSettings() {
            try {
                if (!TryGetApiKey(out string apiKey)) {
                    throw new Exception("API key is null or empty after decryption");
                }

                CancellationToken cancellationToken = new();

                string? username = credentialsService.Username;
                if (string.IsNullOrEmpty(username)) {
                    throw new Exception("Username is null or empty");
                }

                ProjectSelectionResult projectsResult = await projectSelectionService.LoadProjectsAsync(
                    username, apiKey, AppSettings.Default.LastSelectedProjectId, cancellationToken);
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
