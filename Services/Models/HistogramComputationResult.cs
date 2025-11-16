using OxyPlot;

namespace AssetProcessor.Services.Models;

public sealed class HistogramComputationResult {
    public HistogramComputationResult(PlotModel model, HistogramStatistics statistics) {
        Model = model;
        Statistics = statistics;
    }

    public PlotModel Model { get; }
    public HistogramStatistics Statistics { get; }
}
