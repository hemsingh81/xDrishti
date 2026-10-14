using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.BrokerHistory;

/// <summary>Builds the response shapes of the broker history. The wire names of the enums are the lower-case names.</summary>
internal static class BrokerHistoryViews
{
    public static BrokerSyncRunView Run(BrokerSyncRun run) => new(
        run.Id,
        run.AccountId,
        Name(run.Kind),
        Name(run.Trigger),
        run.Status.ToString().ToLowerInvariant(),
        run.FromDate,
        run.ToDate,
        run.StartedAt,
        run.FinishedAt,
        run.Counts,
        run.Error);

    public static string Name(BrokerSyncKind kind) => kind.ToString().ToLowerInvariant();

    public static string Name(BrokerSyncTrigger trigger) => trigger.ToString().ToLowerInvariant();

    public static LedgerEntryView Entry(LedgerEntry entry) => new(
        entry.Id,
        entry.EntryDate,
        entry.VoucherNo,
        entry.Description,
        entry.Narration,
        entry.Exchange,
        entry.Amount,
        LedgerClasses.Code(entry.EntryClass),
        entry.RunningBalance);

    /// <summary>Every class in the listed order, with zeros for the ones without entries.</summary>
    public static IReadOnlyList<LedgerClassTotal> Totals(IReadOnlyList<LedgerTotal> totals) =>
        [.. LedgerClasses.All.Select(c => totals.FirstOrDefault(t => t.Class == c) is { } found ? new LedgerClassTotal(LedgerClasses.Code(c), found.Count, found.Amount) : new LedgerClassTotal(LedgerClasses.Code(c), 0, 0m))];
}
