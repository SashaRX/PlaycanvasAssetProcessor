using System.Windows.Controls;

namespace AssetProcessor.Controls {
    /// <summary>
    /// Chunk slots panel: manages shader chunk slot assignments and chunk browsing.
    /// Used in the Master Materials tab.
    /// Named elements are internal (default WPF access) for use from MainWindow.MasterMaterials.cs.
    /// Event handlers are wired programmatically from MainWindow.
    /// </summary>
    public partial class ChunkSlotsPanel : UserControl {
        public ChunkSlotsPanel() {
            InitializeComponent();
        }
    }
}
