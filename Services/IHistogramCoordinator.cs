using AssetProcessor.Services.Models;
using OxyPlot;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace AssetProcessor.Services;

public interface IHistogramCoordinator {
    HistogramComputationResult? CurrentResult { get; }
    HistogramStatistics? CurrentStatistics { get; }
    HistogramComputationResult BuildHistogram(BitmapSource bitmapSource, bool isGray = false);
    Task<HistogramComputationResult> BuildHistogramAsync(BitmapSource bitmapSource, bool isGray = false);
}
