using AssetProcessor.Services;
using AssetProcessor.Services.Models;
using OxyPlot;
using OxyPlot.Series;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace AssetProcessor.Tests.Services;

public class HistogramServiceTests {
    private readonly HistogramService service = new();

    [Fact]
    public void AddSeriesToModel_AddsAreaSeriesWithPoints() {
        PlotModel model = new();
        int[] histogram = new int[256];
        histogram[10] = 5;

        service.AddSeriesToModel(model, histogram, OxyColors.Red);

        Assert.Single(model.Series);
        AreaSeries series = Assert.IsType<AreaSeries>(model.Series[0]);
        Assert.Equal(256, series.Points.Count);
        Assert.Equal(256, series.Points2.Count);
    }

    [Fact]
    public void CalculateStatistics_ReturnsExpectedValues() {
        int[] histogram = new int[256];
        histogram[10] = 2;
        histogram[20] = 2;

        HistogramStatistics stats = service.CalculateStatistics(histogram);

        Assert.Equal(10, stats.Min);
        Assert.Equal(20, stats.Max);
        Assert.Equal(15, stats.Mean);
        Assert.Equal(10, stats.Median);
        Assert.Equal(5, stats.StdDev);
        Assert.Equal(4, stats.TotalPixels);
    }

    [Fact]
    public void ProcessImage_PopulatesHistograms() {
        WriteableBitmap bitmap = new(2, 1, 96, 96, PixelFormats.Bgra32, null);
        byte[] pixels = [
            0, 0, 255, 255, // Red pixel (B, G, R, A)
            0, 255, 0, 255  // Green pixel
        ];
        bitmap.WritePixels(new Int32Rect(0, 0, 2, 1), pixels, 8, 0);

        int[] red = new int[256];
        int[] green = new int[256];
        int[] blue = new int[256];

        service.ProcessImage(bitmap, red, green, blue);

        Assert.Equal(1, red[255]);
        Assert.Equal(1, red[0]);
        Assert.Equal(1, green[255]);
        Assert.Equal(1, green[0]);
        Assert.Equal(2, blue[0]);
    }
}
