using AssetProcessor.Resources;
using AssetProcessor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLog;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace AssetProcessor.ViewModels {
    /// <summary>
    /// ViewModel for texture selection and preview loading.
    /// Handles debouncing, cancellation, and state management.
    /// Raises events for UI to handle actual preview loading.
    /// </summary>
    public partial class TextureSelectionViewModel : ObservableObject {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly ILogService logService;

        private CancellationTokenSource? textureLoadCts;
        private readonly object ctsLock = new();

        // Debounce settings
        private const int DebounceDelayMs = 150;

        #region Observable Properties

        [ObservableProperty]
        private TextureResource? selectedTexture;

        [ObservableProperty]
        private bool isPreviewLoading;

        [ObservableProperty]
        private bool isKtxPreviewAvailable;

        [ObservableProperty]
        private string? previewStatusMessage;

        /// <summary>
        /// Currently selected ORM subgroup name (for highlighting)
        /// </summary>
        [ObservableProperty]
        private string? selectedORMSubGroupName;

        #endregion

        #region Events

        /// <summary>
        /// Raised when debounced selection is ready for processing.
        /// UI should handle actual preview loading.
        /// </summary>
        public event EventHandler<TextureSelectionReadyEventArgs>? SelectionReady;

        /// <summary>
        /// Raised when an ORM texture is selected (needs special UI handling)
        /// </summary>
        public event EventHandler<ORMTextureSelectedEventArgs>? ORMTextureSelected;

        /// <summary>
        /// Raised to request UI panel visibility changes
        /// </summary>
        public event EventHandler<PanelVisibilityRequestEventArgs>? PanelVisibilityRequested;

        #endregion

        public TextureSelectionViewModel(ILogService logService) {
            this.logService = logService;
        }

        /// <summary>
        /// Gets the current cancellation token for ongoing operations.
        /// UI can use this to cancel when calling loading methods.
        /// </summary>
        public CancellationToken GetCurrentCancellationToken() {
            lock (ctsLock) {
                return textureLoadCts?.Token ?? CancellationToken.None;
            }
        }

        /// <summary>
        /// Main command for texture selection from DataGrid.
        /// Handles debouncing and cancellation, then raises event for UI to load preview.
        /// </summary>
        [RelayCommand]
        private async Task SelectTextureAsync(BaseResource? resource, CancellationToken externalCt = default) {
            // Cancel any pending texture load immediately
            CancelPendingLoad();

            CancellationTokenSource cts;
            lock (ctsLock) {
                textureLoadCts = new CancellationTokenSource();
                cts = textureLoadCts;
            }

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, externalCt);
            var ct = linkedCts.Token;

            // Clear ORM subgroup selection when selecting a row
            if (resource != null) {
                SelectedORMSubGroupName = null;
            }

            // Debounce: wait before starting heavy texture loading
            try {
                await Task.Delay(DebounceDelayMs, ct);
            } catch (OperationCanceledException) {
                return; // Another selection happened, abort this one
            }

            if (resource == null) {
                SelectedTexture = null;
                return;
            }

            IsPreviewLoading = true;
            PreviewStatusMessage = "Loading...";

            try {
                if (resource is ORMTextureResource ormTexture) {
                    await HandleORMTextureSelectionAsync(ormTexture, ct);
                } else if (resource is TextureResource texture) {
                    await HandleTextureSelectionAsync(texture, ct);
                }
            } catch (OperationCanceledException) {
                logService.LogInfo($"[TextureSelection] Cancelled for: {resource.Name}");
            } catch (Exception ex) {
                logService.LogError($"Error in texture selection {resource.Name}: {ex.Message}");
                PreviewStatusMessage = $"Error: {ex.Message}";
            }
            // Note: IsPreviewLoading will be set to false by UI when loading completes
        }

        /// <summary>
        /// Call this from UI when preview loading completes
        /// </summary>
        public void OnPreviewLoadCompleted(bool success, string? message = null) {
            IsPreviewLoading = false;
            PreviewStatusMessage = success ? null : message;
        }

        /// <summary>
        /// Refresh the currently selected texture preview
        /// </summary>
        [RelayCommand]
        private async Task RefreshPreviewAsync(CancellationToken ct = default) {
            if (SelectedTexture == null) return;

            await SelectTextureAsync(SelectedTexture, ct);
        }

        /// <summary>
        /// Cancel any pending load operation
        /// </summary>
        public void CancelPendingLoad() {
            lock (ctsLock) {
                textureLoadCts?.Cancel();
                textureLoadCts?.Dispose();
                textureLoadCts = null;
            }
        }

        #region Private Methods

        private Task HandleORMTextureSelectionAsync(ORMTextureResource ormTexture, CancellationToken ct) {
            logService.LogInfo($"[TextureSelection] Selected ORM texture: {ormTexture.Name}");

            SelectedTexture = ormTexture;

            // Request UI to show ORM panel, hide conversion settings
            PanelVisibilityRequested?.Invoke(this, new PanelVisibilityRequestEventArgs {
                ShowORMPanel = true,
                ShowConversionSettingsPanel = false
            });

            // Raise event for UI to initialize ORM panel and load preview
            ORMTextureSelected?.Invoke(this, new ORMTextureSelectedEventArgs(ormTexture));

            bool isPacked = !string.IsNullOrEmpty(ormTexture.Path) && File.Exists(ormTexture.Path);

            // Raise selection ready event for UI to handle preview loading
            SelectionReady?.Invoke(this, new TextureSelectionReadyEventArgs(
                ormTexture,
                isORM: true,
                isPacked: isPacked,
                cancellationToken: ct
            ));

            return Task.CompletedTask;
        }

        private Task HandleTextureSelectionAsync(TextureResource texture, CancellationToken ct) {
            logService.LogInfo($"[TextureSelection] Selected texture: {texture.Name}, Path: {texture.Path ?? "NULL"}");

            SelectedTexture = texture;

            // Request UI to show conversion settings panel, hide ORM panel
            PanelVisibilityRequested?.Invoke(this, new PanelVisibilityRequestEventArgs {
                ShowORMPanel = false,
                ShowConversionSettingsPanel = true
            });

            // Raise selection ready event for UI to handle preview loading
            SelectionReady?.Invoke(this, new TextureSelectionReadyEventArgs(
                texture,
                isORM: false,
                isPacked: false,
                cancellationToken: ct
            ));

            return Task.CompletedTask;
        }

        #endregion

        #region Cleanup

        /// <summary>
        /// Call when disposing/closing to cancel any pending operations
        /// </summary>
        public void Cleanup() {
            CancelPendingLoad();
        }

        #endregion
    }

    #region Event Args

    /// <summary>
    /// Event args for when texture selection is ready after debounce
    /// </summary>
    public class TextureSelectionReadyEventArgs : EventArgs {
        public TextureResource Texture { get; }
        public bool IsORM { get; }
        public bool IsPacked { get; }
        public CancellationToken CancellationToken { get; }

        public TextureSelectionReadyEventArgs(
            TextureResource texture,
            bool isORM,
            bool isPacked,
            CancellationToken cancellationToken) {
            Texture = texture;
            IsORM = isORM;
            IsPacked = isPacked;
            CancellationToken = cancellationToken;
        }
    }

    public class ORMTextureSelectedEventArgs : EventArgs {
        public ORMTextureResource ORMTexture { get; }

        public ORMTextureSelectedEventArgs(ORMTextureResource ormTexture) {
            ORMTexture = ormTexture;
        }
    }

    public class PanelVisibilityRequestEventArgs : EventArgs {
        public bool ShowORMPanel { get; set; }
        public bool ShowConversionSettingsPanel { get; set; }
    }

    #endregion
}
