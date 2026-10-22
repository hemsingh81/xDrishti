using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Lab;

/// <summary>Reads one Lab run.</summary>
internal sealed class GetLabRunHandler(ILabRunStore runs) : IQueryHandler<GetLabRunQuery, Result<LabRunView>>
{
    public async ValueTask<Result<LabRunView>> HandleAsync(GetLabRunQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var run = await runs.GetAsync(query.Id, cancellationToken);
        return run is null ? Result.Failure<LabRunView>(LabErrors.RunNotFound(query.Id)) : Result.Success(LabRunView.Of(run));
    }
}
