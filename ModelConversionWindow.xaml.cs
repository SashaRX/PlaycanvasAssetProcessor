using System.Windows;
using AssetProcessor.ViewModels;

namespace AssetProcessor {
    /// <summary>
    /// Interaction logic for ModelConversionWindow.xaml
    /// </summary>
    public partial class ModelConversionWindow : Window {
        public ModelConversionWindow() {
            InitializeComponent();
            DataContext = new ModelConversionViewModel();
        }
    }
}
