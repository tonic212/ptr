namespace PokerCompanion.Core.Recognition;

// Separates the small rank and suit in a calibrated card corner. It does not
// inspect the large central suit artwork, player labels or another card.
public sealed class CardGlyphExtractor
{
    public const int MaskWidth = 24;
    public const int MaskHeight = 32;
    public const int MaskLength = MaskWidth * MaskHeight;

    public CardGlyphs Extract(PixelFrame frame, CardRegion region)
    {
        if (!region.IsValid) throw new ArgumentException("Invalid card region.");
        int x0 = Math.Clamp((int)Math.Round(region.X * frame.Width), 0, frame.Width - 1);
        int y0 = Math.Clamp((int)Math.Round(region.Y * frame.Height), 0, frame.Height - 1);
        int width = Math.Min(frame.Width - x0, (int)Math.Round(region.Width * frame.Width));
        int height = Math.Min(frame.Height - y0, (int)Math.Round(region.Height * frame.Height));
        if (width < 5 || height < 12 || width > 240 || height > 400)
            throw new InvalidOperationException("Select only the small rank and suit in the card corner.");
        var brightness = new double[width * height];
        int n = 0;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = ((y0 + y) * frame.Width + x0 + x) * 4;
                brightness[n++] = (frame.Bgra[i] + frame.Bgra[i + 1] + frame.Bgra[i + 2]) / 3.0;
            }
        Array.Sort(brightness);
        double background = brightness[(int)((brightness.Length - 1) * .85)];
        if (background < 75) throw new InvalidOperationException("No readable card background.");
        var mask = new bool[width * height];
        var rowCounts = new int[height];
        int foreground = 0;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = ((y0 + y) * frame.Width + x0 + x) * 4;
                int b = frame.Bgra[i], g = frame.Bgra[i + 1], r = frame.Bgra[i + 2];
                int high = Math.Max(r, Math.Max(g, b)), low = Math.Min(r, Math.Min(g, b));
                bool dark = (r + g + b) / 3.0 < background * .65;
                bool colored = high - low > 45 && low < 150;
                if (dark || colored) { mask[y * width + x] = true; rowCounts[y]++; foreground++; }
            }
        double density = foreground / (double)mask.Length;
        if (density < .08 || density > .65) throw new InvalidOperationException("The corner is empty or obstructed.");
        int first = Array.FindIndex(rowCounts, count => count > 0);
        int last = Array.FindLastIndex(rowCounts, count => count > 0);
        int gapStart = -1, gapLength = 0;
        for (int y = first + 3; y <= last - 3; y++)
        {
            if (rowCounts[y] != 0) continue;
            int start = y;
            while (y <= last && rowCounts[y] == 0) y++;
            int length = y - start;
            if (start - first >= 4 && last - y + 1 >= 3 && length > gapLength)
            { gapStart = start; gapLength = length; }
        }
        if (gapStart < 0)
        {
            // Small antialiased symbols can touch by one or two pixels.
            int from = first + (int)Math.Ceiling((last - first + 1) * .35);
            int to = first + (int)Math.Floor((last - first + 1) * .65);
            int valley = from;
            for (int y = from; y <= to; y++) if (rowCounts[y] < rowCounts[valley]) valley = y;
            if (rowCounts[valley] <= rowCounts.Max() * .25 && valley - first >= 4 && last - valley >= 3)
            { gapStart = valley; gapLength = 1; }
        }
        if (gapStart < 0) throw new InvalidOperationException("Rank/suit separation failed. Recalibrate the corner.");
        return new(Normalize(mask, width, first, gapStart), Normalize(mask, width, gapStart + gapLength, last + 1));
    }

    private static byte[] Normalize(bool[] mask, int width, int top, int bottom)
    {
        int left = width, right = -1, minY = bottom, maxY = -1;
        for (int y = top; y < bottom; y++)
            for (int x = 0; x < width; x++)
                if (mask[y * width + x])
                { left = Math.Min(left, x); right = Math.Max(right, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
        if (right < left || maxY - minY < 2) throw new InvalidOperationException("Incomplete card symbol.");
        var result = new byte[MaskLength];
        for (int y = 0; y < MaskHeight; y++)
            for (int x = 0; x < MaskWidth; x++)
            {
                int sx = left + Math.Min(right - left, (int)((x + .5) * (right - left + 1) / MaskWidth));
                int sy = minY + Math.Min(maxY - minY, (int)((y + .5) * (maxY - minY + 1) / MaskHeight));
                result[y * MaskWidth + x] = mask[sy * width + sx] ? (byte)1 : (byte)0;
            }
        return result;
    }
}
