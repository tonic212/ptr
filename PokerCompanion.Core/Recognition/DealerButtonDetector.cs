namespace PokerCompanion.Core.Recognition;

public sealed record DealerButtonGlyph(int Width, int Height, byte[] Bgra);
public sealed record DealerButtonMatch(SeatPoint Centre, int SeatIndex, double Score);

// Matches the small white D badge silhouette, including the printed D interior.
// No model or OCR call occurs on this path.
public sealed class DealerButtonDetector
{
    private const int MaskWidth = 24, MaskHeight = 28;
    private readonly byte[][] _templates;
    private bool[] _visited = [];
    private int[] _queue = [];
    public DealerButtonDetector(IEnumerable<DealerButtonGlyph> glyphs)
    {
        _templates = glyphs.Select(g => Normalize(new PixelFrame(g.Width, g.Height, g.Bgra), 0, 0, g.Width, g.Height)).ToArray();
        if (_templates.Length == 0) throw new ArgumentException("Dealer badge templates are required.");
    }
    public bool TryFind(PixelFrame frame, IReadOnlyList<SeatPoint> seats, out DealerButtonMatch? match)
    {
        match = null;
        if (frame.Width / (double)frame.Height >= .85 || seats.Count < 3) return false;
        int size = frame.Width * frame.Height;
        if (_visited.Length != size) { _visited = new bool[size]; _queue = new int[size]; }
        else Array.Clear(_visited);
        var candidates = new List<DealerButtonMatch>();
        for (int y = (int)(frame.Height * .18); y < frame.Height * .96; y++)
            for (int x = 0; x < frame.Width; x++)
            {
                int index = y * frame.Width + x;
                if (_visited[index] || !White(frame, x, y)) continue;
                int head = 0, tail = 1, minX = x, maxX = x, minY = y, maxY = y;
                _queue[0] = index; _visited[index] = true;
                while (head < tail)
                {
                    int current = _queue[head++], cx = current % frame.Width, cy = current / frame.Width;
                    minX = Math.Min(minX, cx); maxX = Math.Max(maxX, cx); minY = Math.Min(minY, cy); maxY = Math.Max(maxY, cy);
                    Visit(cx - 1, cy); Visit(cx + 1, cy); Visit(cx, cy - 1); Visit(cx, cy + 1);
                }
                int width = maxX - minX + 1, height = maxY - minY + 1;
                double density = tail / (double)(width * height);
                if (width < Math.Max(7, frame.Width * .015) || width > frame.Width * .07
                    || height < Math.Max(8, frame.Height * .008) || height > frame.Height * .04
                    || width / (double)height is < .65 or > 1.3 || density is < .22 or > .82) continue;
                var point = new SeatPoint((minX + width / 2.0) / frame.Width, (minY + height / 2.0) / frame.Height);
                if (point.X > .32 && point.X < .68 && point.Y > .30 && point.Y < .72) continue;
                byte[] mask = Normalize(frame, minX, minY, width, height);
                double score = _templates.Max(t => Dice(mask, t));
                if (score < .87) continue;
                var distances = seats.Select((seat, i) => (Index: i, Distance: Math.Sqrt(Math.Pow(seat.X - point.X, 2)
                    + Math.Pow((seat.Y - point.Y) * frame.Height / frame.Width, 2)))).OrderBy(d => d.Distance).ToArray();
                if (distances[0].Distance > .24 || distances[1].Distance - distances[0].Distance < .04) continue;
                candidates.Add(new(point, distances[0].Index, score));

                void Visit(int nx, int ny)
                {
                    if (nx < 0 || ny < 0 || nx >= frame.Width || ny >= frame.Height) return;
                    int next = ny * frame.Width + nx;
                    if (_visited[next]) return;
                    _visited[next] = true;
                    if (White(frame, nx, ny)) _queue[tail++] = next;
                }
            }
        var ordered = candidates.OrderByDescending(c => c.Score).ToArray();
        if (ordered.Length == 0 || (ordered.Length > 1 && ordered[0].Score - ordered[1].Score < .06)) return false;
        match = ordered[0]; return true;
    }
    private static bool White(PixelFrame frame, int x, int y)
    {
        int i = (y * frame.Width + x) * 4;
        int min = Math.Min(frame.Bgra[i], Math.Min(frame.Bgra[i + 1], frame.Bgra[i + 2]));
        int max = Math.Max(frame.Bgra[i], Math.Max(frame.Bgra[i + 1], frame.Bgra[i + 2]));
        return min >= 175 && max - min <= 65;
    }
    private static byte[] Normalize(PixelFrame frame, int x0, int y0, int width, int height)
    {
        byte[] mask = new byte[MaskWidth * MaskHeight];
        for (int y = 0; y < MaskHeight; y++)
            for (int x = 0; x < MaskWidth; x++)
                mask[y * MaskWidth + x] = White(frame, x0 + x * width / MaskWidth, y0 + y * height / MaskHeight) ? (byte)1 : (byte)0;
        return mask;
    }
    private static double Dice(byte[] a, byte[] b)
    {
        int total = 0, overlap = 0;
        for (int i = 0; i < a.Length; i++) { total += a[i] + b[i]; if (a[i] != 0 && b[i] != 0) overlap++; }
        return total == 0 ? 0 : 2.0 * overlap / total;
    }
}
