using AssetProcessor.Services.Models;
using OxyPlot;
using OxyPlot.Axes;
using System;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace AssetProcessor.Services;

public class HistogramCoordinator : IHistogramCoordinator {
    private readonly IHistogramService histogramService;
    public HistogramComputationResult? CurrentResult { get; private set; }
    public HistogramStatistics? CurrentStatistics => CurrentResult?.Statistics;

    public HistogramCoordinator(IHistogramService histogramService) {
        this.histogramService = histogramService ?? throw new ArgumentNullException(nameof(histogramService));
    }

    public HistogramComputationResult BuildHistogram(BitmapSource bitmapSource, bool isGray = false) {
        ArgumentNullException.ThrowIfNull(bitmapSource);

        PlotModel histogramModel = new() {
            // Dark theme background
            Background = OxyColor.FromRgb(0x2D, 0x2D, 0x30),
            PlotAreaBackground = OxyColor.FromRgb(0x2D, 0x2D, 0x30),
            PlotAreaBorderColor = OxyColor.FromRgb(0x3F, 0x3F, 0x46),
            PlotAreaBorderThickness = new OxyThickness(0)
        };
        int[] redHistogram = new int[256];
        int[] greenHistogram = new int[256];
        int[] blueHistogram = new int[256];

        histogramService.ProcessImage(bitmapSource, redHistogram, greenHistogram, blueHistogram);

        int[] combinedHistogram = new int[256];
        for (int i = 0; i < 256; i++) {
            combinedHistogram[i] = redHistogram[i] + greenHistogram[i] + blueHistogram[i];
        }

        var stats = histogramService.CalculateStatistics(combinedHistogram);

        if (!isGray) {
            histogramService.AddSeriesToModel(histogramModel, redHistogram, OxyColors.Red);
            histogramService.AddSeriesToModel(histogramModel, greenHistogram, OxyColors.Green);
            histogramService.AddSeriesToModel(histogramModel, blueHistogram, OxyColors.Blue);
        } else {
            histogramService.AddSeriesToModel(histogramModel, redHistogram, OxyColors.Black);
        }

        histogramModel.Axes.Add(new LinearAxis {
            Position = AxisPosition.Bottom,
            IsAxisVisible = false,
            AxislineThickness = 0.5,
            MajorGridlineThickness = 0.5,
            MinorGridlineThickness = 0.5
        });

        histogramModel.Axes.Add(new LinearAxis {
            Position = AxisPosition.Left,
            IsAxisVisible = false,
            AxislineThickness = 0.5,
            MajorGridlineThickness = 0.5,
            MinorGridlineThickness = 0.5
        });

        CurrentResult = new HistogramComputationResult(histogramModel, stats);
        return CurrentResult;
    }

    public Task<HistogramComputationResult> BuildHistogramAsync(BitmapSource bitmapSource, bool isGray = false) {
        ArgumentNullException.ThrowIfNull(bitmapSource);
        return Task.Run(() => BuildHistogram(bitmapSource, isGray));
    }
}
