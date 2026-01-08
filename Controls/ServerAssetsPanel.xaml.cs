using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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

        private string? _projectFolderPath;
        private string? _projectName;
        private CancellationTokenSource? _refreshCts;
        private bool _isInitialized;

        public ServerAssetsPanel() {
            InitializeComponent();
            ServerAssetsDataGrid.ItemsSource = _filteredAssets;
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

                ApplyFilters();

                var totalSizeMB = totalSize / (1024.0 * 1024.0);
                CountText.Text = $"{_allAssets.Count} files";
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
                // Try to find local file by matching the remote path structure
                // Remote: projectName/textures/filename.ktx2
                // Local: projectFolder/server/assets/content/.../filename.ktx2

                var fileName = Path.GetFileName(asset.RemotePath);

                // Search in server/assets/content
                var contentPath = Path.Combine(_projectFolderPath, "server", "assets", "content");
                string? localPath = null;

                if (Directory.Exists(contentPath)) {
                    var matchingFiles = Directory.GetFiles(contentPath, fileName, SearchOption.AllDirectories);
                    if (matchingFiles.Length > 0) {
                        localPath = matchingFiles[0];
                    }
                }

                // Also check for mapping.json in server/
                if (fileName.Equals("mapping.json", StringComparison.OrdinalIgnoreCase)) {
                    var mappingPath = Path.Combine(_projectFolderPath, "server", "mapping.json");
                    if (File.Exists(mappingPath)) {
                        localPath = mappingPath;
                    }
                }

                if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath)) {
                    asset.LocalPath = localPath;

                    // Compare hashes
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

        /// <summary>
        /// Вычисляет SHA1 хеш файла
        /// </summary>
        private static async Task<string> ComputeFileHashAsync(string filePath, CancellationToken ct) {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            var hash = await SHA1.HashDataAsync(stream, ct);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>
        /// Применяет фильтры к списку
        /// </summary>
        private void ApplyFilters() {
            _filteredAssets.Clear();

            var typeFilter = (FilterComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
            var statusFilter = (StatusFilterComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
            var searchText = SearchTextBox.Text?.Trim().ToLowerInvariant() ?? "";

            foreach (var asset in _allAssets) {
                // Type filter
                if (typeFilter != "All") {
                    var matchType = typeFilter switch {
                        "Textures" => asset.FileType == "Texture",
                        "Models" => asset.FileType == "Model",
                        "JSON" => asset.FileType == "JSON",
                        _ => true
                    };
                    if (!matchType) continue;
                }

                // Status filter
                if (statusFilter != "All" && asset.SyncStatus != statusFilter) {
                    continue;
                }

                // Search filter
                if (!string.IsNullOrEmpty(searchText)) {
                    if (!asset.RemotePath.ToLowerInvariant().Contains(searchText) &&
                        !asset.FileName.ToLowerInvariant().Contains(searchText)) {
                        continue;
                    }
                }

                _filteredAssets.Add(asset);
            }

            CountText.Text = $"{_filteredAssets.Count} / {_allAssets.Count} files";
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

        /// <summary>
        /// Событие при выборе файла в таблице
        /// </summary>
        public event EventHandler<ServerAssetViewModel?>? SelectionChanged;

        private void ServerAssetsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            var selectedAsset = ServerAssetsDataGrid.SelectedItem as ServerAssetViewModel;
            SelectionChanged?.Invoke(this, selectedAsset);
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
                    if (string.IsNullOrEmpty(asset.FileId)) {
                        failed++;
                        continue;
                    }

                    try {
                        var success = await b2Service.DeleteFileAsync(asset.RemotePath);
                        if (success) {
                            _allAssets.Remove(asset);
                            _filteredAssets.Remove(asset);
                            deleted++;
                        } else {
                            failed++;
                        }
                    } catch {
                        failed++;
                    }
                }

                StatusText.Text = $"Deleted {deleted} files, {failed} failed";
                CountText.Text = $"{_filteredAssets.Count} / {_allAssets.Count} files";

            } catch (Exception ex) {
                Logger.Error(ex, "Failed to delete files");
                StatusText.Text = $"Error: {ex.Message}";
            } finally {
                DeleteSelectedButton.IsEnabled = true;
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
                Clipboard.SetText(selectedAsset.CdnUrl);
                StatusText.Text = $"Copied: {selectedAsset.CdnUrl}";
            } else {
                StatusText.Text = "No CDN URL available for this file.";
            }
        }
    }
}
