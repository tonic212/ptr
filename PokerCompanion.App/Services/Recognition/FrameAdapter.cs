using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PokerCompanion.Core.Recognition;

namespace PokerCompanion.App.Services.Recognition;

public static class FrameAdapter
{
    public static BitmapSource FocusTable(BitmapSource source)
    {
        if (source.PixelWidth / (double)source.PixelHeight < .85) return source;
        if (!new PokerTableLocator().TryLocate(ToPixels(source), out TableViewport? table, out _)) return source;
        var cropped = new CroppedBitmap(source, new Int32Rect(table!.X, table.Y, table.Width, table.Height));
        cropped.Freeze();
        return cropped;
    }

    public static PixelFrame ToPixels(BitmapSource source)
    {
        BitmapSource bitmap = source.Format == PixelFormats.Bgra32 ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int stride = checked(bitmap.PixelWidth * 4);
        var pixels = new byte[checked(stride * bitmap.PixelHeight)];
        bitmap.CopyPixels(pixels, stride, 0);
        return new(bitmap.PixelWidth, bitmap.PixelHeight, pixels);
    }
}
