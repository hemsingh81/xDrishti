using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Scanning;

namespace XDrishti.Infrastructure.Planning;

/// <summary>
/// The tickets of the next session from stored data (spec P7-04, ADR 0088): the newest cell statistics before the plan date, the latest stored version of each cell's strategy, the members of the baskets now and their 1-minute bars up
/// to the close of the session. Each instrument's bars are read once and every cell is classified on them by the pure <see cref="TicketBuilder"/>. Read only; nothing here talks to a broker.
/// </summary>
internal sealed class StoredTicketSource(
    ICellStatsStore stats,
    ILearningChangelog changelog,
    IStrategyStore strategies,
    IBasketQueries baskets,
    IBarStore bars,
    ITradingCalendar tradingCalendar) : ITicketSource
{
    /// <summary>Calendar days of bars read before the close: about 270 sessions, enough for a 200-day average.</summary>
    public const int LookbackDays = 400;

    private const int MaxReasons = 20;

    private static readonly TimeSpan IndiaOffset = TimeSpan.FromHours(5.5);

    public async ValueTask<Result<TicketSourceResult>> ReadAsync(DateOnly session, DateOnly forDate, CancellationToken cancellationToken)
    {
        var rows = await stats.ListLatestBeforeAsync(forDate, null, CellWindow.All.Code(), cancellationToken);
        if (rows.Count == 0)
        {
            return Result.Success(new TicketSourceResult([], 0, 0, []));
        }

        var cut = new DateTimeOffset(forDate.ToDateTime(TimeOnly.MinValue), IndiaOffset);
        var cells = new List<(StoredCellStats Stored, CellState? State, PreparedTicketStrategy Strategy)>();
        var versions = new Dictionary<string, StoredVersion?>(StringComparer.Ordinal);
        var reasons = new SortedSet<string>(StringComparer.Ordinal);
        var unavailable = 0;
        foreach (var stored in rows.OrderBy(r => r.Row.Cell.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cell = stored.Row.Cell;
            if (!versions.TryGetValue(cell.Strategy, out var version))
            {
                version = await strategies.GetVersionAsync(cell.Strategy, null, cancellationToken);
                versions[cell.Strategy] = version;
            }

            if (version is null || !Timeframes.TryParse(cell.Timeframe, out var timeframe) || !TrySide(cell.Side, out var side))
            {
                unavailable++;
                AddReason(reasons, "strategy or cell not usable: " + cell.Strategy);
                continue;
            }

            // Read, hash-checked and compiled once for the cell, then used for every instrument.
            var prepared = PreparedTicketStrategy.Prepare(version.SourceText, version.Hash, timeframe, side, cell.Style, cell.Exit);
            if (prepared.Problem is { } problem)
            {
                unavailable++;
                AddReason(reasons, problem);
                continue;
            }

            var history = await changelog.HistoryAsync(cell.Key, cancellationToken);
            cells.Add((stored, CellStateAsOf.Of(history, cut), prepared));
        }

        var members = await MembersAsync(cancellationToken);
        var calendar = await tradingCalendar.GetAsync(cancellationToken);
        var to = new DateTimeOffset(session.AddDays(1).ToDateTime(TimeOnly.MinValue), IndiaOffset);
        var from = to.AddDays(-LookbackDays);
        var found = new List<TicketCandidate>();
        var rejected = 0;
        foreach (var (id, symbol) in members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var minutes = await bars.ReadAsync(id, from, to, cancellationToken);
            var prepared = TicketBars.Prepare(minutes, session, calendar); // truncated and sorted once for every cell of the instrument
            foreach (var (stored, state, strategy) in cells)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var draft = TicketBuilder.Build(strategy, prepared, symbol, session, forDate, calendar);
                switch (draft.Verdict)
                {
                    case TicketVerdict.Conditional:
                        found.Add(new TicketCandidate(draft, stored.Row.Cell, stored.Row, state, id));
                        break;
                    case TicketVerdict.Rejected:
                        rejected++;
                        break;
                    default:
                        unavailable++;
                        AddReason(reasons, Generic(draft.Reason));

                        break;
                }
            }
        }

        return Result.Success(new TicketSourceResult(found, rejected, unavailable, [.. reasons]));
    }

    private async Task<List<(Guid Id, string Symbol)>> MembersAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, string>();
        foreach (var basket in await baskets.ListAsync(false, cancellationToken))
        {
            if (await baskets.MembersAsync(basket.Id, null, null, cancellationToken) is { } members)
            {
                foreach (var m in members.Items.Where(m => m.ValidTo is null))
                {
                    result.TryAdd(m.Instrument.Id, m.Instrument.Symbol);
                }
            }
        }

        return [.. result.OrderBy(p => p.Value, StringComparer.Ordinal).Select(p => (p.Key, p.Value))];
    }

    private static void AddReason(SortedSet<string> reasons, string reason)
    {
        if (reasons.Count < MaxReasons)
        {
            reasons.Add(reason);
        }
    }

    private static bool TrySide(string text, out Side side)
    {
        side = text == "SELL" ? Side.Sell : Side.Buy;
        return text is "BUY" or "SELL";
    }

    /// <summary>The reason without the dates and path lists, so the same cause is one line.</summary>
    private static string Generic(string reason) => reason.Contains("not ready", StringComparison.Ordinal) ? "history not ready at the close"
        : reason.Contains("not complete", StringComparison.Ordinal) ? "session not complete in the bars"
        : reason;
}
