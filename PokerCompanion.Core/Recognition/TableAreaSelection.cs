namespace PokerCompanion.Core.Recognition;

// A user-selected table rectangle within the unmodified capture. This is separate
// from the two glyph corners, which are relative to the resulting table image.
public sealed record TableAreaSelection(CardRegion Region, int ReferenceWidth, int ReferenceHeight)
{
    public bool TryGetViewport(int width, int height, out TableViewport? viewport, out string reason)
    {
        viewport = null;
        reason = "Saved table area is invalid. Select the table area again.";
        if (Region is null || !Region.IsValid || width <= 0 || height <= 0 || ReferenceWidth <= 0 || ReferenceHeight <= 0) return false;
        double ratio = (width / (double)height) / (ReferenceWidth / (double)ReferenceHeight);
        if (Math.Abs(ratio - 1) > .03)
        { reason = "Capture proportions changed. Select the table area again."; return false; }
        int x = (int)Math.Round(Region.X * width), y = (int)Math.Round(Region.Y * height);
        int w = Math.Min(width - x, (int)Math.Round(Region.Width * width));
        int h = Math.Min(height - y, (int)Math.Round(Region.Height * height));
        if (w < 60 || h < 120 || w / (double)h < .30 || w / (double)h > .85)
        { reason = "Select the entire portrait PPPoker table, including its title bar and bottom controls."; return false; }
        viewport = new(x, y, w, h); reason = "Using the selected PPPoker table area."; return true;
    }
}
