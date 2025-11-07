using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using NLog;

namespace AssetProcessor.TextureViewer;

/// <summary>
/// Test window for D3D11 texture viewer.
/// </summary>
public partial class D3D11TestWindow : Window {
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();

    private TextureData? currentTexture;
    private float currentZoom = 1.0f;
    private int currentMipLevel = 0;

    // Pan state
    private bool isPanning = false;
    private Point lastMousePosition;
    private float panX = 0.0f;
    private float panY = 0.0f;

    public D3D11TestWindow() {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) {
        // Start render loop
        CompositionTarget.Rendering += OnRendering;
        logger.Info("D3D11 test window loaded, render loop started");
    }

    private void OnRendering(object? sender, EventArgs e) {
        TextureViewer?.RenderFrame();
    }

    private void LoadPngButton_Click(object sender, RoutedEventArgs e) {
        var dialog = new OpenFileDialog {
            Filter = "PNG Files (*.png)|*.png|JPEG Files (*.jpg;*.jpeg)|*.jpg;*.jpeg|All Files (*.*)|*.*",
            Title = "Select PNG/JPEG Texture"
        };

        if (dialog.ShowDialog() == true) {
            try {
                StatusText.Text = "Loading PNG...";
                logger.Info($"Loading PNG: {dialog.FileName}");

                currentTexture = PngTextureLoader.LoadFromFile(dialog.FileName);
                LoadTextureToViewer();

                StatusText.Text = $"Loaded: {Path.GetFileName(dialog.FileName)}";
                logger.Info($"PNG loaded successfully: {currentTexture.Width}x{currentTexture.Height}");
            } catch (Exception ex) {
                StatusText.Text = $"Error: {ex.Message}";
                logger.Error(ex, "Failed to load PNG texture");
                MessageBox.Show($"Failed to load PNG:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void LoadKtx2Button_Click(object sender, RoutedEventArgs e) {
        var dialog = new OpenFileDialog {
            Filter = "KTX2 Files (*.ktx2)|*.ktx2|All Files (*.*)|*.*",
            Title = "Select KTX2 Texture"
        };

        if (dialog.ShowDialog() == true) {
            try {
                StatusText.Text = "Loading KTX2...";
                logger.Info($"Loading KTX2: {dialog.FileName}");
                logger.Info("About to call Ktx2TextureLoader.LoadFromFile...");

                currentTexture = Ktx2TextureLoader.LoadFromFile(dialog.FileName);
                logger.Info("Ktx2TextureLoader.LoadFromFile returned successfully");
                LoadTextureToViewer();

                StatusText.Text = $"Loaded: {Path.GetFileName(dialog.FileName)}";
                logger.Info($"KTX2 loaded successfully: {currentTexture.Width}x{currentTexture.Height}, {currentTexture.MipCount} mips");
            } catch (Exception ex) {
                StatusText.Text = $"Error: {ex.Message}";
                logger.Error(ex, "Failed to load KTX2 texture");
                MessageBox.Show($"Failed to load KTX2:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void LoadTextureToViewer() {
        if (currentTexture == null) {
            logger.Warn("currentTexture is null");
            return;
        }

        if (TextureViewer == null) {
            logger.Warn("TextureViewer control is null");
            return;
        }

        if (TextureViewer.Renderer == null) {
            logger.Error("D3D11 Renderer is null - D3D11 initialization failed!");
            MessageBox.Show(
                "D3D11 renderer failed to initialize.\nCheck logs for details.",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        logger.Info($"Loading texture to D3D11 viewer: {currentTexture.Width}x{currentTexture.Height}, {currentTexture.MipCount} mips");

        // Load texture to GPU
        try {
            TextureViewer.Renderer.LoadTexture(currentTexture);
            logger.Info("Texture loaded to GPU successfully");
        } catch (Exception ex) {
            logger.Error(ex, "Failed to load texture to GPU");
            MessageBox.Show($"Failed to load texture to GPU:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Update UI
        MipSlider.Maximum = Math.Max(0, currentTexture.MipCount - 1);
        MipSlider.Value = 0;
        currentMipLevel = 0;

        ZoomSlider.Value = 1.0;
        currentZoom = 1.0f;

        SrgbCheckBox.IsChecked = currentTexture.IsSRGB;

        // Update histogram correction checkbox state
        bool hasHistogram = TextureViewer.Renderer.HasHistogramMetadata();
        HistogramCorrectionCheckBox.IsEnabled = hasHistogram;
        HistogramCorrectionCheckBox.IsChecked = hasHistogram; // Enable if metadata present

        UpdateInfoText();
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
        if (TextureViewer?.Renderer == null) return;

        currentZoom = (float)e.NewValue;
        TextureViewer.Renderer.SetZoom(currentZoom);

        if (ZoomText != null) {
            ZoomText.Text = $"{currentZoom * 100:F0}%";
        }
    }

    private void MipSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
        if (TextureViewer?.Renderer == null) return;

        currentMipLevel = (int)e.NewValue;
        TextureViewer.Renderer.SetMipLevel(currentMipLevel);

        if (MipText != null) {
            MipText.Text = $"Mip {currentMipLevel}";
        }
    }

    private void ResetZoomButton_Click(object sender, RoutedEventArgs e) {
        ZoomSlider.Value = 1.0;
        panX = 0.0f;
        panY = 0.0f;
        if (TextureViewer?.Renderer != null) {
            TextureViewer.Renderer.SetPan(0, 0);
        }
    }

    private void FilterCheckBox_Changed(object sender, RoutedEventArgs e) {
        if (TextureViewer?.Renderer == null) return;

        bool linear = LinearFilterCheckBox.IsChecked == true;
        TextureViewer.Renderer.SetFilter(linear);
    }

    private void SrgbCheckBox_Changed(object sender, RoutedEventArgs e) {
        // To change sRGB, we need to reload the texture with different format
        // For now, just update the checkbox state
        // TODO: Implement reload with different format
    }

    private void HistogramCorrectionCheckBox_Changed(object sender, RoutedEventArgs e) {
        if (TextureViewer?.Renderer == null) return;

        bool enabled = HistogramCorrectionCheckBox.IsChecked == true;
        TextureViewer.Renderer.SetHistogramCorrection(enabled);

        logger.Info($"Histogram correction {(enabled ? "enabled" : "disabled")} by user");
    }

    private void Overlay_MouseWheel(object sender, MouseWheelEventArgs e) {
        if (TextureViewer?.Renderer == null) return;

        logger.Info($"Overlay mouse wheel: delta={e.Delta}, zoom before={currentZoom}");

        // Zoom with mouse wheel
        float zoomDelta = e.Delta > 0 ? 1.1f : 0.9f;
        currentZoom *= zoomDelta;
        currentZoom = Math.Clamp(currentZoom, 0.125f, 16.0f);

        ZoomSlider.Value = currentZoom;
        TextureViewer.Renderer.SetZoom(currentZoom);

        logger.Info($"Zoom after={currentZoom}");
        e.Handled = true;
    }

    private void Overlay_MouseDown(object sender, MouseButtonEventArgs e) {
        if (e.MiddleButton == MouseButtonState.Pressed) {
            Point mousePos = e.GetPosition(ViewerBorder);
            logger.Info($"Overlay middle button down at {mousePos.X}, {mousePos.Y}");

            isPanning = true;
            lastMousePosition = mousePos;
            MouseCaptureOverlay.CaptureMouse();
            e.Handled = true;
        }
    }

    private void Overlay_MouseUp(object sender, MouseButtonEventArgs e) {
        if (e.MiddleButton == MouseButtonState.Released && isPanning) {
            logger.Info("Overlay middle button up - stop panning");
            isPanning = false;
            MouseCaptureOverlay.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void Overlay_MouseMove(object sender, MouseEventArgs e) {
        if (!isPanning || TextureViewer?.Renderer == null) return;

        Point currentPosition = e.GetPosition(ViewerBorder);
        double deltaX = currentPosition.X - lastMousePosition.X;
        double deltaY = currentPosition.Y - lastMousePosition.Y;

        // Convert screen delta to normalized texture space
        // Account for zoom level
        float panSpeed = 0.002f / currentZoom;
        panX += (float)deltaX * panSpeed;
        panY -= (float)deltaY * panSpeed; // Invert Y for correct direction

        TextureViewer.Renderer.SetPan(panX, panY);

        lastMousePosition = currentPosition;
        e.Handled = true;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e) {
        if (TextureViewer?.Renderer == null) return;

        // Arrow keys for panning
        float panSpeed = 0.02f / currentZoom;
        bool handled = true;

        switch (e.Key) {
            case Key.Left:
                panX -= panSpeed;
                break;
            case Key.Right:
                panX += panSpeed;
                break;
            case Key.Up:
                panY -= panSpeed;
                break;
            case Key.Down:
                panY += panSpeed;
                break;
            default:
                handled = false;
                break;
        }

        if (handled) {
            TextureViewer.Renderer.SetPan(panX, panY);
            e.Handled = true;
        }
    }

    private void UpdateInfoText() {
        if (currentTexture == null) {
            InfoText.Text = "No texture loaded";
            return;
        }

        string histogramInfo = "";
        if (currentTexture.HistogramMetadata != null) {
            var meta = currentTexture.HistogramMetadata;
            string scaleStr = meta.IsPerChannel
                ? $"[{meta.Scale[0]:F3}, {meta.Scale[1]:F3}, {meta.Scale[2]:F3}]"
                : meta.Scale[0].ToString("F3");
            histogramInfo = $" | Histogram: scale={scaleStr}";
        }

        InfoText.Text = $"Resolution: {currentTexture.Width}x{currentTexture.Height} | " +
                       $"Mips: {currentTexture.MipCount} | " +
                       $"Format: {currentTexture.SourceFormat} | " +
                       $"sRGB: {currentTexture.IsSRGB} | " +
                       $"Alpha: {currentTexture.HasAlpha} | " +
                       $"HDR: {currentTexture.IsHDR}" +
                       histogramInfo;
    }

    protected override void OnClosed(EventArgs e) {
        base.OnClosed(e);
        CompositionTarget.Rendering -= OnRendering;
        currentTexture?.Dispose();
        logger.Info("D3D11 test window closed");
    }
}
