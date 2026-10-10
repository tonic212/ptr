namespace PokerCompanion.Core.Recognition;

public sealed class HandRecognitionSession
{
    private readonly CardGlyphExtractor _extractor = new();
    private readonly TemplateMatcher _matcher = new();
    private string _candidate = "";
    private int _frames;
    public RecognitionProfile Profile { get; }
    public string Coverage => $"Learned ranks: {Profile.Templates.Where(x => x.Kind == "rank").Select(x => x.Label).Distinct().Count()}/13; suits: {Profile.Templates.Where(x => x.Kind == "suit").Select(x => x.Label).Distinct().Count()}/4.";

    public HandRecognitionSession(RecognitionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Version != 1 || profile.Templates is null || profile.Templates.Any(t => t is null || !TemplateMatcher.IsValid(t)))
            throw new ArgumentException("Unsupported or invalid recognition profile.");
        Profile = profile;
    }

    public void SetRegion(int slot, CardRegion region, int width, int height)
    {
        if (slot is not (1 or 2) || !region.IsValid || width <= 0 || height <= 0) throw new ArgumentException("Invalid calibration.");
        if (Profile.ReferenceWidth != width || Profile.ReferenceHeight != height)
        { Profile.FirstCard = null; Profile.SecondCard = null; }
        Profile.ReferenceWidth = width; Profile.ReferenceHeight = height;
        if (slot == 1) Profile.FirstCard = region; else Profile.SecondCard = region;
        Reset();
    }

    public void Reset() { _candidate = ""; _frames = 0; }

    public HandObservation Observe(PixelFrame frame)
    {
        HandObservation result = Inspect(frame);
        if (!result.Success) return Unknown(result.Status);
        if (result.Cards == _candidate) _frames++; else { _candidate = result.Cards; _frames = 1; }
        if (_frames < 3) return new(false, "", "", $"Checking consistent frames ({_frames}/3).");
        return result with { Status = "Recognized from three matching frames. Chart scenario and stack remain manual." };
    }

    // Single-frame inspection is useful for screenshot calibration and teaching.
    // It does not count as a temporal confirmation.
    public HandObservation Inspect(PixelFrame frame)
    {
        try
        {
            ValidateFrame(frame);
            CardGlyphs a = _extractor.Extract(frame, Profile.FirstCard!);
            CardGlyphs b = _extractor.Extract(frame, Profile.SecondCard!);
            GlyphMatch ar = _matcher.Match(a.Rank, "rank", Profile.Templates), ass = _matcher.Match(a.Suit, "suit", Profile.Templates);
            GlyphMatch br = _matcher.Match(b.Rank, "rank", Profile.Templates), bs = _matcher.Match(b.Suit, "suit", Profile.Templates);
            GlyphMatch? failed = new[] { ar, ass, br, bs }.FirstOrDefault(x => !x.Success);
            if (failed is not null) return Unknown(failed.Reason);
            string first = ar.Label + ass.Label, second = br.Label + bs.Label;
            if (first == second) return Unknown("Duplicate cards detected. Check both calibration regions.");
            string cards = first + " " + second;
            return new(true, cards, ToHandClass(first, second), "Single-frame symbol match.");
        }
        catch (InvalidOperationException ex) { return Unknown(ex.Message); }
        catch (ArgumentException ex) { return Unknown(ex.Message); }
    }

    public void Teach(PixelFrame frame, string cards)
    {
        ValidateFrame(frame);
        string[] parts = cards.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || parts.Any(p => p.Length != 2 || !"akqjt98765432".Contains(p[0]) || !"cdhs".Contains(p[1])) || parts[0] == parts[1])
            throw new ArgumentException("Enter two distinct cards, for example 9c 6s. Use T for ten and c/d/h/s for suits.");
        CardGlyphs first = _extractor.Extract(frame, Profile.FirstCard!), second = _extractor.Extract(frame, Profile.SecondCard!);
        Add("rank", char.ToUpperInvariant(parts[0][0]).ToString(), first.Rank);
        Add("suit", parts[0][1].ToString(), first.Suit);
        Add("rank", char.ToUpperInvariant(parts[1][0]).ToString(), second.Rank);
        Add("suit", parts[1][1].ToString(), second.Suit);
        Reset();
    }

    private void Add(string kind, string label, byte[] mask)
    {
        if (Profile.Templates.Any(t => t.Kind == kind && t.Label == label && t.Mask.SequenceEqual(mask))) return;
        var existing = Profile.Templates.Where(t => t.Kind == kind && t.Label == label).ToList();
        if (existing.Count >= 8) Profile.Templates.Remove(existing[0]);
        Profile.Templates.Add(new() { Kind = kind, Label = label, Mask = mask });
    }
    private void ValidateFrame(PixelFrame frame)
    {
        if (Profile.FirstCard is null || Profile.SecondCard is null || !Profile.FirstCard.IsValid || !Profile.SecondCard.IsValid
            || Profile.ReferenceWidth <= 0 || Profile.ReferenceHeight <= 0)
            throw new InvalidOperationException("Calibrate both card corners in the window preview.");
        CardRegion a = Profile.FirstCard, b = Profile.SecondCard;
        if (a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height)
            throw new InvalidOperationException("Card corners overlap. Recalibrate separate corners.");
        double referenceRatio = Profile.ReferenceWidth / (double)Profile.ReferenceHeight;
        double currentRatio = frame.Width / (double)frame.Height;
        if (Math.Abs(currentRatio / referenceRatio - 1) > .03)
            throw new InvalidOperationException("Window proportions changed. Recalibrate both corners.");
    }
    private HandObservation Unknown(string reason) { Reset(); return new(false, "", "", reason); }

    public static string ToHandClass(string first, string second)
    {
        const string ranks = "AKQJT98765432";
        if (first.Length != 2 || second.Length != 2 || first == second
            || !ranks.Contains(first[0]) || !ranks.Contains(second[0]) || !"cdhs".Contains(first[1]) || !"cdhs".Contains(second[1]))
            throw new ArgumentException("Invalid card pair.");
        if (first[0] == second[0]) return new string(first[0], 2);
        char high = ranks.IndexOf(first[0]) < ranks.IndexOf(second[0]) ? first[0] : second[0];
        char low = high == first[0] ? second[0] : first[0];
        return $"{high}{low}" + (first[1] == second[1] ? "s" : "o");
    }
}
