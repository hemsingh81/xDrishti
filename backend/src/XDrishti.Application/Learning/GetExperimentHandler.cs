using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>One recorded experiment in full (spec P6-11).</summary>
internal sealed class GetExperimentHandler(IExperimentLedger ledger) : IQueryHandler<GetExperimentQuery, Result<ExperimentView>>
{
    public async ValueTask<Result<ExperimentView>> HandleAsync(GetExperimentQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var stored = await ledger.FindAsync(query.Id, cancellationToken);
        return stored is null ? Result.Failure<ExperimentView>(ExperimentErrors.NotFound(query.Id)) : Result.Success(ExperimentView.Of(stored));
    }
}
