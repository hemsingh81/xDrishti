using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market;
using XDrishti.Domain.Paper;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Replay;

/// <summary>
/// Replays one session S of the system (SR-01). It reveals the exits that fall on S, reads the book at the close of S from the replay book, builds the candidates from the as-of facts, runs the <b>same plan builder as the live nightly</b>
/// (<see cref="SelectionPlanBuilder"/>: selection, smart rules, cluster check, portfolio fill, size scaling, wiring) over ports that hand it the as-of facts and the replay account, and books the next session's fills of the taken items through the one replay path
/// (<see cref="TicketReplayer"/>). Equity, open risk and the realised series come only from <see cref="PaperLedger"/>. It calls no gate of the nightly pipeline, no plan store and writes nothing: the book is in memory.
/// </summary>
internal sealed class ReplaySessionRunner(IReplayFacts facts, IReplayCandidates candidates, TicketReplayer replayer)
{
    /// <summary>The label of a note when the next session lies beyond the end of the range.</summary>
    public const string EndOfRangeNote = "end_of_range: no entries after the last replayed session";

    public async ValueTask<Result<ReplaySessionRecord>> RunAsync(ReplayRunContext run, DateOnly session, ReplayBook book, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(book);
        var p = run.Parameters;
        var forDate = ReplayFacts.NextSession(run.Calendar, session);
        var notes = new List<string>();

        // 1. The exits of positions held from earlier sessions that fall on or before S (the replay reads no bar after S).
        foreach (var held in book.Unexited(session).Where(t => t.EntrySession < session))
        {
            var again = await replayer.ReplayAsync(held.ForDate, session, run.Calendar, held.Item, run.Costs, cancellationToken);
            if (again.Trade is { } closed && again.Grade is not null)
            {
                var exit = PaperPnl.Exit(closed, held.Qty, run.Sheet);
                if (exit.IsFailure)
                {
                    return Result.Failure<ReplaySessionRecord>(exit.Error);
                }

                held.Exit = exit.Value;
            }
            else if (again.Trade is null && again.Pending is not null)
            {
                notes.Add("exit_pending: " + held.Item.Symbol);
            }
        }

        var exitsToday = book.Trades.Where(t => t.ExitSession == session).Select(t => t.Item.Symbol).Order(StringComparer.Ordinal).ToList();
        var figures = book.FiguresAsOf(session); // 2. equity, open risk and drawdown: the paper ledger over what is known at S

        // 3. Candidates from the as-of facts, and the share of the universe whose bars are incomplete.
        var set = await candidates.ReadAsync(p, session, forDate, cancellationToken);
        notes.AddRange(set.Notes);
        var labels = HindsightLabels.For(p).ToList();
        if (set.Survivorship)
        {
            labels.Add(HindsightLabels.Survivorship);
        }

        var share = set.Universe == 0 ? 0.0 : set.IncompleteBars / (double)set.Universe;
        var blocked = new List<ReplayBlocked>();
        blocked.AddRange(set.NoVersionCells.Order(StringComparer.Ordinal).Select(c => new ReplayBlocked(string.Empty, c, BlockReason.NoVersion, "no_version")));
        var candidateItems = new Dictionary<(string, string), int>();
        List<ReplayCandidateRow> ranked = [];
        List<ReplayCandidateRow> taken = [];
        List<NewPlanItem> takenItems = [];
        if (share > run.Settings.IncompleteDataShare)
        {
            notes.Add(string.Create(CultureInfo.InvariantCulture, $"data_missing: bars of {set.IncompleteBars} of {set.Universe} instruments are incomplete"));
            blocked.AddRange(set.Found.Select(c => new ReplayBlocked(c.Draft.Symbol, c.Cell.Key, BlockReason.DataMissing, "incomplete_bars")));
        }
        else
        {
            var close = Close(session);
            var builder = new SelectionPlanBuilder(
                new FixedTicketSource(new TicketSourceResult(set.Found, set.Rejected, set.Unavailable, [])),
                new ReplayContextSource(facts, run, book, session),
                NullLogger<SelectionPlanBuilder>.Instance,
                run.Settings.Rules,
                run.Settings.Smart,
                run.Settings.Fill,
                null,
                null,
                new ReplaySizingInputs(facts),
                new SnapshotBasketSettings(run.Settings.Basket));
            var planned = await builder.BuildAsync(new PlanBuildRequest(session, forDate, close, close), cancellationToken);
            if (planned.IsFailure)
            {
                return Result.Failure<ReplaySessionRecord>(planned.Error);
            }

            foreach (var item in planned.Value.Items.OrderBy(i => i.Rank))
            {
                candidateItems.TryAdd((item.Symbol, item.Evidence.CellKey), item.Rank);
                var row = new ReplayCandidateRow(item.Symbol, item.Side, item.Evidence.CellKey, item.Rank);
                if (item.Group == TicketGroup.Primary)
                {
                    if (QuantityOf(item) < 1)
                    {
                        blocked.Add(new ReplayBlocked(item.Symbol, item.Evidence.CellKey, BlockReason.Capital, "no_quantity"));
                        continue;
                    }

                    taken.Add(row);
                    ranked.Add(row);
                    takenItems.Add(item);
                }
                else
                {
                    if (item.Group == TicketGroup.Reserve)
                    {
                        ranked.Add(row);
                    }

                    blocked.Add(BlockReasonMap.Of(item));
                }
            }
        }

        // 4. The fills of the taken items on the next session, through the one replay path. Past the end of the range nothing is read.
        var fills = new List<ReplayFill>();
        if (forDate > run.LastAllowed)
        {
            notes.Add(EndOfRangeNote);
        }
        else
        {
            foreach (var item in takenItems)
            {
                fills.Add(await FillAsync(run, book, item, set, forDate, cancellationToken));
            }
        }

        var candidatesRows = set.Found
            .OrderBy(c => c.Draft.Symbol, StringComparer.Ordinal).ThenBy(c => c.Cell.Key, StringComparer.Ordinal)
            .Select(c => new ReplayCandidateRow(c.Draft.Symbol, c.Draft.Side, c.Cell.Key, candidateItems.GetValueOrDefault((c.Draft.Symbol, c.Cell.Key))))
            .ToList();
        return Result.Success(new ReplaySessionRecord(
            session, candidatesRows, ranked, taken, blocked, fills, exitsToday,
            figures.Equity ?? 0m, figures.MaxDrawdownPct, figures.OpenRiskPct, figures.OpenCount, labels, notes));
    }

    private async ValueTask<ReplayFill> FillAsync(ReplayRunContext run, ReplayBook book, NewPlanItem item, ReplayCandidateSet set, DateOnly forDate, CancellationToken cancellationToken)
    {
        var qty = QuantityOf(item);
        var replay = await replayer.ReplayAsync(forDate, forDate, run.Calendar, item, run.Costs, cancellationToken);
        ReplayFill None(string status, string detail) => new(item.Symbol, item.Side, item.Evidence.CellKey, forDate, status, 0, null, detail);
        if (replay.Trade is null)
        {
            return replay.Grade is { } grade ? None(ReplayFill.NotTriggered, grade.Detail) : None(ReplayFill.Pending, replay.Pending ?? "replay_pending");
        }

        var trade = replay.Trade;
        if (trade.Legs is not null)
        {
            return None(ReplayFill.Pending, "The trade has several legs; only single-fill trades are booked.");
        }

        var cell = CellId.TryParse(item.Evidence.CellKey);
        var entryAt = DateTimeOffset.FromUnixTimeSeconds(trade.EntryEpochSeconds);
        var entry = PaperPnl.Price(trade.EntryFilledPrice);
        var booked = new ReplayTrade(
            book.Trades.Count + 1, item, forDate, qty, set.Found.FirstOrDefault(c => c.Draft.Symbol == item.Symbol && c.Cell.Key == item.Evidence.CellKey)?.InstrumentId, entryAt, entry, PaperPnl.Price(trade.StopPrice),
            string.Equals(cell?.Style, "swing", StringComparison.OrdinalIgnoreCase), trade.Key);
        if (replay.Grade is not null)
        {
            var exit = PaperPnl.Exit(trade, qty, run.Sheet);
            if (exit.IsFailure)
            {
                return None(ReplayFill.Pending, exit.Error.Code);
            }

            booked.Exit = exit.Value;
        }

        book.Add(booked);
        return new ReplayFill(item.Symbol, item.Side, item.Evidence.CellKey, IndiaTime.Today(entryAt), ReplayFill.Filled, qty, entry, string.Empty);
    }

    private static int QuantityOf(NewPlanItem item) => item.Allocations.FirstOrDefault(a => a.AccountId == ReplayBook.AccountId)?.Quantity ?? 0;

    private static DateTimeOffset Close(DateOnly session) => new DateTimeOffset(session.ToDateTime(new TimeOnly(15, 30)), TimeSpan.FromHours(5.5));
}
