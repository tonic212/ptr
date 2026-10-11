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
            options = new { temperature = 0, num_predict = 96, num_ctx = 4096 },
            messages = new[] { new { role = "user", content = Prompt, images = new[] { Convert.ToBase64String(png) } } }
        };
        string? content = await SendReadingAsync(payload, cancellationToken);
        return content is null ? new(false, "", "", "Ollama returned an incomplete reading.") : VisionCardResponseParser.Parse(content);
    }

    public async Task<PositionObservation> ReadPositionAsync(byte[] png, string model, CancellationToken cancellationToken)
    {
        ValidateModel(model);
        if (_host is not null) await _host.EnsureReadyAsync(model, cancellationToken);
        if (png.Length == 0 || png.Length > 4 * 1024 * 1024) throw new ArgumentException("Invalid table image size.");
        const string prompt = "Locate poker PLAYER seats and the DEALER BUTTON in this portrait PPPoker table. "
            + "Return seats as avatar/name-panel centre coordinates normalized x,y from 0 to 1 relative to the entire image. "
            + "Include ALL players dealt into this hand, even if they have folded. Exclude empty seats, sit-out seats, central decorative dealer avatar, board cards and controls. "
            + "The hero is the player at bottom centre. Locate the small circular D dealer-button badge; do not confuse it with a player avatar or the decorative dealer. "
            + "dealer is the ZERO-BASED index in your seats array of the player holding that D button. Do not derive it from betting or visible cards. "
            + "clear=true only if all participating seats and the button association are unambiguous; otherwise clear=false, seats=[], dealer=-1. "
            + "Do not guess whether a sitting-out player was dealt in. Do not output poker positions or actions. Return only JSON.";
        var schema = new
        {
            type = "object", properties = new
            {
                clear = new { type = "boolean" },
                seats = new { type = "array", maxItems = 9, items = new {
                    type = "object", properties = new { x = new { type = "number", minimum = 0, maximum = 1 }, y = new { type = "number", minimum = 0, maximum = 1 } },
                    required = new[] { "x", "y" }, additionalProperties = false } },
                dealer = new { type = "integer", minimum = -1, maximum = 8 }
            }, required = new[] { "clear", "seats", "dealer" }, additionalProperties = false
        };
        var payload = new
        {
            model, stream = false, format = schema, keep_alive = "5m",
            options = new { temperature = 0, num_predict = 384, num_ctx = 4096 },
            messages = new[] { new { role = "user", content = prompt, images = new[] { Convert.ToBase64String(png) } } }
        };
        string? content = await SendReadingAsync(payload, cancellationToken);
        return content is null ? PositionObservation.Unknown("Incomplete seat/button reading.") : TablePositionParser.Parse(content);
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
