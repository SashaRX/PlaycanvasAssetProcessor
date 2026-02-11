using AssetProcessor.Resources;
using AssetProcessor.Services;
using AssetProcessor.TextureViewer;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;

namespace AssetProcessor {
    public partial class MainWindow {

        #region Viewer Button Handlers

        private void FitResetButton_Click(object sender, RoutedEventArgs e) {
            D3D11TextureViewer?.ResetView();

            texturePreviewService.CurrentActiveChannelMask = null;
            if (texturePreviewService.IsUsingD3D11Renderer && D3D11TextureViewer?.Renderer != null) {
                D3D11TextureViewer.Renderer.SetChannelMask(0xFFFFFFFF);
                D3D11TextureViewer.Renderer.RestoreOriginalGamma();
                D3D11TextureViewer.Renderer.Render();
            }

            UpdateChannelButtonsState();
        }

        private void FilterToggleButton_Click(object sender, RoutedEventArgs e) {
            if (sender is ToggleButton button) {
                bool useLinearFilter = button.IsChecked ?? true;
                D3D11TextureViewer?.Renderer?.SetFilter(useLinearFilter);
            }
        }

        private void TileToggleButton_Click(object sender, RoutedEventArgs e) {
            if (sender is ToggleButton button && D3D11TextureViewer?.Renderer != null) {
                bool enableTiling = button.IsChecked ?? false;
                D3D11TextureViewer.Renderer.SetTiling(enableTiling);
                D3D11TextureViewer.Renderer.Render();
            }
        }

        #endregion

        #region Preview State

        private void ResetPreviewState() {
            CancelPendingD3DPreviewLoad();
            texturePreviewService.ResetPreviewState();
            ClearPreviewReferenceSize();
            HideMipmapControls();
            UpdatePreviewSourceControls();
            ClearHistogram();
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

        #endregion

        #region Preview Image Display

        private void UpdatePreviewImage(BitmapSource bitmap, bool setReference, bool preserveViewport) {
            if (bitmap == null || D3D11TextureViewer == null) return;

            try {
                bool isSRGB = IsSRGBTexture(texturePreviewService.CurrentSelectedTexture);
                LoadTextureToD3D11Viewer(bitmap, isSRGB);
            } catch (Exception ex) {
                logger.Error(ex, "Exception in UpdatePreviewImage");
            }
        }

        private static (double width, double height) GetImageSizeInDips(BitmapSource bitmap) {
            if (bitmap == null) {
                return (0, 0);
            }

            double dpiX = bitmap.DpiX;
            double dpiY = bitmap.DpiY;
            if (dpiX <= 0) dpiX = 96.0;
            if (dpiY <= 0) dpiY = 96.0;

            double width = bitmap.PixelWidth * 96.0 / dpiX;
            double height = bitmap.PixelHeight * 96.0 / dpiY;
            return (width, height);
        }

        /// <summary>
        /// Determines if a texture should be treated as sRGB based on its type or name.
        /// </summary>
        private bool IsSRGBTexture(TextureResource? texture) {
            if (texture == null) {
                return true;
            }

            string? textureType = texture.TextureType;
            if (string.IsNullOrEmpty(textureType)) {
                textureType = TextureResource.DetermineTextureType(texture.Name ?? "");
            }

            return textureType?.ToLower() switch {
                "normal" => false,
                "gloss" => false,
                "roughness" => false,
                "metallic" => false,
                "ao" => false,
                "opacity" => false,
                "height" => false,
                _ => true
            };
        }

        /// <summary>
        /// Prepares a BitmapSource for WPF display (gamma correction disabled due to distortion issues).
        /// </summary>
        private BitmapSource PrepareForWPFDisplay(BitmapSource bitmap) {
            return bitmap;
        }

        private static byte ApplyGammaToByteChannel(byte value) {
            float normalized = value / 255.0f;
            float corrected = MathF.Pow(normalized, 1.0f / 2.2f);
            return (byte)Math.Clamp(corrected * 255.0f, 0, 255);
        }

        #endregion

        #region Preview Source Mode

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
                SetPreviewSourceToOriginal();
            } else if (mode == TexturePreviewSourceMode.Ktx2) {
                SetPreviewSourceToKtx2();
            }

            UpdatePreviewSourceControls();
        }

        private void SetPreviewSourceToOriginal() {
            texturePreviewService.IsKtxPreviewActive = false;
            HideMipmapControls();

            if (texturePreviewService.OriginalFileBitmapSource != null) {
                texturePreviewService.OriginalBitmapSource = texturePreviewService.OriginalFileBitmapSource;
                _ = UpdateHistogramAsync(texturePreviewService.OriginalBitmapSource);
                ShowOriginalImage(preserveMask: true);

                if (TextureFormatTextBlock != null) {
                    bool isSRGB = IsSRGBTexture(texturePreviewService.CurrentSelectedTexture);
                    string formatInfo = isSRGB ? "PNG (sRGB data)" : "PNG (Linear data)";
                    TextureFormatTextBlock.Text = $"Format: {formatInfo}";
                }
            } else {
                ClearD3D11Viewer();
            }
        }

        private void SetPreviewSourceToKtx2() {
            texturePreviewService.IsKtxPreviewActive = true;

            if (texturePreviewService.OriginalFileBitmapSource != null) {
                _ = UpdateHistogramAsync(texturePreviewService.OriginalFileBitmapSource);
            }

            string? savedMask = texturePreviewService.CurrentActiveChannelMask;

            if (texturePreviewService.CurrentKtxMipmaps != null && texturePreviewService.CurrentKtxMipmaps.Count > 0) {
                // Old method: extracted PNG mipmaps available (WPF mode)
                UpdateMipmapControls(texturePreviewService.CurrentKtxMipmaps);
                SetCurrentMipLevel(texturePreviewService.CurrentMipLevel);
            } else if (D3D11TextureViewer?.Renderer != null &&
                       D3D11TextureViewer.Renderer.GetCurrentTexturePath() == texturePreviewService.CurrentLoadedKtx2Path &&
                       !string.IsNullOrEmpty(texturePreviewService.CurrentLoadedKtx2Path)) {
                // KTX2 already loaded in D3D11
                UpdateD3D11MipmapControls(D3D11TextureViewer.Renderer.MipCount);
                UpdateHistogramCorrectionButtonState();

                if (savedMask != null && texturePreviewService.CurrentActiveChannelMask != "Normal") {
                    texturePreviewService.CurrentActiveChannelMask = savedMask;
                    _ = FilterChannelAsync(savedMask);
                }
            } else if (texturePreviewService.IsUsingD3D11Renderer && !string.IsNullOrEmpty(texturePreviewService.CurrentLoadedKtx2Path)) {
                // Need to reload KTX2 to D3D11
                if (IsKtx2Loading(texturePreviewService.CurrentLoadedKtx2Path)) {
                    return;
                }

                if (D3D11TextureViewer?.Renderer != null) {
                    string? currentTexturePath = D3D11TextureViewer.Renderer.GetCurrentTexturePath();
                    if (currentTexturePath != null && string.Equals(currentTexturePath, texturePreviewService.CurrentLoadedKtx2Path, StringComparison.OrdinalIgnoreCase)) {
                        UpdateD3D11MipmapControls(D3D11TextureViewer.Renderer.MipCount);
                        UpdateHistogramCorrectionButtonState();
                        return;
                    }
                }

                _ = Task.Run(async () => {
                    try {
                        await LoadKtx2ToD3D11ViewerAsync(texturePreviewService.CurrentLoadedKtx2Path);
                        _ = Dispatcher.BeginInvoke(new Action(() => {
                            if (D3D11TextureViewer?.Renderer != null && D3D11TextureViewer.Renderer.MipCount > 0) {
                                UpdateD3D11MipmapControls(D3D11TextureViewer.Renderer.MipCount);

                                if (savedMask != null && texturePreviewService.CurrentActiveChannelMask != "Normal") {
                                    texturePreviewService.CurrentActiveChannelMask = savedMask;
                                    _ = FilterChannelAsync(savedMask);
                                }
                            }
                        }));
                    } catch (Exception ex) {
                        logger.Error(ex, "Failed to reload KTX2 when switching to KTX2 mode");
                    }
                });
            } else {
                HideMipmapControls();
            }
        }

        #endregion

        #region Mipmap Controls

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
                    ? $"Mip 0 of {Math.Max(0, mipmaps.Count - 1)} | {mipmaps[0].Width}x{mipmaps[0].Height}"
                    : "No mipmaps";
            } finally {
                texturePreviewService.IsUpdatingMipLevel = false;
            }
        }

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
                MipmapInfoTextBlock.Text = $"Mip 0 of {Math.Max(0, mipCount - 1)} | {width}x{height} (D3D11)";
            } finally {
                texturePreviewService.IsUpdatingMipLevel = false;
            }
        }

        private void UpdateMipmapInfo(KtxMipLevel mipLevel, int totalLevels) {
            if (MipmapInfoTextBlock != null) {
                int maxLevel = Math.Max(0, totalLevels - 1);
                MipmapInfoTextBlock.Text = $"Mip {mipLevel.Level} of {maxLevel} | {mipLevel.Width}x{mipLevel.Height}";
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

            _ = Dispatcher.BeginInvoke(new Action(() => {
                UpdatePreviewImage(texturePreviewService.OriginalBitmapSource, setReference: clampedLevel == 0, preserveViewport: false);

                if (WpfTexturePreviewImage.Visibility == Visibility.Visible) {
                    WpfTexturePreviewImage.Source = PrepareForWPFDisplay(texturePreviewService.OriginalBitmapSource);
                }

                UpdateHistogram(texturePreviewService.OriginalBitmapSource);
            }));

            UpdateMipmapInfo(mip, texturePreviewService.CurrentKtxMipmaps.Count);
        }

        private void MipmapLevelSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            if (texturePreviewService.IsUpdatingMipLevel || !texturePreviewService.IsKtxPreviewActive) {
                return;
            }

            int newLevel = (int)Math.Round(e.NewValue);
            if (newLevel != texturePreviewService.CurrentMipLevel) {
                if (texturePreviewService.CurrentKtxMipmaps != null && texturePreviewService.CurrentKtxMipmaps.Count > 0) {
                    SetCurrentMipLevel(newLevel, updateSlider: false);
                } else if (D3D11TextureViewer?.Renderer != null) {
                    texturePreviewService.CurrentMipLevel = newLevel;
                    D3D11TextureViewer.Renderer.SetMipLevel(newLevel);

                    if (MipmapInfoTextBlock != null) {
                        int mipCount = D3D11TextureViewer.Renderer.MipCount;
                        int width = D3D11TextureViewer.Renderer.TextureWidth >> newLevel;
                        int height = D3D11TextureViewer.Renderer.TextureHeight >> newLevel;
                        MipmapInfoTextBlock.Text = $"Mip {newLevel} of {Math.Max(0, mipCount - 1)} | {width}x{height} (D3D11)";
                    }
                }
            }
        }

        #endregion

        #region Preview Height Resize

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

        #endregion
    }
}
