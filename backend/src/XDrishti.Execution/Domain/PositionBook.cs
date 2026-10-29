namespace XDrishti.Execution.Domain;

public sealed record BookFill(Guid AccountId, string Symbol, string Product, ExecSide Side, int Qty, decimal Price);

/// <summary>Net position of one account, symbol and product. <see cref="Oversold"/> is true once the net went negative (never clamped): a short is a fault to alarm, not a state to manage.</summary>
public sealed record BookPosition(Guid AccountId, string Symbol, string Product, int NetQty, decimal AvgPrice, decimal Realised, bool Oversold);

/// <summary>The fold of fills into positions, per account, symbol and product (average cost; realised profit on the closed part).</summary>
public sealed class PositionBook
{
    private readonly Dictionary<(Guid, string, string), BookPosition> _positions = [];

    public IReadOnlyCollection<BookPosition> Positions => _positions.Values;

    public BookPosition? Find(Guid accountId, string symbol, string product) => _positions.GetValueOrDefault((accountId, symbol, product));

    public void Apply(BookFill fill)
    {
        var key = (fill.AccountId, fill.Symbol, fill.Product);
        var p = _positions.GetValueOrDefault(key) ?? new BookPosition(fill.AccountId, fill.Symbol, fill.Product, 0, 0m, 0m, false);
        var signed = fill.Side == ExecSide.Buy ? fill.Qty : -fill.Qty;
        var net = p.NetQty + signed;
        var realised = p.Realised;
        var avg = p.AvgPrice;

        if (p.NetQty == 0 || Math.Sign(p.NetQty) == Math.Sign(signed))
        {
            avg = ((Math.Abs(p.NetQty) * p.AvgPrice) + (fill.Qty * fill.Price)) / (Math.Abs(p.NetQty) + fill.Qty);
        }
        else
        {
            var closing = Math.Min(Math.Abs(p.NetQty), fill.Qty);
            realised += closing * (p.NetQty > 0 ? fill.Price - p.AvgPrice : p.AvgPrice - fill.Price);
            if (Math.Sign(net) != Math.Sign(p.NetQty) && net != 0)
            {
                avg = fill.Price;
            }
        }

        _positions[key] = p with { NetQty = net, AvgPrice = net == 0 ? 0m : avg, Realised = realised, Oversold = p.Oversold || net < 0 };
    }
}
