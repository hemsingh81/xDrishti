using System.Globalization;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Ai.Proposals;

/// <summary>
/// What the engine knows (spec P9-08 AC 1, 3), read through the stores and handlers the screens use: the newest stored cell snapshot, the stored runs, the hypothesis log, the baskets and the report engine. Read only. A report that cannot answer is <c>unavailable</c>, never a value.
/// A scoped instance keeps what it read for the length of one request or job, so a pack and its checks read each thing once.
/// </summary>
internal sealed class ProposalFacts(
    ICellStatsStore cells,
    IRunStore runs,
    IHypothesisLog hypotheses,
    IBasketQueries baskets,
    IPortfolioBasketSource portfolio,
    IInstrumentQueries instruments,
    IQueryHandler<GetReportGroupsQuery, Result<GroupsView>> groups) : IProposalFacts
{
    private readonly Dictionary<string, RunFact?> _runs = new(StringComparer.Ordinal);
    private IReadOnlyList<InstrumentTradesFact>? _allInstruments;
    private bool _allRead;

    public async ValueTask<IReadOnlyList<CellFact>> CellsAsync(CancellationToken cancellationToken)
    {
        var rows = await cells.ListLatestAsync(null, CellWindow.All.Code(), cancellationToken);
        var list = new List<CellFact>(rows.Count);
        foreach (var row in rows.Where(r => r.Row.Window == CellWindow.All))
        {
            list.Add(await FactOfAsync(row, cancellationToken));
        }

        return list;
    }

    public async ValueTask<CellFact?> CellAsync(string cellKey, CancellationToken cancellationToken)
    {
        var found = await cells.FindLatestAsync(cellKey, cancellationToken);
        var row = found.FirstOrDefault(r => r.Row.Window == CellWindow.All);
        return row is null ? null : await FactOfAsync(row, cancellationToken);
    }

    public async ValueTask<RunFact?> RunAsync(string runKey, CancellationToken cancellationToken)
    {
        if (_runs.TryGetValue(runKey, out var known))
        {
            return known;
        }

        RunFact? fact = null;
        if (RunRecordText.IsHex64(runKey) && await runs.FindAsync(runKey, cancellationToken) is { } header && await runs.ReadRecordTextAsync(runKey, cancellationToken) is { } record
            && RunDefinition.TryRead(record, header.Fills, out _) is { } definition)
        {
            fact = new RunFact(runKey, header.StrategyId, definition);
        }

        _runs[runKey] = fact;
        return fact;
    }

    public async ValueTask<IReadOnlyList<OpenHypothesisFact>> OpenHypothesesAsync(CancellationToken cancellationToken)
    {
        var rows = await hypotheses.ListAsync(null, 200, cancellationToken);
        return [.. rows.Where(r => HypothesisStateText.TryParse(r.Row.State) is { } s && HypothesisLifecycle.IsOpen(s)).Select(r => new OpenHypothesisFact(r.HypothesisId, r.Row.State, r.Row.CellKey, r.Row.KnobName))];
    }

    public async ValueTask<IReadOnlyList<BasketFact>> BasketsAsync(CancellationToken cancellationToken)
    {
        var list = await baskets.ListAsync(true, cancellationToken);
        var result = new List<BasketFact>();
        foreach (var basket in list.Where(b => !b.System).Take(50))
        {
            result.Add(new BasketFact(basket.Id, basket.Name, basket.ArchivedAt is not null, false, await MembersAsync(basket.Id, cancellationToken)));
        }

        return result;
    }

    public async ValueTask<BasketFact?> BasketAsync(Guid id, CancellationToken cancellationToken)
    {
        var basket = await baskets.GetAsync(id, cancellationToken);
        if (basket is not null)
        {
            return new BasketFact(basket.Id, basket.Name, basket.ArchivedAt is not null, basket.System, basket.System ? [] : await MembersAsync(basket.Id, cancellationToken));
        }

        return await portfolio.ExistsAsync(id, cancellationToken) ? new BasketFact(id, string.Empty, false, true, []) : null;
    }

    public async ValueTask<InstrumentFact?> InstrumentAsync(string symbol, CancellationToken cancellationToken)
    {
        var found = await instruments.FindForResolveAsync([symbol.ToUpperInvariant()], [], cancellationToken);
        var match = found.FirstOrDefault(c => string.Equals(c.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
        return match is null ? null : new InstrumentFact(match.Id, match.Symbol, match.Tracked, match.Retired);
    }

    public async ValueTask<InstrumentTradesFact> InstrumentTradesAsync(string symbol, CancellationToken cancellationToken)
    {
        var all = await AllInstrumentTradesAsync(cancellationToken);
        if (all is null)
        {
            return new InstrumentTradesFact(symbol, "unavailable", 0, null, null, null, [], string.Empty, []);
        }

        return all.FirstOrDefault(i => string.Equals(i.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
            ?? new InstrumentTradesFact(symbol, "value", 0, null, null, null, [], string.Empty, []);
    }

    public async ValueTask<IReadOnlyList<InstrumentTradesFact>?> AllInstrumentTradesAsync(CancellationToken cancellationToken)
    {
        if (_allRead)
        {
            return _allInstruments;
        }

        _allRead = true;
        var query = ReportQuery.Parse(new ReportQueryInput(Lens: "instrument"));
        if (query.IsFailure)
        {
            return null;
        }

        var result = await groups.HandleAsync(new GetReportGroupsQuery(query.Value), cancellationToken);
        if (result.IsFailure)
        {
            return null;
        }

        var evidence = result.Value.Evidence;
        _allInstruments =
        [
            .. result.Value.Groups.Select(g =>
            {
                var symbol = g.Parts.FirstOrDefault(p => p.Dimension == "instrument")?.Value ?? string.Empty;
                var lower = g.ExpectancyLowerR;
                var usable = lower.State is "value" or "provisional" && lower.Number is not null;
                return new InstrumentTradesFact(symbol, usable ? "value" : lower.State == "insufficient" ? "insufficient" : "unavailable", g.N, usable ? lower.Number : null, g.Evidence.FirstDate, g.Evidence.LastDate, g.Evidence.RunKeys, evidence.Basis, evidence.Limits);
            }).Where(i => i.Symbol.Length > 0),
        ];
        return _allInstruments;
    }

    private async ValueTask<IReadOnlyList<string>> MembersAsync(Guid basketId, CancellationToken cancellationToken)
    {
        var members = await baskets.MembersAsync(basketId, null, null, cancellationToken);
        return members is null ? [] : [.. members.Items.Where(m => m.ValidTo is null).Select(m => m.Instrument.Symbol.ToUpperInvariant()).Order(StringComparer.Ordinal)];
    }

    private async ValueTask<CellFact> FactOfAsync(StoredCellStats stored, CancellationToken cancellationToken)
    {
        var r = stored.Row;
        string? run = null;
        if (CellSource.TryParseList(stored.Sources) is { } sources)
        {
            foreach (var source in sources.Where(s => s.Style == r.Cell.Style && s.Exit == r.Cell.Exit))
            {
                if ((await RunAsync(source.RunKey, cancellationToken))?.StrategyId == r.Cell.Strategy)
                {
                    run = source.RunKey;
                    break;
                }
            }
        }

        return new CellFact(r.Cell.Key, r.Cell.Strategy, r.N, r.ExpectancyR, r.WinRateLower, r.Enough, stored.Options.MinTrades, r.Costed ? "net R (after charges)" : "gross R (before charges)", r.AsOf, run);
    }

    internal static string Number(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
