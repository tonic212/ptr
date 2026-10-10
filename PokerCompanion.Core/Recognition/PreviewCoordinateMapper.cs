namespace PokerCompanion.Core.Recognition;

public static class PreviewCoordinateMapper
{
    public static CardRegion? Map(double x1, double y1, double x2, double y2,
        double viewWidth, double viewHeight, int frameWidth, int frameHeight)
    {
        if (viewWidth <= 0 || viewHeight <= 0 || frameWidth <= 0 || frameHeight <= 0) return null;
        double scale = Math.Min(viewWidth / frameWidth, viewHeight / frameHeight);
        double drawnWidth = frameWidth * scale, drawnHeight = frameHeight * scale;
        double offsetX = (viewWidth - drawnWidth) / 2, offsetY = (viewHeight - drawnHeight) / 2;
        double left = Math.Min(x1, x2), top = Math.Min(y1, y2);
        double right = Math.Max(x1, x2), bottom = Math.Max(y1, y2);
        if (left < offsetX || top < offsetY || right > offsetX + drawnWidth || bottom > offsetY + drawnHeight) return null;
        if ((right - left) / scale < 5 || (bottom - top) / scale < 12) return null;
        return new((left - offsetX) / drawnWidth, (top - offsetY) / drawnHeight,
            (right - left) / drawnWidth, (bottom - top) / drawnHeight);
    }
}
