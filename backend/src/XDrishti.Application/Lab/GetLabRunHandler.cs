using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Lab;

/// <summary>Reads one Lab run, with the verdict of a finished full test (spec P5-04).</summary>
internal sealed class GetLabRunHandler(ILabRunStore runs, ILabResultStore results) : IQueryHandler<GetLabRunQuery, Result<LabRunView>>
{
    public async ValueTask<Result<LabRunView>> HandleAsync(GetLabRunQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var run = await runs.GetAsync(query.Id, cancellationToken);
        if (run is null)
        {
            return Result.Failure<LabRunView>(LabErrors.RunNotFound(query.Id));
        }

        var verdict = run.State == LabRunState.Succeeded && string.Equals(run.Method, LabSettings.FullMethod, StringComparison.Ordinal)
            ? await results.GetAsync(run.Id, cancellationToken)
            : null;
        return Result.Success(LabRunView.Of(run, verdict));
    }
}
