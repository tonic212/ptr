using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using PokerCompanion.Core.Recognition;

namespace PokerCompanion.App.Services.Recognition;

// Dispatcher-owned capture state. The CPU tracker runs even while GPU inference
// is busy. GPU requests remain sequential; layout initialization runs once.
public sealed class VisionRecognitionController : IDisposable
{
    private readonly IVisionCardReader _reader;
    private readonly FastPositionTracker? _fast;
    private readonly VisionHandConfirmation _confirmation = new();
    private readonly PositionConfirmation _fallbackConfirmation = new();
    private PositionObservation? _fallback;
    private DateTime _fallbackExpires, _nextLayoutRead, _nextRead;
    private BitmapSource? _latestTable;
    private HeroVisionImage? _latest, _anchor;
    private CancellationTokenSource? _cancellation, _layoutCancellation;
    private long _generation, _cardGeneration;
    private bool _busy, _confirmed, _pendingStatic, _disposed;
    private string _model = "";
    public bool DetectPosition { get; set; } = true;
    public event Action<PositionObservation, bool>? PositionReady;
    public event Action<HandObservation, bool>? ObservationReady;

    public VisionRecognitionController(IVisionCardReader reader, FastPositionTracker? fast = null) { _reader = reader; _fast = fast; }
    private void ResetCards()
    {
        _cardGeneration++; _cancellation?.Cancel(); _confirmation.Reset();
        _latest = null; _anchor = null; _confirmed = false; _nextRead = DateTime.MinValue;
        _fallback = null; _fallbackConfirmation.Reset();
    }
    public void Reset()
    {
        _generation++; _layoutCancellation?.Cancel(); ResetCards(); _fast?.Reset();
        _latestTable = null; _pendingStatic = false; _nextLayoutRead = DateTime.MinValue;
        PositionReady?.Invoke(PositionObservation.Unknown("Identifying seats for fast D-badge tracking."), false);
    }
    public void Submit(BitmapSource table, bool staticPreview, string model)
    {
        if (_disposed) return;
        _latestTable = table; _model = model;
        if (DetectPosition && !staticPreview)
        {
            var fast = _fast?.Observe(FrameAdapter.ToPixels(table), DateTime.UtcNow)
                ?? PositionObservation.Unknown("Fast badge templates unavailable. Using vision fallback.");
            var result = _fast?.HasLayout != true && _fallback?.Success == true && DateTime.UtcNow < _fallbackExpires ? _fallback : fast;
            PositionReady?.Invoke(result, false);
        }
        try
        {
            var image = HeroVisionImage.Create(table);
            if (_anchor is not null && !VisionFrameGuard.Matches(_anchor.Pixels, image.Pixels))
            { ResetCards(); ObservationReady?.Invoke(new(false, "", "", "Hero image changed. Checking current cards."), staticPreview); }
            _latest = image; _anchor ??= image;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        { ResetCards(); ObservationReady?.Invoke(new(false, "", "", ex.Message), staticPreview); }
        if (_busy && staticPreview) _pendingStatic = true;
        if (_busy || (!staticPreview && DateTime.UtcNow < _nextRead)) return;
        _ = ReadAsync(staticPreview, model, _generation);
    }
    private async Task ReadAsync(bool staticPreview, string model, long generation)
    {
        _busy = true;
        try
        {
            if (DetectPosition && _reader is IVisionPositionReader positions && _latestTable is not null
                && (staticPreview || (_fast?.HasLayout != true && DateTime.UtcNow >= _nextLayoutRead)))
                await ReadLayoutAsync(positions, _latestTable, staticPreview, model, generation);
            if (_disposed || generation != _generation || _latest is null) return;
            HeroVisionImage image = _latest;
            long cardGeneration = _cardGeneration;
            using var cancellation = new CancellationTokenSource(); _cancellation = cancellation;
            if (!_confirmed) ObservationReady?.Invoke(new(false, "", "", "Reading hero cards with local vision. Position tracking runs independently."), staticPreview);
            try
            {
                var reading = await _reader.ReadAsync(image.Png, model, cancellation.Token);
                if (_disposed || generation != _generation || cardGeneration != _cardGeneration || _latest is null
                    || !VisionFrameGuard.Matches(image.Pixels, _latest.Pixels)) return;
                reading = image.Evidence?.Validate(reading) ?? reading;
                var result = staticPreview ? reading : _confirmation.Observe(reading);
                _confirmed = !staticPreview && result.Success;
                ObservationReady?.Invoke(result, staticPreview);
            }
            catch (OperationCanceledException)
            {
                if (!_disposed && generation == _generation && cardGeneration == _cardGeneration)
                { ResetCards(); ObservationReady?.Invoke(new(false, "", "", "Card request timed out. Position tracking continues."), staticPreview); }
            }
            catch (Exception ex) when (IsReadingError(ex))
            {
                if (!_disposed && generation == _generation && cardGeneration == _cardGeneration)
                { ResetCards(); ObservationReady?.Invoke(new(false, "", "", ex.Message), staticPreview); }
            }
        }
        finally
        {
            _busy = false; _cancellation = null; _nextRead = DateTime.UtcNow.AddSeconds(2);
            if (_pendingStatic && !_disposed && _latestTable is not null)
            { _pendingStatic = false; _ = ReadAsync(true, _model, _generation); }
        }
    }
    private async Task ReadLayoutAsync(IVisionPositionReader reader, BitmapSource table, bool staticPreview, string model, long generation)
    {
        using var cancellation = new CancellationTokenSource(); _layoutCancellation = cancellation;
        DateTime capturedAt = DateTime.UtcNow;
        try
        {
            var reading = await reader.ReadPositionAsync(PositionVisionImages.Create(table), model, cancellation.Token);
            if (_disposed || generation != _generation || _latestTable is null) return;
            Debug.WriteLine($"Position layout parsed: success={reading.Success}, players={reading.PlayerCount}, vacant={reading.VacantSeats.Count}, complete={reading.LayoutComplete}, elapsed={(DateTime.UtcNow - capturedAt).TotalSeconds:F1}s, status={reading.Status}");
            if (staticPreview) { PositionReady?.Invoke(reading, true); return; }
            if (DateTime.UtcNow - capturedAt >= TimeSpan.FromSeconds(20))
                reading = PositionObservation.Unknown("Layout response is too old. Checking a fresh capture.");
            if (reading.Success && _fast?.Initialize(FrameAdapter.ToPixels(table), reading, capturedAt) == true)
            {
                Debug.WriteLine("Position tracker: " + _fast.InitializationStatus);
                _fallback = null; _fallbackConfirmation.Reset();
                PositionReady?.Invoke(_fast.Observe(FrameAdapter.ToPixels(_latestTable), DateTime.UtcNow), false);
            }
            else
            {
                string diagnostic = !reading.Success ? reading.Status : _fast?.InitializationStatus ?? "CPU tracker is unavailable.";
                Debug.WriteLine("Position tracker: " + diagnostic);
                var result = _fallbackConfirmation.Observe(reading);
                if (reading.Success) result = result with { Status = result.Status + " Slow vision fallback: " + diagnostic };
                _fallback = result.Success ? result : null;
                _fallbackExpires = capturedAt.AddSeconds(20);
                PositionReady?.Invoke(_fallback ?? result, false);
            }
        }
        catch (OperationCanceledException)
        { if (!_disposed && generation == _generation) PositionFailed("Layout request timed out."); }
        catch (Exception ex) when (IsReadingError(ex))
        { if (!_disposed && generation == _generation) PositionFailed("Position reading: " + ex.Message); }
        finally { _layoutCancellation = null; _nextLayoutRead = DateTime.UtcNow.AddSeconds(4); }
    }
    private void PositionFailed(string status)
    {
        _fallback = null; _fallbackConfirmation.Reset();
        PositionReady?.Invoke(PositionObservation.Unknown(status), false);
    }
    private static bool IsReadingError(Exception ex) => ex is HttpRequestException or InvalidOperationException or ArgumentException
        or System.Text.Json.JsonException or System.IO.IOException;
    public void Dispose() { _disposed = true; Reset(); }
}
