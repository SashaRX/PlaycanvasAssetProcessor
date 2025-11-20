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

namespace AssetProcessor {
    public partial class MainWindow {
        private readonly SemaphoreSlim d3dTextureLoadSemaphore = new(1, 1);
        private readonly object d3dPreviewCtsLock = new();
        private CancellationTokenSource? d3dTexturePreviewCts;

        private void MainWindow_Loaded(object sender, RoutedEventArgs e) {
            logger.Info("MainWindow loaded - D3D11 viewer ready");

            // Apply UseD3D11Preview setting on startup
            bool useD3D11 = AppSettings.Default.UseD3D11Preview;
            _ = ApplyRendererPreferenceAsync(useD3D11);
            logger.Info($"Applied UseD3D11Preview setting on startup: {useD3D11}");
        }

        private void OnD3D11Rendering(object? sender, EventArgs e) {
            if (texturePreviewService.IsD3D11RenderLoopEnabled) {
                D3D11TextureViewer?.RenderFrame();
            }
        }

        private void TexturePreviewViewport_SizeChanged(object sender, SizeChangedEventArgs e) {
            // D3D11TextureViewerControl will handle resize automatically via OnRenderSizeChanged

            if (lastPreviewTextureWidth > 0 && lastPreviewTextureHeight > 0) {
                AdjustPreviewHeightToTextureAspect(lastPreviewTextureWidth, lastPreviewTextureHeight);
            }
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
            logger.Info($"LoadTextureToD3D11Viewer called: bitmap={bitmap?.PixelWidth}x{bitmap?.PixelHeight}, isSRGB={isSRGB}");

            if (bitmap == null) {
                logger.Warn("Bitmap is null");
                CompleteD3DPreviewLoad(loadCts);
                return;
            }

            if (D3D11TextureViewer?.Renderer == null) {
                logger.Warn("D3D11 viewer or renderer is null");
                CompleteD3DPreviewLoad(loadCts);
                return;
            }

            // Save current selected texture path to check if it's still valid after loading
            string? texturePathAtStart = texturePreviewService.CurrentSelectedTexture?.Path;
            bool semaphoreEntered = false;
            bool renderLoopDisabled = false;

            try {
                logger.Info("Waiting for D3D11 texture preview semaphore...");
                await d3dTextureLoadSemaphore.WaitAsync(cancellationToken);
                semaphoreEntered = true;

                logger.Info("D3D11TextureViewer and Renderer are not null, proceeding...");

                // Check cancellation before starting
                cancellationToken.ThrowIfCancellationRequested();

                // Temporarily disable render loop to avoid deadlock
                texturePreviewService.IsD3D11RenderLoopEnabled = false;
                renderLoopDisabled = true;
                logger.Info("Render loop disabled");

                // Perform heavy operations (format conversion and pixel copying) in background thread
                int width = bitmap.PixelWidth;
                int height = bitmap.PixelHeight;
                int stride = width * 4; // RGBA8
                byte[] pixels = new byte[stride * height];

                // Freeze bitmap for safe access from another thread
                if (!bitmap.IsFrozen) {
                    bitmap.Freeze();
                }

                // Convert and copy pixels in background thread
                logger.Info("Starting pixel conversion in background thread...");
                await Task.Run(() => {
                    cancellationToken.ThrowIfCancellationRequested();
                    logger.Info("Converting bitmap to BGRA32 format...");
                    // Convert to BGRA32 (which is actually RGBA in memory)
                    var convertedBitmap = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                    convertedBitmap.Freeze(); // Freeze for safe access
                    logger.Info("Copying pixels from bitmap...");
                    convertedBitmap.CopyPixels(pixels, stride, 0);
                    logger.Info("Pixel conversion completed");
                }, cancellationToken);

                logger.Info("Pixel conversion task completed, checking cancellation...");
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

                logger.Info($"About to call D3D11TextureRenderer.LoadTexture: {width}x{height}, sRGB={isSRGB}");

                // Allow UI thread to process other messages before heavy operation
                logger.Info("Yielding to UI thread...");
                await Task.Yield();

                logger.Info("Checking cancellation before Dispatcher.InvokeAsync...");
                cancellationToken.ThrowIfCancellationRequested();

                // LoadTexture must be called in UI thread (requires D3D11 context)
                // Use Normal priority instead of Background to avoid hanging when UI thread is busy
                // Background priority can be delayed indefinitely if UI thread is processing higher priority messages
                logger.Info("Invoking LoadTexture on UI thread with Normal priority...");
                await Dispatcher.InvokeAsync(() => {
                    logger.Info("Inside Dispatcher.InvokeAsync, checking cancellation...");
                    cancellationToken.ThrowIfCancellationRequested();

                    // Check if texture is still valid (user might have switched textures)
                    if (texturePathAtStart != null && texturePreviewService.CurrentSelectedTexture?.Path != texturePathAtStart) {
                        logger.Info($"Texture was switched during loading (from {texturePathAtStart} to {texturePreviewService.CurrentSelectedTexture?.Path}), ignoring result");
                        return;
                    }

                    if (D3D11TextureViewer?.Renderer == null) {
                        logger.Error("D3D11TextureViewer.Renderer is null!");
                        return;
                    }

                    logger.Info("Calling LoadTexture on renderer...");
                    D3D11TextureViewer.Renderer.LoadTexture(textureData);
                    D3D11TextureViewer.Renderer.SetTiling(texturePreviewService.IsTilingEnabled);
                    logger.Info($"D3D11TextureRenderer.LoadTexture completed successfully");

                    // Update format info in UI
                    if (TextureFormatTextBlock != null) {
                        string formatInfo = isSRGB ? "PNG (sRGB data)" : "PNG (Linear data)";
                        TextureFormatTextBlock.Text = $"Format: {formatInfo}";
                    }

                    // Update histogram correction button state (PNG has no metadata)
                    UpdateHistogramCorrectionButtonState();
                }, System.Windows.Threading.DispatcherPriority.Normal);

                // Note: NOT resetting zoom/pan to preserve user's viewport when switching sources
            } catch (OperationCanceledException) {
                logger.Info("Texture loading was cancelled");
                throw;
            } catch (Exception ex) {
                logger.Error(ex, "Failed to load texture to D3D11 viewer");
            } finally {
                if (semaphoreEntered) {
                    d3dTextureLoadSemaphore.Release();
                }

                if (renderLoopDisabled) {
                    texturePreviewService.IsD3D11RenderLoopEnabled = true;
                    logger.Info("Render loop re-enabled");
                }

                // Force immediate render to update viewport with current zoom/pan (only if not cancelled and texture is still valid)
                if (!cancellationToken.IsCancellationRequested &&
                    (texturePathAtStart == null || texturePreviewService.CurrentSelectedTexture?.Path == texturePathAtStart)) {
                    await Dispatcher.InvokeAsync(() => {
                        D3D11TextureViewer?.Renderer?.Render();
                        logger.Info("Forced render to apply current zoom/pan");
                    }, System.Windows.Threading.DispatcherPriority.Normal);
                } else if (texturePathAtStart != null && texturePreviewService.CurrentSelectedTexture?.Path != texturePathAtStart) {
                    logger.Info("Skipping render - texture was switched during loading");
                }

                CompleteD3DPreviewLoad(loadCts);
            }
        }

        /// <summary>
        /// Synchronous wrapper for backward compatibility (calls async method).
        /// </summary>
        private void LoadTextureToD3D11Viewer(BitmapSource bitmap, bool isSRGB) {
            CancellationTokenSource loadCts = CreateD3DPreviewCts();

            // Start async load without waiting to avoid blocking calling thread
            // Use Dispatcher.InvokeAsync instead of Task.Run to avoid deadlocks when UI thread is busy
            // This ensures the async operation is scheduled on the UI thread's message queue
            _ = Dispatcher.InvokeAsync(async () => {
                try {
                    await LoadTextureToD3D11ViewerAsync(bitmap, isSRGB, loadCts);
                } catch (OperationCanceledException) {
                    logger.Info("Texture loading was cancelled (expected when switching textures)");
                } catch (Exception ex) {
                    logger.Error(ex, "Error in async LoadTextureToD3D11Viewer");
                }
            }, System.Windows.Threading.DispatcherPriority.Background);
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

            // Atomic check and set flag to prevent duplicate loading
            // Use CompareExchange for atomic check and set to avoid TOCTOU
            // Try to set flag to 1 if it was 0 (atomic operation)
            int wasLoadingBefore = Interlocked.CompareExchange(ref _isLoadingKtx2, 1, 0);
            
            // Flag indicating if we actually started loading
            // This is needed for proper flag reset in finally block
            bool loadStarted = false;
            
            if (wasLoadingBefore != 0) {
                // Already loading - check path for more informative message
                string? currentPath = Volatile.Read(ref _currentLoadingKtx2Path);
                if (currentPath != null && string.Equals(currentPath, ktxPath, StringComparison.OrdinalIgnoreCase)) {
                    logger.Warn($"KTX2 file already loading: {ktxPath}, skipping duplicate load");
                } else {
                    logger.Warn($"Another KTX2 file is loading: {currentPath}, skipping load of: {ktxPath}");
                }
                // Important: do not reset flag, as another thread set it and should reset it
                return false;
            }

            // Successfully set loading flag atomically - set path and loading start flag
            // Path setting doesn't require atomicity, as flag already protects from concurrent access
            Interlocked.Exchange(ref _currentLoadingKtx2Path, ktxPath);
            loadStarted = true;

            try {
                // Use Ktx2TextureLoader to load the KTX2 file
                logger.Info($"Loading KTX2 file to D3D11 viewer: {ktxPath}");
                var textureData = await Task.Run(() => Ktx2TextureLoader.LoadFromFile(ktxPath));

                await Dispatcher.InvokeAsync(() => {
                    D3D11TextureViewer.Renderer.LoadTexture(textureData);
                    D3D11TextureViewer.Renderer.SetTiling(texturePreviewService.IsTilingEnabled);
                    logger.Info($"Loaded KTX2 to D3D11 viewer: {textureData.Width}x{textureData.Height}, {textureData.MipCount} mips");

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
                    logger.Info("Forced render to apply current zoom/pan after KTX2 load");

                    // AUTO-ENABLE Normal reconstruction for normal maps
                    // Priority 1: Check NormalLayout metadata (for new KTX2 files with metadata)
                    // Priority 2: Check TextureType (for older KTX2 files without metadata)
                    bool shouldAutoEnableNormal = false;
                    string autoEnableReason = "";

                    if (textureData.NormalLayoutMetadata != null) {
                        shouldAutoEnableNormal = true;
                        autoEnableReason = $"KTX2 normal map with metadata (layout: {textureData.NormalLayoutMetadata.Layout})";
                    } else if (texturePreviewService.CurrentSelectedTexture?.TextureType?.ToLower() == "normal") {
                        shouldAutoEnableNormal = true;
                        autoEnableReason = "KTX2 normal map detected by TextureType (no metadata)";
                    }

                    if (shouldAutoEnableNormal && D3D11TextureViewer?.Renderer != null) {
                        texturePreviewService.CurrentActiveChannelMask = "Normal";
                        D3D11TextureViewer.Renderer.SetChannelMask(0x20); // Normal reconstruction bit
                        D3D11TextureViewer.Renderer.Render();
                        UpdateChannelButtonsState(); // Sync button UI
                        logger.Info($"Auto-enabled Normal reconstruction mode for {autoEnableReason}");
                    }
                });

                return true;
            } catch (Exception ex) {
                logger.Error(ex, $"Failed to load KTX2 file to D3D11 viewer: {ktxPath}");
                return false;
            } finally {
                // Atomically reset flag only if we actually started loading
                // If we returned earlier due to race condition detection, flags are already restored
                // and should not be reset here, otherwise it will allow other threads to bypass protection
                if (loadStarted) {
                    Interlocked.Exchange(ref _isLoadingKtx2, 0);
                    Interlocked.Exchange(ref _currentLoadingKtx2Path, null);
                }
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


