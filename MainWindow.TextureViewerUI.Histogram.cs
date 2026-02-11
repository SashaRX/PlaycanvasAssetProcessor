using AssetProcessor.Helpers;
using AssetProcessor.Settings;
using OxyPlot;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace AssetProcessor {
    public partial class MainWindow {

        #region Histogram

        private void ClearHistogram() {
            var bgColor = ThemeHelper.GetHistogramBackgroundColor();
            var borderColor = ThemeHelper.GetHistogramBorderColor();
            var emptyModel = new PlotModel {
                Background = OxyColor.FromRgb(bgColor.R, bgColor.G, bgColor.B),
                PlotAreaBackground = OxyColor.FromRgb(bgColor.R, bgColor.G, bgColor.B),
                PlotAreaBorderColor = OxyColor.FromRgb(borderColor.R, borderColor.G, borderColor.B),
                PlotAreaBorderThickness = new OxyThickness(0)
            };
            HistogramPlotView.Model = emptyModel;

            HistogramMinTextBlock.Text = "0";
            HistogramMaxTextBlock.Text = "255";
            HistogramMeanTextBlock.Text = "127.5";
            HistogramMedianTextBlock.Text = "128";
            HistogramStdDevTextBlock.Text = "45.2";
            HistogramPixelsTextBlock.Text = "0";
        }

        private void UpdateHistogram(BitmapSource bitmapSource, bool isGray = false) {
            if (bitmapSource == null) return;

            HistogramComputationResult result = histogramCoordinator.BuildHistogram(bitmapSource, isGray);
            _ = Dispatcher.BeginInvoke(new Action(() => ApplyHistogramResult(result)));
        }

        private async Task UpdateHistogramAsync(BitmapSource bitmapSource, bool isGray = false) {
            if (bitmapSource == null) return;

            HistogramComputationResult result = await histogramCoordinator.BuildHistogramAsync(bitmapSource, isGray);
            _ = Dispatcher.BeginInvoke(new Action(() => ApplyHistogramResult(result)));
        }

        private void ApplyHistogramResult(HistogramComputationResult result) {
            HistogramPlotView.Model = result.Model;
            UpdateHistogramStatisticsUI(result.Statistics);
        }

        private void UpdateHistogramStatisticsUI(HistogramStatistics stats) {
            HistogramMinTextBlock.Text = $"{stats.Min:F0}";
            HistogramMaxTextBlock.Text = $"{stats.Max:F0}";
            HistogramMeanTextBlock.Text = $"{stats.Mean:F2}";
            HistogramMedianTextBlock.Text = $"{stats.Median:F0}";
            HistogramStdDevTextBlock.Text = $"{stats.StdDev:F2}";
            HistogramPixelsTextBlock.Text = $"{stats.TotalPixels:N0}";
        }

        #endregion

        #region Histogram Correction

        private void HistogramCorrectionButton_Click(object sender, RoutedEventArgs e) {
            if (sender is System.Windows.Controls.Primitives.ToggleButton button && D3D11TextureViewer?.Renderer != null) {
                bool enabled = button.IsChecked ?? true;
                D3D11TextureViewer.Renderer.SetHistogramCorrection(enabled);

                AppSettings.Default.HistogramCorrectionEnabled = enabled;
                AppSettings.Default.Save();

                D3D11TextureViewer.Renderer.Render();
            }
        }

        /// <summary>
        /// Updates the histogram correction button state based on whether current texture has histogram metadata.
        /// </summary>
        private void UpdateHistogramCorrectionButtonState() {
            if (HistogramCorrectionButton == null || D3D11TextureViewer?.Renderer == null) {
                return;
            }

            bool hasHistogram = D3D11TextureViewer.Renderer.HasHistogramMetadata();
            HistogramCorrectionButton.IsEnabled = hasHistogram;

            if (hasHistogram) {
                bool savedEnabled = AppSettings.Default.HistogramCorrectionEnabled;
                HistogramCorrectionButton.IsChecked = savedEnabled;
                D3D11TextureViewer.Renderer.SetHistogramCorrection(savedEnabled);
                logger.Info($"Histogram correction {(savedEnabled ? "enabled" : "disabled")} (restored from settings)");

                var meta = D3D11TextureViewer.Renderer.GetHistogramMetadata();
                if (meta != null) {
                    string scaleStr = meta.IsPerChannel
                        ? $"[{meta.Scale[0]:F3}, {meta.Scale[1]:F3}, {meta.Scale[2]:F3}]"
                        : meta.Scale[0].ToString("F3");
                    HistogramCorrectionButton.ToolTip = $"Histogram compensation\nScale: {scaleStr}\nOffset: {meta.Offset[0]:F3}";
                    logger.Info($"Histogram metadata found: scale={scaleStr}");
                }
            } else {
                HistogramCorrectionButton.IsChecked = false;
                HistogramCorrectionButton.ToolTip = "No histogram metadata in this texture";
            }
        }

        #endregion
    }
}
