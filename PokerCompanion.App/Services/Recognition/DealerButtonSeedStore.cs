using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using PokerCompanion.Core.Recognition;

namespace PokerCompanion.App.Services.Recognition;

public static class DealerButtonSeedStore
{
    public static DealerButtonDetector Load()
    {
        string path = Path.Combine(BundledDataStore.Root, "Data", "dealer-button-seeds.json");
        var glyphs = JsonSerializer.Deserialize<List<DealerButtonGlyph>>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Dealer badge templates are missing.");
        return new(glyphs);
    }
}
