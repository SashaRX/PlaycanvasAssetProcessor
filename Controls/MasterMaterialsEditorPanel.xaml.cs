using System.Windows.Controls;

namespace AssetProcessor.Controls {
    /// <summary>
    /// Master materials editor panel: master materials list + chunk code editor (GLSL/WGSL).
    /// Named elements are internal (default WPF access) for use from MainWindow.MasterMaterials.cs.
    /// Event handlers are wired programmatically from MainWindow.
    /// </summary>
    public partial class MasterMaterialsEditorPanel : UserControl {
        public MasterMaterialsEditorPanel() {
            InitializeComponent();
        }
    }
}
