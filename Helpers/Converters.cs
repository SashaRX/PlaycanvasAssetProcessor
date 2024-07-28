using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace TexTool.Helpers {
    public class SizeConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is int size) {
                double sizeInMB = Math.Round(size / (1024.0 * 1000.0), 3);
                return $"{sizeInMB} MB";
            }
            return "0 MB";
        }

        public object Convert(object value) {
            if (value is int size) {
                double sizeInMB = Math.Round(size / (1024.0 * 1000.0), 3);
                return $"{sizeInMB} MB";
            }
            return "0 MB";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }

        internal string Convert(object value, object targetType, object parameter, object culture) {
            throw new NotImplementedException();
        }
    }

    public class ResolutionConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is int[] resolution && resolution.Length == 2) {
                return $"{resolution[0]}x{resolution[1]}";
            }
            return "0x0";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }

    public class HashToColorConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is string hash) {
                return string.Concat("#", hash.AsSpan(0, 6));
            }
            return "#000000";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }

    public class StatusToVisibilityConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is string status) {
                return status == "Downloading" ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }

    public class StatusToVisibilityInverseConverter : IValueConverter {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
            if (value is string status) {
                return status == "On Server" ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }

    public class StatusToBackgroundConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value == null)
                return Brushes.Transparent;

            string? status = value.ToString();

            return status switch {
                "Error" => Brushes.Red,
                "Downloaded" => Brushes.LightGreen,
                "Empty File" => Brushes.Yellow,
                "Corrupted" => Brushes.Orange,
                "Size Mismatch" => Brushes.Pink,
                "Downloading" => Brushes.Transparent,
                _ => Brushes.Transparent,
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }
}
