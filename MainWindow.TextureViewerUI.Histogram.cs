using AssetProcessor.Helpers;
using AssetProcessor.Services.Models;
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
            viewModel.HistogramPlotModel = emptyModel;

            viewModel.HistogramMin = "0";
            viewModel.HistogramMax = "255";
            viewModel.HistogramMean = "127.5";
            viewModel.HistogramMedian = "128";
            viewModel.HistogramStdDev = "45.2";
            viewModel.HistogramPixels = "0";
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
            viewModel.HistogramPlotModel = result.Model;
            UpdateHistogramStatisticsUI(result.Statistics);
        }

        private void UpdateHistogramStatisticsUI(HistogramStatistics stats) {
            viewModel.HistogramMin = $"{stats.Min:F0}";
            viewModel.HistogramMax = $"{stats.Max:F0}";
            viewModel.HistogramMean = $"{stats.Mean:F2}";
            viewModel.HistogramMedian = $"{stats.Median:F0}";
            viewModel.HistogramStdDev = $"{stats.StdDev:F2}";
            viewModel.HistogramPixels = $"{stats.TotalPixels:N0}";
        }

        #endregion

        #region Histogram Correction

        private void HistogramCorrectionButton_Click(object sender, RoutedEventArgs e) {
            if (D3D11TextureViewer?.Renderer != null) {
                bool enabled = viewModel.IsHistogramCorrectionChecked;
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
            if (D3D11TextureViewer?.Renderer == null) {
                return;
            }

            bool hasHistogram = D3D11TextureViewer.Renderer.HasHistogramMetadata();
            viewModel.IsHistogramCorrectionEnabled = hasHistogram;

            if (hasHistogram) {
                bool savedEnabled = AppSettings.Default.HistogramCorrectionEnabled;
                viewModel.IsHistogramCorrectionChecked = savedEnabled;
                D3D11TextureViewer.Renderer.SetHistogramCorrection(savedEnabled);
                logger.Info($"Histogram correction {(savedEnabled ? "enabled" : "disabled")} (restored from settings)");

                var meta = D3D11TextureViewer.Renderer.GetHistogramMetadata();
                if (meta != null) {
                    string scaleStr = meta.IsPerChannel
                        ? $"[{meta.Scale[0]:F3}, {meta.Scale[1]:F3}, {meta.Scale[2]:F3}]"
                        : meta.Scale[0].ToString("F3");
                    viewModel.HistogramCorrectionToolTip = $"Histogram compensation\nScale: {scaleStr}\nOffset: {meta.Offset[0]:F3}";
                    logger.Info($"Histogram metadata found: scale={scaleStr}");
                }
            } else {
                viewModel.IsHistogramCorrectionChecked = false;
                viewModel.HistogramCorrectionToolTip = "No histogram metadata in this texture";
            }
        }

        #endregion
    }
}
