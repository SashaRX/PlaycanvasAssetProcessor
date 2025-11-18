using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using AssetProcessor.Services;
using AssetProcessor.Services.Models;
using OxyPlot;
using Xunit;

namespace AssetProcessor.Tests.Services;

public class HistogramCoordinatorTests {
    [Fact]
    public void BuildHistogram_UpdatesCurrentResultAndAddsColorSeries() {
        FakeHistogramService histogramService = new();
        HistogramCoordinator coordinator = new(histogramService);
        BitmapSource bitmap = new WriteableBitmap(2, 2, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);

        HistogramComputationResult result = coordinator.BuildHistogram(bitmap, isGray: false);

        Assert.NotNull(result);
        Assert.Same(result, coordinator.CurrentResult);
        Assert.Equal(histogramService.Statistics, result.Statistics);
        Assert.Same(bitmap, histogramService.LastProcessedBitmap);
        Assert.Equal(3, histogramService.AddedSeries.Count);
        Assert.Contains(histogramService.AddedSeries, entry => entry.Color == OxyColors.Red);
        Assert.Contains(histogramService.AddedSeries, entry => entry.Color == OxyColors.Green);
        Assert.Contains(histogramService.AddedSeries, entry => entry.Color == OxyColors.Blue);
        Assert.Equal(6, histogramService.LastCombinedHistogram![1]);
    }

    [Fact]
    public void BuildHistogram_WhenGray_AddsSingleSeries() {
        FakeHistogramService histogramService = new();
        HistogramCoordinator coordinator = new(histogramService);
        BitmapSource bitmap = new WriteableBitmap(1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);

        HistogramComputationResult result = coordinator.BuildHistogram(bitmap, isGray: true);

        Assert.Single(histogramService.AddedSeries);
        Assert.Equal(OxyColors.Black, histogramService.AddedSeries[0].Color);
        Assert.Same(result, coordinator.CurrentResult);
        Assert.Equal(histogramService.Statistics, coordinator.CurrentStatistics);
    }

    [Fact]
    public async Task BuildHistogramAsync_UsesThreadPoolAndReturnsResult() {
        FakeHistogramService histogramService = new();
        HistogramCoordinator coordinator = new(histogramService);
        BitmapSource bitmap = new WriteableBitmap(4, 4, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);

        HistogramComputationResult result = await coordinator.BuildHistogramAsync(bitmap);

        Assert.NotNull(result);
        Assert.Same(result, coordinator.CurrentResult);
        Assert.True(histogramService.ProcessImageCallCount > 0);
    }

    private sealed class FakeHistogramService : IHistogramService {
        public HistogramStatistics Statistics { get; } = new() {
            Min = 1,
            Max = 2,
            Mean = 1.5,
            Median = 1,
            StdDev = 0.5,
            TotalPixels = 3
        };

        public List<(PlotModel Model, int[] Histogram, OxyColor Color)> AddedSeries { get; } = new();
        public BitmapSource? LastProcessedBitmap { get; private set; }
        public int[]? LastCombinedHistogram { get; private set; }
        public int ProcessImageCallCount { get; private set; }

        public void AddSeriesToModel(PlotModel model, int[] histogram, OxyColor color) {
            AddedSeries.Add((model, (int[])histogram.Clone(), color));
        }

        public HistogramStatistics CalculateStatistics(int[] histogram) {
            LastCombinedHistogram = (int[])histogram.Clone();
            return Statistics;
        }

        public void ProcessImage(BitmapSource bitmapSource, int[] redHistogram, int[] greenHistogram, int[] blueHistogram) {
            LastProcessedBitmap = bitmapSource;
            ProcessImageCallCount++;
            redHistogram[1] = 1;
            greenHistogram[1] = 2;
            blueHistogram[1] = 3;
        }
    }
}
