using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Trades;

/// <summary>The trades of a holding's security (its ISIN, else its symbol), newest first, voided ones included and flagged: <c>page</c> from 1, <c>pageSize</c> up to 200 (default 50).</summary>
public sealed record ListHoldingTradesQuery(Guid HoldingId, int? Page, int? PageSize) : IQuery<Result<TradePage>>;

internal sealed class ListHoldingTradesHandler(IHoldingQueries holdings, ITradeStore trades) : IQueryHandler<ListHoldingTradesQuery, Result<TradePage>>
{
    public const int MaxPageSize = 200;

    public const int DefaultPageSize = 50;

    public const int MaxPage = 100_000;

    public async ValueTask<Result<TradePage>> HandleAsync(ListHoldingTradesQuery query, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
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
            return Result.Failure<TradePage>(PortfolioErrors.Invalid(errors));
        }

        var holding = await holdings.GetAsync(query.HoldingId, cancellationToken);
        if (holding is null)
        {
            return Result.Failure<TradePage>(PortfolioErrors.HoldingNotFound);
        }

        var (items, total) = await trades.ListForSecurityAsync(holding.AccountId, TradeRules.NormaliseIsin(holding.Isin), TradeRules.NormaliseSymbol(holding.TradingSymbol), page, pageSize, cancellationToken);
        return Result.Success(new TradePage([.. items.Select(TradeViews.Trade)], page, pageSize, total));
    }
}
