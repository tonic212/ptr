using System.Windows.Media;
using System.Windows.Media.Imaging;
using PokerCompanion.Core.Recognition;

namespace PokerCompanion.App.Services.Recognition;

public static class FrameAdapter
{
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
