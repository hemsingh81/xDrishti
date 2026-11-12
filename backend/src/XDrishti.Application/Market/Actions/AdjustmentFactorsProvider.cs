using XDrishti.Application.Abstractions.Market.Actions;
using XDrishti.Domain.Market.Actions;

namespace XDrishti.Application.Market.Actions;

internal sealed class AdjustmentFactorsProvider(ICorporateActionQueries queries) : IAdjustmentFactors
{
    public async ValueTask<AdjustmentFactors> GetAsync(Guid instrumentId, CancellationToken cancellationToken) =>
        AdjustmentFactors.From(await queries.ListAppliedAsync(instrumentId, cancellationToken));

    public async ValueTask<DateOnly?> CsvAdjustedThroughAsync(Guid instrumentId, CancellationToken cancellationToken) =>
        (await queries.ListCsvAdjustedThroughAsync([instrumentId], cancellationToken)).TryGetValue(instrumentId, out var date) ? date : null;
}
