namespace PokerCompanion.Core.Recognition;

// Conservative geometry check for a printed "10": a tall stroke on the left
// and exactly one enclosed hole on the right. This never replaces a model rank;
// it can only veto a conflicting model answer.
public static class PrintedTenDetector
{
    public static bool IsPrintedTen(byte[] mask)
    {
        const int width = CardGlyphExtractor.MaskWidth, height = CardGlyphExtractor.MaskHeight;
        if (mask.Length != width * height || mask.Any(p => p > 1)) return false;
        var seen = new bool[mask.Length];
        var queue = new Queue<int>();
        int holes = 0, holeSize = 0;
        double holeX = 0, holeY = 0;
        for (int start = 0; start < mask.Length; start++)
        {
            if (mask[start] != 0 || seen[start]) continue;
            bool edge = false;
            int count = 0;
            long sumX = 0, sumY = 0;
            seen[start] = true; queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int index = queue.Dequeue(), x = index % width, y = index / width;
                count++; sumX += x; sumY += y;
                if (x == 0 || y == 0 || x == width - 1 || y == height - 1) edge = true;
                Visit(x - 1, y); Visit(x + 1, y); Visit(x, y - 1); Visit(x, y + 1);
            }
            if (!edge && count >= 4)
            { holes++; holeSize = count; holeX = sumX / (double)count / width; holeY = sumY / (double)count / height; }
        }
        if (holes != 1 || holeSize < mask.Length * .02 || holeSize > mask.Length * .25
            || holeX < .60 || holeX > .85 || holeY < .20 || holeY > .80) return false;
        for (int x = 1; x <= width * .30; x++)
        {
            int count = 0;
            for (int y = 0; y < height; y++) count += mask[y * width + x];
            if (count >= height * .72) return true;
        }
        return false;

        void Visit(int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return;
            int i = y * width + x;
            if (seen[i] || mask[i] != 0) return;
            seen[i] = true; queue.Enqueue(i);
        }
    }
}
