namespace PokerCompanion.Core.Recognition;

public interface ILocalModelHost
{
    Task EnsureReadyAsync(string model, CancellationToken cancellationToken);
}
