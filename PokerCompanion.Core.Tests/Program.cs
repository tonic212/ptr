using System.Text.Json;
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
Console.WriteLine($"PASS: {assertions} recognition assertions.");
