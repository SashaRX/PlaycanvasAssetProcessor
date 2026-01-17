using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AssetProcessor.Helpers;
using AssetProcessor.Settings;
using AssetProcessor.Upload;
using AssetProcessor.ViewModels;
using NLog;

namespace AssetProcessor.Controls {
    /// <summary>
    /// Панель для просмотра и управления ассетами на сервере
    /// </summary>
    public partial class ServerAssetsPanel : UserControl {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly ObservableCollection<ServerAssetViewModel> _allAssets = new();
        private readonly ObservableCollection<ServerAssetViewModel> _filteredAssets = new();
        private readonly ObservableCollection<ServerAssetViewModel> _displayedAssets = new();

        private string? _projectFolderPath;
        private string? _projectName;
        private CancellationTokenSource? _refreshCts;
        private bool _isInitialized;
        private bool _isTreeView = true;
        private ServerFolderNode? _rootNode;
        private ServerFolderNode? _selectedFolder;

        public ServerAssetsPanel() {
            InitializeComponent();
            ServerAssetsDataGrid.ItemsSource = _displayedAssets;
            _isInitialized = true;
        }

        /// <summary>
        /// Устанавливает путь к папке проекта для сравнения локальных файлов
        /// </summary>
        public void SetProjectContext(string? projectFolderPath, string? projectName) {
            _projectFolderPath = projectFolderPath;
            _projectName = projectName;
        }

        /// <summary>
        /// Обновляет список файлов с сервера
        /// </summary>
        private async void RefreshButton_Click(object sender, RoutedEventArgs e) {
            await RefreshServerAssetsAsync();
        }

        /// <summary>
        /// Переключает режим отображения (дерево/список)
        /// </summary>
        private void ToggleViewButton_Click(object sender, RoutedEventArgs e) {
            _isTreeView = !_isTreeView;
            UpdateViewMode();
        }

        private void UpdateViewMode() {
            if (!_isInitialized) return;

            if (_isTreeView) {
                ToggleViewButton.Content = "☰ List";
                TreeColumn.Width = new GridLength(250);
                TreeColumn.MinWidth = 150;
                FolderTreeView.Visibility = Visibility.Visible;
                TreeSplitter.Visibility = Visibility.Visible;

                // Show files from selected folder
                if (_selectedFolder != null) {
                    ShowFolderFiles(_selectedFolder);
                } else if (_rootNode != null) {
                    ShowFolderFiles(_rootNode);
                }
            } else {
                ToggleViewButton.Content = "🌳 Tree";
                TreeColumn.Width = new GridLength(0);
                TreeColumn.MinWidth = 0;
                FolderTreeView.Visibility = Visibility.Collapsed;
                TreeSplitter.Visibility = Visibility.Collapsed;

                // Show all filtered files
                ShowAllFiles();
            }
        }

        private void ShowFolderFiles(ServerFolderNode folder) {
            _displayedAssets.Clear();
            AddFolderFilesRecursive(folder);
            UpdateFileCount();
        }

        private void AddFolderFilesRecursive(ServerFolderNode folder) {
            foreach (var file in folder.Files) {
                if (PassesFilter(file)) {
                    _displayedAssets.Add(file);
                }
            }
            foreach (var child in folder.Children) {
                AddFolderFilesRecursive(child);
            }
        }

        private void ShowAllFiles() {
            _displayedAssets.Clear();
            foreach (var asset in _filteredAssets) {
                _displayedAssets.Add(asset);
            }
            UpdateFileCount();
        }

        private void UpdateFileCount() {
            CountText.Text = $"{_displayedAssets.Count} / {_allAssets.Count} files";
        }

        /// <summary>
        /// Загружает список файлов с B2 сервера
        /// </summary>
        public async Task RefreshServerAssetsAsync() {
            // Cancel previous refresh if running
            _refreshCts?.Cancel();
            _refreshCts = new CancellationTokenSource();
            var ct = _refreshCts.Token;

            // Validate B2 credentials
            if (string.IsNullOrEmpty(AppSettings.Default.B2KeyId) ||
                string.IsNullOrEmpty(AppSettings.Default.B2BucketName)) {
                StatusText.Text = "B2 credentials not configured. Go to Settings -> CDN/Upload.";
                return;
            }

            try {
                RefreshButton.IsEnabled = false;
                RefreshButton.Content = "Loading...";
                StatusText.Text = "Connecting to B2...";

                _allAssets.Clear();
                _filteredAssets.Clear();
                _displayedAssets.Clear();

                using var b2Service = new B2UploadService();

                // Get B2 settings
                if (!AppSettings.Default.TryGetDecryptedB2ApplicationKey(out var appKey) || string.IsNullOrEmpty(appKey)) {
                    StatusText.Text = "Failed to decrypt B2 application key.";
                    return;
                }

                var settings = new B2UploadSettings {
                    KeyId = AppSettings.Default.B2KeyId,
                    ApplicationKey = appKey,
                    BucketName = AppSettings.Default.B2BucketName,
                    BucketId = AppSettings.Default.B2BucketId,
                    PathPrefix = AppSettings.Default.B2PathPrefix,
                    CdnBaseUrl = AppSettings.Default.CdnBaseUrl
                };

                var authorized = await b2Service.AuthorizeAsync(settings, ct);
                if (!authorized) {
                    StatusText.Text = "Failed to authorize with B2. Check credentials.";
                    return;
                }

                StatusText.Text = "Loading file list...";

                // List all files with prefix (if project name is set)
                var prefix = !string.IsNullOrEmpty(_projectName) ? _projectName : "";
                var files = await b2Service.ListFilesAsync(prefix, 10000, ct);

                long totalSize = 0;

                foreach (var file in files) {
                    if (ct.IsCancellationRequested) break;

                    var asset = new ServerAssetViewModel {
                        RemotePath = file.FileName,
                        Size = file.ContentLength,
                        UploadedAt = file.UploadTime,
                        ContentSha1 = file.ContentSha1 ?? "",
                        CdnUrl = settings.BuildCdnUrl(file.FileName),
                        FileId = file.FileId
                    };

                    // Try to find local file and compare
                    await CompareWithLocalAsync(asset, ct);

                    _allAssets.Add(asset);
                    totalSize += file.ContentLength;
                }

                // Apply filters
                ApplyFilters();

                // Build tree
                _rootNode = ServerFolderNode.BuildTree(_allAssets, _projectName ?? "Server");
                FolderTreeView.ItemsSource = new[] { _rootNode };

                // Update view
                UpdateViewMode();

                var totalSizeMB = totalSize / (1024.0 * 1024.0);
                SizeText.Text = $"{totalSizeMB:F2} MB total";
                StatusText.Text = $"Loaded {_allAssets.Count} files from server";

                Logger.Info($"Loaded {_allAssets.Count} files from B2, total size: {totalSizeMB:F2} MB");

            } catch (OperationCanceledException) {
                StatusText.Text = "Refresh cancelled";
            } catch (Exception ex) {
                Logger.Error(ex, "Failed to refresh server assets");
                StatusText.Text = $"Error: {ex.Message}";
            } finally {
                RefreshButton.IsEnabled = true;
                RefreshButton.Content = "Refresh";
            }
        }

        /// <summary>
        /// Сравнивает серверный файл с локальным
        /// </summary>
        private async Task CompareWithLocalAsync(ServerAssetViewModel asset, CancellationToken ct) {
            if (string.IsNullOrEmpty(_projectFolderPath)) {
                asset.SyncStatus = "ServerOnly";
                return;
            }

            try {
                var fileName = Path.GetFileName(asset.RemotePath);
                var contentPath = Path.Combine(_projectFolderPath, "server", "assets", "content");
                string? localPath = null;

                if (Directory.Exists(contentPath)) {
                    var matchingFiles = Directory.GetFiles(contentPath, fileName, SearchOption.AllDirectories);
                    if (matchingFiles.Length > 0) {
                        localPath = matchingFiles[0];
                    }
                }

                if (fileName.Equals("mapping.json", StringComparison.OrdinalIgnoreCase)) {
                    var mappingPath = Path.Combine(_projectFolderPath, "server", "mapping.json");
                    if (File.Exists(mappingPath)) {
                        localPath = mappingPath;
                    }
                }

                if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath)) {
                    asset.LocalPath = localPath;
                    var localHash = await ComputeFileHashAsync(localPath, ct);

                    if (string.Equals(localHash, asset.ContentSha1, StringComparison.OrdinalIgnoreCase)) {
                        asset.SyncStatus = "Synced";
                    } else {
                        asset.SyncStatus = "HashMismatch";
                    }
                } else {
                    asset.SyncStatus = "ServerOnly";
                }
            } catch (Exception ex) {
                Logger.Warn(ex, $"Failed to compare {asset.RemotePath} with local");
                asset.SyncStatus = "Unknown";
            }
        }

        private static async Task<string> ComputeFileHashAsync(string filePath, CancellationToken ct) {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            var hash = await SHA1.HashDataAsync(stream, ct);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private bool PassesFilter(ServerAssetViewModel asset) {
            if (!_isInitialized || FilterComboBox == null || StatusFilterComboBox == null || SearchTextBox == null)
                return true;

            var typeFilter = (FilterComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
            var statusFilter = (StatusFilterComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
            var searchText = SearchTextBox.Text?.Trim().ToLowerInvariant() ?? "";

            // Type filter
            if (typeFilter != "All") {
                var matchType = typeFilter switch {
                    "Textures" => asset.FileType == "Texture",
                    "Models" => asset.FileType == "Model",
                    "JSON" => asset.FileType == "JSON",
                    _ => true
                };
                if (!matchType) return false;
            }

            // Status filter
            if (statusFilter != "All" && asset.SyncStatus != statusFilter) {
                return false;
            }

            // Search filter
            if (!string.IsNullOrEmpty(searchText)) {
                if (!asset.RemotePath.ToLowerInvariant().Contains(searchText) &&
                    !asset.FileName.ToLowerInvariant().Contains(searchText)) {
                    return false;
                }
            }

            return true;
        }

        private void ApplyFilters() {
            if (!_isInitialized) return;

            _filteredAssets.Clear();

            foreach (var asset in _allAssets) {
                if (PassesFilter(asset)) {
                    _filteredAssets.Add(asset);
                }
            }

            // Rebuild tree with filtered assets
            if (_filteredAssets.Any()) {
                _rootNode = ServerFolderNode.BuildTree(_filteredAssets, _projectName ?? "Server");
                FolderTreeView.ItemsSource = new[] { _rootNode };
            }

            UpdateViewMode();
        }

        private void FilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (_isInitialized) ApplyFilters();
        }

        private void StatusFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (_isInitialized) ApplyFilters();
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e) {
            if (_isInitialized) ApplyFilters();
        }

        private void FolderTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) {
            if (!_isInitialized) return;
            if (e.NewValue is ServerFolderNode folder) {
                _selectedFolder = folder;
                ShowFolderFiles(folder);
            }
        }

        /// <summary>
        /// Событие при выборе файла в таблице
        /// </summary>
        public event EventHandler<ServerAssetViewModel?>? SelectionChanged;

        /// <summary>
        /// Событие для навигации к ресурсу в основных таблицах
        /// </summary>
        public event EventHandler<string>? NavigateToResourceRequested;

        private void ServerAssetsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (!_isInitialized || !IsLoaded) return;
            var selectedAsset = ServerAssetsDataGrid.SelectedItem as ServerAssetViewModel;
            SelectionChanged?.Invoke(this, selectedAsset);
        }

        private void GoToResourceLink_Click(object sender, RoutedEventArgs e) {
            if (sender is System.Windows.Documents.Hyperlink hyperlink &&
                hyperlink.DataContext is ServerAssetViewModel asset) {
                NavigateToResourceRequested?.Invoke(this, asset.FileName);
            }
        }

        /// <summary>
        /// Удаляет выбранные файлы с сервера
        /// </summary>
        private async void DeleteSelectedButton_Click(object sender, RoutedEventArgs e) {
            var selectedAssets = ServerAssetsDataGrid.SelectedItems.Cast<ServerAssetViewModel>().ToList();

            if (!selectedAssets.Any()) {
                MessageBox.Show("Select files to delete.", "Delete", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Are you sure you want to delete {selectedAssets.Count} file(s) from the server?\n\nThis action cannot be undone.",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try {
                DeleteSelectedButton.IsEnabled = false;
                StatusText.Text = "Deleting files...";

                using var b2Service = new B2UploadService();

                if (!AppSettings.Default.TryGetDecryptedB2ApplicationKey(out var appKey) || string.IsNullOrEmpty(appKey)) {
                    StatusText.Text = "Failed to decrypt B2 application key.";
                    return;
                }

                var settings = new B2UploadSettings {
                    KeyId = AppSettings.Default.B2KeyId,
                    ApplicationKey = appKey,
                    BucketName = AppSettings.Default.B2BucketName,
                    BucketId = AppSettings.Default.B2BucketId
                };

                await b2Service.AuthorizeAsync(settings);

                int deleted = 0;
                int failed = 0;

                foreach (var asset in selectedAssets) {
                    try {
                        var success = await b2Service.DeleteFileAsync(asset.RemotePath);
                        if (success) {
                            _allAssets.Remove(asset);
                            _filteredAssets.Remove(asset);
                            _displayedAssets.Remove(asset);
                            deleted++;
                        } else {
                            failed++;
                        }
                    } catch (Exception ex) {
                        Logger.Warn(ex, $"Failed to delete file: {asset.RemotePath}");
                        failed++;
                    }
                }

                // Rebuild tree
                if (_allAssets.Any()) {
                    _rootNode = ServerFolderNode.BuildTree(_filteredAssets, _projectName ?? "Server");
                    FolderTreeView.ItemsSource = new[] { _rootNode };
                }

                StatusText.Text = $"Deleted {deleted} files, {failed} failed";
                UpdateFileCount();

            } catch (Exception ex) {
                Logger.Error(ex, "Failed to delete files");
                StatusText.Text = $"Error: {ex.Message}";
            } finally {
                DeleteSelectedButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// Удаляет выбранную папку и все файлы в ней с сервера
        /// </summary>
        private async void DeleteFolderMenuItem_Click(object sender, RoutedEventArgs e) {
            if (_selectedFolder == null) {
                MessageBox.Show("Select a folder to delete.", "Delete Folder", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Count all files recursively
            int fileCount = CountFilesRecursive(_selectedFolder);
            if (fileCount == 0) {
                MessageBox.Show("Selected folder is empty.", "Delete Folder", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Are you sure you want to delete folder '{_selectedFolder.Name}' and all {fileCount} file(s) in it from the server?\n\nThis action cannot be undone.",
                "Confirm Delete Folder",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try {
                StatusText.Text = $"Deleting folder {_selectedFolder.Name}...";

                using var b2Service = new B2UploadService();

                if (!AppSettings.Default.TryGetDecryptedB2ApplicationKey(out var appKey) || string.IsNullOrEmpty(appKey)) {
                    StatusText.Text = "Failed to decrypt B2 application key.";
                    return;
                }

                var settings = new B2UploadSettings {
                    KeyId = AppSettings.Default.B2KeyId,
                    ApplicationKey = appKey,
                    BucketName = AppSettings.Default.B2BucketName,
                    BucketId = AppSettings.Default.B2BucketId
                };

                await b2Service.AuthorizeAsync(settings);

                // Get folder path (prefix for B2)
                var folderPath = _selectedFolder.FullPath;

                var progress = new Progress<(int current, int total, string fileName)>(p => {
                    StatusText.Text = $"Deleting {p.current}/{p.total}: {p.fileName}";
                });

                var (deleted, failed) = await b2Service.DeleteFolderAsync(folderPath, progress);

                // Remove files from local collections
                RemoveFolderFilesRecursive(_selectedFolder);

                // Rebuild tree
                if (_allAssets.Any()) {
                    _rootNode = ServerFolderNode.BuildTree(_filteredAssets, _projectName ?? "Server");
                    FolderTreeView.ItemsSource = new[] { _rootNode };
                } else {
                    FolderTreeView.ItemsSource = null;
                }

                _selectedFolder = null;
                UpdateViewMode();

                StatusText.Text = $"Deleted {deleted} files, {failed} failed";
                UpdateFileCount();

                Logger.Info($"Deleted folder '{folderPath}': {deleted} files deleted, {failed} failed");

            } catch (Exception ex) {
                Logger.Error(ex, $"Failed to delete folder: {_selectedFolder?.Name}");
                StatusText.Text = $"Error: {ex.Message}";
            }
        }

        private int CountFilesRecursive(ServerFolderNode folder) {
            int count = folder.Files.Count;
            foreach (var child in folder.Children) {
                count += CountFilesRecursive(child);
            }
            return count;
        }

        private void RemoveFolderFilesRecursive(ServerFolderNode folder) {
            foreach (var file in folder.Files.ToList()) {
                _allAssets.Remove(file);
                _filteredAssets.Remove(file);
                _displayedAssets.Remove(file);
            }
            foreach (var child in folder.Children) {
                RemoveFolderFilesRecursive(child);
            }
        }

        /// <summary>
        /// Копирует CDN URL выбранного файла в буфер обмена
        /// </summary>
        private void CopyUrlButton_Click(object sender, RoutedEventArgs e) {
            var selectedAsset = ServerAssetsDataGrid.SelectedItem as ServerAssetViewModel;

            if (selectedAsset == null) {
                MessageBox.Show("Select a file to copy its URL.", "Copy URL", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!string.IsNullOrEmpty(selectedAsset.CdnUrl)) {
                if (ClipboardHelper.SetText(selectedAsset.CdnUrl)) {
                    StatusText.Text = $"Copied: {selectedAsset.CdnUrl}";
                } else {
                    StatusText.Text = "Failed to copy to clipboard. Try again.";
                }
            } else {
                StatusText.Text = "No CDN URL available for this file.";
            }
        }
    }
}
