using System.Windows.Controls;

namespace AssetProcessor.Controls {
    /// <summary>
    /// Connection panel: PlayCanvas project connect button, project/branch combo boxes.
    /// Named elements are internal (default WPF access) for use from MainWindow.Connection.cs.
    /// </summary>
    public partial class ConnectionPanel : UserControl {
        public ConnectionPanel() {
            InitializeComponent();
        }
    }
}
