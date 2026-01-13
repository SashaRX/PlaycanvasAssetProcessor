using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace AssetProcessor.Helpers {
    public class SizeConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            // Handle both int and long types
            long size = value switch {
                int intSize => intSize,
                long longSize => longSize,
                _ => 0
            };

            if (size > 0) {
                double sizeInKB = size / 1024.0;
                if (sizeInKB < 1024) {
                    return $"{Math.Round(sizeInKB, 1)} KB";
                } else {
                    double sizeInMB = sizeInKB / 1024.0;
                    return $"{Math.Round(sizeInMB, 2)} MB";
                }
            }
            return "";
        }

        public static object Convert(object value) {
            // Handle both int and long types
            long size = value switch {
                int intSize => intSize,
                long longSize => longSize,
                _ => 0
            };

            if (size > 0) {
                double sizeInKB = size / 1024.0;
                if (sizeInKB < 1024) {
                    return $"{Math.Round(sizeInKB, 1)} KB";
                } else {
                    double sizeInMB = sizeInKB / 1024.0;
                    return $"{Math.Round(sizeInMB, 2)} MB";
                }
            }
            return "";
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

    public class ChannelColorConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            string? channel = value as string;

            if (parameter.ToString() == "Foreground") {
                return channel switch {
                    "R" => Brushes.Red,
                    "G" => Brushes.Green,
                    "B" => Brushes.Blue,
                    "RGB" => Brushes.Black,
                    "A" => Brushes.White,
                    _ => Brushes.Purple,
                };
            } else if (parameter.ToString() == "Background") {
                return channel switch {
                    "R" => Brushes.Transparent,
                    "G" => Brushes.Transparent,
                    "B" => Brushes.Transparent,
                    "RGB" => Brushes.Transparent,
                    "A" => Brushes.Black,
                    _ => Brushes.Black,
                };
            }
            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }

    public class ChannelColorDataTemplateSelector : DataTemplateSelector {
        public override DataTemplate SelectTemplate(object item, DependencyObject container) {
            if (container is ComboBoxItem comboBoxItem && item is string channel) {
                SolidColorBrush brush = channel switch {
                    "R" => Brushes.Red,
                    "G" => Brushes.Green,
                    "B" => Brushes.Blue,
                    "A" => Brushes.Black,
                    "RGB" => Brushes.White,
                    _ => Brushes.Purple,
                };
                comboBoxItem.Background = brush;
                comboBoxItem.Foreground = channel == "RGB" ? Brushes.Black : Brushes.White;
            }

            return base.SelectTemplate(item, container);
        }
    }

    public class TextureTypeToBackgroundConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value == null)
                return Brushes.Transparent;

            string? textureType = value.ToString();

            return textureType switch {
                "Gloss" => new SolidColorBrush(Color.FromRgb(128, 128, 128)), // Серый
                "AO" => new SolidColorBrush(Color.FromRgb(255, 255, 255)), // Белый
                "Normal" => new SolidColorBrush(Color.FromRgb(128, 128, 255)), // #8080ff
                "Albedo" => new SolidColorBrush(Color.FromRgb(156, 127, 37)), // #9c7f25
                _ => Brushes.Transparent,
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converter to check if texture name is an ORM texture (_og, _ogm, _ogmh)
    /// </summary>
    public class ORMTextureColorConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is string textureName) {
                return textureName.EndsWith("_og", StringComparison.OrdinalIgnoreCase) ||
                       textureName.EndsWith("_ogm", StringComparison.OrdinalIgnoreCase) ||
                       textureName.EndsWith("_ogmh", StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Конвертер для извлечения типа ORM (OG/OGM/OGMH) из имени текстуры
    /// </summary>
    public class ORMTypeExtractor : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is string name) {
                if (name.EndsWith("_ogmh", StringComparison.OrdinalIgnoreCase))
                    return "OGMH";
                if (name.EndsWith("_ogm", StringComparison.OrdinalIgnoreCase))
                    return "OGM";
                if (name.EndsWith("_og", StringComparison.OrdinalIgnoreCase))
                    return "OG";
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// MultiValueConverter для сравнения двух значений на равенство
    /// </summary>
    public class EqualityConverter : IMultiValueConverter {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
            if (values.Length < 2)
                return false;

            var value1 = values[0]?.ToString();
            var value2 = values[1]?.ToString();

            // Оба null или пустые - не равны (чтобы не выделять пустые подгруппы)
            if (string.IsNullOrEmpty(value1) || string.IsNullOrEmpty(value2))
                return false;

            return string.Equals(value1, value2, StringComparison.OrdinalIgnoreCase);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Inverts a boolean value. Used for binding Expander.IsExpanded to CollapseTextureGroups setting.
    /// CollapseTextureGroups=true means IsExpanded=false, and vice versa.
    /// </summary>
    public class InverseBoolConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            return value is bool b ? !b : value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            return value is bool b ? !b : value;
        }
    }

    /// <summary>
    /// MultiValueConverter to check if a chunk is included in the selected master material.
    /// Values: [0] = ChunkId (string), [1] = SelectedMaster (MasterMaterial)
    /// Returns: true if chunk is in master's ChunkIds list
    /// </summary>
    public class ChunkInMasterConverter : IMultiValueConverter {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
            if (values.Length < 2)
                return false;

            if (values[0] is not string chunkId)
                return false;

            if (values[1] is not MasterMaterials.Models.MasterMaterial master)
                return false;

            return master.ChunkIds.Contains(chunkId);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converter to check if chunk toggle is enabled (master is not built-in)
    /// </summary>
    public class ChunkToggleEnabledConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is MasterMaterials.Models.MasterMaterial master) {
                return !master.IsBuiltIn;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }

}
