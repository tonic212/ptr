using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PokerCompanion.Core.Recognition;

namespace PokerCompanion.App.Services.Recognition;

public static class PositionVisionImages
{
    public static PositionVisionInput Create(BitmapSource table)
    {
        var details = PositionVisionPreparation.CreateDetails(FrameAdapter.ToPixels(table));
        double scale = System.Math.Min(1, 1000.0 / table.PixelHeight);
        BitmapSource full = scale < 1 ? new TransformedBitmap(table, new ScaleTransform(scale, scale)) : table;
        return new(Encode(full), details.Select(frame => Encode(BitmapSource.Create(frame.Width, frame.Height,
            96, 96, PixelFormats.Bgra32, null, frame.Bgra, frame.Width * 4))).ToArray());
    }
    private static byte[] Encode(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
}
