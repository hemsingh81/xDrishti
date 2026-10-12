using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Bars.Csv;
using XDrishti.Application.Market.Bars.Dhan;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Bars.Pipeline;

/// <summary>
/// The Dhan phase (ADR 0016 §2, §7): asks the <see cref="IIntradayBarSource"/> for the 1-minute bars each tracked instrument is missing (from two days before its
/// last stored bar, or <c>Data:Dhan:InitialDays</c> back, in windows of at most 90 days), checks them (<see cref="IntradayBarCheck"/>), compares the overlap with
/// the stored bars and merges them (Dhan overwrites CSV). Skipped with a reason — never failing the job — when there is no connected primary Data account or the
/// source is not available; a refused token ends the phase (<c>data.dhan_not_connected</c>); one instrument failing does not stop the others.
/// </summary>
internal sealed class DhanPhaseRunner(
    IIntradayBarSource source,
    IDataAccountStatus account,
    IFetchTargets targets,
    IBarStore bars,
    IDataSettings settings,
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
        if (list.Count == 0)
        {
            return End(phase with { AccountName = state.Name }, PhaseStatus.Skipped, null, NoTargets);
        }

        var plan = DhanFetchPlanner.Plan(list, clock.GetUtcNow(), settings.DhanInitialDays);
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
                : await StoreAsync(phase, errors, reporter, target, result, cancellationToken);
            await reporter.ReportAsync(Errors(phase, errors), cancellationToken);
        }

        phase = Errors(phase, errors);
        if (phase.InstrumentsFetched == 0 && phase.InstrumentsFailed > 0)
        {
            return End(phase, PhaseStatus.Failed, null, $"Dhan returned errors for all {phase.InstrumentsFailed} instruments: {errors[0]}");
        }

        return End(phase, PhaseStatus.Succeeded, null, phase.InstrumentsFailed > 0 ? $"{phase.InstrumentsFailed} of {phase.Total} instruments could not be fetched: {errors[0]}" : null);
    }

    private async ValueTask<DhanPhase> StoreAsync(
        DhanPhase phase, List<string> errors, JobReporter reporter, FetchTarget target, IntradayResult result, CancellationToken cancellationToken)
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

            return phase with
            {
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
