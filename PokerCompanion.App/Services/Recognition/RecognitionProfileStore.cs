using System;
using System.IO;
using System.Text.Json;
using PokerCompanion.Core.Recognition;

namespace PokerCompanion.App.Services.Recognition;

public sealed class RecognitionProfileStore
{
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PokerCompanion", "recognition-profile.json");
    public string? LoadWarning { get; private set; }

    public RecognitionProfile Load()
    {
        if (File.Exists(_path))
        {
            try
            {
                var saved = JsonSerializer.Deserialize<RecognitionProfile>(File.ReadAllText(_path));
                if (saved is null) throw new InvalidDataException("Empty recognition profile.");
                _ = new HandRecognitionSession(saved);
                return saved;
            }
            catch (Exception ex) when (ex is IOException or JsonException or ArgumentException or UnauthorizedAccessException)
            { LoadWarning = "Saved calibration could not be loaded; using seed templates. " + ex.Message; }
        }
        string seedPath = Path.Combine(AppContext.BaseDirectory, "Data", "recognition-seeds.json");
        return JsonSerializer.Deserialize<RecognitionProfile>(File.ReadAllText(seedPath))
            ?? throw new InvalidDataException("Seed templates are missing.");
    }
    public void Save(RecognitionProfile profile)
    {
        string directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
