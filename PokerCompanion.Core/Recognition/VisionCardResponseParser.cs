using System.Text.Json;

namespace PokerCompanion.Core.Recognition;

public static class VisionCardResponseParser
{
    public static HandObservation Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("visible", out var visible)
                || visible.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !root.TryGetProperty("cards", out var cards) || cards.ValueKind != JsonValueKind.Array)
                return Unknown("Vision model returned an invalid card response.");
            if (!visible.GetBoolean()) return Unknown("Vision model could not read two face-up hero cards.");
            if (cards.GetArrayLength() != 2 || cards.EnumerateArray().Any(c => c.ValueKind != JsonValueKind.String))
                return Unknown("Vision model must return exactly two cards.");
            string first = Normalize(cards[0].GetString()!), second = Normalize(cards[1].GetString()!);
            string hand = HandRecognitionSession.ToHandClass(first, second);
            return new(true, first + " " + second, hand, "Local vision model: single-frame reading.");
        }
        catch (JsonException) { return Unknown("Vision model returned malformed JSON."); }
        catch (ArgumentException) { return Unknown("Vision model returned invalid or duplicate cards."); }
    }

    private static string Normalize(string card)
    {
        card = card.Trim();
        if (card.StartsWith("10", StringComparison.Ordinal)) card = "T" + card[2..];
        if (card.Length != 2) throw new ArgumentException("Invalid card.");
        return char.ToUpperInvariant(card[0]).ToString() + char.ToLowerInvariant(card[1]);
    }
    private static HandObservation Unknown(string status) => new(false, "", "", status);
}
