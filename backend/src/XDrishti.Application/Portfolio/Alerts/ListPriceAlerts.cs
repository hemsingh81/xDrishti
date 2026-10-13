using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Alerts;

/// <summary>The alerts, newest first, one page: <c>status</c> <c>active|triggered|cancelled|all</c> (default all), <c>page</c> from 1, <c>pageSize</c> up to 100 (default 50).</summary>
public sealed record ListPriceAlertsQuery(string? Status, int? Page, int? PageSize) : IQuery<Result<PriceAlertPage>>;

internal sealed class ListPriceAlertsHandler(IPriceAlertStore alerts) : IQueryHandler<ListPriceAlertsQuery, Result<PriceAlertPage>>
{
    public const int MaxPageSize = 100;

    public const int DefaultPageSize = 50;

    public const int MaxPage = 100_000;

    public async ValueTask<Result<PriceAlertPage>> HandleAsync(ListPriceAlertsQuery query, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        AlertStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            switch (query.Status.Trim().ToLowerInvariant())
            {
                case "active":
                    status = AlertStatus.Active;
                    break;
                case "triggered":
                    status = AlertStatus.Triggered;
                    break;
                case "cancelled":
                    status = AlertStatus.Cancelled;
                    break;
                case "all":
                    break;
                default:
                    errors.Add("status", "Use active, triggered, cancelled or all.");
                    break;
            }
        }

        var page = query.Page ?? 1;
        if (page is < 1 or > MaxPage)
        {
            errors.Add("page", $"Use 1 to {MaxPage}.");
        }

        var pageSize = query.PageSize ?? DefaultPageSize;
        if (pageSize is < 1 or > MaxPageSize)
        {
            errors.Add("pageSize", $"Use 1 to {MaxPageSize}.");
        }

        if (errors.HasErrors)
        {
            return Result.Failure<PriceAlertPage>(PortfolioErrors.Invalid(errors));
        }

        var (items, total) = await alerts.ListAsync(status, page, pageSize, cancellationToken);
        var active = await alerts.CountActiveAsync(cancellationToken);
        return Result.Success(new PriceAlertPage([.. items.Select(PriceAlertViews.Of)], page, pageSize, total, active, PortfolioErrors.MaxActiveAlerts));
    }
}
