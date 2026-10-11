using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PokerCompanion.Core.Recognition;

namespace PokerCompanion.App.Services.Recognition;

public sealed class OllamaVisionCardReader : IVisionCardReader, IVisionPositionReader, IDisposable
{
    private readonly HttpClient _http;
    private readonly ILocalModelHost? _host;
    private const string Prompt = "Read the TWO face-up hero playing cards, left to right. The image may show two isolated enlarged card corners on white panels. "
        + "When panels are shown, the left is card 1 and the right is card 2. Read each small printed rank and suit. Ignore avatars, chips, badges, buttons and other text. "
        + "Transcribe each printed rank exactly: A K Q J 10 9 8 7 6 5 4 3 2. "
        + "The printed two-digit number 10 is TEN, not A, not 1, and not 0. Preserve both digits as 10 in your response. "
        + "Suits: c=club (three lobes), d=diamond, h=heart, s=spade. "
        + "The deck may use green clubs, blue diamonds, red hearts, black spades. Use symbol shape as well as colour. "
        + "Return visible=true only if BOTH printed ranks and suits are clear; otherwise visible=false and cards=[]. "
        + "Return the two card strings in left-to-right order using the printed rank followed by c/d/h/s. "
        + "Do not infer, guess, or complete unclear text. Return only the requested JSON object.";

    public OllamaVisionCardReader() : this(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { }
    public OllamaVisionCardReader(ILocalModelHost host) : this(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }, host) { }
    public OllamaVisionCardReader(HttpMessageHandler handler, ILocalModelHost? host = null)
    {
        _host = host;
        _http = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:11435/"), Timeout = TimeSpan.FromSeconds(90) };
    }

    public async Task<string> CheckAsync(string model, CancellationToken cancellationToken)
    {
        ValidateModel(model);
        if (_host is not null) await _host.EnsureReadyAsync(model, cancellationToken);
        using var response = await _http.PostAsJsonAsync("api/show", new { model }, cancellationToken);
        await EnsureSuccess(response, cancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!json.RootElement.TryGetProperty("capabilities", out var caps) || caps.ValueKind != JsonValueKind.Array
            || !caps.EnumerateArray().Any(c => c.ValueKind == JsonValueKind.String && c.GetString() == "vision"))
            throw new InvalidOperationException("Selected model does not advertise vision support. Select a vision model.");
        return "Connected to local Ollama. Vision model installed: " + model + ".";
    }

    public async Task<HandObservation> ReadAsync(byte[] png, string model, CancellationToken cancellationToken)
    {
        ValidateModel(model);
        if (_host is not null) await _host.EnsureReadyAsync(model, cancellationToken);
        if (png.Length == 0 || png.Length > 4 * 1024 * 1024) throw new ArgumentException("Invalid card crop size.");
        var schema = new
        {
            type = "object",
            properties = new
            {
                visible = new { type = "boolean" },
                cards = new { type = "array", items = new { type = "string", pattern = "^(10|[AKQJT2-9])[cdhs]$" }, maxItems = 2 }
            },
            required = new[] { "visible", "cards" },
            additionalProperties = false
        };
        var payload = new
        {
            model, stream = false, format = schema, keep_alive = "5m",
            options = new { temperature = 0, num_predict = 96, num_ctx = 8192 },
            messages = new[] { new { role = "user", content = Prompt, images = new[] { Convert.ToBase64String(png) } } }
        };
        string? content = await SendReadingAsync(payload, cancellationToken);
        return content is null ? new(false, "", "", "Ollama returned an incomplete reading.") : VisionCardResponseParser.Parse(content);
    }

    public Task<PositionObservation> ReadPositionAsync(byte[] png, string model, CancellationToken cancellationToken)
        => ReadPositionAsync(new PositionVisionInput(png, Array.Empty<byte[]>()), model, cancellationToken);

    public async Task<PositionObservation> ReadPositionAsync(PositionVisionInput images, string model, CancellationToken cancellationToken)
    {
        ValidateModel(model);
        if (_host is not null) await _host.EnsureReadyAsync(model, cancellationToken);
        if (images.DetailPngs.Count is not (0 or 3)) throw new ArgumentException("Expected left, right and bottom seat detail images.");
        byte[][] pngs = new[] { images.TablePng }.Concat(images.DetailPngs).ToArray();
        if (pngs.Any(png => png.Length == 0 || png.Length > 4 * 1024 * 1024)) throw new ArgumentException("Invalid table image size.");
        const string prompt = "Locate occupied OPPONENT player seats and the DEALER BUTTON on this PPPoker table. Return each player avatar CENTRE, not a bounding box corner. "
            + "Image 1 is the FULL TABLE. If four images are supplied, images 2, 3, 4 are enlarged LEFT, RIGHT, BOTTOM seat areas from the SAME capture. "
            + "Use those detail images to read tiny badges, but return OPPONENT seat x,y coordinates normalized from 0 to 1 in IMAGE 1, never in a detail crop. Image coordinates start at TOP LEFT (0,0) and end at BOTTOM RIGHT (1,1). "
            + "A player seat has an avatar with a player-name/stack panel. Include folded players: Fold text, grey avatars, or missing hole cards do NOT mean an empty seat. "
            + "Exclude seats explicitly labelled Empty and the decorative female dealer at the TOP CENTRE, which has no player-name/stack panel. "
            + "A single occupied seat may show a pair of card backs, bets, action labels or flags; these are not additional players. "
            + "The bottom-centre player is HERO and is already known by the app. NEVER include hero in seats; return ONLY the other occupied player seats. Find the small WHITE outlined badge printed D BESIDE a player avatar, often on its inner table side. "
            + "The badge may be HEXAGONAL or circular, with a dark/green D. Inspect the enlarged side images. It is not the decorative woman, a chip, diamond suit, Bet/Raise label or Empty marker. "
            + "dealer is the ZERO-BASED index in your OPPONENT seats array of the player beside that D badge. If the D belongs to bottom HERO, return dealer=-1 with clear=true and reason=ok. Do not derive it from betting or cards. "
            + "clear=true when occupied seats and D ownership are visually unambiguous, including folded players. Return vacant as centres of every visible Empty seat badge; never include these in occupied seats. Return layout_complete=true only when every occupied AND Empty player slot is accounted for. "
            + "If explicit sit-out status makes participation uncertain, or a seat is obstructed, use clear=false. "
            + "For clear=false use seats=[], dealer=-1 and reason=button_not_found or seats_unclear; for clear=true use reason=ok. "
            + "Do not guess a missing D. Do not output poker positions or actions. Return only JSON.";
        var schema = new
        {
            type = "object", properties = new
            {
                clear = new { type = "boolean" },
                seats = new { type = "array", maxItems = 8, items = new {
                    type = "object", properties = new { x = new { type = "number", minimum = 0, maximum = 1 }, y = new { type = "number", minimum = 0, maximum = 1 } },
                    required = new[] { "x", "y" }, additionalProperties = false } },
                vacant = new { type = "array", maxItems = 6, items = new {
                    type = "object", properties = new { x = new { type = "number", minimum = 0, maximum = 1 }, y = new { type = "number", minimum = 0, maximum = 1 } },
                    required = new[] { "x", "y" }, additionalProperties = false } },
                layout_complete = new { type = "boolean" },
                dealer = new { type = "integer", minimum = -1, maximum = 7 },
                reason = new { type = "string", @enum = new[] { "ok", "button_not_found", "seats_unclear" } }
            }, required = new[] { "clear", "seats", "vacant", "layout_complete", "dealer", "reason" }, additionalProperties = false
        };
        var payload = new
        {
            model, stream = false, format = schema, keep_alive = "5m",
            options = new { temperature = 0, num_predict = 512, num_ctx = 8192 },
            messages = new[] { new { role = "user", content = prompt, images = pngs.Select(Convert.ToBase64String).ToArray() } }
        };
        string? content = await SendReadingAsync(payload, cancellationToken);
        if (content is not null) System.Diagnostics.Debug.WriteLine("Position model JSON: " + content);
        return content is null ? PositionObservation.Unknown("Incomplete seat/button reading.") : TablePositionParser.ParseOpponents(content);
    }

    private async Task<string?> SendReadingAsync(object payload, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync("api/chat", payload, cancellationToken);
        await EnsureSuccess(response, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int length;
        while ((length = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + length > 65536) throw new InvalidOperationException("Ollama response exceeded the reading limit.");
            buffer.Write(chunk, 0, length);
        }
        using var document = JsonDocument.Parse(buffer.ToArray());
        if (!document.RootElement.TryGetProperty("done", out var done) || done.ValueKind != JsonValueKind.True
            || !document.RootElement.TryGetProperty("message", out var message)
            || !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String) return null;
        return content.GetString();
    }

    private static void ValidateModel(string model)
    {
        if (string.IsNullOrWhiteSpace(model) || model.Length > 100 || model.Contains("cloud", StringComparison.OrdinalIgnoreCase)
            || model.Any(c => !(char.IsAsciiLetterOrDigit(c) || "-_.:/".Contains(c))))
            throw new ArgumentException("Enter an installed local model name, for example qwen3-vl:4b-instruct-q4_K_M.");
    }
    private static async Task EnsureSuccess(HttpResponseMessage response, CancellationToken token)
    {
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("Model not installed. Click Prepare local vision to download it.");
        string body = await response.Content.ReadAsStringAsync(token);
        throw new InvalidOperationException("Ollama request failed (" + (int)response.StatusCode + "). " + body[..Math.Min(body.Length, 300)]);
    }
    public void Dispose() => _http.Dispose();
}
