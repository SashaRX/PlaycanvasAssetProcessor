using System.Windows.Controls;

namespace AssetProcessor.Controls {
    /// <summary>
    /// Histogram panel: OxyPlot histogram chart + statistics (Min, Max, Mean, Median, StdDev, Pixels).
    /// Named elements are internal (default WPF access) for use from MainWindow.TextureViewerUI.Histogram.cs.
    /// </summary>
    public partial class HistogramPanel : UserControl {
        public HistogramPanel() {
            InitializeComponent();
        }
    }
}
