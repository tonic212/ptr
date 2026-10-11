using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace PokerCompanion.App.Services;

public sealed class ChartImageConverter : IValueConverter
{
    private string? _path;
    private BitmapImage? _image;
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path)) return null;
        if (_path == path && _image is not null) return _image;
        var image = new BitmapImage();
        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path, UriKind.Absolute); image.EndInit(); image.Freeze();
        _path = path; _image = image; return image;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}
