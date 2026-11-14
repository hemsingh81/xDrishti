namespace XDrishti.Application.Abstractions.Market.Strength;

/// <summary>The index the relative-strength features measure against (BK-09): the tracked Nifty 50 instrument whose stored <c>1D</c> bars give <c>rs_vs_index</c>.</summary>
public interface IBenchmarkIndex
{
    /// <summary>The id of the tracked, not retired Nifty 50 index instrument with a Dhan id in IDX_I (found by canonical symbol <c>NSE:NIFTY</c>, then by display name <c>Nifty 50</c>); null when there is none.</summary>
    ValueTask<Guid?> FindAsync(CancellationToken cancellationToken);
}
