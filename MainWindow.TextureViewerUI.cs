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
        private async void FilterButton_Click(object sender, RoutedEventArgs e) {
            // Ignore programmatic updates to prevent recursive calls
            if (isUpdatingChannelButtons) {
                return;
            }

            if (sender is ToggleButton button) {
                string? channel = button.Tag.ToString();
                if (button.IsChecked == true) {
                    // Сброс всех остальных кнопок (including NormalButton)
                    isUpdatingChannelButtons = true;
                    try {
                        RChannelButton.IsChecked = button == RChannelButton;
                        GChannelButton.IsChecked = button == GChannelButton;
                        BChannelButton.IsChecked = button == BChannelButton;
                        AChannelButton.IsChecked = button == AChannelButton;
                        NormalButton.IsChecked = button == NormalButton;
                    } finally {
                        isUpdatingChannelButtons = false;
                    }

                    // Применяем фильтр
                    if (!string.IsNullOrEmpty(channel)) {
                        await FilterChannelAsync(channel);
                    }
                } else {
                    // Сбрасываем фильтр, если кнопка была отжата
                    HandleChannelMaskCleared();
                }
            }
        }

        /// <summary>
        /// Synchronize channel button GUI state with texturePreviewService.CurrentActiveChannelMask.
        /// </summary>
        private void UpdateChannelButtonsState() {
            isUpdatingChannelButtons = true;
            try {
                RChannelButton.IsChecked = texturePreviewService.CurrentActiveChannelMask == "R";
                GChannelButton.IsChecked = texturePreviewService.CurrentActiveChannelMask == "G";
                BChannelButton.IsChecked = texturePreviewService.CurrentActiveChannelMask == "B";
                AChannelButton.IsChecked = texturePreviewService.CurrentActiveChannelMask == "A";
                NormalButton.IsChecked = texturePreviewService.CurrentActiveChannelMask == "Normal";
            } finally {
                isUpdatingChannelButtons = false;
            }
        }

        /// <summary>
        /// Clears active channel mask without forcing texture reloads in D3D11 mode.
        /// </summary>
        private void HandleChannelMaskCleared() {
            texturePreviewService.CurrentActiveChannelMask = null;
            UpdateChannelButtonsState();

            if (texturePreviewService.IsUsingD3D11Renderer && D3D11TextureViewer?.Renderer != null) {
                D3D11TextureViewer.Renderer.SetChannelMask(0xFFFFFFFF);
                D3D11TextureViewer.Renderer.RestoreOriginalGamma();
                D3D11TextureViewer.Renderer.Render();
                logger.Info("Cleared D3D11 channel mask without reloading texture");

                // Refresh histogram using best available bitmap reference
                BitmapSource? histogramSource = texturePreviewService.OriginalBitmapSource ?? texturePreviewService.OriginalFileBitmapSource;
                if (histogramSource != null) {
                    UpdateHistogram(histogramSource);
                }

                return;
            }

            ShowOriginalImage();
        }

        private void FitResetButton_Click(object sender, RoutedEventArgs e) {
            // Reset zoom/pan (call on control, not renderer, to reset local state too)
            D3D11TextureViewer?.ResetView();
            logger.Info("Fit/Reset: Reset zoom/pan");

            // Reset channel masks without reloading texture or changing Source/KTX mode
            texturePreviewService.CurrentActiveChannelMask = null;
            if (texturePreviewService.IsUsingD3D11Renderer && D3D11TextureViewer?.Renderer != null) {
                D3D11TextureViewer.Renderer.SetChannelMask(0xFFFFFFFF); // All channels
                D3D11TextureViewer.Renderer.RestoreOriginalGamma(); // Restore original gamma (before mask override)
                D3D11TextureViewer.Renderer.Render(); // Force redraw
            }

            // Reset channel buttons UI
            UpdateChannelButtonsState();

            logger.Info("Fit/Reset: Reset channel masks (without changing Source/KTX mode)");
        }

        private void FilterToggleButton_Click(object sender, RoutedEventArgs e) {
            if (sender is ToggleButton button) {
                bool useLinearFilter = button.IsChecked ?? true;
                D3D11TextureViewer?.Renderer?.SetFilter(useLinearFilter);
                logger.Info($"Filter toggle: {(useLinearFilter ? "Trilinear" : "Point")}");
            }
        }

        private void HistogramCorrectionButton_Click(object sender, RoutedEventArgs e) {
            if (sender is ToggleButton button && D3D11TextureViewer?.Renderer != null) {
                bool enabled = button.IsChecked ?? true;
                D3D11TextureViewer.Renderer.SetHistogramCorrection(enabled);

                // Save setting for next session
                AppSettings.Default.HistogramCorrectionEnabled = enabled;
                AppSettings.Default.Save();

                logger.Info($"Histogram correction {(enabled ? "enabled" : "disabled")} by user (saved to settings)");

                // Force immediate render to show the change
                D3D11TextureViewer.Renderer.Render();
            }
        }

        /// <summary>
        /// Updates the histogram correction button state based on whether current texture has histogram metadata
        /// </summary>
        private void UpdateHistogramCorrectionButtonState() {
            if (HistogramCorrectionButton == null || D3D11TextureViewer?.Renderer == null) {
                return;
            }

            bool hasHistogram = D3D11TextureViewer.Renderer.HasHistogramMetadata();
            HistogramCorrectionButton.IsEnabled = hasHistogram;

            if (hasHistogram) {
                // Restore saved setting (default to enabled if metadata present)
                bool savedEnabled = AppSettings.Default.HistogramCorrectionEnabled;
                HistogramCorrectionButton.IsChecked = savedEnabled;
                D3D11TextureViewer.Renderer.SetHistogramCorrection(savedEnabled);
                logger.Info($"Histogram correction {(savedEnabled ? "enabled" : "disabled")} (restored from settings)");

                // Update tooltip with metadata info
                var meta = D3D11TextureViewer.Renderer.GetHistogramMetadata();
                if (meta != null) {
                    string scaleStr = meta.IsPerChannel
                        ? $"[{meta.Scale[0]:F3}, {meta.Scale[1]:F3}, {meta.Scale[2]:F3}]"
                        : meta.Scale[0].ToString("F3");
                    HistogramCorrectionButton.ToolTip = $"Histogram compensation\nScale: {scaleStr}\nOffset: {meta.Offset[0]:F3}";
                    logger.Info($"Histogram metadata found: scale={scaleStr}");
                }
            } else {
                // No histogram metadata - disable button
                HistogramCorrectionButton.IsChecked = false;
                HistogramCorrectionButton.ToolTip = "No histogram metadata in this texture";
                logger.Info("No histogram metadata in current texture");
            }
        }

        private void ResetPreviewState() {
            CancelPendingD3DPreviewLoad();
            // Zoom/pan state now handled by D3D11TextureViewerControl
            texturePreviewService.ResetPreviewState();
            ClearPreviewReferenceSize();
            HideMipmapControls();
            UpdatePreviewSourceControls();
        }

        private void ClearPreviewReferenceSize() {
            texturePreviewService.PreviewReferenceWidth = 0;
            texturePreviewService.PreviewReferenceHeight = 0;
        }

        private void SetPreviewReferenceSize(BitmapSource bitmap) {
            (double width, double height) = GetImageSizeInDips(bitmap);

            texturePreviewService.PreviewReferenceWidth = double.IsFinite(width) && width > 0 ? width : 0;
            texturePreviewService.PreviewReferenceHeight = double.IsFinite(height) && height > 0 ? height : 0;
        }

        private void EnsurePreviewReferenceSize(BitmapSource bitmap) {
            if (texturePreviewService.PreviewReferenceWidth <= 0 || texturePreviewService.PreviewReferenceHeight <= 0) {
                SetPreviewReferenceSize(bitmap);
            }
        }

        private double GetScaleMultiplier(double imageWidth, double imageHeight) {
            double multiplierX = (texturePreviewService.PreviewReferenceWidth > 0 && imageWidth > 0)
                ? texturePreviewService.PreviewReferenceWidth / imageWidth
                : double.NaN;
            double multiplierY = (texturePreviewService.PreviewReferenceHeight > 0 && imageHeight > 0)
                ? texturePreviewService.PreviewReferenceHeight / imageHeight
                : double.NaN;

            double multiplier = double.IsFinite(multiplierX) && multiplierX > 0 ? multiplierX : double.NaN;
            if (!double.IsFinite(multiplier) || multiplier <= 0) {
                multiplier = double.IsFinite(multiplierY) && multiplierY > 0 ? multiplierY : 1.0;
            }

            if (!double.IsFinite(multiplier) || multiplier <= 0) {
                multiplier = 1.0;
            }

            return multiplier;
        }

        private double GetScaleMultiplier(BitmapSource bitmap) {
            EnsurePreviewReferenceSize(bitmap);
            (double imageWidth, double imageHeight) = GetImageSizeInDips(bitmap);
            return GetScaleMultiplier(imageWidth, imageHeight);
        }

        private static double ClampNormalized(double value) {
            if (!double.IsFinite(value)) {
                return 0.5;
            }

            return Math.Clamp(value, 0.0, 1.0);
        }

        // Updated: Use D3D11 viewer for texture preview
        private void UpdatePreviewImage(BitmapSource bitmap, bool setReference, bool preserveViewport) {
            logger.Info($"UpdatePreviewImage called: bitmap={bitmap?.PixelWidth}x{bitmap?.PixelHeight}");

            if (bitmap == null) {
                logger.Warn("Bitmap is null in UpdatePreviewImage");
                return;
            }

            logger.Info("About to check D3D11TextureViewer");

            if (D3D11TextureViewer == null) {
                logger.Warn("D3D11TextureViewer is null in UpdatePreviewImage");
                return;
            }

            logger.Info("D3D11TextureViewer is not null");
            logger.Info("About to call LoadTextureToD3D11Viewer from UpdatePreviewImage");

            try {
                // Determine if texture is sRGB based on texture type
                bool isSRGB = IsSRGBTexture(texturePreviewService.CurrentSelectedTexture);
                logger.Info("Getting bitmap dimensions...");
                int w = bitmap.PixelWidth;
                int h = bitmap.PixelHeight;
                logger.Info($"Bitmap dimensions: {w}x{h}");
                logger.Info($"Calling LoadTextureToD3D11Viewer with bitmap {w}x{h}, isSRGB={isSRGB} (type={texturePreviewService.CurrentSelectedTexture?.TextureType})");
                LoadTextureToD3D11Viewer(bitmap, isSRGB);
                logger.Info("LoadTextureToD3D11Viewer returned successfully from UpdatePreviewImage");
            } catch (Exception ex) {
                logger.Error(ex, "Exception in UpdatePreviewImage when calling LoadTextureToD3D11Viewer");
            }
        }

        // LEGACY: UpdateZoomUi removed - controls deleted
        // private void UpdateZoomUi() {
        //     if (ZoomValueTextBlock != null) {
        //         ZoomValueTextBlock.Text = $"{currentZoom * 100:0.#}%";
        //     }
        //
        //     if (ZoomSlider != null) {
        //         bool previous = isUpdatingZoomSlider;
        //         isUpdatingZoomSlider = true;
        //         ZoomSlider.Value = currentZoom;
        //         isUpdatingZoomSlider = previous;
        //     }
        // }

        // LEGACY: ScheduleFitZoomUpdate disabled
        private void ScheduleFitZoomUpdate(bool forceApply) {
            // Disabled for fallback mode
        }

        // LEGACY: RecalculateFitZoom disabled
        private void RecalculateFitZoom(bool apply) {
            // Disabled for fallback mode
        }

        private static (double width, double height) GetImageSizeInDips(BitmapSource bitmap) {
            if (bitmap == null) {
                return (0, 0);
            }

            double dpiX = bitmap.DpiX;
            double dpiY = bitmap.DpiY;
            if (dpiX <= 0) {
                dpiX = 96.0;
            }
            if (dpiY <= 0) {
                dpiY = 96.0;
            }

            double width = bitmap.PixelWidth * 96.0 / dpiX;
            double height = bitmap.PixelHeight * 96.0 / dpiY;
            return (width, height);
        }

        private (double width, double height) GetViewportSize() {
            if (TexturePreviewViewport != null) {
                double width = TexturePreviewViewport.ActualWidth;
                double height = TexturePreviewViewport.ActualHeight;

                if (double.IsNaN(width) || width < 0) {
                    width = 0;
                }

                if (double.IsNaN(height) || height < 0) {
                    height = 0;
                }

                return (width, height);
            }

            return (0, 0);
        }

        private double GetEffectiveZoom(BitmapSource bitmap, double zoom) {
            EnsurePreviewReferenceSize(bitmap);
            double scaleMultiplier = GetScaleMultiplier(bitmap);
            double effectiveZoom = zoom * scaleMultiplier;
            if (!double.IsFinite(effectiveZoom) || effectiveZoom <= 0) {
                effectiveZoom = 1.0;
            }

            return effectiveZoom;
        }

        // LEGACY: UpdateTransform disabled - using simple Stretch="Uniform" instead
        private void UpdateTransform(bool rebuildFromCenter) {
            // Disabled for fallback mode
        }

        // LEGACY: ApplyFitZoom disabled
        private void ApplyFitZoom() {
            // Disabled for fallback mode
        }

        private void ApplyZoomWithPivot(double newZoom, Point pivot) {
            // LEGACY: Now handled by D3D11 viewer
        }

        private void SetZoomAndCenter(double zoom) {
            // LEGACY: Now handled by D3D11 viewer
        }

        private void ResetPan() {
            // LEGACY: Now handled by D3D11 viewer
        }

        private Point ToImageSpace(Point referencePoint, FrameworkElement referenceElement) {
            // LEGACY: Now handled by D3D11 viewer
            return referencePoint;
        }

        private Point GetViewportCenterInImageSpace() {
            // LEGACY: Now handled by D3D11 viewer
            return new Point(0, 0);
        }

        private FrameworkElement? GetPanReferenceElement() {
            return TexturePreviewViewport;
        }

        private void StartPanning(Point startPosition) {
            // LEGACY: Now handled by D3D11 viewer mouse events
        }

        private void StopPanning() {
            // LEGACY: Now handled by D3D11 viewer mouse events
        }

        private void ApplyPanDelta(Vector delta) {
            // LEGACY: Now handled by D3D11 viewer
        }

        private void UpdatePreviewSourceControls() {
            if (PreviewSourceOriginalRadioButton == null || PreviewSourceKtxRadioButton == null) {
                return;
            }

            texturePreviewService.IsUpdatingPreviewSourceControls = true;

            try {
                PreviewSourceOriginalRadioButton.IsEnabled = texturePreviewService.IsSourcePreviewAvailable;
                PreviewSourceKtxRadioButton.IsEnabled = texturePreviewService.IsKtxPreviewAvailable;

                PreviewSourceOriginalRadioButton.IsChecked = texturePreviewService.CurrentPreviewSourceMode == TexturePreviewSourceMode.Source;
                PreviewSourceKtxRadioButton.IsChecked = texturePreviewService.CurrentPreviewSourceMode == TexturePreviewSourceMode.Ktx2;
            } finally {
                texturePreviewService.IsUpdatingPreviewSourceControls = false;
            }
        }

        /// <summary>
        /// Determines if a texture should be treated as sRGB based on its type or name.
        /// </summary>
        private bool IsSRGBTexture(TextureResource? texture) {
            if (texture == null) {
                return true; // Default to sRGB
            }

            string? textureType = texture.TextureType;
            if (string.IsNullOrEmpty(textureType)) {
                // Auto-detect from filename if type not set
                textureType = TextureResource.DetermineTextureType(texture.Name ?? "");
            }

            // Linear texture types
            return textureType?.ToLower() switch {
                "normal" => false,      // Normal maps are linear
                "gloss" => false,       // Gloss is linear
                "roughness" => false,   // Roughness is linear
                "metallic" => false,    // Metallic is linear
                "ao" => false,          // Ambient occlusion is linear
                "opacity" => false,     // Opacity is linear
                "height" => false,      // Height is linear
                _ => true               // Default: Albedo, Emissive, Specular, Other = sRGB
            };
        }

        /// <summary>
        /// Prepares a BitmapSource for WPF display.
        /// DISABLED: Gamma correction was causing image distortion (compression and left shift).
        /// WPF fallback mode now shows raw image data without gamma correction.
        /// </summary>
        private BitmapSource PrepareForWPFDisplay(BitmapSource bitmap) {
            // DISABLED: Gamma correction was causing image distortion issues
            // - Images appeared compressed horizontally
            // - Images shifted to the left
            // - Inconsistent with D3D11 display
            //
            // Since WPF mode is only a fallback when D3D11 fails,
            // and most users will use D3D11 mode which handles gamma correctly,
            // we're disabling gamma correction in WPF mode to avoid distortion.
            //
            // If gamma correction is needed in the future for WPF mode,
            // the issue might be related to:
            // 1. Incorrect stride calculation
            // 2. Pixel format mismatch
            // 3. DPI scaling issues

            return bitmap; // Return original bitmap without modification
        }

        /// <summary>
        /// Applies sRGB gamma correction to a single byte channel (Linear -> sRGB).
        /// </summary>
        private static byte ApplyGammaToByteChannel(byte value) {
            float normalized = value / 255.0f;
            float corrected = MathF.Pow(normalized, 1.0f / 2.2f); // Linear -> sRGB
            return (byte)Math.Clamp(corrected * 255.0f, 0, 255);
        }

        // Removed: PreviewWidthSlider methods (slider was removed from UI)
        // Removed: Old TexturePreviewViewport_SizeChanged and TexturePreviewImage_SizeChanged (now handled by D3D11)

        // LEGACY: Mouse wheel zoom removed

        // LEGACY: Zoom/Pan controls removed - fallback to simple preview
        // private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
        //     if (isUpdatingZoomSlider || TexturePreviewImage?.Source == null) {
        //         return;
        //     }
        //
        //     double newZoom = e.NewValue;
        //     if (double.IsNaN(newZoom) || newZoom <= 0) {
        //         return;
        //     }
        //
        //     Point pivot = GetViewportCenterInImageSpace();
        //     ApplyZoomWithPivot(newZoom, pivot);
        // }
        //
        // private void FitZoomButton_Click(object sender, RoutedEventArgs e) {
        //     if (TexturePreviewImage?.Source == null) {
        //         return;
        //     }
        //
        //     isFitMode = true;
        //     ScheduleFitZoomUpdate(true);
        // }
        //
        // private void Zoom100Button_Click(object sender, RoutedEventArgs e) {
        //     SetZoomAndCenter(1.0);
        // }
        //
        // private void Zoom200Button_Click(object sender, RoutedEventArgs e) {
        //     SetZoomAndCenter(2.0);
        // }
        //
        // private void ResetPanButton_Click(object sender, RoutedEventArgs e) {
        //     ResetPan();
        // }

        // LEGACY: Old mouse event handlers removed - now handled by D3D11 viewer overlay

        private void TextureViewerScroll_SizeChanged(object sender, SizeChangedEventArgs e) {
            ClampPreviewContentHeight();
        }

        private void PreviewHeightGridSplitter_DragDelta(object sender, DragDeltaEventArgs e) {
            if (PreviewContentRow == null) {
                return;
            }

            double desiredHeight = PreviewContentRow.ActualHeight + e.VerticalChange;
            UpdatePreviewContentHeight(desiredHeight);
            e.Handled = true;
        }

        private void ClampPreviewContentHeight() {
            if (PreviewContentRow == null) {
                return;
            }

            double currentHeight = PreviewContentRow.ActualHeight;

            if (currentHeight <= 0) {
                if (PreviewContentRow.Height.IsAbsolute && PreviewContentRow.Height.Value > 0) {
                    currentHeight = PreviewContentRow.Height.Value;
                } else {
                    currentHeight = DefaultPreviewContentHeight;
                }
            }

            UpdatePreviewContentHeight(currentHeight);
        }

        private void UpdatePreviewContentHeight(double desiredHeight) {
            if (PreviewContentRow == null) {
                return;
            }

            double clampedHeight = Math.Clamp(desiredHeight, MinPreviewContentHeight, MaxPreviewContentHeight);
            PreviewContentRow.Height = new GridLength(clampedHeight);
        }

        // Removed: UpdatePreviewWidthText (PreviewWidthSlider was removed)

        private void PreviewSourceRadioButton_Checked(object sender, RoutedEventArgs e) {
            if (texturePreviewService.IsUpdatingPreviewSourceControls) {
                return;
            }

            if (sender == PreviewSourceOriginalRadioButton) {
                SetPreviewSourceMode(TexturePreviewSourceMode.Source, initiatedByUser: true);
            } else if (sender == PreviewSourceKtxRadioButton) {
                SetPreviewSourceMode(TexturePreviewSourceMode.Ktx2, initiatedByUser: true);
            }
        }

        private void SetPreviewSourceMode(TexturePreviewSourceMode mode, bool initiatedByUser) {
            if (initiatedByUser) {
                texturePreviewService.IsUserPreviewSelection = true;
            }

            if (mode == TexturePreviewSourceMode.Ktx2 && !texturePreviewService.IsKtxPreviewAvailable) {
                UpdatePreviewSourceControls();
                return;
            }

            if (mode == TexturePreviewSourceMode.Source && !texturePreviewService.IsSourcePreviewAvailable) {
                UpdatePreviewSourceControls();
                return;
            }

            texturePreviewService.CurrentPreviewSourceMode = mode;

            if (mode == TexturePreviewSourceMode.Source) {
                texturePreviewService.IsKtxPreviewActive = false;
                HideMipmapControls();

                if (texturePreviewService.OriginalFileBitmapSource != null) {
                    texturePreviewService.OriginalBitmapSource = texturePreviewService.OriginalFileBitmapSource;
                    _ = UpdateHistogramAsync(texturePreviewService.OriginalBitmapSource);
                    // Preserve channel mask when switching from KTX to Source
                    ShowOriginalImage(preserveMask: true);

                    // Update format text for Source mode
                    if (TextureFormatTextBlock != null) {
                        bool isSRGB = IsSRGBTexture(texturePreviewService.CurrentSelectedTexture);
                        string formatInfo = isSRGB ? "PNG (sRGB data)" : "PNG (Linear data)";
                        TextureFormatTextBlock.Text = $"Format: {formatInfo}";
                    }
                } else {
                    ClearD3D11Viewer();
                }
            } else if (mode == TexturePreviewSourceMode.Ktx2) {
                texturePreviewService.IsKtxPreviewActive = true;

                // Update histogram from source image (KTX2 is compressed, use source for histogram)
                if (texturePreviewService.OriginalFileBitmapSource != null) {
                    _ = UpdateHistogramAsync(texturePreviewService.OriginalFileBitmapSource);
                }

                // Save current mask before switching
                string? savedMask = texturePreviewService.CurrentActiveChannelMask;

                // FIXED: Check if we're using D3D11 native KTX2 (no extracted mipmaps)
                // or old PNG extraction method
                if (texturePreviewService.CurrentKtxMipmaps != null && texturePreviewService.CurrentKtxMipmaps.Count > 0) {
                    // Old method: extracted PNG mipmaps available (WPF mode)
                    UpdateMipmapControls(texturePreviewService.CurrentKtxMipmaps);
                    SetCurrentMipLevel(texturePreviewService.CurrentMipLevel);
                } else if (D3D11TextureViewer?.Renderer != null &&
                           D3D11TextureViewer.Renderer.GetCurrentTexturePath() == texturePreviewService.CurrentLoadedKtx2Path &&
                           !string.IsNullOrEmpty(texturePreviewService.CurrentLoadedKtx2Path)) {
                    // CRITICAL: KTX2 is ALREADY loaded in D3D11 (check path match to prevent PNG confusion)
                    UpdateD3D11MipmapControls(D3D11TextureViewer.Renderer.MipCount);
                    logger.Info($"KTX2 already loaded in D3D11 - {D3D11TextureViewer.Renderer.MipCount} mip levels available");

                    // Update histogram correction button state
                    UpdateHistogramCorrectionButtonState();

                    // Restore channel mask if already loaded
                    // BUT: Don't restore if auto-enable already set Normal mode for normal maps
                    if (savedMask != null && texturePreviewService.CurrentActiveChannelMask != "Normal") {
                        texturePreviewService.CurrentActiveChannelMask = savedMask;
                        _ = FilterChannelAsync(savedMask);
                        logger.Info($"Restored channel mask '{savedMask}' for already loaded KTX2");
                    } else if (texturePreviewService.CurrentActiveChannelMask == "Normal") {
                        logger.Info($"Skipping mask restore - Normal mode was auto-enabled for normal map");
                    }
                } else if (texturePreviewService.IsUsingD3D11Renderer && !string.IsNullOrEmpty(texturePreviewService.CurrentLoadedKtx2Path)) {
                    // New method: Reload KTX2 natively to D3D11 (only if not already loaded or loading)
                    // Проверяем, не загружается ли уже этот файл через LoadKtx2ToD3D11ViewerAsync
                    if (IsKtx2Loading(texturePreviewService.CurrentLoadedKtx2Path)) {
                        logger.Info($"KTX2 file already loading via LoadKtx2ToD3D11ViewerAsync, skipping reload in SetPreviewSourceMode: {texturePreviewService.CurrentLoadedKtx2Path}");
                        return; // Выходим, не вызывая LoadKtx2ToD3D11ViewerAsync
                    }

                    // Проверяем, не загружена ли уже текстура в renderer (включая загрузку через ViewModel_TexturePreviewLoaded)
                    if (D3D11TextureViewer?.Renderer != null) {
                        string? currentTexturePath = D3D11TextureViewer.Renderer.GetCurrentTexturePath();
                        if (currentTexturePath != null && string.Equals(currentTexturePath, texturePreviewService.CurrentLoadedKtx2Path, StringComparison.OrdinalIgnoreCase)) {
                            logger.Info($"KTX2 already loaded in D3D11 renderer, skipping reload in SetPreviewSourceMode: {texturePreviewService.CurrentLoadedKtx2Path}");
                            // Обновляем UI, но не перезагружаем текстуру
                            UpdateD3D11MipmapControls(D3D11TextureViewer.Renderer.MipCount);
                            UpdateHistogramCorrectionButtonState();
                            return;
                        }
                    }

                    _ = Task.Run(async () => {
                        try {
                            await LoadKtx2ToD3D11ViewerAsync(texturePreviewService.CurrentLoadedKtx2Path);
                            await Dispatcher.InvokeAsync(() => {
                                if (D3D11TextureViewer?.Renderer != null && D3D11TextureViewer.Renderer.MipCount > 0) {
                                    UpdateD3D11MipmapControls(D3D11TextureViewer.Renderer.MipCount);
                                    logger.Info($"Reloaded KTX2 to D3D11 when switching to KTX2 mode - {D3D11TextureViewer.Renderer.MipCount} mip levels");

                                    // Restore channel mask after KTX2 load
                                    // BUT: Don't restore if auto-enable already set Normal mode for normal maps
                                    if (savedMask != null && texturePreviewService.CurrentActiveChannelMask != "Normal") {
                                        texturePreviewService.CurrentActiveChannelMask = savedMask;
                                        _ = FilterChannelAsync(savedMask);
                                        logger.Info($"Restored channel mask '{savedMask}' after switching to KTX2 mode");
                                    } else if (texturePreviewService.CurrentActiveChannelMask == "Normal") {
                                        logger.Info($"Skipping mask restore - Normal mode was auto-enabled for normal map");
                                    }
                                }
                            });
                        } catch (Exception ex) {
                            logger.Error(ex, "Failed to reload KTX2 when switching to KTX2 mode");
                        }
                    });
                } else {
                    HideMipmapControls();
                }
            }

            UpdatePreviewSourceControls();
        }

        private void HideMipmapControls() {
            if (MipmapSliderPanel != null) {
                MipmapSliderPanel.Visibility = Visibility.Collapsed;
            }

            if (MipmapLevelSlider != null) {
                texturePreviewService.IsUpdatingMipLevel = true;
                MipmapLevelSlider.Value = 0;
                MipmapLevelSlider.Maximum = 0;
                MipmapLevelSlider.IsEnabled = false;
                texturePreviewService.IsUpdatingMipLevel = false;
            }

            if (MipmapInfoTextBlock != null) {
                MipmapInfoTextBlock.Text = string.Empty;
            }
        }

        private void ShowMipmapControls() {
            if (MipmapSliderPanel != null) {
                MipmapSliderPanel.Visibility = Visibility.Visible;
            }

            if (MipmapLevelSlider != null) {
                MipmapLevelSlider.IsEnabled = true;
            }
        }

        private void UpdateMipmapControls(IList<KtxMipLevel> mipmaps) {
            if (MipmapSliderPanel == null || MipmapLevelSlider == null || MipmapInfoTextBlock == null) {
                return;
            }

            texturePreviewService.IsUpdatingMipLevel = true;

            try {
                MipmapSliderPanel.Visibility = Visibility.Visible;
                MipmapLevelSlider.Minimum = 0;
                MipmapLevelSlider.Maximum = Math.Max(0, mipmaps.Count - 1);
                MipmapLevelSlider.Value = 0;
                MipmapLevelSlider.IsEnabled = mipmaps.Count > 1;
                MipmapInfoTextBlock.Text = mipmaps.Count > 0
                    ? $"Мип-уровень 0 из {Math.Max(0, mipmaps.Count - 1)} — {mipmaps[0].Width}×{mipmaps[0].Height}"
                    : "Мип-уровни недоступны";
            } finally {
                texturePreviewService.IsUpdatingMipLevel = false;
            }
        }

        /// <summary>
        /// Update mipmap controls for D3D11 native KTX2 texture.
        /// </summary>
        private void UpdateD3D11MipmapControls(int mipCount) {
            if (MipmapSliderPanel == null || MipmapLevelSlider == null || MipmapInfoTextBlock == null) {
                return;
            }

            if (D3D11TextureViewer?.Renderer == null) {
                return;
            }

            texturePreviewService.IsUpdatingMipLevel = true;

            try {
                MipmapSliderPanel.Visibility = Visibility.Visible;
                MipmapLevelSlider.Minimum = 0;
                MipmapLevelSlider.Maximum = Math.Max(0, mipCount - 1);
                MipmapLevelSlider.Value = 0;
                MipmapLevelSlider.IsEnabled = mipCount > 1;

                int width = D3D11TextureViewer.Renderer.TextureWidth;
                int height = D3D11TextureViewer.Renderer.TextureHeight;
                MipmapInfoTextBlock.Text = $"Мип-уровень 0 из {Math.Max(0, mipCount - 1)} — {width}×{height} (D3D11)";
            } finally {
                texturePreviewService.IsUpdatingMipLevel = false;
            }
        }

        private void UpdateMipmapInfo(KtxMipLevel mipLevel, int totalLevels) {
            if (MipmapInfoTextBlock != null) {
                int maxLevel = Math.Max(0, totalLevels - 1);
                MipmapInfoTextBlock.Text = $"Мип-уровень {mipLevel.Level} из {maxLevel} — {mipLevel.Width}×{mipLevel.Height}";
            }
        }

        private void SetCurrentMipLevel(int level, bool updateSlider = true) {
            if (texturePreviewService.CurrentKtxMipmaps == null || texturePreviewService.CurrentKtxMipmaps.Count == 0) {
                return;
            }

            int clampedLevel = Math.Clamp(level, 0, texturePreviewService.CurrentKtxMipmaps.Count - 1);
            texturePreviewService.CurrentMipLevel = clampedLevel;

            if (updateSlider && MipmapLevelSlider != null) {
                texturePreviewService.IsUpdatingMipLevel = true;
                MipmapLevelSlider.Value = clampedLevel;
                texturePreviewService.IsUpdatingMipLevel = false;
            }

            var mip = texturePreviewService.CurrentKtxMipmaps[clampedLevel];
            texturePreviewService.OriginalBitmapSource = mip.Bitmap.Clone();

            // Обновляем изображение
            Dispatcher.Invoke(() => {
                UpdatePreviewImage(texturePreviewService.OriginalBitmapSource, setReference: clampedLevel == 0, preserveViewport: false);

                // Update WPF Image if in WPF preview mode
                if (WpfTexturePreviewImage.Visibility == Visibility.Visible) {
                    WpfTexturePreviewImage.Source = PrepareForWPFDisplay(texturePreviewService.OriginalBitmapSource);
                    logger.Info($"Updated WPF Image in SetCurrentMipLevel: level {clampedLevel}");
                }

                UpdateHistogram(texturePreviewService.OriginalBitmapSource);
            });

            ScheduleFitZoomUpdate(false);
            UpdateMipmapInfo(mip, texturePreviewService.CurrentKtxMipmaps.Count);
        }

        private async Task FilterChannelAsync(string channel) {
            // Save current active mask
            texturePreviewService.CurrentActiveChannelMask = channel;

            // Sync GUI buttons
            Dispatcher.Invoke(() => UpdateChannelButtonsState());

            // Apply channel filter to D3D11 renderer if active
            if (texturePreviewService.IsUsingD3D11Renderer && D3D11TextureViewer?.Renderer != null) {
                // Convert channel to mask
                // channelMask: bit 0=R, bit 1=G, bit 2=B, bit 3=A, bit 4=grayscale, bit 5=normal reconstruction
                uint mask = channel switch {
                    "R" => 0x11, // R channel + grayscale (0x01 | 0x10)
                    "G" => 0x12, // G channel + grayscale (0x02 | 0x10)
                    "B" => 0x14, // B channel + grayscale (0x04 | 0x10)
                    "A" => 0x18, // A channel + grayscale (0x08 | 0x10)
                    "Normal" => 0x20, // Normal reconstruction mode (bit 5)
                    _ => 0xFFFFFFFF // All channels
                };
                D3D11TextureViewer.Renderer.SetChannelMask(mask);
                // Note: Gamma correction for masks is now handled automatically in shader
                // No need to override gamma here - shader will display linear values for masks
                D3D11TextureViewer.Renderer.Render();

                logger.Info($"Applied D3D11 channel mask: {channel} = 0x{mask:X}");

                // Update histogram for the filtered channel (D3D11 filters on GPU, but histogram needs CPU filtering)
                if (texturePreviewService.OriginalBitmapSource != null) {
                    if (channel == "Normal") {
                        // For normal map mode, show RGB histogram (no grayscale)
                        Dispatcher.Invoke(() => {
                            UpdateHistogram(texturePreviewService.OriginalBitmapSource, false);
                        });
                    } else {
                        // For R/G/B/A channels, show grayscale histogram
                        BitmapSource filteredBitmap = await textureChannelService.ApplyChannelFilterAsync(texturePreviewService.OriginalBitmapSource, channel);
                        Dispatcher.Invoke(() => {
                            UpdateHistogram(filteredBitmap, true);  // Update histogram in grayscale mode
                        });
                    }
                }

                return;
            }

            // WPF mode: use bitmap filtering
            if (texturePreviewService.OriginalBitmapSource != null) {
                BitmapSource filteredBitmap = await textureChannelService.ApplyChannelFilterAsync(texturePreviewService.OriginalBitmapSource, channel);

                // Обновляем UI в основном потоке
                Dispatcher.Invoke(() => {
                    UpdatePreviewImage(filteredBitmap, setReference: false, preserveViewport: true);

                    // Update WPF Image if in WPF preview mode
                    if (WpfTexturePreviewImage.Visibility == Visibility.Visible) {
                        // Note: filtered bitmaps are already processed, may not need gamma correction
                        // But apply it anyway for consistency if texture is linear
                        WpfTexturePreviewImage.Source = PrepareForWPFDisplay(filteredBitmap);
                        logger.Info($"Updated WPF Image in FilterChannelAsync: {channel}");
                    }

                    UpdateHistogram(filteredBitmap, true);  // Обновление гистограммы
                });
            }
        }

        private void MipmapLevelSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            if (texturePreviewService.IsUpdatingMipLevel || !texturePreviewService.IsKtxPreviewActive) {
                return;
            }

            int newLevel = (int)Math.Round(e.NewValue);
            if (newLevel != texturePreviewService.CurrentMipLevel) {
                // Check if we're using D3D11 native mipmaps or extracted PNG mipmaps
                if (texturePreviewService.CurrentKtxMipmaps != null && texturePreviewService.CurrentKtxMipmaps.Count > 0) {
                    // Old method: extracted PNG mipmaps
                    SetCurrentMipLevel(newLevel, updateSlider: false);
                } else if (D3D11TextureViewer?.Renderer != null) {
                    // New method: D3D11 native mipmaps
                    texturePreviewService.CurrentMipLevel = newLevel;
                    D3D11TextureViewer.Renderer.SetMipLevel(newLevel);

                    // Update info text
                    if (MipmapInfoTextBlock != null) {
                        int mipCount = D3D11TextureViewer.Renderer.MipCount;
                        int width = D3D11TextureViewer.Renderer.TextureWidth >> newLevel;
                        int height = D3D11TextureViewer.Renderer.TextureHeight >> newLevel;
                        MipmapInfoTextBlock.Text = $"Мип-уровень {newLevel} из {Math.Max(0, mipCount - 1)} — {width}×{height} (D3D11)";
                    }

                    logger.Info($"D3D11 mip level changed to {newLevel}");
                }
            }
        }

        private async void ShowOriginalImage(bool recalculateFitZoom = false, bool preserveMask = false) {
            // Only clear mask if explicitly requested (NOT when switching Source<->KTX)
            if (!preserveMask) {
                texturePreviewService.CurrentActiveChannelMask = null;

                // Reset channel filter in D3D11 mode
                if (texturePreviewService.IsUsingD3D11Renderer && D3D11TextureViewer?.Renderer != null) {
                    D3D11TextureViewer.Renderer.SetChannelMask(0xFFFFFFFF); // All channels
                    D3D11TextureViewer.Renderer.RestoreOriginalGamma(); // Restore original gamma (if it was overridden for mask)
                    logger.Info($"Reset D3D11 channel mask to all channels and restored original gamma");
                }
            } else {
                // Preserve mask - reapply it if active
                if (texturePreviewService.CurrentActiveChannelMask != null && texturePreviewService.IsUsingD3D11Renderer && D3D11TextureViewer?.Renderer != null) {
                    // Reapply the current mask to the renderer
                    _ = FilterChannelAsync(texturePreviewService.CurrentActiveChannelMask);
                    logger.Info($"Preserved and reapplied channel mask '{texturePreviewService.CurrentActiveChannelMask}' when switching to Source mode");
                }
            }

            if (texturePreviewService.OriginalBitmapSource != null) {
                await Dispatcher.InvokeAsync(() => {
                    UpdatePreviewImage(texturePreviewService.OriginalBitmapSource, setReference: true, preserveViewport: !recalculateFitZoom);

                    // Update WPF Image if in WPF preview mode
                    if (WpfTexturePreviewImage.Visibility == Visibility.Visible) {
                        WpfTexturePreviewImage.Source = PrepareForWPFDisplay(texturePreviewService.OriginalBitmapSource);
                        logger.Info($"Updated WPF Image in ShowOriginalImage");
                    }

                    UpdateChannelButtonsState();
                    UpdateHistogram(texturePreviewService.OriginalBitmapSource);
                    ScheduleFitZoomUpdate(recalculateFitZoom);

                    // AUTO-ENABLE Normal reconstruction for normal map viewModel.Textures (PNG)
                    // Must be AFTER all reset operations to prevent being cleared
                    if (!preserveMask && texturePreviewService.CurrentSelectedTexture?.TextureType?.ToLower() == "normal" && D3D11TextureViewer?.Renderer != null) {
                        texturePreviewService.CurrentActiveChannelMask = "Normal";
                        D3D11TextureViewer.Renderer.SetChannelMask(0x20); // Normal reconstruction bit
                        D3D11TextureViewer.Renderer.Render();
                        UpdateChannelButtonsState(); // Sync button UI
                        logger.Info("Auto-enabled Normal reconstruction mode for normal map texture (PNG)");
                    }
                });
            }
        }

        private void UpdateHistogram(BitmapSource bitmapSource, bool isGray = false) {
            if (bitmapSource == null) return;

            HistogramComputationResult result = histogramCoordinator.BuildHistogram(bitmapSource, isGray);

            Dispatcher.Invoke(() => {
                HistogramPlotView.Model = result.Model;
                UpdateHistogramStatisticsUI(result.Statistics);
            });
        }
        private async Task UpdateHistogramAsync(BitmapSource bitmapSource, bool isGray = false) {
            if (bitmapSource == null) return;

            HistogramComputationResult result = await histogramCoordinator.BuildHistogramAsync(bitmapSource, isGray);

            Dispatcher.Invoke(() => {
                HistogramPlotView.Model = result.Model;
                UpdateHistogramStatisticsUI(result.Statistics);
            });
        }
private void UpdateHistogramStatisticsUI(HistogramStatistics stats) {
            HistogramMinTextBlock.Text = $"{stats.Min:F0}";
            HistogramMaxTextBlock.Text = $"{stats.Max:F0}";
            HistogramMeanTextBlock.Text = $"{stats.Mean:F2}";
            HistogramMedianTextBlock.Text = $"{stats.Median:F0}";
            HistogramStdDevTextBlock.Text = $"{stats.StdDev:F2}";
            HistogramPixelsTextBlock.Text = $"{stats.TotalPixels:N0}";
        }

    }
}



