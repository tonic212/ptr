namespace PokerCompanion.Core.Recognition;

public sealed class FastPositionTracker
{
    private readonly DealerButtonDetector _detector;
    private readonly PositionConfirmation _confirmation = new();
    private PositionObservation? _layout;
    private SeatPoint[] _slots = [];
    private byte[][] _fingerprints = [];
    private double _aspect;
    private DateTime _expires;
    public bool HasLayout => _layout is not null;
    public FastPositionTracker(DealerButtonDetector detector) => _detector = detector;
    public void Reset() { _layout = null; _slots = []; _fingerprints = []; _confirmation.Reset(); }
    public bool Initialize(PixelFrame frame, PositionObservation layout, DateTime now)
    {
        Reset();
        if (!layout.Success || !layout.LayoutComplete || layout.Seats.Count != layout.PlayerCount
            || !_detector.TryFind(frame, layout.Seats, out var badge) || badge!.SeatIndex != layout.DealerSeatIndex) return false;
        _layout = layout; _slots = layout.Seats.Concat(layout.VacantSeats).ToArray();
        _fingerprints = _slots.Select(s => Fingerprint(frame, s)).ToArray();
        _aspect = frame.Width / (double)frame.Height; _expires = now.AddMinutes(2);
        return true;
    }
    public PositionObservation Observe(PixelFrame frame, DateTime now)
    {
        if (_layout is null) return PositionObservation.Unknown("Identifying occupied and empty seats once for fast badge tracking.");
        if (now >= _expires || Math.Abs(frame.Width / (double)frame.Height / _aspect - 1) > .03)
        { Reset(); return PositionObservation.Unknown("Seat layout expired or table proportions changed. Refreshing layout."); }
        for (int i = 0; i < _slots.Length; i++)
        {
            byte[] current = Fingerprint(frame, _slots[i]);
            double difference = current.Zip(_fingerprints[i], (a, b) => Math.Abs(a - b)).Average() / 255;
            if (difference > .22)
            { Reset(); return PositionObservation.Unknown("Player or empty-seat appearance changed. Refreshing seat layout."); }
        }
        if (!_detector.TryFind(frame, _layout.Seats, out var badge))
        { _confirmation.Reset(); return PositionObservation.Unknown("D badge is hidden or ambiguous. Waiting for a clear frame."); }
        var reading = TablePositionParser.Calculate(_layout.Seats, badge!.SeatIndex);
        var confirmed = _confirmation.Observe(reading);
        return confirmed.Success ? confirmed with { Status = $"Fast D-badge tracking: hero {confirmed.Position} • {confirmed.PlayerCount} players." } : confirmed;
    }
    private static byte[] Fingerprint(PixelFrame frame, SeatPoint seat)
    {
        byte[] values = new byte[8 * 8 * 3];
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                int px = Math.Clamp((int)((seat.X + (x / 7.0 - .5) * .07) * frame.Width), 0, frame.Width - 1);
                int py = Math.Clamp((int)((seat.Y + (y / 7.0 - .5) * .035) * frame.Height), 0, frame.Height - 1);
                Array.Copy(frame.Bgra, (py * frame.Width + px) * 4, values, (y * 8 + x) * 3, 3);
            }
        return values;
    }
}
