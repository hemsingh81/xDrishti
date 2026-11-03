namespace XDrishti.Application.Abstractions.Pairs;

/// <summary>The F&amp;O shares of the master list (P14-13): read only, today's labels, ordered by symbol.</summary>
public interface IPairUniverseSource
{
    ValueTask<IReadOnlyList<PairInstrument>> ListAsync(CancellationToken cancellationToken);
}
