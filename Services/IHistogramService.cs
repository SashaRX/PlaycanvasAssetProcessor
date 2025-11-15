using AssetProcessor.Services.Models;
using OxyPlot;
using System.Windows.Media.Imaging;

namespace AssetProcessor.Services;

public interface IHistogramService {
    void AddSeriesToModel(PlotModel model, int[] histogram, OxyColor color);
    void ProcessImage(BitmapSource bitmapSource, int[] redHistogram, int[] greenHistogram, int[] blueHistogram);
    HistogramStatistics CalculateStatistics(int[] histogram);
}
