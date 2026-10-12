namespace XDrishti.Application.Abstractions.Market.Bars;

/// <summary>An instrument the Dhan phase fetches: tracked, not retired, a share, ETF or index with a Dhan security id. <see cref="LastBar"/> is its newest stored bar (null when it has none).</summary>
public sealed record FetchTarget(Guid InstrumentId, string Symbol, string Type, string SecurityId, string Segment, DateTimeOffset? LastBar);

public interface IFetchTargets
{
    ValueTask<IReadOnlyList<FetchTarget>> ListAsync(CancellationToken cancellationToken);
}
