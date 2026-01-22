using AssetProcessor.Helpers;
using AssetProcessor.Resources;
using AssetProcessor.Services;
using AssetProcessor.Services.Models;
using AssetProcessor.Settings;
using AssetProcessor.ViewModels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using OxyPlot;
using OxyPlot.Axes;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives; // DragDeltaEventArgs for GridSplitter
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Xceed.Wpf.Toolkit;
using MessageBox = System.Windows.MessageBox;
using System.Linq;
using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureConversion.Settings;
using AssetProcessor.TextureViewer;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace AssetProcessor {
    public partial class MainWindow {
        private readonly SemaphoreSlim d3dTextureLoadSemaphore = new(1, 1);
        private readonly object d3dPreviewCtsLock = new();
        private CancellationTokenSource? d3dTexturePreviewCts;

        // Pending assets data when loading completes while window is inactive
        private AssetsLoadedEventArgs? _pendingAssetsData = null;

        // Alt+Tab fix: Track window active state to skip render loops when inactive
        private CancellationTokenSource? _activationCts;
        private readonly object _activationLock = new();
        private HwndSource? _hwndSource;

        // Win32 constants
        private const int WM_ACTIVATEAPP = 0x001C;
        private const int WM_ACTIVATE = 0x0006;
        private const int WA_INACTIVE = 0;

        private void SetupAltTabFix() {
            // Hook into Win32 message loop BEFORE WPF processes messages
            SourceInitialized += (s, e) => {
                _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
                _hwndSource?.AddHook(WndProcHook);
                logger.Debug($"Win32 WndProc hook installed. Initial _isWindowActive={_isWindowActive}");
            };

            Closed += (s, e) => {
                _hwndSource?.RemoveHook(WndProcHook);
                _hwndSource = null;
            };
        }

        private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
            // WM_ACTIVATEAPP fires when app gains/loses focus (before WPF events)
            if (msg == WM_ACTIVATEAPP) {
                bool isActivating = wParam != IntPtr.Zero;

                if (!isActivating) {
                    // IMMEDIATELY disable D3D11 rendering BEFORE WPF processes deactivation
                    _isWindowActive = false;
                    AssetProcessor.TextureViewer.D3D11TextureRenderer.GlobalRenderingEnabled = false;

                    // Cancel pending activation
                    lock (_activationLock) {
                        _activationCts?.Cancel();
                    }

                    bool hasPendingData = _pendingAssetsData != null;
                    bool hasPendingGridShow = _pendingDataGridShow;
                    logger.Debug($"WM_ACTIVATEAPP: DEACTIVATED. PendingData={hasPendingData}, PendingGridShow={hasPendingGridShow}");
                } else {
                    // Activation - schedule delayed re-enable
                    bool hasPendingData = _pendingAssetsData != null;
                    bool hasPendingGridShow = _pendingDataGridShow;
                    logger.Debug($"WM_ACTIVATEAPP: ACTIVATING. PendingData={hasPendingData}, PendingGridShow={hasPendingGridShow}");
                    ScheduleDelayedActivation();
                }
            }
            // WM_ACTIVATE fires when window gains/loses focus
            else if (msg == WM_ACTIVATE) {
                int activateState = (int)(wParam.ToInt64() & 0xFFFF);

                if (activateState == WA_INACTIVE) {
                    // Window is being deactivated - disable rendering immediately
                    _isWindowActive = false;
                    AssetProcessor.TextureViewer.D3D11TextureRenderer.GlobalRenderingEnabled = false;

                    lock (_activationLock) {
                        _activationCts?.Cancel();
                    }

                    logger.Debug("WM_ACTIVATE: INACTIVE (Win32 level)");
                }
            }

            return IntPtr.Zero; // Let WPF continue processing
        }

        private void ScheduleDelayedActivation() {
            lock (_activationLock) {
                _activationCts?.Cancel();
                _activationCts = new CancellationTokenSource();
            }

            var cts = _activationCts;

            // Check if there's pending data - use shorter delay if so
            bool hasPendingData = _pendingAssetsData != null || _pendingDataGridShow;

            // Start delayed activation on thread pool
            _ = Task.Run(async () => {
                try {
                    if (hasPendingData) {
                        // Shorter delay when there's pending data (200ms for basic stability)
                        logger.Debug("Using short activation delay - pending data exists");
                        await Task.Delay(200, cts.Token);
                    } else {
                        // Full delay for stable focus when no pending data (2 seconds)
                        for (int i = 0; i < 10; i++) {
                            await Task.Delay(200, cts.Token);
                        }
                    }

                    if (cts.Token.IsCancellationRequested) {
                        return;
                    }

                    // Enable rendering on UI thread
                    await Dispatcher.InvokeAsync(() => {
                        if (IsActive && !cts.Token.IsCancellationRequested) {
                            _isWindowActive = true;
                            AssetProcessor.TextureViewer.D3D11TextureRenderer.GlobalRenderingEnabled = true;

                            // Apply any deferred resize
                            D3D11TextureViewer?.ApplyPendingResize();

                            // Apply pending assets data that was deferred when window was inactive
                            if (_pendingAssetsData != null) {
                                logger.Debug("Applying deferred assets data after window activation");
                                var pendingData = _pendingAssetsData;
                                _pendingAssetsData = null;
                                ApplyAssetsToUI(pendingData);
                            }

                            // Show DataGrids that were deferred when window was inactive
                            ApplyPendingDataGridShow();

                            logger.Debug($"Render ENABLED after {(hasPendingData ? "200ms" : "2s")} stable focus");
                        }
                    });
                } catch (OperationCanceledException) {
                    // Expected when deactivated during wait
                }
            });
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e) {
            logger.Info("MainWindow loaded - D3D11 viewer ready");

            // Apply UseD3D11Preview setting on startup
            bool useD3D11 = AppSettings.Default.UseD3D11Preview;
            _ = ApplyRendererPreferenceAsync(useD3D11);
            logger.Info($"Applied UseD3D11Preview setting on startup: {useD3D11}");

            // Configure HelixViewport3D CameraController after template is applied
            viewPort3d.Loaded += (_, __) => {
                if (viewPort3d.CameraController != null) {
                    // Disable inertia completely for instant response
                    viewPort3d.CameraController.IsInertiaEnabled = false;
                    viewPort3d.CameraController.InertiaFactor = 0;

                    // Configure zoom
                    viewPort3d.CameraController.ZoomSensitivity = 1;

                    logger.Info("[HelixViewport] CameraController configured: InertiaEnabled=false, ZoomSensitivity=1");
                } else {
                    logger.Warn("[HelixViewport] CameraController is null after Loaded event");
                }
            };

            // Load saved column order for all DataGrids
            LoadColumnOrder(TexturesDataGrid);
            LoadColumnOrder(ModelsDataGrid);
            LoadColumnOrder(MaterialsDataGrid);

            // Load saved column widths for all DataGrids
            LoadColumnWidths(TexturesDataGrid);
            LoadColumnWidths(ModelsDataGrid);
            LoadColumnWidths(MaterialsDataGrid);

            // Load saved column visibility for all DataGrids
            LoadAllColumnVisibility();

            // Subscribe to column width changes for neighbor-based resizing
            SubscribeToColumnWidthChanges();

            // Restore right panel width
            RestoreRightPanelWidth();

            // Initialize dark theme checkbox state
            InitializeDarkThemeCheckBox();

            // Initialize export counts for the unified panel
            UpdateExportCounts();

            // Show texture tools panel if starting on Textures tab
            if (tabControl.SelectedItem is TabItem selectedTab && selectedTab.Header?.ToString() == "Textures") {
                TextureToolsPanel.Visibility = Visibility.Visible;
            }
        }

        private void InitializeDarkThemeCheckBox() {
            DarkThemeCheckBox.IsChecked = ThemeHelper.IsDarkTheme;
        }

        private void RestoreRightPanelWidth() {
            double savedWidth = AppSettings.Default.RightPanelWidth;
            if (savedWidth >= 256 && savedWidth <= 512) {
                PreviewColumn.Width = new GridLength(savedWidth);
                isViewerVisible = true;
                ToggleViewButton.Content = "◄";
            } else if (savedWidth <= 0) {
                // Panel was hidden
                PreviewColumn.Width = new GridLength(0);
                PreviewColumn.MinWidth = 0;
                isViewerVisible = false;
                ToggleViewButton.Content = "►";
            }
        }

        private void LoadAllColumnVisibility() {
            // Load visibility for Textures
            LoadColumnVisibility(TexturesDataGrid, nameof(AppSettings.TexturesColumnVisibility),
                (ContextMenu)FindResource("TextureColumnHeaderContextMenu"));

            // Load visibility for Models
            LoadColumnVisibility(ModelsDataGrid, nameof(AppSettings.ModelsColumnVisibility),
                (ContextMenu)FindResource("ModelColumnHeaderContextMenu"));

            // Load visibility for Materials
            LoadColumnVisibility(MaterialsDataGrid, nameof(AppSettings.MaterialsColumnVisibility),
                (ContextMenu)FindResource("MaterialColumnHeaderContextMenu"));

            // Fill remaining space after loading visibility
            FillRemainingSpaceForGrid(TexturesDataGrid);
            FillRemainingSpaceForGrid(ModelsDataGrid);
            FillRemainingSpaceForGrid(MaterialsDataGrid);
        }

        // Diagnostic counter for render loop calls
        private static int _renderLoopCallCount = 0;
        private static int _renderLoopSkippedCount = 0;

        private void OnD3D11Rendering(object? sender, EventArgs e) {
            // Skip rendering when window is inactive to prevent GPU resource conflicts
            if (!_isWindowActive) {
                return;
            }

            if (texturePreviewService.IsD3D11RenderLoopEnabled) {
                _renderLoopCallCount++;
                D3D11TextureViewer?.RenderFrame();
            } else {
                _renderLoopSkippedCount++;
                // Log every 60 skipped calls (roughly once per second at 60fps)
                if (_renderLoopSkippedCount % 60 == 1) {
                    logger.Info($"[DIAG] Render loop SKIPPED (disabled). Total skipped: {_renderLoopSkippedCount}");
                }
            }
        }

        private void TexturePreviewViewport_SizeChanged(object sender, SizeChangedEventArgs e) {
            // D3D11TextureViewerControl will handle resize automatically via OnRenderSizeChanged
        }

        // Mouse wheel zoom handler for D3D11 viewer
        // IMPORTANT: HwndHost does NOT receive WPF routed events, so we handle on parent Grid
        // CRITICAL: e.GetPosition() is also buggy for HwndHost! Use Mouse.GetPosition()!
        private void TexturePreviewViewport_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
            if (!texturePreviewService.IsUsingD3D11Renderer || D3D11TextureViewer == null || sender is not Grid grid) {
                return; // Let event bubble for scrolling
            }
            D3D11TextureViewer.HandleZoomFromWpf(e.Delta);
            e.Handled = true;
        }

        private void TexturePreviewViewport_MouseEnter(object sender, MouseEventArgs e) {
            if (TexturePreviewViewport == null) {
                return;
            }

            // Set focus on viewport to receive keyboard events
            TexturePreviewViewport.Focus();
        }

        private void TexturePreviewViewport_MouseLeave(object sender, MouseEventArgs e) {
            if (TexturePreviewViewport == null) {
                return;
            }

            if (!TexturePreviewViewport.IsKeyboardFocusWithin) {
                return;
            }

            DependencyObject focusScope = FocusManager.GetFocusScope(TexturePreviewViewport);
            if (focusScope != null) {
                FocusManager.SetFocusedElement(focusScope, null);
            }

            Keyboard.ClearFocus();
        }

        // Mouse wheel zoom handler for D3D11 viewer (WM_MOUSEWHEEL goes to parent for child windows)
        private void D3D11TextureViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
            if (!texturePreviewService.IsUsingD3D11Renderer) {
                return;
            }

            if (sender is FrameworkElement element) {
                Point position = e.GetPosition(element);
                if (position.X < 0 || position.Y < 0 || position.X > element.ActualWidth || position.Y > element.ActualHeight) {
                    return;
                }

                if (!element.IsMouseOver) {
                    return;
                }
            } else if (TexturePreviewViewport is FrameworkElement viewport && !viewport.IsMouseOver) {
                return;
            }

            D3D11TextureViewer?.HandleZoomFromWpf(e.Delta);
            e.Handled = true;
        }

        // Mouse event handlers for pan removed - now handled natively in D3D11TextureViewerControl

        /// <summary>
        /// Loads texture data into the D3D11 viewer.
        /// </summary>
        private async Task LoadTextureToD3D11ViewerAsync(BitmapSource bitmap, bool isSRGB, CancellationTokenSource loadCts) {
            CancellationToken cancellationToken = loadCts.Token;

            if (bitmap == null) {
                CompleteD3DPreviewLoad(loadCts);
                return;
            }

            if (D3D11TextureViewer?.Renderer == null) {
                CompleteD3DPreviewLoad(loadCts);
                return;
            }

            // Save current selected texture path to check if it's still valid after loading
            string? texturePathAtStart = texturePreviewService.CurrentSelectedTexture?.Path;
            bool semaphoreEntered = false;
            bool renderLoopDisabled = false;

            try {
                // Try to acquire semaphore immediately without waiting
                // If semaphore is busy, another load is in progress - skip this one
                // Using Wait(0) is non-blocking and won't cause deadlock
                bool acquired = d3dTextureLoadSemaphore.Wait(0);
                if (!acquired) {
                    CompleteD3DPreviewLoad(loadCts);
                    return;
                }
                semaphoreEntered = true;

                // Check cancellation before starting
                cancellationToken.ThrowIfCancellationRequested();

                // Temporarily disable render loop to avoid deadlock
                texturePreviewService.IsD3D11RenderLoopEnabled = false;
                renderLoopDisabled = true;

                // Perform heavy operations (format conversion and pixel copying) in background thread
                int width = bitmap.PixelWidth;
                int height = bitmap.PixelHeight;
                int stride = width * 4; // RGBA8
                byte[] pixels = new byte[stride * height];

                // Convert bitmap format on UI thread to avoid WPF dispatcher deadlocks
                // FormatConvertedBitmap internally uses Dispatcher and can deadlock if created on background thread
                FormatConvertedBitmap convertedBitmap;
                if (bitmap.Format == PixelFormats.Bgra32) {
                    // Already in correct format, use directly
                    convertedBitmap = new FormatConvertedBitmap();
                    convertedBitmap.BeginInit();
                    convertedBitmap.Source = bitmap;
                    convertedBitmap.EndInit();
                } else {
                    convertedBitmap = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                }
                convertedBitmap.Freeze(); // Freeze for safe access from background thread

                // Copy pixels in background thread (safe operation on frozen bitmap)
                // NOTE: NO NLog calls inside Task.Run - causes deadlock with UI thread!
                await Task.Run(() => {
                    cancellationToken.ThrowIfCancellationRequested();
                    convertedBitmap.CopyPixels(pixels, stride, 0);
                    // Check cancellation immediately after copy to fail fast if user switched textures
                    cancellationToken.ThrowIfCancellationRequested();
                }, cancellationToken).ConfigureAwait(false);

                // Back on thread pool thread after ConfigureAwait(false)
                // Check cancellation before scheduling UI work
                cancellationToken.ThrowIfCancellationRequested();

                var mipLevel = new MipLevel {
                    Level = 0,
                    Width = width,
                    Height = height,
                    Data = pixels,
                    RowPitch = stride
                };

                // For PNG textures, always load as non-sRGB format (R8G8B8A8_UNorm)
                // This preserves raw byte values for accurate channel visualization
                // IsSRGB field indicates whether PNG *contains* sRGB data (not GPU format)
                var textureData = new TextureData {
                    Width = width,
                    Height = height,
                    MipLevels = new List<MipLevel> { mipLevel },
                    IsSRGB = isSRGB, // True if PNG contains sRGB data (Albedo), False if Linear (Normal, Roughness)
                    HasAlpha = true,
                    IsHDR = false,
                    SourceFormat = $"PNG/RGBA8 (isSRGB={isSRGB})"
                };

                // Check cancellation before UI work
                cancellationToken.ThrowIfCancellationRequested();

                // Now schedule UI work via Dispatcher (we're on thread pool thread)
                // NOTE: NO NLog calls inside BeginInvoke - causes deadlock!
                // Capture semaphoreEntered value before clearing (closures capture by reference)
                bool needReleaseSemaphore = semaphoreEntered;
                _ = Dispatcher.BeginInvoke(new Action(() => {
                    try {
                        // Check if texture is still valid (user might have switched textures)
                        if (texturePathAtStart != null && texturePreviewService.CurrentSelectedTexture?.Path != texturePathAtStart) {
                            Trace.WriteLine($"[PNG] Texture was switched during loading, ignoring result");
                            return;
                        }

                        var viewer = D3D11TextureViewer;
                        if (viewer?.Renderer == null) {
                            Trace.WriteLine("[PNG] D3D11TextureViewer.Renderer is null!");
                            return;
                        }

                        Trace.WriteLine($"[PNG] Calling LoadTexture: {width}x{height}, sRGB={isSRGB}");
                        viewer.Renderer.LoadTexture(textureData);
                        Trace.WriteLine("[PNG] LoadTexture completed");

                        // Update format info in UI
                        if (TextureFormatTextBlock != null) {
                            string formatInfo = isSRGB ? "PNG (sRGB data)" : "PNG (Linear data)";
                            TextureFormatTextBlock.Text = $"Format: {formatInfo}";
                        }

                        // Update histogram correction button state (PNG has no metadata)
                        UpdateHistogramCorrectionButtonState();

                        // Force immediate render to update viewport with current zoom/pan
                        viewer.Renderer.Render();
                        Trace.WriteLine("[PNG] Render completed");
                    } catch (Exception ex) {
                        Trace.WriteLine($"[PNG] ERROR: {ex.Message}");
                        // Schedule error logging to avoid potential deadlock
                        Dispatcher.BeginInvoke(new Action(() => logger.Error(ex, "Error in PNG UI update")), System.Windows.Threading.DispatcherPriority.Background);
                    } finally {
                        // Release semaphore and re-enable render loop
                        if (needReleaseSemaphore) {
                            d3dTextureLoadSemaphore.Release();
                            Trace.WriteLine("[PNG] Semaphore released");
                        }

                        texturePreviewService.IsD3D11RenderLoopEnabled = true;
                        Trace.WriteLine("[PNG] Render loop re-enabled");

                        CompleteD3DPreviewLoad(loadCts);
                    }
                }));

                // Mark that we've scheduled cleanup in BeginInvoke
                semaphoreEntered = false;
                renderLoopDisabled = false;
                Trace.WriteLine("[PNG] BeginInvoke scheduled, returning");
            } catch (OperationCanceledException) {
                Trace.WriteLine("[PNG] Texture loading was cancelled");
                throw;
            } catch (Exception ex) {
                Trace.WriteLine($"[PNG] Exception: {ex.Message}");
                // Schedule error logging to avoid potential deadlock
                _ = Dispatcher.BeginInvoke(new Action(() => logger.Error(ex, "Failed to load texture to D3D11 viewer")), System.Windows.Threading.DispatcherPriority.Background);
            } finally {
                // Only release if not already transferred to BeginInvoke
                if (semaphoreEntered) {
                    d3dTextureLoadSemaphore.Release();
                    Trace.WriteLine("[PNG] Semaphore released (finally)");
                }

                if (renderLoopDisabled) {
                    texturePreviewService.IsD3D11RenderLoopEnabled = true;
                    Trace.WriteLine("[PNG] Render loop re-enabled (finally)");
                }

                // CompleteD3DPreviewLoad is called in BeginInvoke if successful
                // Only call here if we didn't reach BeginInvoke
                if (semaphoreEntered || renderLoopDisabled) {
                    CompleteD3DPreviewLoad(loadCts);
                }
            }
        }

        /// <summary>
        /// Synchronous wrapper for backward compatibility (calls async method).
        /// </summary>
        private void LoadTextureToD3D11Viewer(BitmapSource bitmap, bool isSRGB) {
            CancellationTokenSource loadCts = CreateD3DPreviewCts();

            // Start async load without waiting to avoid blocking calling thread
            // Use Dispatcher.InvokeAsync with Normal priority to prevent starvation
            // when user rapidly switches textures (Background priority was getting starved)
            _ = Dispatcher.InvokeAsync(async () => {
                try {
                    await LoadTextureToD3D11ViewerAsync(bitmap, isSRGB, loadCts);
                } catch (OperationCanceledException) {
                    logger.Info("Texture loading was cancelled (expected when switching textures)");
                } catch (Exception ex) {
                    logger.Error(ex, "Error in async LoadTextureToD3D11Viewer");
                }
            }, System.Windows.Threading.DispatcherPriority.Normal);
        }

        private int _isLoadingKtx2 = 0; // 0 = false, 1 = true (use int for Interlocked)
        private string? _currentLoadingKtx2Path = null; // Path to file being loaded

        /// <summary>
        /// Checks if KTX2 file is already loading (any or specific).
        /// </summary>
        private bool IsKtx2Loading(string? ktxPath = null) {
            int isLoading = Volatile.Read(ref _isLoadingKtx2);
            if (isLoading == 0) {
                return false;
            }

            // If specific path is provided, check if it's the same file
            if (ktxPath != null) {
                string? currentPath = Volatile.Read(ref _currentLoadingKtx2Path);
                return string.Equals(currentPath, ktxPath, StringComparison.OrdinalIgnoreCase);
            }

            // If path is not provided, just check if any file is loading
            return true;
        }

        /// <summary>
        /// Cancels any ongoing texture loading operation.
        /// </summary>
        private void CancelTextureLoading() {
            // Use the existing textureLoadCancellation field from MainWindow.xaml.cs
            var cts = textureLoadCancellation;
            if (cts != null) {
                try {
                    cts.Cancel();
                    logger.Info("Cancelled previous texture loading operation");
                } catch (ObjectDisposedException) {
                    // Already disposed, ignore
                }
            }
        }

        /// <summary>
        /// Loads KTX2 texture directly into D3D11 viewer (for native KTX2 support).
        /// </summary>
        private async Task<bool> LoadKtx2ToD3D11ViewerAsync(string ktxPath) {
            if (D3D11TextureViewer?.Renderer == null) {
                logger.Warn("D3D11 viewer or renderer is null");
                return false;
            }

            // Set loading flag to prevent concurrent loads
            int wasLoading = Interlocked.CompareExchange(ref _isLoadingKtx2, 1, 0);
            if (wasLoading != 0) {
                return false;
            }
            Volatile.Write(ref _currentLoadingKtx2Path, ktxPath);

            // CRITICAL: Disable render loop BEFORE any async operations to prevent deadlock
            // The render loop and LoadTexture both use renderLock, and CompositionTarget.Rendering
            // can fire at any time on the UI thread
            texturePreviewService.IsD3D11RenderLoopEnabled = false;

            try {
                // Use Ktx2TextureLoader to load the KTX2 file

                // Load texture data in background thread, but use ConfigureAwait(false)
                // to avoid capturing SynchronizationContext and prevent deadlock
                var textureData = await Task.Run(() => Ktx2TextureLoader.LoadFromFile(ktxPath)).ConfigureAwait(false);

                // NOTE: Removed logger.Info calls here - potential NLog deadlock when UI thread
                // and thread pool both try to log at same time during ORM selection

                // Now we're on a thread pool thread, use BeginInvoke to update UI
                // NOTE: NO NLog calls inside BeginInvoke or after it - causes deadlock with thread pool thread!
                _ = Dispatcher.BeginInvoke(new Action(() => {
                    // Render loop already disabled at method start (before Task.Run)
                    try {
                        var viewer = D3D11TextureViewer;
                        if (viewer == null) return;

                        var rendererRef = viewer.Renderer;
                        if (rendererRef == null) return;

                        rendererRef.LoadTexture(textureData);

                        // Update histogram correction button state
                        UpdateHistogramCorrectionButtonState();

                        // Update format info in UI
                        bool hasHistogram = D3D11TextureViewer.Renderer.HasHistogramMetadata();
                        if (TextureFormatTextBlock != null) {
                            string compressionFormat = textureData.CompressionFormat ?? "Unknown";
                            string srgbInfo = compressionFormat.Contains("SRGB") ? " (sRGB)" : compressionFormat.Contains("UNORM") ? " (Linear)" : "";
                            string histInfo = hasHistogram ? " + Histogram" : "";
                            TextureFormatTextBlock.Text = $"Format: KTX2/{compressionFormat}{srgbInfo}{histInfo}";
                        }

                        // Trigger immediate render to show the updated texture with preserved zoom/pan
                        D3D11TextureViewer.Renderer.Render();

                        // AUTO-ENABLE Normal reconstruction for normal maps
                        // Priority 1: Check NormalLayout metadata (for new KTX2 files with metadata)
                        // Priority 2: Check TextureType (for older KTX2 files without metadata)
                        bool shouldAutoEnableNormal = false;

                        if (textureData.NormalLayoutMetadata != null) {
                            shouldAutoEnableNormal = true;
                        } else if (texturePreviewService.CurrentSelectedTexture?.TextureType?.ToLower() == "normal") {
                            shouldAutoEnableNormal = true;
                        }

                        if (shouldAutoEnableNormal && D3D11TextureViewer?.Renderer != null) {
                            texturePreviewService.CurrentActiveChannelMask = "Normal";
                            D3D11TextureViewer.Renderer.SetChannelMask(0x20); // Normal reconstruction bit
                            D3D11TextureViewer.Renderer.Render();
                            UpdateChannelButtonsState(); // Sync button UI
                        }
                    } catch (Exception ex) {
                        // Log error AFTER BeginInvoke completes (schedule it)
                        Dispatcher.BeginInvoke(new Action(() => logger.Error(ex, "Error in KTX2 UI update")), System.Windows.Threading.DispatcherPriority.Background);
                    } finally {
                        // Re-enable render loop
                        texturePreviewService.IsD3D11RenderLoopEnabled = true;
                        // Clear loading flag
                        Volatile.Write(ref _isLoadingKtx2, 0);
                        Volatile.Write(ref _currentLoadingKtx2Path, null);
                    }
                }));

                // NOTE: NO NLog calls here - thread pool thread might call this while BeginInvoke callback runs on UI thread = DEADLOCK
                return true;
            } catch (Exception ex) {
                // Re-enable render loop on error (it was disabled at the start)
                texturePreviewService.IsD3D11RenderLoopEnabled = true;
                // Clear loading flag on error
                Volatile.Write(ref _isLoadingKtx2, 0);
                Volatile.Write(ref _currentLoadingKtx2Path, null);
                // Schedule error logging to avoid potential deadlock
                _ = Dispatcher.BeginInvoke(new Action(() => logger.Error(ex, $"Failed to load KTX2 file to D3D11 viewer: {ktxPath}")), System.Windows.Threading.DispatcherPriority.Background);
                return false;
            }
        }

        /// <summary>
        /// Clears the D3D11 viewer (equivalent to setting Image.Source = null).
        /// </summary>
        private void ClearD3D11Viewer() {
            CancelPendingD3DPreviewLoad();

            // D3D11TextureRenderer doesn't have a Clear method
            // Note: NOT resetting zoom/pan to preserve user's viewport between textures

            // Reset channel masks when clearing viewer (switching textures)
            texturePreviewService.CurrentActiveChannelMask = null;
            if (D3D11TextureViewer?.Renderer != null) {
                D3D11TextureViewer.Renderer.SetChannelMask(0xFFFFFFFF);
                D3D11TextureViewer.Renderer.RestoreOriginalGamma();
            }
            UpdateChannelButtonsState();
        }

        private CancellationTokenSource CreateD3DPreviewCts() {
            lock (d3dPreviewCtsLock) {
                d3dTexturePreviewCts?.Cancel();
                d3dTexturePreviewCts = new CancellationTokenSource();
                return d3dTexturePreviewCts;
            }
        }

        private void CompleteD3DPreviewLoad(CancellationTokenSource loadCts) {
            if (loadCts == null) {
                return;
            }

            lock (d3dPreviewCtsLock) {
                if (ReferenceEquals(d3dTexturePreviewCts, loadCts)) {
                    d3dTexturePreviewCts = null;
                }
            }

            loadCts.Dispose();
        }

        private void CancelPendingD3DPreviewLoad() {
            lock (d3dPreviewCtsLock) {
                d3dTexturePreviewCts?.Cancel();
            }
        }
    }
}


