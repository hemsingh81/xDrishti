using XDrishti.Domain.Accounts;
using XDrishti.Domain.Paper;

namespace XDrishti.Application.Abstractions.Paper;

/// <summary>A paper account as the ledger needs it: the Trading-role account whose mode is paper now.</summary>
/// <param name="AccountId">The account.</param>
/// <param name="Name">Its name.</param>
/// <param name="Capital">The fixed capital; null for a BrokerFunds account (no capital figure: equity and every percentage are unavailable).</param>
/// <param name="CapitalSource">Where the capital comes from, in words.</param>
public sealed record PaperAccountInput(Guid AccountId, string Name, decimal? Capital, string CapitalSource)
{
    /// <summary>The one mapping from a Trading-role account: Fixed capital is the amount the plan sizes on; BrokerFunds has no capital figure.</summary>
    public static PaperAccountInput From(BrokerAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return account.Trading.CapitalSource == Domain.Accounts.CapitalSource.Fixed && account.Trading.CapitalAmount is { } amount
            ? new PaperAccountInput(account.Id, account.Name, amount, "the account's fixed capital (the amount the plan sizes on)")
            : new PaperAccountInput(account.Id, account.Name, null, "no capital figure: the account takes its funds from the broker, which a paper account cannot read");
    }
}

/// <summary>A newest stored daily bar of an instrument and the instrument's id (for its sector and baskets).</summary>
/// <param name="InstrumentId">The instrument master id.</param>
/// <param name="Close">The newest daily bar at or before the cut.</param>
public sealed record InstrumentClose(Guid InstrumentId, DailyClose Close);

/// <summary>Independent sums read straight from the stored rows, for the reconciliation to compare with the derived ledger.</summary>
/// <param name="ExitResult">The sum of <c>coalesce(net_pnl, gross_pnl)</c> of the account's exits.</param>
/// <param name="EntriesWithoutExit">Entries of the account that have no exit.</param>
/// <param name="ExitBeforeEntry">Exits whose instant is before their entry's instant.</param>
public sealed record StoredTotals(decimal ExitResult, int EntriesWithoutExit, int ExitBeforeEntry);

/// <summary>
/// The raw reads behind the paper ledger (spec P10-02, ADR 0118): stored paper trades, which sessions finished capture, the newest stored daily closes and independent totals. Read only: the port has no write member and
/// cannot place an order.
/// </summary>
public interface IPaperLedgerData
{
    /// <summary>The epoch (the newest one when <paramref name="epochId"/> is null), or null when there is none.</summary>
    ValueTask<EpochRow?> EpochAsync(long? epochId, CancellationToken cancellationToken);

    /// <summary>Every stored trade of the epoch (of one account when given) with its exit, oldest entry first.</summary>
    ValueTask<IReadOnlyList<LedgerTrade>> TradesAsync(long epochId, Guid? accountId, CancellationToken cancellationToken);

    /// <summary>The sessions from <paramref name="from"/> that have a capture row with outcome done and nothing pending.</summary>
    ValueTask<IReadOnlySet<DateOnly>> FinishedSessionsAsync(DateOnly from, CancellationToken cancellationToken);

    /// <summary>The newest stored daily bar at or before <paramref name="upTo"/> for each symbol that has one (a symbol with none is missing).</summary>
    ValueTask<IReadOnlyDictionary<string, InstrumentClose>> ClosesAsync(IReadOnlyCollection<string> symbols, DateOnly upTo, CancellationToken cancellationToken);

    /// <summary>The accounts that have an entry in the epoch.</summary>
    ValueTask<IReadOnlyList<Guid>> EntryAccountsAsync(long epochId, CancellationToken cancellationToken);

    /// <summary>Independent totals of one account in the epoch.</summary>
    ValueTask<StoredTotals> TotalsAsync(long epochId, Guid accountId, CancellationToken cancellationToken);
}
