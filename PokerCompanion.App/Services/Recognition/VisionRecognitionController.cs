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
        _latest = image; _anchor ??= image; _model = model;
        if (_busy && staticPreview) _pendingStatic = true;
        if (_busy || (!staticPreview && DateTime.UtcNow < _nextRead)) return;
        _ = ReadAsync(image, staticPreview, model, _generation);
    }
    private async Task ReadAsync(HeroVisionImage image, bool staticPreview, string model, long generation)
    {
        _busy = true;
        using var cancellation = new CancellationTokenSource(); _cancellation = cancellation;
        if (!_confirmed) Publish(new(false, "", "", "Reading hero cards with local vision. First model load can take longer."), staticPreview);
        try
        {
            HandObservation reading = await _reader.ReadAsync(image.Png, model, cancellation.Token);
            if (_disposed || generation != _generation || _latest is null
                || !VisionFrameGuard.Matches(image.Pixels, _latest.Pixels)) return;
            var result = staticPreview ? reading : _confirmation.Observe(reading);
            _confirmed = !staticPreview && result.Success;
            Publish(result, staticPreview);
        }
        catch (OperationCanceledException)
        {
            if (!_disposed && generation == _generation)
            { _confirmation.Reset(); _confirmed = false; Publish(new(false, "", "", "Vision request timed out. Check Ollama and try again."), staticPreview); }
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException or System.IO.IOException)
        {
            if (!_disposed && generation == _generation)
            {
                _confirmation.Reset(); _confirmed = false;
                string message = ex is HttpRequestException ? "Cannot connect to local Ollama. Start Ollama, pull the model, then click Check Ollama." : ex.Message;
                Publish(new(false, "", "", message), staticPreview);
            }
        }
        finally
        {
            _busy = false; _cancellation = null;
            _nextRead = DateTime.UtcNow.AddSeconds(2);
            if (_pendingStatic && !_disposed && _latest is not null)
            {
                _pendingStatic = false;
                _ = ReadAsync(_latest, true, _model, _generation);
            }
        }
    }
    private void Publish(HandObservation observation, bool staticPreview) => ObservationReady?.Invoke(observation, staticPreview);
    public void Dispose() { _disposed = true; Reset(); }
}
