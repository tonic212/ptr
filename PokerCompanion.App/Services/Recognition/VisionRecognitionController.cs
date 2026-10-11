using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using PokerCompanion.Core.Recognition;

namespace PokerCompanion.App.Services.Recognition;

// Called on the WPF dispatcher. Network I/O is asynchronous; only one request
// runs at a time, while every capture continues to update the current image.
public sealed class VisionRecognitionController : IDisposable
{
    private readonly IVisionCardReader _reader;
    private readonly VisionHandConfirmation _confirmation = new();
    private readonly PositionConfirmation _positionConfirmation = new();
    private BitmapSource? _latestTable;
    private DateTime _nextPositionRead, _positionExpires;
    public bool DetectPosition { get; set; } = true;
    public event Action<PositionObservation, bool>? PositionReady;
    private HeroVisionImage? _latest, _anchor;
    private bool _pendingStatic;
    private string _model = "";
    private CancellationTokenSource? _cancellation;
    private long _generation;
    private bool _busy, _confirmed, _disposed;
    private DateTime _nextRead;
    public event Action<HandObservation, bool>? ObservationReady;

    public VisionRecognitionController(IVisionCardReader reader) => _reader = reader;
    public void Reset()
    {
        _generation++; _cancellation?.Cancel(); _confirmation.Reset();
        _latest = null; _anchor = null; _pendingStatic = false; _confirmed = false; _nextRead = DateTime.MinValue;
        _latestTable = null; _positionConfirmation.Reset(); _nextPositionRead = DateTime.MinValue; _positionExpires = DateTime.MinValue;
        PositionReady?.Invoke(PositionObservation.Unknown("Waiting for current seats and dealer button."), false);
    }
    public void Submit(BitmapSource table, bool staticPreview, string model)
    {
        if (_disposed) return;
        HeroVisionImage image;
        try { image = HeroVisionImage.Create(table); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        { Reset(); Publish(new(false, "", "", ex.Message), staticPreview); return; }
        if (_anchor is not null && !VisionFrameGuard.Matches(_anchor.Pixels, image.Pixels))
        {
            Reset(); Publish(new(false, "", "", "Hero image changed. Checking the current cards."), staticPreview);
        }
        _latest = image; _latestTable = table; _anchor ??= image; _model = model;
        if (DetectPosition && _positionExpires != DateTime.MinValue && DateTime.UtcNow >= _positionExpires)
        {
            _positionExpires = DateTime.MinValue; _positionConfirmation.Reset();
            PositionReady?.Invoke(PositionObservation.Unknown("Position expired. Checking the current button."), staticPreview);
        }
        if (_busy && staticPreview) _pendingStatic = true;
        if (_busy || (!staticPreview && DateTime.UtcNow < _nextRead)) return;
        _ = ReadAsync(image, table, staticPreview, model, _generation);
    }
    private async Task ReadAsync(HeroVisionImage image, BitmapSource table, bool staticPreview, string model, long generation)
    {
        _busy = true;
        using var cancellation = new CancellationTokenSource(); _cancellation = cancellation;
        if (!_confirmed) Publish(new(false, "", "", "Reading hero cards with local vision. First model load can take longer."), staticPreview);
        try
        {
            HandObservation reading = await _reader.ReadAsync(image.Png, model, cancellation.Token);
            if (_disposed || generation != _generation || _latest is null
                || !VisionFrameGuard.Matches(image.Pixels, _latest.Pixels)) return;
            reading = image.Evidence?.Validate(reading) ?? reading;
            var result = staticPreview ? reading : _confirmation.Observe(reading);
            _confirmed = !staticPreview && result.Success;
            Publish(result, staticPreview);
            if (!result.Success)
            {
                _positionConfirmation.Reset(); _positionExpires = DateTime.MinValue;
                PositionReady?.Invoke(PositionObservation.Unknown("Waiting for confirmed hero cards before detecting position."), staticPreview);
            }
            else if (DetectPosition && _reader is IVisionPositionReader positions && (staticPreview || DateTime.UtcNow >= _nextPositionRead))
            {
                // Use the freshest capture after card inference, not the older frame
                // from before it. Detail crops and full table come from this one capture.
                BitmapSource positionTable = _latestTable ?? table;
                DateTime capturedAt = DateTime.UtcNow;
                var position = await positions.ReadPositionAsync(PositionVisionImages.Create(positionTable), model, cancellation.Token);
                if (_disposed || generation != _generation || _latest is null || !VisionFrameGuard.Matches(image.Pixels, _latest.Pixels)) return;
                if (!staticPreview && DateTime.UtcNow - capturedAt >= TimeSpan.FromSeconds(20))
                    position = PositionObservation.Unknown("Seat/button response is too old. Checking a fresh capture.");
                position = staticPreview ? position : _positionConfirmation.Observe(position);
                _positionExpires = position.Success && !staticPreview ? capturedAt.AddSeconds(20) : DateTime.MinValue;
                _nextPositionRead = DateTime.UtcNow.AddSeconds(4);
                PositionReady?.Invoke(position, staticPreview);
            }
        }
        catch (OperationCanceledException)
        {
            if (!_disposed && generation == _generation)
            { Reset(); Publish(new(false, "", "", "Vision request timed out. Prepare local vision and try again."), staticPreview); }
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException or System.IO.IOException)
        {
            if (!_disposed && generation == _generation)
            {
                Reset();
                string message = ex is HttpRequestException ? "Local vision could not connect. Click Prepare local vision to retry setup." : ex.Message;
                Publish(new(false, "", "", message), staticPreview);
            }
        }
        finally
        {
            _busy = false; _cancellation = null;
            _nextRead = DateTime.UtcNow.AddSeconds(2);
            if (_pendingStatic && !_disposed && _latest is not null && _latestTable is not null)
            {
                _pendingStatic = false;
                _ = ReadAsync(_latest, _latestTable, true, _model, _generation);
            }
        }
    }
    private void Publish(HandObservation observation, bool staticPreview) => ObservationReady?.Invoke(observation, staticPreview);
    public void Dispose() { _disposed = true; Reset(); }
}
