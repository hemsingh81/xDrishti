namespace XDrishti.Application.Abstractions.Lab;

/// <summary>
/// The statistics of one variant of a strategy family (CF-03, ADR 0205): the per-trade results of a run that ended in a result, summarised. A figure that cannot be computed (no spread, too few trades) is null.
/// </summary>
/// <param name="StrategyId">The family.</param>
/// <param name="RunId">The run the variant belongs to: the Lab run itself for the judged variant, the ledger row's run id for a sweep point.</param>
/// <param name="VariantHash">The strategy hash of the variant (the ledger's).</param>
/// <param name="N">Trades.</param>
/// <param name="MeanR">Mean R.</param>
/// <param name="SdR">Sample standard deviation of R.</param>
/// <param name="Skew">Skewness.</param>
/// <param name="Kurt">Kurtosis (3 for a normal).</param>
/// <param name="SrPerTrade">Mean over standard deviation, per trade.</param>
/// <param name="Method">What the trades are: <c>full</c> or <c>robust</c> for the walk-forward out-of-sample trades, <c>sweep</c> for a sweep point (in-sample, plain run of the default exit).</param>
/// <param name="CreatedAt">When it was written.</param>
public sealed record VariantStatsRow(
    string StrategyId,
    Guid RunId,
    string VariantHash,
    int N,
    double? MeanR,
    double? SdR,
    double? Skew,
    double? Kurt,
    double? SrPerTrade,
    string Method,
    DateTimeOffset CreatedAt);

/// <summary>
/// <c>lab.variant_stats</c> behind a port (CF-03, ADR 0205): insert-only for the runtime role. The trial Sharpe ratios of a family are read from here to deflate the Sharpe ratio of the variant being judged.
/// </summary>
public interface IVariantStatsStore
{
    /// <summary>Appends the rows; a row whose (run id, variant hash) is already stored writes nothing. Returns the number written.</summary>
    ValueTask<int> AppendAsync(IReadOnlyList<VariantStatsRow> rows, CancellationToken cancellationToken);

    /// <summary>The latest row of each variant hash of the family.</summary>
    ValueTask<IReadOnlyList<VariantStatsRow>> ListAsync(string strategyId, CancellationToken cancellationToken);
}
