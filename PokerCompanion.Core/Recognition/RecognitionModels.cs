using System.Text.Json.Serialization;

namespace PokerCompanion.Core.Recognition;

public sealed record CardRegion(double X, double Y, double Width, double Height)
{
    [JsonIgnore]
    public bool IsValid => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Width) && double.IsFinite(Height)
        && X >= 0 && Y >= 0 && Width > 0 && Height > 0 && X + Width <= 1.000001 && Y + Height <= 1.000001;
}

public sealed class PixelFrame
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Bgra { get; }
    public PixelFrame(int width, int height, byte[] bgra)
    {
        if (width <= 0 || height <= 0 || width > 8192 || height > 8192 || bgra.Length != checked(width * height * 4))
            throw new ArgumentException("Invalid frame dimensions or pixel buffer.");
        Width = width; Height = height; Bgra = bgra;
    }
}

public sealed class GlyphTemplate
{
    public string Kind { get; set; } = "";
    public string Label { get; set; } = "";
    public byte[] Mask { get; set; } = Array.Empty<byte>();
}

public sealed class RecognitionProfile
{
    public int Version { get; set; } = 1;
    public int ReferenceWidth { get; set; }
    public int ReferenceHeight { get; set; }
    public CardRegion? FirstCard { get; set; }
    public CardRegion? SecondCard { get; set; }
    public List<GlyphTemplate> Templates { get; set; } = new();
}

public sealed record CardGlyphs(byte[] Rank, byte[] Suit);
public sealed record GlyphMatch(bool Success, string Label, double Score, string Reason);
public sealed record HandObservation(bool Success, string Cards, string HandClass, string Status);
