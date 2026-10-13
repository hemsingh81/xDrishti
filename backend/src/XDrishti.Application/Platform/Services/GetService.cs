using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Platform.Services;

/// <summary>One service as the screen shows it. <c>services.not_found</c> for an unknown id.</summary>
public sealed record GetServiceQuery(string ServiceId) : IQuery<Result<ServiceView>>;

internal sealed class GetServiceHandler(ServiceViewReader reader) : IQueryHandler<GetServiceQuery, Result<ServiceView>>
{
    public async ValueTask<Result<ServiceView>> HandleAsync(GetServiceQuery query, CancellationToken cancellationToken) =>
        await reader.ReadAsync(query.ServiceId, cancellationToken) is { } view ? Result.Success(view) : Result.Failure<ServiceView>(ServiceErrors.NotFound);
}
