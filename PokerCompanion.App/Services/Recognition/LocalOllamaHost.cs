using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PokerCompanion.Core.Recognition;

namespace PokerCompanion.App.Services.Recognition;

public sealed class LocalOllamaHost : ILocalModelHost, IDisposable
{
    public const string Endpoint = "http://127.0.0.1:11435/";
    private readonly HttpClient _http = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        { BaseAddress = new Uri(Endpoint), Timeout = Timeout.InfiniteTimeSpan };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly OllamaRuntimeInstaller _installer = new();
    private Process? _process;
    private string? _readyModel;
    private bool _disposed;
    public event Action<string>? Progress;

    public async Task EnsureReadyAsync(string model, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        linked.CancelAfter(TimeSpan.FromMinutes(45));
        await _gate.WaitAsync(linked.Token);
        try
        {
            if (_readyModel == model && await IsRunningAsync(linked.Token)) return;
            if (!await IsRunningAsync(linked.Token))
            {
                string executable = FindInstalledRuntime() ?? await _installer.EnsureAsync(Report, linked.Token);
                var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(executable)! };
                start.ArgumentList.Add("serve");
                start.Environment["OLLAMA_HOST"] = "127.0.0.1:11435";
                start.Environment["OLLAMA_NO_CLOUD"] = "1";
                start.Environment["OLLAMA_NUM_PARALLEL"] = "1";
                start.Environment["OLLAMA_MAX_LOADED_MODELS"] = "1";
                start.Environment["OLLAMA_CONTEXT_LENGTH"] = "4096";
                // Reuse the standard model cache, including models already pulled
                // by the user. This does not change system environment settings.
                start.Environment["OLLAMA_MODELS"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ollama", "models");
                _process?.Dispose(); _process = Process.Start(start) ?? throw new InvalidOperationException("Could not start local vision runtime.");
                Report("Starting local vision runtime.");
                bool ready = false;
                for (int attempt = 0; attempt < 120; attempt++)
                {
                    if (await IsRunningAsync(linked.Token)) { ready = true; break; }
                    if (_process.HasExited) throw new InvalidOperationException("Local vision runtime exited before startup. Check GPU drivers and retry setup.");
                    await Task.Delay(500, linked.Token);
                }
                if (!ready) throw new InvalidOperationException("Local vision runtime did not start within 60 seconds.");
            }
            using var show = await _http.PostAsJsonAsync("api/show", new { model }, linked.Token);
            if (show.StatusCode == HttpStatusCode.NotFound) await PullAsync(model, linked.Token);
            else show.EnsureSuccessStatusCode();
            _readyModel = model; Report("Local vision ready: " + model + ". Launching this app now starts vision automatically.");
        }
        finally { _gate.Release(); }
    }
    private async Task PullAsync(string model, CancellationToken token)
    {
        Report("Downloading " + model + " (first setup only). Keep the app open.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/pull") { Content = JsonContent.Create(new { model, stream = true }) };
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(token);
        using var reader = new StreamReader(stream);
        bool success = false;
        DateTime lastProgress = DateTime.MinValue;
        while (await reader.ReadLineAsync(token) is string line)
        {
            using var message = JsonDocument.Parse(line);
            var root = message.RootElement;
            if (root.TryGetProperty("error", out var error)) throw new InvalidOperationException("Model download failed: " + error.GetString());
            string status = root.TryGetProperty("status", out var value) ? value.GetString() ?? "Downloading model" : "Downloading model";
            if (status == "success") success = true;
            if ((DateTime.UtcNow - lastProgress).TotalSeconds < 1 && !success) continue;
            if (root.TryGetProperty("total", out var total) && total.TryGetInt64(out long size) && size > 0
                && root.TryGetProperty("completed", out var completed) && completed.TryGetInt64(out long count))
                status += $" {count * 100.0 / size:F0}%";
            Report("Model setup: " + status); lastProgress = DateTime.UtcNow;
        }
        if (!success) throw new IOException("Model download ended before completion. Click Prepare local vision to retry.");
    }
    private async Task<bool> IsRunningAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            using var response = await _http.GetAsync("api/version", timeout.Token);
            if (!response.IsSuccessStatusCode) return false;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            return json.RootElement.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String;
        }
        catch (HttpRequestException) { return false; }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return false; }
        catch (JsonException) { return false; }
    }
    private static string? FindInstalledRuntime()
    {
        string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe");
        return File.Exists(local) ? local : null;
    }
    private void Report(string status) => Progress?.Invoke(status);
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel();
        try { if (_process is not null && !_process.HasExited) _process.Kill(entireProcessTree: true); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        _process?.Dispose();
        // Keep synchronization objects alive until cancelled callers unwind.
    }
}
