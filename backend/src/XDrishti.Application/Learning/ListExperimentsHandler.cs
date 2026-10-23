using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>Lists recorded experiments (spec P6-11). An empty list is an answer: nothing was tried yet.</summary>
internal sealed class ListExperimentsHandler(IExperimentLedger ledger) : IQueryHandler<ListExperimentsQuery, Result<IReadOnlyList<ExperimentView>>>
{
    public async ValueTask<Result<IReadOnlyList<ExperimentView>>> HandleAsync(ListExperimentsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Limit is < 1 or > 200)
        {
            return Result.Failure<IReadOnlyList<ExperimentView>>(LearningErrors.Invalid([new FieldError("limit", "The limit is 1 to 200.")]));
        }

        var rows = await ledger.ListAsync(query.Limit, cancellationToken);
        return Result.Success<IReadOnlyList<ExperimentView>>([.. rows.Select(ExperimentView.Of)]);
    }
}
