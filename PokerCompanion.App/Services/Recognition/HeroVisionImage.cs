using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PokerCompanion.Core.Recognition;

namespace PokerCompanion.App.Services.Recognition;

public sealed record HeroVisionImage(PixelFrame Pixels, byte[] Png)
{
    public static HeroVisionImage Create(BitmapSource table)
    {
        if (table.PixelWidth / (double)table.PixelHeight >= .85)
            throw new InvalidOperationException("Select the entire portrait PPPoker table area before using vision recognition.");
        var pixels = FrameAdapter.ToPixels(table);
        double x = .32, y = .70, right = .83, bottom = .92;
        if (new BottomHeroCardLocator().TryLocate(pixels, out var cards, out _))
        {
            x = Math.Max(0, cards!.First.X - .006);
            y = Math.Max(0, Math.Min(cards.First.Y, cards.Second.Y) - .006);
            right = Math.Min(1, cards.Second.X + cards.Second.Width + .065);
            bottom = Math.Min(.94, y + .10);
        }
        int left = (int)(x * table.PixelWidth), top = (int)(y * table.PixelHeight);
        int width = Math.Min(table.PixelWidth - left, (int)Math.Ceiling((right - x) * table.PixelWidth));
        int height = Math.Min(table.PixelHeight - top, (int)Math.Ceiling((bottom - y) * table.PixelHeight));
        var crop = new CroppedBitmap(table, new Int32Rect(left, top, width, height)); crop.Freeze();
        double scale = Math.Clamp(320.0 / width, 1, 4);
        var enlarged = new TransformedBitmap(crop, new ScaleTransform(scale, scale)); enlarged.Freeze();
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(enlarged));
        using var stream = new MemoryStream(); encoder.Save(stream);
        return new(FrameAdapter.ToPixels(crop), stream.ToArray());
    }
}
