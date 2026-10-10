namespace PokerCompanion.Core.Recognition;

public interface IVisionCardReader
{
    Task<HandObservation> ReadAsync(byte[] png, string model, CancellationToken cancellationToken);
}
