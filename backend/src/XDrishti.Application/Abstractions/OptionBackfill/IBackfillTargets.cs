namespace XDrishti.Application.Abstractions.OptionBackfill;

/// <summary>The underlyings of the backfill: the app setting <c>options.underlyings</c> resolved to Dhan ids from the master list, the shares ranked by turnover (best first). Reads reference data only.</summary>
public interface IBackfillTargets
{
    /// <summary>The targets; the shares in the order of the frozen list when there is one, else in the live ranking.</summary>
    ValueTask<IReadOnlyList<BackfillTarget>> LoadAsync(CancellationToken cancellationToken);

    /// <summary>Stores the ranked shares once (the first call wins), so that later runs do not re-rank them and move the plan. Called only when every gate is open.</summary>
    ValueTask FreezeStocksAsync(CancellationToken cancellationToken);
}
