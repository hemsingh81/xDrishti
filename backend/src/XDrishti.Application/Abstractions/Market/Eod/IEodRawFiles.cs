namespace XDrishti.Application.Abstractions.Market.Eod;

/// <summary>Keeps the raw files of the probe under the git-ignored <c>data/</c> folder (0600). Returns the path written.</summary>
public interface IEodRawFiles
{
    ValueTask<string> SaveAsync(string name, byte[] content, CancellationToken cancellationToken);
}
