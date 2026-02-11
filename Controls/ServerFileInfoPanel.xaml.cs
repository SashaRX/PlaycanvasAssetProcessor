using System.Windows.Controls;

namespace AssetProcessor.Controls {
    /// <summary>
    /// Server file info panel: displays details about a selected server asset
    /// (name, type, size, sync status, SHA1, CDN URL, etc.).
    /// Named elements are internal (default WPF access) for use from MainWindow.ServerAssets.cs.
    /// Event handlers are wired programmatically from MainWindow.
    /// </summary>
    public partial class ServerFileInfoPanel : UserControl {
        public ServerFileInfoPanel() {
            InitializeComponent();
        }
    }
}
