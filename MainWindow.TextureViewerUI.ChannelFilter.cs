using AssetProcessor.Resources;
using AssetProcessor.Services;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;

namespace AssetProcessor {
    public partial class MainWindow {
        private async void FilterButton_Click(object sender, RoutedEventArgs e) {
            // Ignore programmatic updates to prevent recursive calls
            if (isUpdatingChannelButtons) {
                return;
            }

            if (sender is not ToggleButton button) return;

            string? channel = button.Tag?.ToString();

            if (button.IsChecked == true) {
                // Set flag to prevent re-entrancy from TwoWay binding updates
                isUpdatingChannelButtons = true;
                try {
                    // Снять все остальные кнопки (including NormalButton)
                    viewModel.IsRChannelChecked = channel == "R";
                    viewModel.IsGChannelChecked = channel == "G";
                    viewModel.IsBChannelChecked = channel == "B";
                    viewModel.IsAChannelChecked = channel == "A";
                    viewModel.IsNormalChannelChecked = channel == "Normal";
                } finally {
                    isUpdatingChannelButtons = false;
                }

                // Применить фильтр
                if (!string.IsNullOrEmpty(channel)) {
                    await FilterChannelAsync(channel);
                }
            } else {
                // Сбрасываем фильтр, если кнопка была снята
                HandleChannelMaskCleared();
            }
        }

        /// <summary>
        /// Synchronize channel button GUI state with texturePreviewService.CurrentActiveChannelMask.
        /// </summary>
        private void UpdateChannelButtonsState() {
            isUpdatingChannelButtons = true;
            try {
                var mask = texturePreviewService.CurrentActiveChannelMask;
                viewModel.IsRChannelChecked = mask == "R";
                viewModel.IsGChannelChecked = mask == "G";
                viewModel.IsBChannelChecked = mask == "B";
                viewModel.IsAChannelChecked = mask == "A";
                viewModel.IsNormalChannelChecked = mask == "Normal";
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

                // Refresh histogram using best available bitmap reference
                BitmapSource? histogramSource = texturePreviewService.OriginalBitmapSource ?? texturePreviewService.OriginalFileBitmapSource;
                if (histogramSource != null) {
                    UpdateHistogram(histogramSource);
                }

                return;
            }

            ShowOriginalImage();
        }

        private async Task FilterChannelAsync(string channel) {
            // Save current active mask
            texturePreviewService.CurrentActiveChannelMask = channel;

            // Sync GUI buttons - use BeginInvoke to avoid deadlock
            _ = Dispatcher.BeginInvoke(new Action(() => UpdateChannelButtonsState()));

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
                D3D11TextureViewer.Renderer.Render();

                logger.Info($"Applied D3D11 channel mask: {channel} = 0x{mask:X}");

                // Update histogram for the filtered channel (D3D11 filters on GPU, but histogram needs CPU filtering)
                // CRITICAL: For KTX2/D3D11 mode, ALWAYS use OriginalFileBitmapSource (extracted from KTX2)
                BitmapSource? histogramSource = texturePreviewService.OriginalFileBitmapSource;
                if (histogramSource != null) {
                    if (channel == "Normal") {
                        // For normal map mode, show RGB histogram (no grayscale) - use BeginInvoke
                        _ = Dispatcher.BeginInvoke(new Action(() => {
                            UpdateHistogram(histogramSource, false);
                        }));
                    } else {
                        // For R/G/B/A channels, show grayscale histogram
                        BitmapSource filteredBitmap = await textureChannelService.ApplyChannelFilterAsync(histogramSource, channel);
                        _ = Dispatcher.BeginInvoke(new Action(() => {
                            UpdateHistogram(filteredBitmap, true);
                        }));
                    }
                }

                return;
            }

            // WPF mode: use bitmap filtering
            BitmapSource? wpfHistogramSource = texturePreviewService.OriginalFileBitmapSource ?? texturePreviewService.OriginalBitmapSource;
            if (wpfHistogramSource != null) {
                BitmapSource filteredBitmap = await textureChannelService.ApplyChannelFilterAsync(wpfHistogramSource, channel);

                _ = Dispatcher.BeginInvoke(new Action(() => {
                    UpdatePreviewImage(filteredBitmap, setReference: false, preserveViewport: true);

                    if (WpfTexturePreviewImage.Visibility == Visibility.Visible) {
                        WpfTexturePreviewImage.Source = PrepareForWPFDisplay(filteredBitmap);
                    }

                    UpdateHistogram(filteredBitmap, true);
                }));
            }
        }

        private void ShowOriginalImage(bool recalculateFitZoom = false, bool preserveMask = false) {
            // Only clear mask if explicitly requested (NOT when switching Source<->KTX)
            if (!preserveMask) {
                texturePreviewService.CurrentActiveChannelMask = null;

                // Reset channel filter in D3D11 mode
                if (texturePreviewService.IsUsingD3D11Renderer && D3D11TextureViewer?.Renderer != null) {
                    D3D11TextureViewer.Renderer.SetChannelMask(0xFFFFFFFF);
                    D3D11TextureViewer.Renderer.RestoreOriginalGamma();
                }
            } else {
                // Preserve mask - reapply it if active
                if (texturePreviewService.CurrentActiveChannelMask != null && texturePreviewService.IsUsingD3D11Renderer && D3D11TextureViewer?.Renderer != null) {
                    _ = FilterChannelAsync(texturePreviewService.CurrentActiveChannelMask);
                }
            }

            if (texturePreviewService.OriginalBitmapSource != null) {
                _ = Dispatcher.BeginInvoke(new Action(() => {
                    UpdatePreviewImage(texturePreviewService.OriginalBitmapSource, setReference: true, preserveViewport: !recalculateFitZoom);

                    if (WpfTexturePreviewImage.Visibility == Visibility.Visible) {
                        WpfTexturePreviewImage.Source = PrepareForWPFDisplay(texturePreviewService.OriginalBitmapSource);
                    }

                    UpdateChannelButtonsState();
                    UpdateHistogram(texturePreviewService.OriginalBitmapSource);

                    // AUTO-ENABLE Normal reconstruction for normal map textures (PNG)
                    if (!preserveMask && texturePreviewService.CurrentSelectedTexture?.TextureType?.ToLower() == "normal" && D3D11TextureViewer?.Renderer != null) {
                        texturePreviewService.CurrentActiveChannelMask = "Normal";
                        D3D11TextureViewer.Renderer.SetChannelMask(0x20);
                        D3D11TextureViewer.Renderer.Render();
                        UpdateChannelButtonsState();
                    }
                }));
            }
        }
    }
}
