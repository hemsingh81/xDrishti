using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Actions;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.History;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Market.Bars.Dhan;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.History;

namespace XDrishti.Application.Market.History;

/// <summary>Probes the seam of one instrument, or of up to <c>Data:History:Probe:Instruments</c> (every instrument with an applied action first).</summary>
public sealed record ProbeSeamCommand(Guid? InstrumentId = null) : ICommand;

/// <summary>One line per probed instrument; <see cref="Lines"/> is the text for the owner.</summary>
public sealed record ProbeSeamSummary(int Probed, int Passed, int Unresolved, int NoOverlap, int Failed, int Requests, IReadOnlyList<string> Lines)
{
    public string Text => string.Create(CultureInfo.InvariantCulture, $"{Probed} probed: {Passed} passed, {Unresolved} seam_unresolved, {NoOverlap} without overlap, {Failed} not fetched, {Requests} requests");
}

/// <summary>
/// The overlap probe (CF-04 Part B, decision 9): answers with data whether Dhan's candles are on the CSV basis. For each chosen instrument it fetches Dhan's 1-minute bars of the first sessions <b>inside</b> the CSV span into memory (never written: the only thing stored is the
/// instrument's seam row), adjusts them as if they lay before the seam (every applied action after the bar's date), adjusts the CSV bars by the CSV rule and compares the closes (<see cref="SeamProbe"/>). Pass: the instrument's pre-seam bars are trusted for
/// backfilling; fail: <c>seam_unresolved</c>, and the instrument is not backfilled. OFF with the backfill: with <c>Data:History:Backfill:Enabled</c> off nothing is requested.
/// </summary>
internal sealed class ProbeSeamHandler(
    IBackfillSettings settings,
    IIntradayBarSource source,
    IDataAccountStatus account,
    IFetchTargets targets,
    IBarStore bars,
    IBackfillStore store,
    ICorporateActionQueries actionQueries,
    IAdjustmentFactors adjustments,
    TimeProvider clock,
    ILogger<ProbeSeamHandler> logger) : ICommandHandler<ProbeSeamCommand, ProbeSeamSummary>
{
    private const int ReadDays = 60;

    public async ValueTask<Result<ProbeSeamSummary>> HandleAsync(ProbeSeamCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!settings.Enabled)
        {
            return Result.Failure<ProbeSeamSummary>(BackfillErrors.Disabled());
        }

        var list = await targets.ListAsync(cancellationToken);
        if (command.InstrumentId is { } only)
        {
            list = [.. list.Where(t => t.InstrumentId == only)];
            if (list.Count == 0)
            {
                return Result.Failure<ProbeSeamSummary>(BackfillErrors.NoTarget());
            }
        }

        var availability = await source.CheckAsync(cancellationToken);
        if (!availability.Available)
        {
            return Result.Failure<ProbeSeamSummary>(Error.Conflict(DataErrors.DhanNotAvailableCode, availability.Reason ?? "The Dhan Data API fetch is not available."));
        }

        var state = await account.GetAsync(cancellationToken);
        if (!state.CanFetch)
        {
            return Result.Failure<ProbeSeamSummary>(Error.Conflict(DataErrors.DhanNotConnectedCode, state.Reason ?? "There is no connected primary Data account."));
        }

        var chosen = await ChooseAsync(list, command.InstrumentId is not null, cancellationToken);
        var limits = new SeamProbeLimits(settings.ProbeMedianBps, settings.ProbeMaxBps, settings.ProbeMinWithinMaxPct);
        var counts = new int[5]; // passed, unresolved, no overlap, failed, requests
        var lines = new List<string>();
        foreach (var (target, factors) in chosen)
        {
            var outcome = await ProbeOneAsync(target, factors, limits, cancellationToken);
            counts[4] += outcome.Requests;
            if (outcome.Unauthorized)
            {
                lines.Add("Dhan refused the access token of the Data account; the probe stopped.");
                break;
            }

            counts[outcome.Slot]++;
            lines.Add(outcome.Line);
        }

        return Result.Success(new ProbeSeamSummary(lines.Count, counts[0], counts[1], counts[2], counts[3], counts[4], lines));
    }

    // Every instrument with an applied action first (their seams are the ones that can be wrong), then the others, up to the setting; a named instrument is always probed.
    private async ValueTask<List<(FetchTarget Target, AdjustmentFactors Factors)>> ChooseAsync(IReadOnlyList<FetchTarget> list, bool named, CancellationToken cancellationToken)
    {
        var all = new List<(FetchTarget, AdjustmentFactors)>(list.Count);
        foreach (var target in list)
        {
            all.Add((target, await adjustments.GetAsync(target.InstrumentId, cancellationToken)));
        }

        if (named)
        {
            return all;
        }

        var withActions = all.Where(x => x.Item2.Actions.Count > 0).ToList();
        var others = all.Where(x => x.Item2.Actions.Count == 0).Take(Math.Max(0, settings.ProbeInstruments - withActions.Count));
        return [.. withActions, .. others];
    }

    private async ValueTask<ProbeOutcome> ProbeOneAsync(FetchTarget target, AdjustmentFactors factors, SeamProbeLimits limits, CancellationToken cancellationToken)
    {
        var seam = await store.CurrentSeamAsync(target.InstrumentId, cancellationToken);
        if (seam is null)
        {
            return ProbeOutcome.Of(2, $"{target.Symbol}: no CSV bars, nothing to probe", 0);
        }

        var csvAll = await bars.ReadAsync(target.InstrumentId, seam.Value, seam.Value.AddDays(ReadDays), cancellationToken);
        var sessions = csvAll.Select(b => SessionClock.DateOf(b.EpochSeconds)).Distinct().Take(settings.ProbeSessions).ToList();
        if (sessions.Count == 0)
        {
            return ProbeOutcome.Of(2, $"{target.Symbol}: no CSV session to compare", 0);
        }

        var csv = csvAll.Where(b => sessions.Contains(SessionClock.DateOf(b.EpochSeconds))).ToList();
        var window = new FetchWindow(Midnight(sessions[0]), Midnight(sessions[^1].AddDays(1)));
        var plan = new IntradayFetchPlan([new IntradayTarget(target.InstrumentId, target.Symbol, target.Type, target.SecurityId, target.Segment, [window])]);
        IntradayResult? result = null;
        await foreach (var r in source.FetchAsync(plan, cancellationToken))
        {
            result = r;
            break;
        }

        var requests = result?.Requests ?? 1;
        if (result is { Failure.Kind: IntradayFailureKind.Unauthorized })
        {
            return ProbeOutcome.Refused(requests);
        }

        if (result is null || result.Failure is not null)
        {
            return ProbeOutcome.Of(3, $"{target.Symbol}: Dhan did not answer ({result?.Failure?.Message ?? "no result"})", requests);
        }

        var through = (await actionQueries.ListCsvAdjustedThroughAsync([target.InstrumentId], cancellationToken)).TryGetValue(target.InstrumentId, out var date) ? date : (DateOnly?)null;
        var csvRule = new SourceAwareFactors(factors, IndiaTime.Today(seam.Value), through);
        var dhan = IntradayBarCheck.Check(result.Bars).Bars.ToDictionary(b => b.EpochSeconds);
        var dhanAdjusted = new List<double>();
        var csvAdjusted = new List<double>();
        foreach (var bar in csv)
        {
            if (dhan.TryGetValue(bar.EpochSeconds, out var other))
            {
                var day = SessionClock.DateOf(bar.EpochSeconds);
                dhanAdjusted.Add(AdjustmentFactors.Price(other.Close, factors.FactorAt(day)));
                csvAdjusted.Add(csvRule.Price(bar.Close, day));
            }
        }

        var verdict = SeamProbe.Evaluate(dhanAdjusted, csvAdjusted, limits);
        var verdictName = verdict.Verdict.ToString();
        BackfillLog.ProbeDone(logger, target.Symbol, verdictName, verdict.MedianBps, verdict.WithinMaxPct, verdict.Compared);
        if (verdict.Verdict != SeamProbeVerdict.NoOverlap)
        {
            await SaveAsync(target.InstrumentId, seam.Value, verdict, cancellationToken);
        }

        var text = string.Create(CultureInfo.InvariantCulture, $"{target.Symbol}: {verdict.Verdict.ToString().ToLowerInvariant()} (median {verdict.MedianBps:F2} bps, {verdict.WithinMaxPct:F1} % within {limits.MaxBps:F0} bps, {verdict.Compared} minutes)");
        return ProbeOutcome.Of(verdict.Verdict switch { SeamProbeVerdict.Passed => 0, SeamProbeVerdict.Failed => 1, _ => 2 }, text, requests);
    }

    private async ValueTask SaveAsync(Guid id, DateTimeOffset seam, SeamProbeResult verdict, CancellationToken cancellationToken)
    {
        var record = (await store.ListSeamsAsync([id], cancellationToken)).GetValueOrDefault(id) ?? new SeamRecord(id, seam, ProbeStatus.None, null, null, null, null, false, null);
        var passed = verdict.Verdict == SeamProbeVerdict.Passed;
        await store.SaveSeamAsync(
            record with
            {
                SeamAt = seam,
                ProbeStatus = passed ? ProbeStatus.Passed : ProbeStatus.Unresolved,
                ProbedAt = clock.GetUtcNow(),
                MedianBps = verdict.MedianBps,
                WithinMaxPct = verdict.WithinMaxPct,
                Detail = passed ? null : "Dhan's bars do not match the CSV basis after the seam adjustment",
            },
            cancellationToken);
    }

    private static DateTimeOffset Midnight(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset);

    private sealed record ProbeOutcome(int Slot, string Line, int Requests, bool Unauthorized)
    {
        public static ProbeOutcome Of(int slot, string line, int requests) => new(slot, line, requests, false);

        public static ProbeOutcome Refused(int requests) => new(3, string.Empty, requests, true);
    }
}
