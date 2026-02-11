using AssetProcessor.TextureViewer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AssetProcessor {
    /// <summary>
    /// D3D11 texture loading: PNG bitmap loading, native KTX2 loading, CTS management.
    /// </summary>
    public partial class MainWindow {
        private readonly SemaphoreSlim d3dTextureLoadSemaphore = new(1, 1);
        private readonly object d3dPreviewCtsLock = new();
        private CancellationTokenSource? d3dTexturePreviewCts;

        private int _isLoadingKtx2 = 0;
        private string? _currentLoadingKtx2Path = null;

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

            string? texturePathAtStart = texturePreviewService.CurrentSelectedTexture?.Path;
            bool semaphoreEntered = false;
            bool renderLoopDisabled = false;

            try {
                bool acquired = d3dTextureLoadSemaphore.Wait(0);
                if (!acquired) {
                    CompleteD3DPreviewLoad(loadCts);
                    return;
                }
                semaphoreEntered = true;

                cancellationToken.ThrowIfCancellationRequested();

                texturePreviewService.IsD3D11RenderLoopEnabled = false;
                renderLoopDisabled = true;

                int width = bitmap.PixelWidth;
                int height = bitmap.PixelHeight;
                int stride = width * 4;
                byte[] pixels = new byte[stride * height];

                FormatConvertedBitmap convertedBitmap;
                if (bitmap.Format == PixelFormats.Bgra32) {
                    convertedBitmap = new FormatConvertedBitmap();
                    convertedBitmap.BeginInit();
                    convertedBitmap.Source = bitmap;
                    convertedBitmap.EndInit();
                } else {
                    convertedBitmap = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                }
                convertedBitmap.Freeze();

                await Task.Run(() => {
                    cancellationToken.ThrowIfCancellationRequested();
                    convertedBitmap.CopyPixels(pixels, stride, 0);
                    cancellationToken.ThrowIfCancellationRequested();
                }, cancellationToken).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                var mipLevel = new MipLevel {
                    Level = 0,
                    Width = width,
                    Height = height,
                    Data = pixels,
                    RowPitch = stride
                };

                var textureData = new TextureData {
                    Width = width,
                    Height = height,
                    MipLevels = new List<MipLevel> { mipLevel },
                    IsSRGB = isSRGB,
                    HasAlpha = true,
                    IsHDR = false,
                    SourceFormat = $"PNG/RGBA8 (isSRGB={isSRGB})"
                };

                cancellationToken.ThrowIfCancellationRequested();

                bool needReleaseSemaphore = semaphoreEntered;
                _ = Dispatcher.BeginInvoke(new Action(() => {
                    try {
                        if (texturePathAtStart != null && texturePreviewService.CurrentSelectedTexture?.Path != texturePathAtStart) {
                            Trace.WriteLine($"[PNG] Texture was switched during loading, ignoring result");
                            return;
                        }

                        var viewer = D3D11TextureViewer;
                        if (viewer?.Renderer == null) return;

                        viewer.Renderer.LoadTexture(textureData);

                        if (TextureFormatTextBlock != null) {
                            string formatInfo = isSRGB ? "PNG (sRGB data)" : "PNG (Linear data)";
                            TextureFormatTextBlock.Text = $"Format: {formatInfo}";
                        }

                        UpdateHistogramCorrectionButtonState();
                        viewer.Renderer.Render();
                    } catch (Exception ex) {
                        Trace.WriteLine($"[PNG] ERROR: {ex.Message}");
                        Dispatcher.BeginInvoke(new Action(() => logger.Error(ex, "Error in PNG UI update")), System.Windows.Threading.DispatcherPriority.Background);
                    } finally {
                        if (needReleaseSemaphore) {
                            d3dTextureLoadSemaphore.Release();
                        }
                        texturePreviewService.IsD3D11RenderLoopEnabled = true;
                        CompleteD3DPreviewLoad(loadCts);
                    }
                }));

                semaphoreEntered = false;
                renderLoopDisabled = false;
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                _ = Dispatcher.BeginInvoke(new Action(() => logger.Error(ex, "Failed to load texture to D3D11 viewer")), System.Windows.Threading.DispatcherPriority.Background);
            } finally {
                if (semaphoreEntered) {
                    d3dTextureLoadSemaphore.Release();
                }
                if (renderLoopDisabled) {
                    texturePreviewService.IsD3D11RenderLoopEnabled = true;
                }
                if (semaphoreEntered || renderLoopDisabled) {
                    CompleteD3DPreviewLoad(loadCts);
                }
            }
        }

        /// <summary>
        /// Synchronous wrapper for backward compatibility.
        /// </summary>
        private void LoadTextureToD3D11Viewer(BitmapSource bitmap, bool isSRGB) {
            CancellationTokenSource loadCts = CreateD3DPreviewCts();

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

        /// <summary>
        /// Checks if KTX2 file is already loading.
        /// </summary>
        private bool IsKtx2Loading(string? ktxPath = null) {
            int isLoading = Volatile.Read(ref _isLoadingKtx2);
            if (isLoading == 0) return false;

            if (ktxPath != null) {
                string? currentPath = Volatile.Read(ref _currentLoadingKtx2Path);
                return string.Equals(currentPath, ktxPath, StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }

        /// <summary>
        /// Cancels any ongoing texture loading operation.
        /// </summary>
        private void CancelTextureLoading() {
            var cts = textureLoadCancellation;
            if (cts != null) {
                try {
                    cts.Cancel();
                    logger.Info("Cancelled previous texture loading operation");
                } catch (ObjectDisposedException) { }
            }
        }

        /// <summary>
        /// Loads KTX2 texture directly into D3D11 viewer.
        /// </summary>
        private async Task<bool> LoadKtx2ToD3D11ViewerAsync(string ktxPath) {
            if (D3D11TextureViewer?.Renderer == null) {
                return false;
            }

            int wasLoading = Interlocked.CompareExchange(ref _isLoadingKtx2, 1, 0);
            if (wasLoading != 0) return false;
            Volatile.Write(ref _currentLoadingKtx2Path, ktxPath);

            texturePreviewService.IsD3D11RenderLoopEnabled = false;

            try {
                var textureData = await Task.Run(() => Ktx2TextureLoader.LoadFromFile(ktxPath)).ConfigureAwait(false);

                _ = Dispatcher.BeginInvoke(new Action(() => {
                    try {
                        var viewer = D3D11TextureViewer;
                        if (viewer?.Renderer == null) return;

                        viewer.Renderer.LoadTexture(textureData);
                        UpdateHistogramCorrectionButtonState();

                        bool hasHistogram = viewer.Renderer.HasHistogramMetadata();
                        if (TextureFormatTextBlock != null) {
                            string compressionFormat = textureData.CompressionFormat ?? "Unknown";
                            string srgbInfo = compressionFormat.Contains("SRGB") ? " (sRGB)" : compressionFormat.Contains("UNORM") ? " (Linear)" : "";
                            string histInfo = hasHistogram ? " + Histogram" : "";
                            TextureFormatTextBlock.Text = $"Format: KTX2/{compressionFormat}{srgbInfo}{histInfo}";
                        }

                        viewer.Renderer.Render();

                        // Auto-enable Normal reconstruction for normal maps
                        bool shouldAutoEnableNormal = textureData.NormalLayoutMetadata != null
                            || texturePreviewService.CurrentSelectedTexture?.TextureType?.ToLower() == "normal";

                        if (shouldAutoEnableNormal && viewer.Renderer != null) {
                            texturePreviewService.CurrentActiveChannelMask = "Normal";
                            viewer.Renderer.SetChannelMask(0x20);
                            viewer.Renderer.Render();
                            UpdateChannelButtonsState();
                        }
                    } catch (Exception ex) {
                        Dispatcher.BeginInvoke(new Action(() => logger.Error(ex, "Error in KTX2 UI update")), System.Windows.Threading.DispatcherPriority.Background);
                    } finally {
                        texturePreviewService.IsD3D11RenderLoopEnabled = true;
                        Volatile.Write(ref _isLoadingKtx2, 0);
                        Volatile.Write(ref _currentLoadingKtx2Path, null);
                    }
                }));

                return true;
            } catch (Exception ex) {
                texturePreviewService.IsD3D11RenderLoopEnabled = true;
                Volatile.Write(ref _isLoadingKtx2, 0);
                Volatile.Write(ref _currentLoadingKtx2Path, null);
                _ = Dispatcher.BeginInvoke(new Action(() => logger.Error(ex, $"Failed to load KTX2: {ktxPath}")), System.Windows.Threading.DispatcherPriority.Background);
                return false;
            }
        }

        /// <summary>
        /// Clears the D3D11 viewer.
        /// </summary>
        private void ClearD3D11Viewer() {
            CancelPendingD3DPreviewLoad();

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
            if (loadCts == null) return;

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
