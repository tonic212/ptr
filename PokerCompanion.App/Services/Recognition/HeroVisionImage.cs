using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PokerCompanion.Core.Recognition;

namespace PokerCompanion.App.Services.Recognition;

public sealed record HeroVisionImage(PixelFrame Pixels, byte[] Png, HeroVisionPreparation? Evidence)
{
    public static HeroVisionImage Create(BitmapSource table)
    {
        if (table.PixelWidth / (double)table.PixelHeight >= .85)
            throw new InvalidOperationException("Select the entire portrait PPPoker table area before using vision recognition.");
        PixelFrame pixels = FrameAdapter.ToPixels(table);
        HeroVisionPreparation? evidence = null;
        try { evidence = HeroVisionPreparation.Create(pixels); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { }
        BitmapSource image;
        PixelFrame comparison;
        if (evidence is not null && (evidence.FirstPrintedTen || evidence.SecondPrintedTen))
        {
            // Ten-specific close-up. Preserve the established crop for other ranks.
            comparison = evidence.Image;
            image = BitmapSource.Create(comparison.Width, comparison.Height, 96, 96,
                PixelFormats.Bgra32, null, comparison.Bgra, comparison.Width * 4);
            image.Freeze();
        }
        else
        {
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
            comparison = FrameAdapter.ToPixels(crop);
            double scale = Math.Clamp(320.0 / width, 1, 4);
            image = new TransformedBitmap(crop, new ScaleTransform(scale, scale)); image.Freeze();
        }
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream(); encoder.Save(stream);
        return new(comparison, stream.ToArray(), evidence);
    }
}
