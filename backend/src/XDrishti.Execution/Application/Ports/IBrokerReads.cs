using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Application.Ports;

/// <summary>A read that either produced a typed view or says why it could not (a short code, never a body). An unreadable answer is never an empty list and never a guess.</summary>
public sealed record BrokerRead<T>(bool Readable, T? Value, string? Reason);

/// <summary>Builders for <see cref="BrokerRead{T}"/>.</summary>
public static class BrokerRead
{
    public static BrokerRead<T> Ok<T>(T value) => new(true, value, null);

    public static BrokerRead<T> Unreadable<T>(string reason) => new(false, default, reason);
}

public sealed record BrokerOrderView(string OrderId, string? ClientOrderId, string Status, string SecurityId, ExecSide? Side, int Qty, int FilledQty, decimal Price, decimal? Trigger = null, string? LegName = null, string? ParentOrderId = null);

public sealed record BrokerTradeView(string OrderId, string SecurityId, ExecSide? Side, int Qty, decimal Price, string? ExchangeTradeId = null);

public sealed record BrokerPositionView(string SecurityId, int NetQty);

public sealed record BrokerFundsView(decimal Available);

/// <summary>The read-only calls (GET): order book, trade book, positions, funds. They are never counted by the send limiter and never write anything.</summary>
public interface IBrokerReads
{
    Task<BrokerRead<IReadOnlyList<BrokerOrderView>>> GetOrderBookAsync(CancellationToken cancellationToken);

    Task<BrokerRead<IReadOnlyList<BrokerTradeView>>> GetTradeBookAsync(CancellationToken cancellationToken);

    Task<BrokerRead<IReadOnlyList<BrokerPositionView>>> GetPositionsAsync(CancellationToken cancellationToken);

    Task<BrokerRead<BrokerFundsView>> GetFundsAsync(CancellationToken cancellationToken);
}
