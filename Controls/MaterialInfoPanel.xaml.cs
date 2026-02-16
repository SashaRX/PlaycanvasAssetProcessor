using System.Windows.Controls;

namespace AssetProcessor.Controls {
    /// <summary>
    /// Material info panel: displays material properties, texture maps, overrides, and tint colors.
    /// Named elements are internal (default WPF access) for use from MainWindow.Materials.cs.
    /// Event handlers are wired programmatically from MainWindow.
    /// </summary>
    public partial class MaterialInfoPanel : UserControl {
        public MaterialInfoPanel() {
            InitializeComponent();
        }
    }
}
