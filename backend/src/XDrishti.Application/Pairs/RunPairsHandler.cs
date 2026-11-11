using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Pairs;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Market;
using XDrishti.Domain.PairResearch;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Pairs;

/// <summary>
/// <c>xd pairs run</c> (P14-13, ADR 0196): for each calendar quarter that touches the dates, takes the 252 sessions of daily closes before it, finds the candidate pairs (same sector, return correlation at or above 0.8, capped), appends every candidate to the test ledger, <b>and only then</b> tests
/// them (Engle-Granger, Benjamini-Hochberg over all candidates, half-life), stores the formation and walks the selected pairs through the pair walker for each variant. The six trading variants are appended to the ledger before anything else is read. The bars are read once per instrument. Re-running
/// the same quarter appends nothing to the ledger and writes nothing new. Research only: nothing selects, plans or orders a pair.
/// </summary>
internal sealed class RunPairsHandler(
    IPairUniverseSource universe,
    IBarQueries dailyBars,
    IBarStore minuteBars,
    XDrishti.Application.Abstractions.Market.History.IBackfillSeams seams,
    ITestLedger ledger,
    IPairResearchStore store,
    IPairSimulator simulator,
    IPairSettings settings,
    ICostSheetStore sheets,
    TimeProvider time,
    ILogger<RunPairsHandler> logger) : ICommandHandler<RunPairsCommand, PairsRunSummary>
{
    public const string InvalidCode = "pairs.invalid";

    private const int DailyLookbackDays = 420;
    private const int TradingLookbackDays = 30;
    private const string LedgerUniverse = "stored bars";

    public async ValueTask<Result<PairsRunSummary>> HandleAsync(RunPairsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var options = PairStudyOptions.Default with { PairNotional = settings.PairNotional, MaxCandidates = settings.MaxCandidates };
        if (command.From > command.To || !options.IsValid || command.Variants is { Count: 0 })
        {
            return Result.Failure<PairsRunSummary>(Error.Validation(InvalidCode, "The dates must be in order, the Pairs settings in range and the variants from the grid."));
        }

        CostSheet? sheet = null;
        if (!string.IsNullOrWhiteSpace(settings.CostSheetId))
        {
            var found = await sheets.FindAsync(settings.CostSheetId, cancellationToken);
            if (found.IsFailure)
            {
                return Result.Failure<PairsRunSummary>(found.Error);
            }

            sheet = found.Value;
        }

        var variants = command.Variants ?? PairVariant.Grid;
        var fills = FillRules.Default;
        var config = PairConfig.Of(options, sheet);
        var lines = new List<string>();

        // The six variants are counted before anything is read or computed; their window is the span of the quarters the run touches, not the dates typed.
        var quarters = PairQuarter.Between(command.From, command.To);
        var spanFrom = quarters[0];
        var spanTo = PairQuarter.EndOf(quarters[^1]);
        foreach (var variant in variants)
        {
            await AppendAsync(PairTrial.Variant(variant, options, spanFrom, spanTo), "pairs-variant:" + variant.Label, spanFrom, spanTo, "pairs-variant", cancellationToken);
        }

        var instruments = (await universe.ListAsync(cancellationToken)).OrderBy(i => i.Symbol, StringComparer.Ordinal).ToList();
        var asmGsm = instruments.Count(i => i.AsmGsm);
        var eligible = instruments.Where(i => !i.AsmGsm).ToList();
        var series = new Dictionary<string, DailySeries>();
        var firstDaily = quarters[0].AddDays(-DailyLookbackDays);
        foreach (var instrument in eligible)
        {
            var rows = await dailyBars.ReadDailyBarsAsync(instrument.Id, firstDaily, quarters[^1], cancellationToken);
            series[instrument.Symbol] = new DailySeries(
                instrument.Symbol, instrument.Sector,
                [.. rows.Where(r => r.Complete).Select(r => new DailyPoint(DateOnly.FromDateTime(IndiaTime.ToIst(r.Ts).DateTime), r.Close, r.Close * r.Volume))]);
        }

        var formations = 0;
        var tradesWritten = 0;
        foreach (var start in quarters)
        {
            var label = PairQuarter.Label(start);
            var built = PairWindow.Build([.. series.Values], start, options);
            if (built.TooFewSessions)
            {
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"{label}: no formation, {built.SessionsAvailable} of {options.FormationSessions} sessions before the quarter"));
                continue;
            }

            var members = built.Members;
            var incomplete = built.Incomplete;
            var window = built.Sessions;
            var candidates = PairUniverse.Candidates([.. members], options);
            var from = window[0];
            var to = window[^1];

            // Ledger first: every candidate is a trial, appended before any test statistic, p-value, half-life or trade of this formation exists.
            foreach (var candidate in candidates.Candidates)
            {
                await AppendAsync(PairTrial.Candidate(candidate.Y, candidate.X, options, from, to), "pairs-formation:" + label, from, to, "pairs-formation", cancellationToken);
            }

            var formation = PairFormation.Form([.. members], candidates.Candidates, options);
            var exclusions = new SortedDictionary<string, int>(StringComparer.Ordinal)
            {
                ["asm_gsm_excluded"] = asmGsm,
                ["below_correlation"] = candidates.BelowCorrelation,
                ["capped"] = candidates.Capped,
                ["correlation_unavailable"] = candidates.CorrelationUnavailable,
                ["incomplete_window"] = incomplete,
                ["no_sector"] = candidates.NoSector,
                ["same_sector_pairs"] = candidates.SameSectorPairs,
            };
            var formationId = PairFormationId.Of(label, from, to, options, members);
            var detail = formation.DetailJson("as of today", "as of today", "unavailable", exclusions);
            var storedNew = await store.AppendFormationAsync(new PairFormationRow(formationId, PairFamilies.Intraday, label, from, to, candidates.Candidates.Count, formation.Tested, formation.Unavailable, formation.Selected.Count, detail), cancellationToken);
            formations += storedNew ? 1 : 0;
            PairsLog.Formed(logger, label, candidates.Candidates.Count, formation.Selected.Count, storedNew);
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"{label}: members {members.Count} (incomplete {incomplete}), candidates {candidates.Candidates.Count} (capped {candidates.Capped}), tested {formation.Tested}, unavailable {formation.Unavailable}, rejected {formation.Rejected} of m {formation.M}, selected {formation.Selected.Count}{(storedNew ? string.Empty : " (already stored)")}"));

            if (formation.Selected.Count == 0)
            {
                continue;
            }

            // The whole quarter is walked whatever dates were typed, so a run is a function of its quarter alone and its trades never overlap another run of the same quarter.
            var tradeFrom = start;
            var tradeTo = PairQuarter.EndOf(start);
            var bySymbol = eligible.ToDictionary(i => i.Symbol, StringComparer.Ordinal);
            var barCache = new Dictionary<string, IReadOnlyList<Domain.Market.Bars.Bar>>(StringComparer.Ordinal);
            foreach (var symbol in formation.Selected.SelectMany(p => new[] { p.Y, p.X }).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                var instant = new DateTimeOffset(start.AddDays(-TradingLookbackDays).ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).ToUniversalTime();
                var end = new DateTimeOffset(tradeTo.AddDays(1).ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).AddSeconds(-1).ToUniversalTime();
                barCache[symbol] = await XDrishti.Application.Market.History.SeamAwareBars.ReadAsync(minuteBars, seams, bySymbol[symbol].Id, instant, end, cancellationToken);
            }

            foreach (var variant in variants)
            {
                var runId = ExtensionFamilies.Sha256Hex(string.Join('\n', "pair-run/1", PairFamilies.Intraday, variant.Label, label, formationId, simulator.WalkerVersion, simulator.FillEngineVersion, config));
                var rows = new List<PairTradeRow>();
                var counters = PairWalkCounters.Zero;
                var notWalked = 0;
                var costsLost = 0;
                foreach (var pair in formation.Selected)
                {
                    var tickY = TickSpec.FromMasterPaise(bySymbol[pair.Y].TickSizePaise);
                    var tickX = TickSpec.FromMasterPaise(bySymbol[pair.X].TickSizePaise);
                    if (tickY.IsFailure || tickX.IsFailure)
                    {
                        notWalked++;
                        continue;
                    }

                    var walked = simulator.Walk(new PairWalkInput(pair, barCache[pair.Y], barCache[pair.X], tickY.Value, tickX.Value, variant, options, fills, tradeFrom, tradeTo));
                    if (walked.IsFailure)
                    {
                        notWalked++;
                        continue;
                    }

                    counters = counters.Plus(walked.Value.Counters);
                    var ordinal = 0;
                    foreach (var gross in walked.Value.Trades)
                    {
                        var t = PairCosting.Apply(gross, sheet, out var lost);
                        costsLost += lost ? 1 : 0;
                        List<string> labels = ["variant:" + variant.Label, "engine:" + simulator.WalkerVersion, "fills:" + simulator.FillEngineVersion, "cfg:" + config, "quarter:" + label, "formation:" + formationId[..16], "turnover_inr:" + t.TurnoverInr.ToString("F2", CultureInfo.InvariantCulture), .. t.Labels];
                        rows.Add(new PairTradeRow(runId, t.PairKey, ++ordinal, DateTimeOffset.FromUnixTimeSeconds(t.EntryEpochSeconds), DateTimeOffset.FromUnixTimeSeconds(t.ExitEpochSeconds), t.Side == PairSide.LongSpread ? "long_spread" : "short_spread", t.ZEntry, t.ZExit, t.ExitReason, t.QtyY, t.QtyX, t.GrossInr, t.NetInr, t.R, labels));
                    }
                }

                var written = rows.Count == 0 ? 0 : await store.AppendTradesAsync(rows, cancellationToken);
                tradesWritten += written;
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"{label} {variant.Label}: trades {rows.Count} (new {written}), {(sheet is null ? "gross" : "net")}, z_unavailable {counters.ZUnavailable}, leg_unfilled {counters.LegUnfilled}, not_sizable {counters.NotSizable}, beyond_stop {counters.BeyondStop}, leg_late {counters.LegLate}, costs_unavailable {costsLost}, not_walked {notWalked}"));
            }
        }

        var trials = Math.Max(1, await ledger.CountVariantsAsync(PairFamilies.Intraday, cancellationToken));
        var factor = MultipleTesting.Factor(trials);
        lines.Insert(0, string.Create(CultureInfo.InvariantCulture, $"ledger: N {trials}, F {factor:F2} (family {PairFamilies.Intraday}); engine {simulator.WalkerVersion} on {simulator.FillEngineVersion} fills; {(sheet is null ? "gross (no owner charge sheet)" : "net of sheet " + sheet.Id)}"));
        lines.Add("limits: ban_list unavailable; sector as of today; asm_gsm as of today (" + asmGsm.ToString(CultureInfo.InvariantCulture) + " flagged shares excluded)");
        PairsLog.RunDone(logger, formations, tradesWritten, trials);
        return Result.Success(new PairsRunSummary(lines, trials, factor, formations, tradesWritten));
    }

    private async ValueTask AppendAsync(ExtensionTrialId trial, string runKey, DateOnly from, DateOnly to, string method, CancellationToken cancellationToken) =>
        _ = await ledger.AppendAsync(new LedgerRow(trial.RunId, runKey, trial.Family, 1, trial.Hash, null, LedgerUniverse, from, to, method, time.GetUtcNow()), cancellationToken);
}
