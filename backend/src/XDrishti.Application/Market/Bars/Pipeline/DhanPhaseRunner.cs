using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Market.Bars.Csv;
using XDrishti.Application.Market.Bars.Dhan;
using XDrishti.Application.Market.Bars.Gaps;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Market.Bars.Pipeline;

/// <summary>
/// The Dhan phase (ADR 0016 §2, §7): asks the <see cref="IIntradayBarSource"/> for the 1-minute bars each tracked instrument is missing (from two days before its
/// last stored bar, or <c>Data:Dhan:InitialDays</c> back, in windows of at most 90 days), checks them (<see cref="IntradayBarCheck"/>), compares the overlap with
/// the stored bars and merges them (Dhan overwrites CSV). It also backfills gaps (ADR 0017 §3, <see cref="GapBackfill"/>): sessions of the last <c>Data:Gaps:LookbackSessions</c> trading
/// days with fewer 1-minute bars than the calendar expects are refetched in extra windows and end up filled or unfillable. Skipped with a reason — never failing the job — when there is no connected primary Data account or the
/// source is not available; a refused token ends the phase (<c>data.dhan_not_connected</c>); one instrument failing does not stop the others.
/// </summary>
internal sealed class DhanPhaseRunner(
    IIntradayBarSource source,
    IDataAccountStatus account,
    IFetchTargets targets,
    IBarStore bars,
    IDataSettings settings,
    ITradingCalendar tradingCalendar,
    GapBackfill gaps,
    TimeProvider clock,
    ILogger<DhanPhaseRunner> logger)
{
    private const string NoTargets = "No tracked share, ETF or index has a Dhan id: nothing to fetch.";

    public async ValueTask<DhanPhase> RunAsync(JobReporter reporter, CancellationToken cancellationToken)
    {
        var phase = DhanPhase.Empty(PhaseStatus.Running) with { StartedAt = clock.GetUtcNow() };
        await reporter.ReportAsync(phase, cancellationToken, publishNow: true);

        var availability = await source.CheckAsync(cancellationToken);
        if (!availability.Available)
        {
            return End(phase, PhaseStatus.Skipped, DataErrors.DhanNotAvailableCode, availability.Reason ?? "The Dhan Data API fetch is not available.");
        }

        var state = await account.GetAsync(cancellationToken);
        if (!state.CanFetch)
        {
            return End(phase with { AccountName = state.Name }, PhaseStatus.Skipped, DataErrors.DhanNotConnectedCode, state.Reason ?? "There is no connected primary Data account.");
        }

        var list = await targets.ListAsync(cancellationToken);
        if (reporter.Job.InstrumentId is { } only)
        {
            list = [.. list.Where(t => t.InstrumentId == only)];
        }

        if (list.Count == 0)
        {
            return End(phase with { AccountName = state.Name }, PhaseStatus.Skipped, null, NoTargets);
        }

        var calendar = await tradingCalendar.GetAsync(cancellationToken);
        var gapPlans = await PlanGapsAsync(calendar, list, reporter, cancellationToken);
        var plan = DhanFetchPlanner.Plan(list, clock.GetUtcNow(), settings.DhanInitialDays, gapPlans.ToDictionary(p => p.Key, p => p.Value.Windows), settings.DhanInitialDaysIndex);
        phase = phase with { Total = list.Count, AccountName = state.Name };
        await reporter.ReportAsync(phase, cancellationToken, publishNow: true);

        var byId = list.ToDictionary(t => t.InstrumentId);
        var errors = new List<string>();
        await foreach (var result in source.FetchAsync(plan, cancellationToken))
        {
            if (reporter.Ended)
            {
                break;
            }

            if (!byId.TryGetValue(result.InstrumentId, out var target))
            {
                continue;
            }

            phase = phase with { Requests = phase.Requests + result.Requests };
            if (result.Failure is { Kind: IntradayFailureKind.Unauthorized })
            {
                return End(
                    Errors(phase, errors),
                    PhaseStatus.Failed,
                    DataErrors.DhanNotConnectedCode,
                    "Dhan refused the access token of the Data account. Reconnect it under Accounts, then run again.");
            }

            phase = result.Failure is { } failure
                ? Failed(phase, errors, target, failure.Message)
                : await StoreAsync(phase, errors, reporter, target, result, gapPlans.GetValueOrDefault(target.InstrumentId), cancellationToken);
            await reporter.ReportAsync(Errors(phase, errors), cancellationToken);
        }

        phase = Errors(phase, errors);
        var remaining = phase.GapsFound - phase.GapsFilled - phase.GapsUnfillable;
        if (remaining > 0)
        {
            await reporter.WarnAsync($"{remaining} sessions still have missing bars after the fetch; the next update asks for them again.", cancellationToken);
        }

        if (phase.InstrumentsFetched == 0 && phase.InstrumentsFailed > 0)
        {
            return End(phase, PhaseStatus.Failed, null, $"Dhan returned errors for all {phase.InstrumentsFailed} instruments: {errors[0]}");
        }

        return End(phase, PhaseStatus.Succeeded, null, phase.InstrumentsFailed > 0 ? $"{phase.InstrumentsFailed} of {phase.Total} instruments could not be fetched: {errors[0]}" : null);
    }

    private async ValueTask<DhanPhase> StoreAsync(
        DhanPhase phase, List<string> errors, JobReporter reporter, FetchTarget target, IntradayResult result, GapPlan? gapPlan, CancellationToken cancellationToken)
    {
        try
        {
            var checkedBars = IntradayBarCheck.Check(result.Bars);
            var overlap = default(OverlapResult);
            if (target.LastBar is { } lastStored && checkedBars.Bars.Count > 0)
            {
                var from = checkedBars.Bars[0].Start;
                var to = lastStored < checkedBars.Bars[^1].Start ? lastStored : checkedBars.Bars[^1].Start;
                if (from <= to)
                {
                    overlap = OverlapComparison.Compare(checkedBars.Bars, await bars.ReadAsync(target.InstrumentId, from, to, cancellationToken));
                }
            }

            await using var session = await bars.BeginMergeAsync(target.InstrumentId, BarSource.Dhan, cancellationToken);
            var all = checkedBars.Bars.ToArray();
            for (var offset = 0; offset < all.Length; offset += CsvBarReader.BatchSize)
            {
                await session.AddAsync(all.AsMemory(offset, Math.Min(CsvBarReader.BatchSize, all.Length - offset)), cancellationToken);
            }

            var merge = await session.CompleteAsync(null, cancellationToken);
            DataLog.DhanInstrumentFetched(logger, target.Symbol, result.Bars.Count, merge.Inserted, merge.Updated, checkedBars.Rejected);
            if (overlap.Mismatched > 0)
            {
                DataLog.DhanOverlapMismatch(logger, target.Symbol, overlap.Mismatched, overlap.Compared);
                await reporter.WarnAsync($"{target.Symbol}: {overlap.Mismatched} of {overlap.Compared} overlapping bars differ from the stored ones by more than 0.05 %.", cancellationToken);
            }

            if (checkedBars.Rejected > 0)
            {
                await reporter.WarnAsync($"{target.Symbol}: {checkedBars.Rejected} bars from Dhan were not stored (for example {checkedBars.RejectionExample}).", cancellationToken);
            }

            var outcome = await CompleteGapsAsync(target, gapPlan, reporter, cancellationToken);
            return phase with
            {
                GapsFound = phase.GapsFound + outcome.Found,
                GapsFilled = phase.GapsFilled + outcome.Filled,
                GapsUnfillable = phase.GapsUnfillable + outcome.Unfillable,
                Done = phase.Done + 1,
                InstrumentsFetched = phase.InstrumentsFetched + 1,
                Bars = phase.Bars + result.Bars.Count,
                Inserted = phase.Inserted + merge.Inserted,
                Updated = phase.Updated + merge.Updated,
                Unchanged = phase.Unchanged + merge.Unchanged,
                Repaired = phase.Repaired + checkedBars.Repaired,
                Rejected = phase.Rejected + checkedBars.Rejected,
                OverlapCompared = phase.OverlapCompared + overlap.Compared,
                OverlapMismatched = phase.OverlapMismatched + overlap.Mismatched,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // one instrument failing (for example the database refusing its bars) must not stop the others; it is recorded as failed and logged
        catch (Exception ex)
#pragma warning restore CA1031
        {
            DataLog.DhanInstrumentCrashed(logger, target.Symbol, ex);
            return Failed(phase, errors, target, "the bars could not be stored");
        }
    }

    /// <summary>Finds the missing bars of every target before anything is fetched. One instrument whose gaps cannot be worked out is fetched as usual (and the owner is told once).</summary>
    private async ValueTask<Dictionary<Guid, GapPlan>> PlanGapsAsync(
        TradingCalendar calendar, IReadOnlyList<FetchTarget> targets, JobReporter reporter, CancellationToken cancellationToken)
    {
        var plans = new Dictionary<Guid, GapPlan>();
        var failed = 0;
        foreach (var target in targets)
        {
            try
            {
                var plan = await gaps.PlanAsync(calendar, target, reporter.Job.RefetchGaps, cancellationToken);
                if (plan.Items.Count > 0 || plan.Deferred.Count > 0)
                {
                    plans[target.InstrumentId] = plan;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // gap detection is an extra: the instrument is fetched as usual and the next update looks again
            catch (Exception ex)
#pragma warning restore CA1031
            {
                DataLog.GapPlanFailed(logger, target.Symbol, ex);
                failed++;
            }
        }

        if (failed > 0)
        {
            await reporter.WarnAsync($"Missing bars could not be looked for in {failed} instruments; they were fetched as usual.", cancellationToken);
        }

        return plans;
    }

    /// <summary>Decides what became of the gaps of a fetched instrument. A failure here must not fail the instrument: its bars are stored; the next update looks again.</summary>
    private async ValueTask<GapOutcome> CompleteGapsAsync(FetchTarget target, GapPlan? gapPlan, JobReporter reporter, CancellationToken cancellationToken)
    {
        if (gapPlan is null)
        {
            return GapOutcome.None;
        }

        try
        {
            return await gaps.CompleteAsync(target.InstrumentId, gapPlan, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // the bars are stored; only the gap records are behind
        catch (Exception ex)
#pragma warning restore CA1031
        {
            DataLog.GapCompleteFailed(logger, target.Symbol, ex);
            await reporter.WarnAsync($"{target.Symbol}: the missing bars could not be recorded.", cancellationToken);
            return GapOutcome.None;
        }
    }

    private DhanPhase Failed(DhanPhase phase, List<string> errors, FetchTarget target, string message)
    {
        DataLog.DhanInstrumentFailed(logger, target.Symbol, message);
        if (errors.Count < DhanPhase.MaxErrors)
        {
            errors.Add($"{target.Symbol}: {message}");
        }

        return phase with { Done = phase.Done + 1, InstrumentsFailed = phase.InstrumentsFailed + 1 };
    }

    private static DhanPhase Errors(DhanPhase phase, List<string> errors) => phase with { Errors = [.. errors] };

    private DhanPhase End(DhanPhase phase, string status, string? code, string? message) =>
        phase with { Status = status, Code = code, Message = message, FinishedAt = clock.GetUtcNow() };
}
