using System;
using System.IO;
using System.Text.Json;

namespace PokerCompanion.App.Services.Recognition;

public sealed record VisionSettings(bool Enabled = true, string Model = "qwen3-vl:4b-instruct-q4_K_M");

public sealed class VisionSettingsStore
{
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PokerCompanion", "vision-settings.json");
    public VisionSettings Load()
    {
        try { return File.Exists(_path) ? JsonSerializer.Deserialize<VisionSettings>(File.ReadAllText(_path)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public void Save(VisionSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(settings)); File.Move(temporary, _path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
