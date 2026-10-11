using System.Text.Json;

namespace PokerCompanion.Core.Recognition;

public sealed record SeatPoint(double X, double Y);
public sealed record PositionObservation(bool Success, string Position, int PlayerCount, string LayoutKey, string Status)
{
    public IReadOnlyList<SeatPoint> Seats { get; init; } = [];
    public IReadOnlyList<SeatPoint> VacantSeats { get; init; } = [];
    public int DealerSeatIndex { get; init; } = -1;
    public bool LayoutComplete { get; init; }
    public static PositionObservation Unknown(string status) => new(false, "", 0, "", status);
}

public interface IVisionPositionReader
{
    Task<PositionObservation> ReadPositionAsync(PositionVisionInput images, string model, CancellationToken cancellationToken);
}

public sealed record PositionVisionInput(byte[] TablePng, IReadOnlyList<byte[]> DetailPngs);

// The model locates seats/button only. Clockwise ordering and chart position are code.
public static class TablePositionParser
{
    private static readonly string[] Positions = ["UTG", "UTG+1", "UTG+2", "LJ", "HJ", "CO", "BTN", "SB", "BB"];
    public static PositionObservation Parse(string json) => ParseInternal(json, false);
    public static PositionObservation ParseOpponents(string json) => ParseInternal(json, true);
    private static PositionObservation ParseInternal(string json, bool opponentsOnly)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.GetProperty("clear").ValueKind != JsonValueKind.True)
            {
                string? reason = root.TryGetProperty("reason", out var detail) && detail.ValueKind == JsonValueKind.String ? detail.GetString() : null;
                return PositionObservation.Unknown(reason switch
                {
                    "button_not_found" => "Cannot identify the small D badge beside a player. Checking enlarged seat views.",
                    "seats_unclear" => "Cannot identify all occupied player seats. Keep the whole table visible.",
                    _ => "Seat layout or dealer button is unclear. Checking enlarged seat views."
                });
            }
            var seats = root.GetProperty("seats").EnumerateArray()
                .Select(s => new SeatPoint(s.GetProperty("x").GetDouble(), s.GetProperty("y").GetDouble())).ToArray();
            int dealer = root.GetProperty("dealer").GetInt32();
            if (!opponentsOnly) return Calculate(seats, dealer);
            if (seats.Length < 2 || seats.Length > 8 || dealer < -1 || dealer >= seats.Length)
                return PositionObservation.Unknown("Need 2–8 opponents and a visible D badge owner. Heads-up charts are not available.");
            if (seats.Any(s => s.X >= .30 && s.X <= .70 && s.Y >= .72))
                return PositionObservation.Unknown("The model included bottom hero in the opponent list. Checking again.");
            // Hero is the known bottom seat, independently established by card
            // recognition. A model cannot omit or relocate that anchor.
            SeatPoint[] anchored = [new(.5, .86), .. seats];
            var result = Calculate(anchored, dealer + 1);
            if (!result.Success) return result;
            SeatPoint[] vacant = root.TryGetProperty("vacant", out var empty)
                ? empty.EnumerateArray().Select(s => new SeatPoint(s.GetProperty("x").GetDouble(), s.GetProperty("y").GetDouble())).ToArray() : [];
            var all = anchored.Concat(vacant).ToArray();
            if (all.Length > 9 || vacant.Any(s => !ValidSeatLocation(s) || (s.X >= .30 && s.X <= .70 && s.Y >= .72)))
                return PositionObservation.Unknown("Invalid empty-seat layout.");
            for (int i = 0; i < all.Length; i++)
                for (int j = 0; j < i; j++)
                    if (Math.Pow(all[i].X - all[j].X, 2) + Math.Pow(all[i].Y - all[j].Y, 2) < .0064)
                        return PositionObservation.Unknown("Occupied and empty seat locations overlap.");
            return result with { VacantSeats = vacant, LayoutComplete = root.TryGetProperty("layout_complete", out var complete) && complete.ValueKind == JsonValueKind.True };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { return PositionObservation.Unknown("Invalid seat/button reading. Checking again."); }
    }
    private static bool ValidSeatLocation(SeatPoint s) => double.IsFinite(s.X) && double.IsFinite(s.Y)
        && s.X >= 0 && s.X <= 1 && s.Y >= 0 && s.Y <= 1
        && !(Math.Abs(s.X - .5) < .17 && Math.Abs(s.Y - .48) < .17);

    public static PositionObservation Calculate(IReadOnlyList<SeatPoint> seats, int dealer)
    {
        if (seats.Count < 3 || seats.Count > 9 || dealer < 0 || dealer >= seats.Count)
            return PositionObservation.Unknown("Need 3–9 clearly identified players and one dealer button. Heads-up charts are not available.");
        for (int i = 0; i < seats.Count; i++)
        {
            var s = seats[i];
            if (!ValidSeatLocation(s))
                return PositionObservation.Unknown("Invalid player-seat location.");
            for (int j = 0; j < i; j++)
                if (Math.Pow(s.X - seats[j].X, 2) + Math.Pow(s.Y - seats[j].Y, 2) < .0064)
                    return PositionObservation.Unknown("Duplicate or overlapping player seats.");
        }
        var heroes = Enumerable.Range(0, seats.Count).Where(i => seats[i].Y >= .72 && seats[i].X >= .30 && seats[i].X <= .70).ToArray();
        if (heroes.Length != 1) return PositionObservation.Unknown("Cannot identify exactly one bottom hero seat.");
        int hero = heroes[0];
        var order = Enumerable.Range(0, seats.Count).OrderBy(i => Math.Atan2(seats[i].Y - .48, seats[i].X - .5)).ToArray();
        int buttonIndex = Array.IndexOf(order, dealer), heroIndex = Array.IndexOf(order, hero);
        int clockwiseAfterButton = (heroIndex - buttonIndex + seats.Count) % seats.Count;
        string position = clockwiseAfterButton switch
        {
            0 => "BTN", 1 => "SB", 2 => "BB",
            _ => Positions[9 - seats.Count + clockwiseAfterButton - 3]
        };
        // Rotate around hero so response array order and subpixel movement do not alter identity.
        int buttonAfterHero = (buttonIndex - heroIndex + seats.Count) % seats.Count;
        string key = $"{seats.Count}:{buttonAfterHero}:" + string.Join(";", Enumerable.Range(0, seats.Count).Select(k =>
        {
            var s = seats[order[(heroIndex + k) % seats.Count]];
            return s.X < .3 ? "L" : s.X > .7 ? "R" : s.Y < .48 ? "T" : "H";
        }));
        return new(true, position, seats.Count, key, $"Hero: {position} • {seats.Count} players. Nine-seat chart label by distance from BTN.") { Seats = seats.ToArray(), DealerSeatIndex = dealer };
    }
}

public sealed class PositionConfirmation
{
    private string _candidate = "";
    private int _count;
    public void Reset() { _candidate = ""; _count = 0; }
    public PositionObservation Observe(PositionObservation reading)
    {
        if (!reading.Success) { Reset(); return reading; }
        if (reading.LayoutKey == _candidate) _count++; else { _candidate = reading.LayoutKey; _count = 1; }
        return _count >= 2 ? reading : PositionObservation.Unknown("Confirming occupied seats and dealer button on a second capture.");
    }
}
