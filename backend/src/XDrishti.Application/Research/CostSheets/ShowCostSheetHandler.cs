using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Research.CostSheets;

/// <summary>Reads a stored sheet back (spec P3-03f, F4). Unknown is not found; a stored text that no longer parses is <c>costs.sheet_invalid</c>.</summary>
internal sealed class ShowCostSheetHandler(ICostSheetStore store) : IQueryHandler<ShowCostSheetQuery, Result<ShownCostSheet>>
{
    public async ValueTask<Result<ShownCostSheet>> HandleAsync(ShowCostSheetQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var found = await store.FindAsync(query.SheetId, cancellationToken);
        return found.IsFailure
            ? Result.Failure<ShownCostSheet>(found.Error)
            : Result.Success(new ShownCostSheet(found.Value.Id, found.Value.ToCanonicalJson(), found.Value.RatesSha256, CostDescription.SheetLabel(found.Value)));
    }
}
