using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.BrokerHistory;

/// <summary>
/// What was read for one window of the ledger: the usable lines, the lines Dhan sent, those that were unusable, whether the window had to be halved, and how many (half-)windows were taken as empty only because Dhan answered DH-907. <see cref="Drops"/> says why the <see cref="Dropped"/> lines were dropped.
/// </summary>
internal sealed record LedgerWindowRead(IReadOnlyList<BrokerLedgerRow> Rows, int Received, int Dropped, bool Halved, int EmptyByNoData = 0)
{
    public BrokerDrops Drops { get; init; } = BrokerDrops.None;
}
