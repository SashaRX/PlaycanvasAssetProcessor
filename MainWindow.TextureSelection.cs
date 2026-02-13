using AssetProcessor.Resources;
using AssetProcessor.Services.Models;
using AssetProcessor.TextureViewer;
using AssetProcessor.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace AssetProcessor {
    /// <summary>
    /// Partial class containing TextureSelectionViewModel event handlers
    /// and texture preview loading methods.
    /// </summary>
    public partial class MainWindow {

        #region TextureSelectionViewModel Event Handlers

        /// <summary>
        /// Handles panel visibility changes requested by TextureSelectionViewModel
        /// </summary>
        private void OnPanelVisibilityRequested(object? sender, PanelVisibilityRequestEventArgs e) {
            viewModel.IsConversionSettingsVisible = e.ShowConversionSettingsPanel;
            viewModel.IsORMPanelVisible = e.ShowORMPanel;
        }

        /// <summary>
        /// Handles ORM texture selection - initializes ORM panel with available textures
        /// </summary>
        private void OnORMTextureSelected(object? sender, ORMTextureSelectedEventArgs e) {
            logService.LogInfo($"[OnORMTextureSelected] Initializing ORM panel for: {e.ORMTexture.Name}");

            if (ORMPanel != null) {
                // Initialize ORM panel with available textures (exclude other ORM textures)
                var availableTextures = viewModel.Textures.Where(t => !(t is ORMTextureResource)).ToList();
                logService.LogInfo($"[OnORMTextureSelected] availableTextures count: {availableTextures.Count}");
                ORMPanel.Initialize(this, availableTextures);
                ORMPanel.SetORMTexture(e.ORMTexture);
                logService.LogInfo($"[OnORMTextureSelected] ORMPanel initialized and texture set");
            } else {
                logService.LogInfo($"[OnORMTextureSelected] ERROR: ORMPanel is NULL!");
            }
        }

        /// <summary>
        /// Handles debounced texture selection - performs actual preview loading
        /// </summary>
        private async void OnTextureSelectionReady(object? sender, TextureSelectionReadyEventArgs e) {
            var ct = e.CancellationToken;

            try {
                if (e.IsORM) {
                    await LoadORMTexturePreviewAsync((ORMTextureResource)e.Texture, e.IsPacked, ct);
                } else {
                    await LoadTexturePreviewAsync(e.Texture, ct);
                }

                viewModel.TextureSelection.OnPreviewLoadCompleted(true);
            } catch (OperationCanceledException) {
                logService.LogInfo($"[OnTextureSelectionReady] Cancelled for: {e.Texture.Name}");
            } catch (Exception ex) {
                logService.LogError($"[OnTextureSelectionReady] Error loading texture {e.Texture.Name}: {ex.Message}");
                viewModel.TextureSelection.OnPreviewLoadCompleted(false, ex.Message);
            }
        }

        #endregion

        #region Texture Preview Loading

        /// <summary>
        /// Loads preview for ORM texture (packed or unpacked)
        /// </summary>
        private async Task LoadORMTexturePreviewAsync(ORMTextureResource ormTexture, bool isPacked, CancellationToken ct) {
            logService.LogInfo($"[LoadORMTexturePreview] Loading preview for ORM: {ormTexture.Name}, isPacked: {isPacked}");

            // Reset preview state
            ResetPreviewState();
            ClearD3D11Viewer();

            // Update texture info
            viewModel.TextureInfoName = "Texture Name: " + ormTexture.Name;
            viewModel.TextureInfoColorSpace = "Color Space: Linear (ORM)";

            if (!isPacked || string.IsNullOrEmpty(ormTexture.Path)) {
                // Not packed yet - show info
                viewModel.TextureInfoResolution = "Resolution: Not packed yet";
                viewModel.TextureInfoSize = "Size: N/A";
                viewModel.TextureInfoFormat = "Format: Not packed";
                return;
            }

            // Load the packed KTX2 file for preview and histogram
            bool ktxLoaded = false;

            if (texturePreviewService.IsUsingD3D11Renderer) {
                // D3D11 MODE: Try native KTX2 loading
                logService.LogInfo($"[LoadORMTexturePreview] Loading packed ORM to D3D11: {ormTexture.Name}");
                ktxLoaded = await TryLoadKtx2ToD3D11Async(ormTexture, ct);

                if (!ktxLoaded) {
                    // Fallback: Try extracting PNG from KTX2
                    logService.LogInfo($"[LoadORMTexturePreview] D3D11 native loading failed, trying PNG extraction");
                    ktxLoaded = await TryLoadKtx2PreviewAsync(ormTexture, ct);
                }
            } else {
                // WPF MODE: Extract PNG from KTX2
                ktxLoaded = await TryLoadKtx2PreviewAsync(ormTexture, ct);
            }

            // Extract histogram for packed ORM textures
            if (ktxLoaded && !ct.IsCancellationRequested) {
                string? ormPath = ormTexture.Path;
                string ormName = ormTexture.Name ?? "unknown";
                logger.Info($"[ORM Histogram] Starting extraction for: {ormName}, path: {ormPath}");

                _ = Task.Run(async () => {
                    try {
                        if (string.IsNullOrEmpty(ormPath)) {
                            logger.Warn($"[ORM Histogram] Path is empty for: {ormName}");
                            return;
                        }

                        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                        var mipmaps = await texturePreviewService.LoadKtx2MipmapsAsync(ormPath, linkedCts.Token).ConfigureAwait(false);
                        logger.Info($"[ORM Histogram] Extracted {mipmaps.Count} mipmaps for: {ormName}");

                        if (mipmaps.Count > 0 && !linkedCts.Token.IsCancellationRequested) {
                            var mip0Bitmap = mipmaps[0].Bitmap;
                            logger.Info($"[ORM Histogram] Got mip0 bitmap {mip0Bitmap.PixelWidth}x{mip0Bitmap.PixelHeight}");
                            _ = Dispatcher.BeginInvoke(new Action(() => {
                                if (!ct.IsCancellationRequested) {
                                    texturePreviewService.OriginalFileBitmapSource = mip0Bitmap;
                                    UpdateHistogram(mip0Bitmap);
                                    logger.Info($"[ORM Histogram] Histogram updated for: {ormName}");
                                }
                            }));
                        }
                    } catch (OperationCanceledException) {
                        logger.Info($"[ORM Histogram] Extraction cancelled/timeout for: {ormName}");
                    } catch (Exception ex) {
                        logger.Warn(ex, $"[ORM Histogram] Failed to extract for: {ormName}");
                    }
                });
            }

            if (!ktxLoaded) {
                _ = Dispatcher.BeginInvoke(new Action(() => {
                    if (ct.IsCancellationRequested) return;
                    texturePreviewService.IsKtxPreviewAvailable = false;
                    viewModel.TextureInfoFormat = "Format: KTX2 (preview unavailable)";
                    logService.LogWarn($"Failed to load preview for packed ORM texture: {ormTexture.Name}");
                }));
            }
        }

        /// <summary>
        /// Loads preview for regular texture (PNG/JPG source)
        /// </summary>
        private async Task LoadTexturePreviewAsync(TextureResource texture, CancellationToken ct) {
            logService.LogInfo($"[LoadTexturePreview] Loading preview for: {texture.Name}, Path: {texture.Path ?? "NULL"}");

            ResetPreviewState();
            ClearD3D11Viewer();

            if (string.IsNullOrEmpty(texture.Path)) {
                return;
            }

            // Update texture info
            viewModel.TextureInfoName = "Texture Name: " + texture.Name;
            viewModel.TextureInfoResolution = "Resolution: " + string.Join("x", texture.Resolution);
            AssetProcessor.Helpers.SizeConverter sizeConverter = new();
            object size = AssetProcessor.Helpers.SizeConverter.Convert(texture.Size) ?? "Unknown size";
            viewModel.TextureInfoSize = "Size: " + size;

            // Add color space info
            bool isSRGB = IsSRGBTexture(texture);
            string colorSpace = isSRGB ? "sRGB" : "Linear";
            string textureType = texture.TextureType ?? "Unknown";
            viewModel.TextureInfoColorSpace = $"Color Space: {colorSpace} ({textureType})";
            viewModel.TextureInfoFormat = "Format: Loading...";

            // Load conversion settings for this texture
            logService.LogInfo($"[LoadTexturePreview] Loading conversion settings for: {texture.Name}");
            LoadTextureConversionSettings(texture);

            ct.ThrowIfCancellationRequested();

            bool ktxLoaded = false;

            if (texturePreviewService.IsUsingD3D11Renderer) {
                // D3D11 MODE: Try D3D11 native KTX2 loading
                logService.LogInfo($"[LoadTexturePreview] Attempting KTX2 load for: {texture.Name}");
                ktxLoaded = await TryLoadKtx2ToD3D11Async(texture, ct);
                logService.LogInfo($"[LoadTexturePreview] KTX2 load result: {ktxLoaded}");

                if (ktxLoaded) {
                    // KTX2 loaded successfully, still load source for histogram
                    bool showInViewer = (texturePreviewService.CurrentPreviewSourceMode == TexturePreviewSourceMode.Source);
                    logService.LogInfo($"[LoadTexturePreview] Loading source for histogram, showInViewer: {showInViewer}");
                    await LoadSourcePreviewAsync(texture, ct, loadToViewer: showInViewer);
                } else {
                    // No KTX2 or failed, fallback to source preview
                    logService.LogInfo($"[LoadTexturePreview] No KTX2, loading source preview");
                    await LoadSourcePreviewAsync(texture, ct, loadToViewer: true);
                }
            } else {
                // WPF MODE: Use PNG extraction for mipmaps
                Task<bool> ktxPreviewTask = TryLoadKtx2PreviewAsync(texture, ct);
                await LoadSourcePreviewAsync(texture, ct, loadToViewer: true);
                ktxLoaded = await ktxPreviewTask;
            }

            if (!ktxLoaded) {
                _ = Dispatcher.BeginInvoke(new Action(() => {
                    if (ct.IsCancellationRequested) return;

                    texturePreviewService.IsKtxPreviewAvailable = false;

                    if (!texturePreviewService.IsUserPreviewSelection && texturePreviewService.CurrentPreviewSourceMode == TexturePreviewSourceMode.Ktx2) {
                        SetPreviewSourceMode(TexturePreviewSourceMode.Source, initiatedByUser: false);
                    } else {
                        UpdatePreviewSourceControls();
                    }
                }));
            }
        }

        /// <summary>
        /// Load KTX2 directly to D3D11 viewer (native format, all mipmaps).
        /// </summary>
        private async Task<bool> TryLoadKtx2ToD3D11Async(TextureResource selectedTexture, CancellationToken cancellationToken) {
            string? ktxPath = texturePreviewService.GetExistingKtx2Path(selectedTexture.Path, ProjectFolderPath);
            if (ktxPath == null) {
                logger.Info($"KTX2 file not found for: {selectedTexture.Path}");
                return false;
            }

            logger.Info($"Found KTX2 file: {ktxPath}");

            try {
                // Load KTX2 directly to D3D11 (no PNG extraction)
                logger.Info($"[TryLoadKtx2ToD3D11Async] Calling LoadKtx2ToD3D11ViewerAsync for: {ktxPath}");
                bool loaded = await LoadKtx2ToD3D11ViewerAsync(ktxPath);
                logger.Info($"[TryLoadKtx2ToD3D11Async] LoadKtx2ToD3D11ViewerAsync returned: {loaded}, cancelled: {cancellationToken.IsCancellationRequested}");

                if (!loaded || cancellationToken.IsCancellationRequested) {
                    logger.Warn($"Failed to load KTX2 to D3D11 viewer: {ktxPath}");
                    return false;
                }

                logger.Info($"Loaded KTX2 directly to D3D11 viewer: {ktxPath}");

                // Use BeginInvoke (fire-and-forget) to avoid deadlock when UI thread is busy
                _ = Dispatcher.BeginInvoke(new Action(() => {
                    if (cancellationToken.IsCancellationRequested) {
                        return;
                    }

                    // Save current loaded texture paths for preview renderer switching
                    texturePreviewService.CurrentLoadedTexturePath = selectedTexture.Path;
                    texturePreviewService.CurrentLoadedKtx2Path = ktxPath;

                    // Mark KTX2 preview as available
                    texturePreviewService.IsKtxPreviewAvailable = true;
                    texturePreviewService.IsKtxPreviewActive = true;

                    // Clear old mipmap data (we're using D3D11 native mipmaps now)
                    texturePreviewService.CurrentKtxMipmaps?.Clear();
                    texturePreviewService.CurrentMipLevel = 0;

                    // Update UI to show KTX2 is active
                    if (!texturePreviewService.IsUserPreviewSelection || texturePreviewService.CurrentPreviewSourceMode == TexturePreviewSourceMode.Ktx2) {
                        SetPreviewSourceMode(TexturePreviewSourceMode.Ktx2, initiatedByUser: false);
                    } else {
                        UpdatePreviewSourceControls();
                    }
                }));

                return true;
            } catch (OperationCanceledException) {
                return false;
            } catch (Exception ex) {
                logger.Warn(ex, $"Failed to load KTX2 preview: {ktxPath}");
                return false;
            }
        }

        /// <summary>
        /// Extract KTX2 mipmaps to PNG files and load them.
        /// Used when useD3D11NativeKtx2 = false.
        /// </summary>
        private async Task<bool> TryLoadKtx2PreviewAsync(TextureResource selectedTexture, CancellationToken cancellationToken) {
            string? ktxPath = texturePreviewService.GetExistingKtx2Path(selectedTexture.Path, ProjectFolderPath);
            if (ktxPath == null) {
                logger.Info($"KTX2 file not found for: {selectedTexture.Path}");
                return false;
            }

            logger.Info($"Found KTX2 file: {ktxPath}");

            try {
                // OLD METHOD: Extract to PNG files
                List<KtxMipLevel> mipmaps = await texturePreviewService.LoadKtx2MipmapsAsync(ktxPath, cancellationToken);
                if (mipmaps.Count == 0 || cancellationToken.IsCancellationRequested) {
                    logger.Warn($"Failed to extract mipmaps from KTX2: {ktxPath}");
                    return false;
                }

                logger.Info($"Extracted {mipmaps.Count} mipmaps from KTX2");

                // Use BeginInvoke to avoid deadlock
                _ = Dispatcher.BeginInvoke(new Action(() => {
                    if (cancellationToken.IsCancellationRequested) {
                        return;
                    }

                    texturePreviewService.CurrentKtxMipmaps.Clear();
                    foreach (var mipmap in mipmaps) {
                        texturePreviewService.CurrentKtxMipmaps.Add(mipmap);
                    }
                    texturePreviewService.CurrentMipLevel = 0;
                    texturePreviewService.IsKtxPreviewAvailable = true;

                    if (!texturePreviewService.IsUserPreviewSelection || texturePreviewService.CurrentPreviewSourceMode == TexturePreviewSourceMode.Ktx2) {
                        SetPreviewSourceMode(TexturePreviewSourceMode.Ktx2, initiatedByUser: false);
                    } else {
                        UpdatePreviewSourceControls();
                    }
                }));

                return true;
            } catch (OperationCanceledException) {
                return false;
            } catch (Exception ex) {
                logger.Warn(ex, $"Failed to load KTX2 preview: {ktxPath}");
                return false;
            }
        }

        private async Task LoadSourcePreviewAsync(TextureResource selectedTexture, CancellationToken cancellationToken, bool loadToViewer = true) {
            // Reset channel masks when loading new texture
            // BUT: Don't reset if Normal mode was auto-enabled for normal maps
            if (texturePreviewService.CurrentActiveChannelMask != "Normal") {
                texturePreviewService.CurrentActiveChannelMask = null;
                // Use BeginInvoke to avoid deadlock when called from background thread
                _ = Dispatcher.BeginInvoke(new Action(() => {
                    UpdateChannelButtonsState();
                    // Reset D3D11 renderer mask
                    if (texturePreviewService.IsUsingD3D11Renderer && D3D11TextureViewer?.Renderer != null) {
                        D3D11TextureViewer.Renderer.SetChannelMask(0xFFFFFFFF);
                        D3D11TextureViewer.Renderer.RestoreOriginalGamma();
                    }
                }));
            } else {
                logger.Info("LoadSourcePreviewAsync: Skipping mask reset - Normal mode is active for normal map");
            }

            // Store currently selected texture for sRGB detection
            texturePreviewService.CurrentSelectedTexture = selectedTexture;

            string? texturePath = selectedTexture.Path;
            if (string.IsNullOrEmpty(texturePath)) {
                return;
            }

            if (texturePreviewService.GetCachedImage(texturePath) is BitmapImage cachedImage) {
                // Use BeginInvoke to avoid deadlock
                _ = Dispatcher.BeginInvoke(new Action(() => {
                    if (cancellationToken.IsCancellationRequested) {
                        return;
                    }

                    // Save current loaded texture path for preview renderer switching
                    texturePreviewService.CurrentLoadedTexturePath = texturePath;

                    texturePreviewService.OriginalFileBitmapSource = cachedImage;
                    texturePreviewService.IsSourcePreviewAvailable = true;

                    // Only show in viewer if loadToViewer=true (not when KTX2 is already loaded)
                    if (loadToViewer && texturePreviewService.CurrentPreviewSourceMode == TexturePreviewSourceMode.Source) {
                        texturePreviewService.OriginalBitmapSource = cachedImage;
                        ShowOriginalImage();
                    }

                    // Histogram is updated when full-res image loads (skip for cached to reduce CPU)
                    UpdatePreviewSourceControls();
                }));

                return;
            }

            BitmapImage? thumbnailImage = texturePreviewService.LoadOptimizedImage(texturePath, ThumbnailSize);
            if (thumbnailImage == null) {
                logService.LogInfo($"Error loading thumbnail for texture: {selectedTexture.Name}");
                return;
            }

            // Use BeginInvoke to avoid deadlock
            _ = Dispatcher.BeginInvoke(new Action(() => {
                if (cancellationToken.IsCancellationRequested) {
                    return;
                }

                // Save current loaded texture path for preview renderer switching
                texturePreviewService.CurrentLoadedTexturePath = texturePath;

                texturePreviewService.OriginalFileBitmapSource = thumbnailImage;
                texturePreviewService.IsSourcePreviewAvailable = true;

                // Only show in viewer if loadToViewer=true (not when KTX2 is already loaded)
                if (loadToViewer && texturePreviewService.CurrentPreviewSourceMode == TexturePreviewSourceMode.Source) {
                    texturePreviewService.OriginalBitmapSource = thumbnailImage;
                    ShowOriginalImage();
                }

                // Histogram is updated when full-res image loads (skip for thumbnail to reduce CPU)
                UpdatePreviewSourceControls();
            }));

            if (cancellationToken.IsCancellationRequested) {
                return;
            }

            try {
                await Task.Run(() => {
                    if (cancellationToken.IsCancellationRequested) {
                        return;
                    }

                    BitmapImage? bitmapImage = texturePreviewService.LoadOptimizedImage(texturePath, MaxPreviewSize);

                    if (bitmapImage == null || cancellationToken.IsCancellationRequested) {
                        return;
                    }

                    // Use BeginInvoke to avoid deadlock when called from background thread
                    _ = Dispatcher.BeginInvoke(new Action(() => {
                        if (cancellationToken.IsCancellationRequested) {
                            return;
                        }

                        texturePreviewService.CacheImage(texturePath, bitmapImage);

                        texturePreviewService.OriginalFileBitmapSource = bitmapImage;
                        texturePreviewService.IsSourcePreviewAvailable = true;

                        // Only show in viewer if loadToViewer=true (not when KTX2 is already loaded)
                        if (loadToViewer && texturePreviewService.CurrentPreviewSourceMode == TexturePreviewSourceMode.Source) {
                            texturePreviewService.OriginalBitmapSource = bitmapImage;
                            ShowOriginalImage();
                        }

                        // Always update histogram when full-resolution image is loaded (even if showing KTX2)
                        // This replaces the thumbnail-based histogram with accurate full-image data
                        _ = UpdateHistogramAsync(bitmapImage);

                        UpdatePreviewSourceControls();
                    }));
                }, cancellationToken);
            } catch (OperationCanceledException) {
                // Cancellation is expected, no need to log
            }
        }

        #endregion
    }
}
