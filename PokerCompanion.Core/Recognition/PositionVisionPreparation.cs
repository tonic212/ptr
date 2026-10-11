namespace PokerCompanion.Core.Recognition;

// Extra detail evidence only; position coordinates always refer to the full table.
public static class PositionVisionPreparation
{
    public static readonly CardRegion Left = new(0, .18, .45, .56);
    public static readonly CardRegion Right = new(.55, .18, .45, .56);
    public static readonly CardRegion Bottom = new(.20, .72, .60, .24);

    public static IReadOnlyList<PixelFrame> CreateDetails(PixelFrame table)
    {
        if (table.Width / (double)table.Height >= .85 || table.Width < 100 || table.Height < 200)
            throw new InvalidOperationException("Select the whole portrait PPPoker table for position detection.");
        return new[] { CropAndEnlarge(table, Left), CropAndEnlarge(table, Right), CropAndEnlarge(table, Bottom) };
    }
    private static PixelFrame CropAndEnlarge(PixelFrame table, CardRegion region)
    {
        int x0 = (int)Math.Floor(region.X * table.Width), y0 = (int)Math.Floor(region.Y * table.Height);
        int width = Math.Min(table.Width - x0, (int)Math.Ceiling(region.Width * table.Width));
        int height = Math.Min(table.Height - y0, (int)Math.Ceiling(region.Height * table.Height));
        double scale = Math.Min(2.5, Math.Min(640.0 / width, 1000.0 / height));
        int outputWidth = Math.Max(1, (int)Math.Round(width * scale));
        int outputHeight = Math.Max(1, (int)Math.Round(height * scale));
        byte[] output = new byte[checked(outputWidth * outputHeight * 4)];
        for (int y = 0; y < outputHeight; y++)
            for (int x = 0; x < outputWidth; x++)
            {
                int sourceX = x0 + Math.Min(width - 1, x * width / outputWidth);
                int sourceY = y0 + Math.Min(height - 1, y * height / outputHeight);
                Array.Copy(table.Bgra, (sourceY * table.Width + sourceX) * 4, output, (y * outputWidth + x) * 4, 4);
            }
        return new(outputWidth, outputHeight, output);
    }
}
