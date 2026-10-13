using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Platform.Services;

/// <summary>
/// The runs of a service, newest first, <c>page</c> from 1 and <c>pageSize</c> 1 to 100 (default 25). Only the latest <see cref="ServiceRunLimits.MaxShown"/> are shown. <c>services.not_found</c> for an unknown id,
/// <c>services.invalid</c> for a page outside its bounds. The read-only account sync has no history here (its runs are on the Accounts screen).
/// </summary>
public sealed record ListServiceRunsQuery(string ServiceId, int? Page = null, int? PageSize = null) : IQuery<Result<ServiceRunPage>>;

internal sealed class ListServiceRunsHandler(ServiceRegistry registry, IServiceRunStore runs) : IQueryHandler<ListServiceRunsQuery, Result<ServiceRunPage>>
{
    public async ValueTask<Result<ServiceRunPage>> HandleAsync(ListServiceRunsQuery query, CancellationToken cancellationToken)
    {
        var known = registry.Find(query.ServiceId) is not null;
        if (!known && query.ServiceId != ServiceIds.AccountSync)
        {
            return Result.Failure<ServiceRunPage>(ServiceErrors.NotFound);
        }

        var errors = new FieldErrors();
        var page = query.Page ?? 1;
        var size = query.PageSize ?? ServiceRunLimits.DefaultPageSize;
        if (page < 1)
        {
            errors.Add("page", "Use a page from 1.");
        }

        if (size is < 1 or > ServiceRunLimits.MaxPageSize)
        {
            errors.Add("pageSize", $"Use a page size from 1 to {ServiceRunLimits.MaxPageSize}.");
        }

        if (errors.HasErrors)
        {
            return Result.Failure<ServiceRunPage>(errors.ToError(ServiceErrors.InvalidCode, "The request is not valid."));
        }

        if (!known)
        {
            return Result.Success(new ServiceRunPage([], page, size, 0));
        }

        var total = Math.Min(await runs.CountAsync(query.ServiceId, cancellationToken), ServiceRunLimits.MaxShown);
        var skip = (long)(page - 1) * size;
        var take = (int)Math.Max(0, Math.Min(size, ServiceRunLimits.MaxShown - skip));
        var items = take == 0 ? [] : await runs.ListAsync(query.ServiceId, (int)skip, take, cancellationToken);
        return Result.Success(new ServiceRunPage([.. items.Select(ServiceRunView.From)], page, size, total));
    }
}
