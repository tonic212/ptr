namespace PokerCompanion.Core.Recognition;

public sealed record TableViewport(int X, int Y, int Width, int Height);

// PPPoker can expose a desktop-sized window rectangle. Locate its blue portrait
// table surface inside that capture before interpreting bottom-seat coordinates.
public sealed class PokerTableLocator
{
    public bool TryLocate(PixelFrame frame, out TableViewport? viewport, out string reason)
    {
        viewport = null;
        reason = "Could not locate a clear blue PPPoker table inside the capture.";
        // Bound analysis work while keeping the original pixels for glyph reading.
        int step = Math.Max(1, (int)Math.Ceiling(frame.Width / 900.0));
        int width = (frame.Width + step - 1) / step, height = (frame.Height + step - 1) / step;
        var blue = new bool[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = ((y * step) * frame.Width + x * step) * 4;
                int b = frame.Bgra[i], g = frame.Bgra[i + 1], r = frame.Bgra[i + 2];
                blue[y * width + x] = b >= 55 && b >= r * 1.25 && b >= g * .95 && b - r >= 20;
            }
        var seen = new bool[blue.Length];
        var queue = new Queue<int>();
        var candidates = new List<(TableViewport Bounds, int Area)>();
        for (int start = 0; start < blue.Length; start++)
        {
            if (!blue[start] || seen[start]) continue;
            int minX = width, maxX = -1, minY = height, maxY = -1, count = 0;
            seen[start] = true; queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int i = queue.Dequeue(), x = i % width, y = i / width; count++;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                Visit(x - 1, y); Visit(x + 1, y); Visit(x, y - 1); Visit(x, y + 1);
            }
            int w = maxX - minX + 1, h = maxY - minY + 1;
            if (h < height * .25 || w < width * .06 || w / (double)h < .30 || w / (double)h > .85
                || count < w * h * .35) continue;
            int x0 = minX * step, y0 = Math.Max(0, (int)((minY - h * .06) * step));
            int x1 = Math.Min(frame.Width, (maxX + 1) * step);
            int y1 = Math.Min(frame.Height, (int)((maxY + 1 + h * .10) * step));
            candidates.Add((new(x0, y0, x1 - x0, y1 - y0), count));
        }
        void Visit(int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return;
            int i = y * width + x;
            if (!blue[i] || seen[i]) return;
            seen[i] = true; queue.Enqueue(i);
        }
        candidates.Sort((a, b) => b.Area.CompareTo(a.Area));
        if (candidates.Count == 0) return false;
        if (candidates.Count > 1 && candidates[1].Area >= candidates[0].Area * .35)
        { reason = "More than one substantial table found. Select a capture containing one table."; return false; }
        viewport = candidates[0].Bounds; reason = "PPPoker table located inside the capture."; return true;
    }
}
