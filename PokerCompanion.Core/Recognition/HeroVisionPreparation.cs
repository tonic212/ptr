namespace PokerCompanion.Core.Recognition;

public sealed record HeroVisionPreparation(PixelFrame Image, bool FirstPrintedTen, bool SecondPrintedTen)
{
    public static HeroVisionPreparation Create(PixelFrame table)
    {
        if (table.Width / (double)table.Height >= .85)
            throw new InvalidOperationException("Select the entire portrait PPPoker table area before using vision recognition.");
        if (!new BottomHeroCardLocator().TryLocate(table, out var cards, out string reason))
            throw new InvalidOperationException(reason);
        const int width = 320, height = 256;
        var output = Enumerable.Repeat((byte)255, width * height * 4).ToArray();
        Draw(cards!.First, 0); Draw(cards.Second, 160);
        return new(new(width, height, output), PrintedTen(cards.First), PrintedTen(cards.Second));

        void Draw(CardRegion region, int panelX)
        {
            int x0 = (int)Math.Round(region.X * table.Width), y0 = (int)Math.Round(region.Y * table.Height);
            int sourceWidth = (int)Math.Round(region.Width * table.Width), sourceHeight = (int)Math.Round(region.Height * table.Height);
            double scale = Math.Min(128.0 / sourceWidth, 224.0 / sourceHeight);
            int targetWidth = Math.Max(1, (int)Math.Round(sourceWidth * scale));
            int targetHeight = Math.Max(1, (int)Math.Round(sourceHeight * scale));
            int left = panelX + (160 - targetWidth) / 2, top = (height - targetHeight) / 2;
            // Nearest-neighbour enlargement preserves thin strokes and spaces
            // between digits; no avatars, table labels or large suit artwork.
            for (int y = 0; y < targetHeight; y++)
                for (int x = 0; x < targetWidth; x++)
                {
                    int sx = x0 + Math.Min(sourceWidth - 1, x * sourceWidth / targetWidth);
                    int sy = y0 + Math.Min(sourceHeight - 1, y * sourceHeight / targetHeight);
                    Array.Copy(table.Bgra, (sy * table.Width + sx) * 4, output, ((top + y) * width + left + x) * 4, 4);
                }
        }
        bool PrintedTen(CardRegion region)
        {
            try { return PrintedTenDetector.IsPrintedTen(new CardGlyphExtractor().Extract(table, region).Rank); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return false; }
        }
    }
    public HandObservation Validate(HandObservation reading)
    {
        if (!reading.Success) return reading;
        string[] cards = reading.Cards.Split(' ');
        if (cards.Length != 2) return new(false, "", "", "Invalid vision card pair.");
        if ((FirstPrintedTen && cards[0][0] != 'T') || (SecondPrintedTen && cards[1][0] != 'T'))
            return new(false, "", "", "Rank disagreement: the image shows a printed 10 but the vision model returned a different rank. Hand withheld.");
        return reading;
    }
}
