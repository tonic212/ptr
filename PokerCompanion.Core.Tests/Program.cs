using System.Text.Json;
using System.IO.Compression;
using PokerCompanion.Core.Recognition;

int assertions = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    assertions++;
}
void Reject(Action action, string name)
{
    try { action(); } catch (ArgumentException) { assertions++; return; }
    throw new Exception("FAILED: " + name);
}
Check(HandRecognitionSession.ToHandClass("9c", "6s") == "96o", "offsuit");
Check(HandRecognitionSession.ToHandClass("6s", "9c") == "96o", "rank order");
Check(HandRecognitionSession.ToHandClass("As", "Ad") == "AA", "pair");
Check(HandRecognitionSession.ToHandClass("Kh", "Ah") == "AKs", "suited");
Reject(() => HandRecognitionSession.ToHandClass("Ac", "Ac"), "duplicate card");
Reject(() => new PixelFrame(2, 2, new byte[4]), "buffer dimensions");
using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sample-frame.json")));
JsonElement f = fixture.RootElement;
var frame = new PixelFrame(f.GetProperty("Width").GetInt32(), f.GetProperty("Height").GetInt32(), f.GetProperty("Bgra").GetBytesFromBase64());
var profile = JsonSerializer.Deserialize<RecognitionProfile>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "recognition-seeds.json")))!;
var session = new HandRecognitionSession(profile);
Check(!session.Observe(frame).Success, "requires calibration");
session.SetRegion(1, f.GetProperty("FirstCard").Deserialize<CardRegion>()!, frame.Width, frame.Height);
session.SetRegion(2, f.GetProperty("SecondCard").Deserialize<CardRegion>()!, frame.Width, frame.Height);
Check(session.Inspect(frame).Cards == "9c 6s", "static inspection");
Check(!session.Observe(frame).Success, "first frame withheld");
Check(!session.Observe(frame).Success, "second frame withheld");
var result = session.Observe(frame);
Check(result.Success && result.Cards == "9c 6s" && result.HandClass == "96o", "three consistent frames");
var blank = new PixelFrame(frame.Width, frame.Height, Enumerable.Repeat((byte)255, frame.Bgra.Length).ToArray());
Check(!session.Observe(blank).Success, "blank clears stale result");
Check(!session.Observe(frame).Success, "confirmation resets after blank");
var changed = new PixelFrame(frame.Width * 2, frame.Height, new byte[frame.Bgra.Length * 2]);
Check(!session.Observe(changed).Success && session.Observe(changed).Status.Contains("proportions"), "aspect change rejected");
var enlargedBytes = new byte[frame.Bgra.Length * 4];
for (int y = 0; y < frame.Height * 2; y++)
    for (int x = 0; x < frame.Width * 2; x++)
        Array.Copy(frame.Bgra, ((y / 2) * frame.Width + x / 2) * 4, enlargedBytes, (y * frame.Width * 2 + x) * 4, 4);
var enlarged = new PixelFrame(frame.Width * 2, frame.Height * 2, enlargedBytes);
Check(session.Inspect(enlarged).Cards == "9c 6s", "uniform resize");
int before = profile.Templates.Count;
Reject(() => session.Teach(frame, "9c 9c"), "invalid teach");
Check(profile.Templates.Count == before, "invalid teach atomic");
session.Teach(frame, "9c 6s");
Check(profile.Templates.Count == before, "teach deduplicates samples");
var matcher = new TemplateMatcher();
Check(!matcher.Match(new byte[CardGlyphExtractor.MaskLength], "rank", profile.Templates).Success, "unknown glyph rejected");
var collision = new[] {
    new GlyphTemplate { Kind = "rank", Label = "A", Mask = profile.Templates[0].Mask },
    new GlyphTemplate { Kind = "rank", Label = "K", Mask = profile.Templates[0].Mask }
};
Check(!matcher.Match(profile.Templates[0].Mask, "rank", collision).Success, "ambiguous glyph rejected");
Check(!TemplateMatcher.IsValid(new GlyphTemplate { Kind = "rank", Label = "A", Mask = null! }), "corrupt template rejected");
Check(profile.Templates.Where(t => t.Kind == "rank").Select(t => t.Label).Distinct().Count() == 13, "13 ranks seeded");
Check(profile.Templates.Where(t => t.Kind == "suit").Select(t => t.Label).Distinct().Count() == 4, "4 suits seeded");
CardRegion? mapped = PreviewCoordinateMapper.Map(25, 0, 75, 100, 100, 100, 50, 100);
Check(mapped is not null && mapped.X == 0 && mapped.Width == 1, "letterbox coordinates");
Check(PreviewCoordinateMapper.Map(0, 0, 50, 100, 100, 100, 50, 100) is null, "letterbox padding rejected");
session.SetRegion(2, profile.FirstCard!, frame.Width, frame.Height);
Check(!session.Inspect(frame).Success && session.Inspect(frame).Status.Contains("overlap"), "overlapping corners rejected");
session.SetRegion(1, new(0, 0, .1, .1), 100, 200);
Check(profile.SecondCard is null, "new dimensions clear old calibration");
using var automaticFixtures = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "automatic-frames.json")));
var locator = new BottomHeroCardLocator();
foreach (JsonElement item in automaticFixtures.RootElement.EnumerateArray())
{
    using var compressed = new MemoryStream(item.GetProperty("CompressedBgra").GetBytesFromBase64());
    using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
    using var decoded = new MemoryStream(); gzip.CopyTo(decoded);
    var automaticFrame = new PixelFrame(item.GetProperty("Width").GetInt32(), item.GetProperty("Height").GetInt32(), decoded.ToArray());
    string expected = item.GetProperty("Cards").GetString()!;
    var automaticSession = new HandRecognitionSession(new RecognitionProfile { Templates = profile.Templates.ToList() });
    Check(locator.TryLocate(automaticFrame, out var regions, out _) && regions is not null, "automatic bottom card location: " + expected);
    Check(automaticSession.Inspect(automaticFrame, true).Cards == expected, "automatic symbols: " + expected);
    Check(!automaticSession.Observe(automaticFrame, true).Success, "automatic first frame withheld");
    Check(!automaticSession.Observe(automaticFrame, true).Success, "automatic second frame withheld");
    Check(automaticSession.Observe(automaticFrame, true).Cards == expected, "automatic temporal confirmation");
    Check(automaticSession.Profile.FirstCard is null, "automatic mode preserves manual profile");
    var hidden = new PixelFrame(automaticFrame.Width, automaticFrame.Height, new byte[automaticFrame.Bgra.Length]);
    Check(!automaticSession.Observe(hidden, true).Success, "automatic no cards clears result");
    Check(!automaticSession.Observe(automaticFrame, true).Success, "automatic reappearance needs confirmation");
    byte[] doubled = new byte[automaticFrame.Bgra.Length * 4];
    for (int y = 0; y < automaticFrame.Height * 2; y++)
        for (int x = 0; x < automaticFrame.Width * 2; x++)
            Array.Copy(automaticFrame.Bgra, ((y / 2) * automaticFrame.Width + x / 2) * 4, doubled, (y * automaticFrame.Width * 2 + x) * 4, 4);
    var resized = new PixelFrame(automaticFrame.Width * 2, automaticFrame.Height * 2, doubled);
    Check(automaticSession.Inspect(resized, true).Cards == expected, "automatic resized table: " + expected);
    // Same table inside a desktop-sized host rectangle, at two different offsets.
    foreach (var offset in new[] { (X: 60, Y: 100), (X: 950, Y: 250) })
    {
        const int desktopWidth = 1920, desktopHeight = 1080;
        byte[] desktop = new byte[desktopWidth * desktopHeight * 4];
        for (int y = 0; y < automaticFrame.Height; y++)
            Array.Copy(automaticFrame.Bgra, y * automaticFrame.Width * 4, desktop,
                ((y + offset.Y) * desktopWidth + offset.X) * 4, automaticFrame.Width * 4);
        var desktopFrame = new PixelFrame(desktopWidth, desktopHeight, desktop);
        Check(new PokerTableLocator().TryLocate(desktopFrame, out _, out _), "table inside desktop located");
        Check(automaticSession.Inspect(desktopFrame, true).Cards == expected, "hero in desktop capture: " + expected);
    }
    byte[] oneCard = automaticFrame.Bgra.ToArray();
    CardRegion second = regions!.Second;
    int eraseX = (int)Math.Floor(second.X * automaticFrame.Width) - 2;
    for (int y = (int)(automaticFrame.Height * .70); y < automaticFrame.Height; y++)
        for (int x = Math.Max(0, eraseX); x < automaticFrame.Width; x++)
            Array.Clear(oneCard, (y * automaticFrame.Width + x) * 4, 4);
    Check(!locator.TryLocate(new PixelFrame(automaticFrame.Width, automaticFrame.Height, oneCard), out _, out _), "single card rejected");
}
Check(!new PokerTableLocator().TryLocate(new PixelFrame(800, 600, new byte[800 * 600 * 4]), out _, out _), "desktop without table rejected");
foreach (var template in profile.Templates)
    Check(matcher.Match(template.Mask, template.Kind, profile.Templates).Label == template.Label, "seed separates labels: " + template.Label);
Console.WriteLine($"PASS: {assertions} recognition assertions.");
