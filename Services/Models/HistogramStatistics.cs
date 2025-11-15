namespace AssetProcessor.Services.Models;

public sealed class HistogramStatistics {
    public double Min { get; init; }
    public double Max { get; init; }
    public double Mean { get; init; }
    public double Median { get; init; }
    public double StdDev { get; init; }
    public long TotalPixels { get; init; }
}
