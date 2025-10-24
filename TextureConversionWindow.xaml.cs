using System.Globalization;
using System.Windows;
using System.Windows.Data;
using AssetProcessor.ViewModels;

namespace AssetProcessor {
    /// <summary>
    /// Interaction logic for TextureConversionWindow.xaml
    /// </summary>
    public partial class TextureConversionWindow : Window {
        public TextureConversionWindow() {
            InitializeComponent();
            DataContext = new TextureConversionViewModel();

            // Add converters to resources
            Resources.Add("NullToBoolConverter", new NullToBoolConverter());
            Resources.Add("InverseBoolConverter", new InverseBoolConverter());
        }
    }

    /// <summary>
    /// Converter to check if object is not null
    /// </summary>
    public class NullToBoolConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            return value != null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Inverse boolean converter
    /// </summary>
    public class InverseBoolConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is bool boolValue) {
                return !boolValue;
            }
            return true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is bool boolValue) {
                return !boolValue;
            }
            return false;
        }
    }
}
