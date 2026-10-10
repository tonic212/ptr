namespace PokerCompanion.Core.Recognition;

// Compares the actual card crop, not model confidence. A changed crop invalidates
// a pending reading and any hand previously confirmed from that crop.
public static class VisionFrameGuard
{
    public static bool Matches(PixelFrame first, PixelFrame second)
    {
        if (first.Width != second.Width || first.Height != second.Height) return false;
        long total = 0;
        int changed = 0, count = first.Width * first.Height;
        for (int i = 0; i < first.Bgra.Length; i += 4)
        {
            int difference = 0;
            for (int channel = 0; channel < 3; channel++)
                difference += Math.Abs(first.Bgra[i + channel] - second.Bgra[i + channel]);
            total += difference;
            if (difference > 90) changed++;
        }
        return total / (count * 3.0 * 255) <= .012 && changed / (double)count <= .025;
    }
}

public sealed class VisionHandConfirmation
{
    private string _candidate = "";
    private int _readings;
    public void Reset() { _candidate = ""; _readings = 0; }
    public HandObservation Observe(HandObservation reading)
    {
        if (!reading.Success) { Reset(); return reading; }
        if (reading.Cards == _candidate) _readings++; else { _candidate = reading.Cards; _readings = 1; }
        if (_readings < 2) return new(false, "", "", "Vision reading: " + reading.Cards + ". Waiting for a second matching capture.");
        return reading with { Status = "Local vision: two matching captures. Scenario, position and stack remain manual." };
    }
}
