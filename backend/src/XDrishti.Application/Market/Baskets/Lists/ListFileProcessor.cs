using System.Text;
using System.Text.Json;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Baskets;
using XDrishti.Domain.Market.Baskets.Lists;

namespace XDrishti.Application.Market.Baskets;

/// <summary>What the processor decided for one NSE list file: a rejection (nothing applied), a membership change (the ban list) or the lot rows to store (the lot file); the fetch row carries the counts and warnings.</summary>
internal sealed record ListPlan(
    FetchRecord Fetch, string? Reject, MembershipChange? Change = null, IReadOnlyList<Guid>? Order = null, DateTimeOffset Effective = default, ListOrigin? Origin = null, DateOnly? TradeDate = null);

/// <summary>
/// Reads the two NSE F&amp;O files (BK-05) into a plan without writing anything: <c>fo_secban.csv</c> into the ban list's membership dated by the trade date in its first line, <c>fo_mktlots.csv</c> into a summary and the cross-checks against the master (nothing is stored but the fetch record; a lot table is deferred to the owner's question 5 of the spec review)
/// (a mismatch and a universe difference are warnings; the master stays primary). The caller applies the plan through the same fail-closed steps as the index lists.
/// </summary>
internal sealed class ListFileProcessor(IBasketSourceStore sources, IBasketStore baskets, IListMasterReader master)
{
    public const string StaleTradeDate = "stale_trade_date";
    public const int MaxWarnings = 200;

    public async ValueTask<ListPlan> PlanAsync(BasketSourceInfo source, byte[] body, FetchRecord fetch, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(body);
        var text = Encoding.UTF8.GetString(body);
        return source.Format == ListBaskets.BanFormat
            ? await BanAsync(source, text, fetch, now, cancellationToken)
            : await LotsAsync(source, text, fetch, now, cancellationToken);
    }

    private async ValueTask<ListPlan> BanAsync(BasketSourceInfo source, string text, FetchRecord fetch, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var file = BanListParser.Parse(text);
        if (!file.Ok)
        {
            return new ListPlan(fetch, file.Reason);
        }

        fetch = fetch with { Rows = file.Symbols.Count };
        var today = IndiaTime.Today(now);
        var date = file.TradeDate!.Value;
        if (file.Symbols.Count < source.ExpectedMin || file.Symbols.Count > source.ExpectedMax)
        {
            return new ListPlan(fetch, FetchGuard.RowCount);
        }

        if (date < today.AddDays(-5) || date > today.AddDays(7))
        {
            return new ListPlan(fetch, BanListParser.BadDate);
        }

        var found = await sources.FindInstrumentsAsync([], file.Symbols, cancellationToken);
        var bySymbol = found.GroupBy(i => i.Symbol, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.OrderBy(c => c.RetiredAt is not null).First(), StringComparer.OrdinalIgnoreCase);
        var ids = new List<Guid>();
        var warnings = new List<string>();
        var unmatched = 0;
        foreach (var symbol in file.Symbols)
        {
            if (bySymbol.TryGetValue(symbol, out var hit))
            {
                ids.Add(hit.Id);
            }
            else
            {
                unmatched++;
                warnings.Add($"unmatched:{symbol}");
            }
        }

        if (source.KnownThrough is { } held && date < held)
        {
            return new ListPlan(fetch, StaleTradeDate); // an older file than the last one applied (an empty list counts: the date is the source's)
        }

        var open = await baskets.ListActiveMembersAsync(source.BasketId, cancellationToken);

        var change = MembershipDiff.Compute([.. open.Select(m => m.InstrumentId)], ids);
        fetch = fetch with { Unmatched = unmatched, Added = change.Added.Count, Removed = change.Removed.Count, WarningsJson = Json(warnings) };
        var effective = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).ToUniversalTime(); // the trade date NSE states
        return new ListPlan(fetch, null, change, ids, effective, new ListOrigin(BasketProvenance.Fetched, "fetch", new Dictionary<Guid, string?>(), fetch.Id), date);
    }

    private async ValueTask<ListPlan> LotsAsync(BasketSourceInfo source, string text, FetchRecord fetch, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var today = IndiaTime.Today(now);
        var file = LotSizeParser.Parse(text, today);
        if (!file.Ok)
        {
            return new ListPlan(fetch, file.Reason);
        }

        var shares = (await master.ReadSharesAsync(today, cancellationToken)).Where(s => !s.Flags.Retired).GroupBy(s => s.Symbol, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var shareRows = file.Rows.Where(r => shares.ContainsKey(r.Symbol)).ToList();
        fetch = fetch with { Rows = shareRows.Count };
        if (shareRows.Count < source.ExpectedMin || shareRows.Count > source.ExpectedMax)
        {
            return new ListPlan(fetch, FetchGuard.RowCount);
        }

        var warnings = new List<string> { $"index_rows_skipped:{file.Rows.Count - shareRows.Count}" };
        var nse = shareRows.ToDictionary(r => r.Symbol, r => r.LotSize, StringComparer.OrdinalIgnoreCase);
        var masterLots = shares.Where(s => s.Value.MasterLot is not null).ToDictionary(s => s.Key, s => s.Value.MasterLot!.Value, StringComparer.OrdinalIgnoreCase);
        warnings.AddRange(LotChecks.Mismatches(nse, masterLots).Select(m => $"lot_size_mismatch:{m.Symbol}:master={m.Master}:nse={m.Nse}"));
        var diff = LotChecks.UniverseDiff(nse.Keys, shares.Values.Where(s => s.Flags.FnoEligible).Select(s => s.Symbol));
        if (diff.Differs)
        {
            warnings.Add($"fo_universe_diff:onlyMaster={diff.OnlyMaster.Count},onlyNse={diff.OnlyNse.Count}");
            warnings.AddRange(diff.OnlyMaster.Select(s => $"only_master:{s}"));
            warnings.AddRange(diff.OnlyNse.Select(s => $"only_nse:{s}"));
        }

        var mismatches = LotChecks.Mismatches(nse, masterLots).Count;
        warnings.Insert(0, $"lots_summary:shares={shareRows.Count},nearest={shareRows.FirstOrDefault()?.ExpiryLabel},mismatches={mismatches},withoutLot={file.RowsWithoutLot}");
        fetch = fetch with { WarningsJson = Json(warnings) };
        return new ListPlan(fetch, null);
    }

    private static string Json(List<string> warnings) =>
        JsonSerializer.Serialize(warnings.Count <= MaxWarnings ? warnings : [.. warnings.Take(MaxWarnings), $"…and {warnings.Count - MaxWarnings} more"]);
}
