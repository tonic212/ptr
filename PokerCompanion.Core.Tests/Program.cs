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
var selectedArea = new TableAreaSelection(new(.10, .10, .20, .60), 1920, 1080);
Check(selectedArea.TryGetViewport(1920, 1080, out var selectedViewport, out _) && selectedViewport == new TableViewport(192, 108, 384, 648), "selected table pixel bounds");
Check(selectedArea.TryGetViewport(3840, 2160, out var scaledViewport, out _) && scaledViewport == new TableViewport(384, 216, 768, 1296), "selected table scales with capture");
Check(!selectedArea.TryGetViewport(1080, 1920, out _, out _), "selected area rejects aspect change");
Check(!new TableAreaSelection(new(0, 0, 1, 1), 1920, 1080).TryGetViewport(1920, 1080, out _, out _), "desktop-sized selection rejected");
Check(!new TableAreaSelection(new(.9, 0, .2, .5), 1920, 1080).TryGetViewport(1920, 1080, out _, out _), "outside table selection rejected");
string savedProfile = JsonSerializer.Serialize(new RecognitionProfile { TableArea = selectedArea });
Check(JsonSerializer.Deserialize<RecognitionProfile>(savedProfile)!.TableArea == selectedArea, "table selection persists independently of glyph calibration");
Check(VisionCardResponseParser.Parse("{\"visible\":true,\"cards\":[\"7h\",\"Ac\"]}").HandClass == "A7o", "vision canonical cards");
Check(VisionCardResponseParser.Parse("{\"visible\":true,\"cards\":[\"10D\",\"jc\"]}").Cards == "Td Jc", "vision ten and case normalization");
foreach (string invalid in new[] {
    "not json", "{}", "null", "{\"visible\":true,\"cards\":[\"Ac\",\"Ac\"]}",
    "{\"visible\":true,\"cards\":[\"1c\",\"Kh\"]}", "{\"visible\":true,\"cards\":[\"Ac\"]}",
    "{\"visible\":true,\"cards\":[\"Ac\",\"Kd\",\"Qs\"]}", "{\"visible\":\"true\",\"cards\":[\"Ac\",\"Kh\"]}",
    "{\"visible\":true,\"cards\":[null,\"Kh\"]}", "{\"visible\":false,\"cards\":[]}" })
    Check(!VisionCardResponseParser.Parse(invalid).Success, "vision rejects invalid or missing cards: " + invalid);
var confirmation = new VisionHandConfirmation();
var visionReading = VisionCardResponseParser.Parse("{\"visible\":true,\"cards\":[\"7h\",\"Ac\"]}");
Check(!confirmation.Observe(visionReading).Success, "vision first capture withheld");
Check(confirmation.Observe(visionReading).Success, "vision second capture accepted");
Check(!confirmation.Observe(new(false, "", "", "hidden")).Success, "vision unknown clears confirmation");
Check(!confirmation.Observe(visionReading).Success, "vision reappearance needs new confirmation");
confirmation.Reset();
Check(!confirmation.Observe(visionReading).Success, "vision reset invalidates confirmation");
Check(VisionFrameGuard.Matches(frame, frame), "vision unchanged crop matches");
Check(!VisionFrameGuard.Matches(frame, blank), "vision disappeared cards invalidate pending reading");
Check(!VisionFrameGuard.Matches(frame, enlarged), "vision resized crop invalidates pending reading");
var noise = frame.Bgra.ToArray();
for (int i = 0; i < noise.Length; i += 4) noise[i] = (byte)Math.Clamp(noise[i] + 1, 0, 255);
Check(VisionFrameGuard.Matches(frame, new(frame.Width, frame.Height, noise)), "vision tolerates tiny pixel noise");
using var fake = new FakeOllamaHandler();
using var reader = new PokerCompanion.App.Services.Recognition.OllamaVisionCardReader(fake);
Check((await reader.CheckAsync("qwen3-vl:4b-instruct-q4_K_M", CancellationToken.None)).Contains("Connected"), "local vision availability check");
Check((await reader.ReadAsync(new byte[] { 1, 2, 3 }, "qwen3-vl:4b-instruct-q4_K_M", CancellationToken.None)).Cards == "7h Ac", "Ollama transport response parsed");
using (var sent = JsonDocument.Parse(fake.LastBody!))
{
    Check(fake.LastUri == "http://127.0.0.1:11435/api/chat", "vision endpoint stays local");
    Check(!sent.RootElement.GetProperty("stream").GetBoolean(), "vision nonstreaming JSON");
    Check(sent.RootElement.GetProperty("messages")[0].GetProperty("images")[0].GetString() == "AQID", "vision sends encoded crop");
    Check(sent.RootElement.GetProperty("format").GetProperty("additionalProperties").ValueKind == JsonValueKind.False, "vision constrained response schema");
    Check(sent.RootElement.GetProperty("options").GetProperty("num_predict").GetInt32() == 96, "vision token budget bounded");
}
Reject(() => reader.ReadAsync(new byte[] { 1 }, "qwen3-vl:cloud", CancellationToken.None).GetAwaiter().GetResult(), "cloud model names rejected");
fake.Body = "{\"done\":false,\"message\":{\"content\":\"{}\"}}";
Check(!(await reader.ReadAsync(new byte[] { 1 }, "local", CancellationToken.None)).Success, "incomplete Ollama response rejected");
fake.Status = System.Net.HttpStatusCode.NotFound;
try { await reader.ReadAsync(new byte[] { 1 }, "missing", CancellationToken.None); throw new Exception("FAILED: missing model"); }
catch (InvalidOperationException ex) { Check(ex.Message.Contains("not installed"), "missing model produces install instruction"); }
using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
try { await reader.ReadAsync(new byte[] { 1 }, "local", cancelled.Token); throw new Exception("FAILED: cancellation"); }
catch (OperationCanceledException) { assertions++; }
using var fakeHost = new FakeLocalHost();
using var hostedHandler = new FakeOllamaHandler();
using var hostedReader = new PokerCompanion.App.Services.Recognition.OllamaVisionCardReader(hostedHandler, fakeHost);
await hostedReader.CheckAsync("local-vision", CancellationToken.None);
await hostedReader.ReadAsync(new byte[] { 1 }, "local-vision", CancellationToken.None);
Check(fakeHost.Calls == 2 && fakeHost.Model == "local-vision", "reader prepares owned runtime before check and inference");
fakeHost.Fail = true;
try { await hostedReader.ReadAsync(new byte[] { 1 }, "local-vision", CancellationToken.None); throw new Exception("FAILED: runtime preparation failure"); }
catch (IOException) { assertions++; }
using (var tenDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "vision-ten-frame.json"))))
{
    var ten = tenDocument.RootElement;
    using var compressed = new MemoryStream(ten.GetProperty("CompressedBgra").GetBytesFromBase64());
    using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
    using var decoded = new MemoryStream(); gzip.CopyTo(decoded);
    var tenFrame = new PixelFrame(ten.GetProperty("Width").GetInt32(), ten.GetProperty("Height").GetInt32(), decoded.ToArray());
    var prepared = HeroVisionPreparation.Create(tenFrame);
    Check(!prepared.FirstPrintedTen && prepared.SecondPrintedTen, "real 8/10 corner distinguishes printed ten");
    Check(prepared.Image.Width == 320 && prepared.Image.Height == 256, "vision uses isolated enlarged corner panels");
    Check(prepared.Validate(VisionCardResponseParser.Parse("{\"visible\":true,\"cards\":[\"8s\",\"10d\"]}")).HandClass == "T8o", "vision printed ten normalizes correctly");
    var wrongAce = VisionCardResponseParser.Parse("{\"visible\":true,\"cards\":[\"8s\",\"Ad\"]}");
    Check(!prepared.Validate(wrongAce).Success, "image ten vetoes incorrect ace reading");
    var guardConfirmation = new VisionHandConfirmation();
    Check(!guardConfirmation.Observe(prepared.Validate(wrongAce)).Success
        && !guardConfirmation.Observe(prepared.Validate(wrongAce)).Success, "repeated wrong ace readings never confirm");
    byte[] resizedBytes = new byte[tenFrame.Bgra.Length * 4];
    for (int y = 0; y < tenFrame.Height * 2; y++)
        for (int x = 0; x < tenFrame.Width * 2; x++)
            Array.Copy(tenFrame.Bgra, ((y / 2) * tenFrame.Width + x / 2) * 4, resizedBytes, (y * tenFrame.Width * 2 + x) * 4, 4);
    var resizedTen = HeroVisionPreparation.Create(new(tenFrame.Width * 2, tenFrame.Height * 2, resizedBytes));
    Check(resizedTen.SecondPrintedTen, "printed ten guard survives doubled size");
    try { HeroVisionPreparation.Create(new(800, 600, new byte[800 * 600 * 4])); throw new Exception("FAILED: portrait selection"); }
    catch (InvalidOperationException) { assertions++; }
    try { HeroVisionPreparation.Create(new(tenFrame.Width, tenFrame.Height, new byte[tenFrame.Bgra.Length])); throw new Exception("FAILED: empty vision crop"); }
    catch (InvalidOperationException) { assertions++; }
}
foreach (var template in profile.Templates.Where(t => t.Kind == "rank" && t.Label != "T"))
    Check(!PrintedTenDetector.IsPrintedTen(template.Mask), "ten guard rejects other seeded ranks: " + template.Label);
Console.WriteLine($"PASS: {assertions} recognition assertions.");


sealed class FakeOllamaHandler : HttpMessageHandler
{
    public string? LastBody { get; private set; }
    public string? LastUri { get; private set; }
    public string Body { get; set; } = "{\"done\":true,\"message\":{\"content\":\"{\\\"visible\\\":true,\\\"cards\\\":[\\\"7h\\\",\\\"Ac\\\"]}\"}}";
    public System.Net.HttpStatusCode Status { get; set; } = System.Net.HttpStatusCode.OK;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastUri = request.RequestUri!.AbsoluteUri;
        LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
        string body = request.RequestUri.AbsolutePath == "/api/show" ? "{\"capabilities\":[\"completion\",\"vision\"]}" : Body;
        return new(Status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
    }
}

sealed class FakeLocalHost : ILocalModelHost, IDisposable
{
    public int Calls { get; private set; }
    public string? Model { get; private set; }
    public bool Fail { get; set; }
    public Task EnsureReadyAsync(string model, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++; Model = model;
        if (Fail) throw new IOException("Runtime setup failed.");
        return Task.CompletedTask;
    }
    public void Dispose() { }
}
