namespace PokerCompanion.Core.Recognition;

public sealed record HeroCardRegions(CardRegion First, CardRegion Second);

// Searches the bottom seat only. Locates neutral card backgrounds before reading
// symbols, so locating a card does not depend on knowing its rank or suit.
public sealed class BottomHeroCardLocator
{
    private sealed record Body(int X, int Y, int Width, int Height);

    public bool TryLocate(PixelFrame frame, out HeroCardRegions? regions, out string reason)
    {
        regions = null;
        reason = "Waiting for two face-up cards at the bottom hero seat. Keep the table unobstructed.";
        int left = (int)(frame.Width * .32), right = (int)(frame.Width * .83);
        int top = (int)(frame.Height * .70), bottom = (int)(frame.Height * .92);
        int width = right - left, height = bottom - top;
        if (width < 20 || height < 20) return false;
        var light = new bool[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = ((top + y) * frame.Width + left + x) * 4;
                int b = frame.Bgra[i], g = frame.Bgra[i + 1], r = frame.Bgra[i + 2];
                int low = Math.Min(r, Math.Min(g, b)), high = Math.Max(r, Math.Max(g, b));
                light[y * width + x] = low >= 110 && high - low <= 45;
            }
        var seen = new bool[light.Length];
        var queue = new Queue<int>();
        var bodies = new List<Body>();
        for (int start = 0; start < light.Length; start++)
        {
            if (!light[start] || seen[start]) continue;
            int minX = width, maxX = -1, minY = height, maxY = -1;
            seen[start] = true; queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int index = queue.Dequeue(), x = index % width, y = index / width;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                Visit(x - 1, y); Visit(x + 1, y); Visit(x, y - 1); Visit(x, y + 1);
            }
            int w = maxX - minX + 1, h = maxY - minY + 1;
            if (h >= frame.Height * .04 && h <= frame.Height * .12 && w >= 7 && w >= h * .16 && w <= h * .90
                && minY > 0 && maxY < height - 1)
                bodies.Add(new(left + minX, top + minY, w, h));
        }
        void Visit(int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return;
            int i = y * width + x;
            if (!light[i] || seen[i]) return;
            seen[i] = true; queue.Enqueue(i);
        }
        var pairs = new List<(Body First, Body Second)>();
        bodies.Sort((a, b) => a.X.CompareTo(b.X));
        for (int i = 0; i < bodies.Count; i++)
            for (int j = i + 1; j < bodies.Count; j++)
            {
                Body a = bodies[i], b = bodies[j];
                int h = Math.Min(a.Height, b.Height), gap = b.X - (a.X + a.Width);
                if (Math.Abs(a.Y - b.Y) <= h * .12 && Math.Abs(a.Height - b.Height) <= h * .20
                    && gap >= -2 && gap <= h * .25 && b.X - a.X >= h * .20)
                    pairs.Add((a, b));
            }
        if (pairs.Count != 1)
        {
            if (pairs.Count > 1) reason = "Multiple possible bottom card pairs. Use manual calibration for this layout.";
            return false;
        }
        var pair = pairs[0];
        int commonHeight = Math.Min(pair.First.Height, pair.Second.Height);
        CardRegion? first = Corner(pair.First), second = Corner(pair.Second);
        if (first is null || second is null || first.X + first.Width > second.X) return false;
        regions = new(first, second); reason = "Bottom hero card corners located automatically.";
        return true;

        CardRegion? Corner(Body body)
        {
            // Adjacent gray avatar/button pixels can attach to a card background.
            // Find its continuous left edge instead of using the component's fringe.
            int edge = -1;
            int y1 = body.Y + (int)(body.Height * .10), y2 = body.Y + (int)(body.Height * .90);
            for (int x = body.X; x < body.X + body.Width; x++)
            {
                int count = 0;
                for (int y = y1; y < y2; y++) if (light[(y - top) * width + x - left]) count++;
                if (count >= (y2 - y1) * .85) { edge = x; break; }
            }
            if (edge < 0) return null;
            int inset = Math.Max(1, (int)Math.Round(commonHeight / 44.0));
            int x0 = edge + inset, y0 = body.Y + inset * 2;
            int w = Math.Min(body.X + body.Width - x0 - inset, (int)Math.Round(commonHeight * .30));
            int h = (int)Math.Round(commonHeight * .60);
            if (w < 5 || h < 12) return null;
            return new(x0 / (double)frame.Width, y0 / (double)frame.Height, w / (double)frame.Width, h / (double)frame.Height);
        }
    }
}
