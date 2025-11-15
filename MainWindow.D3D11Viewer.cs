using AssetProcessor.Helpers;
using AssetProcessor.Resources;
using AssetProcessor.Services;
using AssetProcessor.Services.Models;
using AssetProcessor.Settings;
using AssetProcessor.ViewModels;
using Assimp;
using HelixToolkit.Wpf;
using CommunityToolkit.Mvvm.Input;
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
using System.Windows.Controls.Primitives; // DragDeltaEventArgs для GridSplitter
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
        private void MainWindow_Loaded(object sender, RoutedEventArgs e) {
            logger.Info("MainWindow loaded - D3D11 viewer ready");

            // Apply UseD3D11Preview setting on startup
            bool useD3D11 = AppSettings.Default.UseD3D11Preview;
            SwitchPreviewRenderer(useD3D11);
            logger.Info($"Applied UseD3D11Preview setting on startup: {useD3D11}");
        }

        private void OnD3D11Rendering(object? sender, EventArgs e) {
            if (isD3D11RenderLoopEnabled) {
                D3D11TextureViewer?.RenderFrame();
            }
        }

        private void TexturePreviewViewport_SizeChanged(object sender, SizeChangedEventArgs e) {
            // D3D11TextureViewerControl will handle resize automatically via OnRenderSizeChanged
        }

        // Mouse wheel zoom handler for D3D11 viewer
        // IMPORTANT: HwndHost does NOT receive WPF routed events, so we handle on parent Grid
        // CRITICAL: e.GetPosition() ТОЖЕ БАГОВАННЫЙ для HwndHost! Используем Mouse.GetPosition()!
        private void TexturePreviewViewport_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
            if (!isUsingD3D11Renderer || D3D11TextureViewer == null || sender is not Grid grid) {
                return; // Let event bubble for scrolling
            }
            D3D11TextureViewer.HandleZoomFromWpf(e.Delta);
            e.Handled = true;
        }

        private void TexturePreviewViewport_MouseEnter(object sender, MouseEventArgs e) {
            if (TexturePreviewViewport == null) {
                return;
            }

            // Устанавливаем фокус на viewport для получения событий клавиатуры
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
            if (!isUsingD3D11Renderer) {
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
        private void LoadTextureToD3D11Viewer(BitmapSource bitmap, bool isSRGB) {
            logger.Info($"LoadTextureToD3D11Viewer called: bitmap={bitmap?.PixelWidth}x{bitmap?.PixelHeight}, isSRGB={isSRGB}");

            if (bitmap == null) {
                logger.Warn("Bitmap is null");
                return;
            }

            if (D3D11TextureViewer?.Renderer == null) {
                logger.Warn("D3D11 viewer or renderer is null");
                return;
            }

            logger.Info("D3D11TextureViewer and Renderer are not null, proceeding...");

            // Temporarily disable render loop to avoid deadlock
            isD3D11RenderLoopEnabled = false;
            logger.Info("Render loop disabled");

            try {
                // Convert BitmapSource to TextureData
                int width = bitmap.PixelWidth;
                int height = bitmap.PixelHeight;
                int stride = width * 4; // RGBA8
                byte[] pixels = new byte[stride * height];

                // Convert to BGRA32 (which is actually RGBA in memory)
                var convertedBitmap = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                convertedBitmap.CopyPixels(pixels, stride, 0);

                var mipLevel = new MipLevel {
                    Level = 0,
                    Width = width,
                    Height = height,
                    Data = pixels,
                    RowPitch = stride
                };

                // For PNG viewModel.Textures, always load as non-sRGB format (R8G8B8A8_UNorm)
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
                logger.Info($"D3D11TextureViewer.Renderer is null: {D3D11TextureViewer.Renderer == null}");

                if (D3D11TextureViewer.Renderer != null) {
                    logger.Info("Calling LoadTexture on renderer...");
                    D3D11TextureViewer.Renderer.LoadTexture(textureData);
                    logger.Info($"D3D11TextureRenderer.LoadTexture completed successfully");

                    // Update format info in UI
                    Dispatcher.Invoke(() => {
                        if (TextureFormatTextBlock != null) {
                            string formatInfo = isSRGB ? "PNG (sRGB data)" : "PNG (Linear data)";
                            TextureFormatTextBlock.Text = $"Format: {formatInfo}";
                        }

                        // Update histogram correction button state (PNG has no metadata)
                        UpdateHistogramCorrectionButtonState();
                    });

                    // Note: NOT resetting zoom/pan to preserve user's viewport when switching sources
                } else {
                    logger.Error("D3D11TextureViewer.Renderer is null!");
                }
            } catch (Exception ex) {
                logger.Error(ex, "Failed to load texture to D3D11 viewer");
            } finally {
                // Re-enable render loop
                isD3D11RenderLoopEnabled = true;
                logger.Info("Render loop re-enabled");

                // Force immediate render to update viewport with current zoom/pan
                D3D11TextureViewer?.Renderer?.Render();
                logger.Info("Forced render to apply current zoom/pan");
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

            try {
                // Use Ktx2TextureLoader to load the KTX2 file
                logger.Info($"Loading KTX2 file to D3D11 viewer: {ktxPath}");
                var textureData = await Task.Run(() => Ktx2TextureLoader.LoadFromFile(ktxPath));

                await Dispatcher.InvokeAsync(() => {
                    D3D11TextureViewer.Renderer.LoadTexture(textureData);
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
                    } else if (currentSelectedTexture?.TextureType?.ToLower() == "normal") {
                        shouldAutoEnableNormal = true;
                        autoEnableReason = "KTX2 normal map detected by TextureType (no metadata)";
                    }

                    if (shouldAutoEnableNormal && D3D11TextureViewer?.Renderer != null) {
                        currentActiveChannelMask = "Normal";
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
            }
        }

        /// <summary>
        /// Clears the D3D11 viewer (equivalent to setting Image.Source = null).
        /// </summary>
        private void ClearD3D11Viewer() {
            // D3D11TextureRenderer doesn't have a Clear method
            // Note: NOT resetting zoom/pan to preserve user's viewport between textures

            // Reset channel masks when clearing viewer (switching viewModel.Textures)
            currentActiveChannelMask = null;
            if (D3D11TextureViewer?.Renderer != null) {
                D3D11TextureViewer.Renderer.SetChannelMask(0xFFFFFFFF);
                D3D11TextureViewer.Renderer.RestoreOriginalGamma();
            }
            UpdateChannelButtonsState();
        }
    }
}
