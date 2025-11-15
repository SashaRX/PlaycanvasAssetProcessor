using AssetProcessor.Services.Models;
using OxyPlot;
using OxyPlot.Series;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Windows.Media.Imaging;

namespace AssetProcessor.Services;

public sealed class HistogramService : IHistogramService {
    public void AddSeriesToModel(PlotModel model, int[] histogram, OxyColor color) {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(histogram);

        OxyColor colorWithAlpha = OxyColor.FromAColor(100, color);
        AreaSeries series = new() { Color = color, Fill = colorWithAlpha, StrokeThickness = 1 };

        double[] smoothedHistogram = MovingAverage(histogram, 32);

        for (int i = 0; i < 256; i++) {
            series.Points.Add(new DataPoint(i, smoothedHistogram[i]));
            series.Points2.Add(new DataPoint(i, 0));
        }

        model.Series.Add(series);
    }

    public void ProcessImage(BitmapSource bitmapSource, int[] redHistogram, int[] greenHistogram, int[] blueHistogram) {
        ArgumentNullException.ThrowIfNull(bitmapSource);
        ArgumentNullException.ThrowIfNull(redHistogram);
        ArgumentNullException.ThrowIfNull(greenHistogram);
        ArgumentNullException.ThrowIfNull(blueHistogram);

        using Image<Rgba32> image = SixLabors.ImageSharp.Image.Load<Rgba32>(BitmapSourceToArray(bitmapSource));

        object lockObject = new();

        Parallel.For(0, image.Height, () => (Red: new int[256], Green: new int[256], Blue: new int[256]),
            (y, _, localHistograms) => {
                Span<Rgba32> pixelRow = image.Frames.RootFrame.DangerousGetPixelRowMemory(y).Span;
                for (int x = 0; x < pixelRow.Length; x++) {
                    Rgba32 pixel = pixelRow[x];
                    localHistograms.Red[pixel.R]++;
                    localHistograms.Green[pixel.G]++;
                    localHistograms.Blue[pixel.B]++;
                }
                return localHistograms;
            },
            localHistograms => {
                lock (lockObject) {
                    for (int i = 0; i < 256; i++) {
                        redHistogram[i] += localHistograms.Red[i];
                        greenHistogram[i] += localHistograms.Green[i];
                        blueHistogram[i] += localHistograms.Blue[i];
                    }
                }
            });
    }

    public HistogramStatistics CalculateStatistics(int[] histogram) {
        ArgumentNullException.ThrowIfNull(histogram);

        long totalPixels = 0;
        double weightedSum = 0;
        int min = -1;
        int max = -1;

        for (int i = 0; i < histogram.Length; i++) {
            long count = histogram[i];
            if (count > 0) {
                if (min == -1) {
                    min = i;
                }
                max = i;
                totalPixels += count;
                weightedSum += i * count;
            }
        }

        if (totalPixels == 0) {
            return new HistogramStatistics {
                Min = 0,
                Max = 0,
                Mean = 0,
                Median = 0,
                StdDev = 0,
                TotalPixels = 0
            };
        }

        double mean = weightedSum / totalPixels;

        long halfPixels = totalPixels / 2;
        long accumulatedPixels = 0;
        int median = 0;
        for (int i = 0; i < histogram.Length; i++) {
            accumulatedPixels += histogram[i];
            if (accumulatedPixels >= halfPixels) {
                median = i;
                break;
            }
        }

        double varianceSum = 0;
        for (int i = 0; i < histogram.Length; i++) {
            if (histogram[i] > 0) {
                double diff = i - mean;
                varianceSum += diff * diff * histogram[i];
            }
        }
        double stdDev = Math.Sqrt(varianceSum / totalPixels);

        return new HistogramStatistics {
            Min = min,
            Max = max,
            Mean = mean,
            Median = median,
            StdDev = stdDev,
            TotalPixels = totalPixels
        };
    }

    private static double[] MovingAverage(int[] values, int windowSize) {
        double[] result = new double[values.Length];
        double sum = 0;
        for (int i = 0; i < values.Length; i++) {
            sum += values[i];
            if (i >= windowSize) {
                sum -= values[i - windowSize];
            }
            result[i] = sum / Math.Min(windowSize, i + 1);
        }
        return result;
    }

    private static byte[] BitmapSourceToArray(BitmapSource bitmapSource) {
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create((BitmapSource)bitmapSource.Clone()));
        using MemoryStream stream = new();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
