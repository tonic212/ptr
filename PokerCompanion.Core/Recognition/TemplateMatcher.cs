namespace PokerCompanion.Core.Recognition;

public sealed class TemplateMatcher
{
    public const double MinimumScore = .90;
    public const double MinimumMargin = .08;

    public GlyphMatch Match(byte[] mask, string kind, IReadOnlyList<GlyphTemplate> templates)
    {
        if (mask.Length != CardGlyphExtractor.MaskLength) throw new ArgumentException("Invalid glyph mask.");
        var scores = templates.Where(t => t.Kind == kind && IsValid(t))
            .GroupBy(t => t.Label)
            .Select(g => new { Label = g.Key, Score = g.Max(t => Dice(mask, t.Mask)) })
            .OrderByDescending(x => x.Score).ToList();
        if (scores.Count < 2) return new(false, "", 0, $"Teach more {kind} symbols first.");
        double best = scores[0].Score, margin = best - scores[1].Score;
        if (best < MinimumScore || margin < MinimumMargin)
            return new(false, "", best, $"Uncertain {kind}; teach the visible cards or recalibrate.");
        return new(true, scores[0].Label, best, "Matched a learned symbol.");
    }

    public static bool IsValid(GlyphTemplate t) => t.Mask is not null && t.Label is not null && t.Mask.Length == CardGlyphExtractor.MaskLength
        && t.Mask.All(x => x <= 1) && t.Mask.Any(x => x == 1)
        && ((t.Kind == "rank" && t.Label.Length == 1 && "AKQJT98765432".Contains(t.Label))
            || (t.Kind == "suit" && t.Label.Length == 1 && "cdhs".Contains(t.Label)));

    public static double Dice(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) throw new ArgumentException("Mask sizes differ.");
        int sum = 0, intersection = 0;
        for (int i = 0; i < a.Length; i++) { sum += a[i] + b[i]; intersection += a[i] & b[i]; }
        return sum == 0 ? 0 : 2.0 * intersection / sum;
    }
}
