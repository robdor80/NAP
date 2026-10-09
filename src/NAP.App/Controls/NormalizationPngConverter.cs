using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using NAP.Core;

namespace NAP.App.Controls;

/// <summary>Displays the frozen, verified preview buffer, without reopening originals or performing edits.</summary>
public sealed class NormalizationPngConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not byte[] bytes) return null;
        using var validationStream = new MemoryStream(bytes, false);
        var info = new PngMasterValidator().Validate(validationStream).ImageInfo ?? throw new InvalidDataException("Invalid preview PNG.");
        using var input = new MemoryStream(bytes, false);
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        // Bound the larger axis, never upscale a thin image to a fixed width (which could allocate billions of pixels).
        if (info.Width >= info.Height) image.DecodePixelWidth = Math.Min(info.Width, 2048);
        else image.DecodePixelHeight = Math.Min(info.Height, 2048);
        image.StreamSource = input; image.EndInit(); image.Freeze(); return image;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
