using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.BrokerHistory;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Market;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.LiveTrading;

/// <summary>Turns the mapped rows of the two books into stored shapes: the share is found by the ticker, else the ISIN or Dhan's security id (the same resolution as the history), a row whose share cannot be told is left out and counted. Pure apart from the master read.</summary>
internal static class LiveBookMapping
{
    public static async ValueTask<(IReadOnlyList<LiveFill> Fills, int Unresolved)> FillsAsync(ITradeReferenceData reference, Guid accountId, IReadOnlyList<LiveBrokerFill> rows, CancellationToken cancellationToken)
    {
        var resolved = await BrokerTradeSecurities.ResolveAsync(reference, [.. rows.Select(r => new BrokerTrade(r.TradeId, r.OrderId, r.Side == Domain.Research.Side.Buy ? TradeSide.Buy : TradeSide.Sell, r.Symbol, r.Isin, r.SecurityId, r.Qty, r.Price, 0m, r.Date, r.Time))], cancellationToken);
        var fills = new List<LiveFill>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var symbol = resolved[i].Symbol;
            if (symbol.Length == 0)
            {
                continue;
            }

            var r = rows[i];
            fills.Add(new LiveFill(Guid.CreateVersion7(), accountId, r.TradeId, r.OrderId, symbol, r.Side, r.Product, r.Qty, r.Price, r.Date, r.Time));
        }

        return (fills, rows.Count - fills.Count);
    }

    public static async ValueTask<(IReadOnlyList<LiveOrder> Orders, int Unresolved)> OrdersAsync(ITradeReferenceData reference, Guid accountId, IReadOnlyList<LiveBrokerOrder> rows, CancellationToken cancellationToken)
    {
        var resolved = await BrokerTradeSecurities.ResolveAsync(reference, [.. rows.Select(r => new BrokerTrade(r.OrderId, null, r.Side == Domain.Research.Side.Buy ? TradeSide.Buy : TradeSide.Sell, r.Symbol, r.Isin, r.SecurityId, 1, 1m, 0m, DateOnly.FromDateTime(r.CreateTime.DateTime), null))], cancellationToken);
        var orders = new List<LiveOrder>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var symbol = resolved[i].Symbol;
            if (symbol.Length == 0)
            {
                continue;
            }

            var r = rows[i];
            orders.Add(new LiveOrder(accountId, r.OrderId, symbol, r.Side, r.Product, r.OrderType, r.Validity, r.Qty, r.Price, r.TriggerPrice, r.Status, r.FilledQty, r.AvgTradedPrice, r.CreateTime, r.ExchangeTime, r.UpdateTime, r.LegName, r.AlgoId, r.RejectText, DateOnly.FromDateTime(IndiaTime.ToIst(r.CreateTime).DateTime)));
        }

        return (orders, rows.Count - orders.Count);
    }
}
