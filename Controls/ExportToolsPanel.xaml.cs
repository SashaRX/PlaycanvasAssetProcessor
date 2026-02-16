using System.Windows.Controls;

namespace AssetProcessor.Controls {
    /// <summary>
    /// Export tools panel: marked assets count, export/upload buttons, texture tools.
    /// Named elements are internal (default WPF access) for use from MainWindow partial classes.
    /// </summary>
    public partial class ExportToolsPanel : UserControl {
        public ExportToolsPanel() {
            InitializeComponent();
        }
    }
}
