using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.BrokerHistory;

/// <summary>
/// What was read for one window of trades: the usable trades, the rows Dhan sent, those that were unusable and the skipped ones per reason, whether the window had to be halved, and how many (half-)windows were taken as empty only because Dhan
/// answered DH-907 to their first page. <see cref="Drops"/> says why the <see cref="Dropped"/> rows were dropped (per reason, and the location of the first few); it accounts for all of them.
/// </summary>
internal sealed record TradeWindowRead(IReadOnlyList<BrokerTrade> Trades, int Rows, int Dropped, IReadOnlyDictionary<string, int> Skipped, bool Halved, int EmptyByNoData = 0)
{
    public BrokerDrops Drops { get; init; } = BrokerDrops.None;
}
