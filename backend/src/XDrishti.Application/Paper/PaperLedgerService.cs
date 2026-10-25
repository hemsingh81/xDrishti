using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Paper;

namespace XDrishti.Application.Paper;

/// <summary>
/// Builds the derived ledgers of the paper accounts (spec P10-02, ADR 0118) from the stored trades, the newest stored daily closes and the calendar. Shared by the read handlers and the plan's book reader so they cannot disagree.
/// Reads only; nothing is stored, nothing is placed.
/// </summary>
internal sealed class PaperLedgerService(IPaperLedgerData data, IAccountStore accounts)
{
    /// <summary>The Trading-role accounts whose mode is paper now, with the capital the plan sizes them on (Fixed only).</summary>
    public async ValueTask<IReadOnlyList<PaperAccountInput>> PaperAccountsAsync(CancellationToken cancellationToken)
    {
        var active = await accounts.ListActiveAsync(cancellationToken);
        return [.. active
            .Where(a => a.Trading.Enabled && a.Trading.Mode == TradingMode.Paper)
            .OrderBy(a => a.Id)
            .Select(a => a.Trading.CapitalSource == CapitalSource.Fixed && a.Trading.CapitalAmount is { } amount
                ? new PaperAccountInput(a.Id, a.Name, amount, "the account's fixed capital (the amount the plan sizes on)")
                : new PaperAccountInput(a.Id, a.Name, null, "no capital figure: the account takes its funds from the broker, which a paper account cannot read"))];
    }

    /// <summary>The ledgers of <paramref name="inputs"/> in <paramref name="epoch"/> (null: no capture yet, so no trade) as of the last closed session.</summary>
    public async ValueTask<IReadOnlyList<AccountLedger>> BuildAsync(IReadOnlyList<PaperAccountInput> inputs, EpochRow? epoch, DateOnly lastClosed, TradingCalendar calendar, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(calendar);
        var all = epoch is null ? [] : await data.TradesAsync(epoch.Id, null, cancellationToken);
        var byAccount = all.GroupBy(t => t.AccountId).ToDictionary(g => g.Key, g => (IReadOnlyList<LedgerTrade>)[.. g]);
        var held = all.Where(t => !t.Closed && inputs.Any(i => i.AccountId == t.AccountId)).Select(t => t.Symbol).Distinct(StringComparer.Ordinal).ToList();
        var closes = held.Count == 0 ? new Dictionary<string, InstrumentClose>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, InstrumentClose>(await data.ClosesAsync(held, lastClosed, cancellationToken), StringComparer.OrdinalIgnoreCase);

        var result = new List<AccountLedger>(inputs.Count);
        foreach (var input in inputs)
        {
            var trades = byAccount.GetValueOrDefault(input.AccountId) ?? [];
            var figures = PaperLedger.Compute(input.Capital, trades, lastClosed);
            var positions = trades.Where(t => !t.Closed)
                .OrderBy(t => t.EntryAt).ThenBy(t => t.EntryId)
                .Select(t => PaperPosition.Of(
                    t,
                    PaperMark.Of(lastClosed, closes.TryGetValue(t.Symbol, out var c) ? c.Close : null),
                    figures.Equity,
                    Math.Max(1, calendar.TradingDaysBetween(IndiaDate(t).AddDays(-1), lastClosed))))
                .ToList();
            var instruments = positions.Where(p => closes.ContainsKey(p.Trade.Symbol)).Select(p => p.Trade.Symbol).Distinct(StringComparer.Ordinal).ToDictionary(s => s, s => closes[s].InstrumentId, StringComparer.Ordinal);
            result.Add(new AccountLedger(input, trades, figures, positions, instruments));
        }

        return result;
    }

    private static DateOnly IndiaDate(LedgerTrade trade) => XDrishti.Domain.Market.IndiaTime.Today(trade.EntryAt);
}
