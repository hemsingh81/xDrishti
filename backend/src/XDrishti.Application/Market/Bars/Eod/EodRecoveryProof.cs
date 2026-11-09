using System.Globalization;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Market.Quality;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Platform.Services;
using XDrishti.Domain.Market;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Market.Bars.Eod;

/// <summary>
/// The proof of the end-of-day service's recovery steps (spec AU-03): per tracked instrument and missed session, the stored 1-minute bar count (<see cref="IBarDayCounts"/>: the daily aggregate, or the dirty range counted, never the
/// whole history) against the calendar's expected count, the gap record (<see cref="IBarGaps"/>), whether the aggregates are built (<see cref="IBarAggregates"/>), and the last full quality run. Cost: a count read and a gap read per instrument over the step's few sessions (the count comes from the daily aggregate, or the dirty range, never the history) plus one dirty-aggregates list and one quality read.
/// Shortcut: the per-instrument reads are sequential, which is fine for the tracked universe (hundreds); a much larger one would batch them.
/// An instrument short by no more than <c>Data:Quality:MaxMissingPercent</c> of the expected minutes (the quality check's own tolerance), or unfillable after a successful fetch, is served (spec AU-03, review). A session is judged by <see cref="RecoveryProofRules"/>; before an instrument's first bar there was nothing to miss, and an instrument without bars is not evidence.
/// </summary>
internal sealed class EodRecoveryProof(
    IFetchTargets targets,
    IBarDayCounts counts,
    IBarGaps gaps,
    IBarAggregates aggregates,
    IQualityQueries quality,
    ITradingCalendar calendar,
    IDataSettings dataSettings) : IRecoveryProof
{
    public string ServiceId => EodService.Id;

    public async ValueTask<RecoveryProofResult> ProveAsync(IReadOnlyList<DateOnly> sessions, DateTimeOffset runStartedAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        var cal = await calendar.GetAsync(cancellationToken);
        var from = sessions.Min();
        var to = sessions.Max();
        var dirty = (await aggregates.ListDirtyAsync(cancellationToken)).ToLookup(d => d.InstrumentId);
        var lastQuality = await quality.GetLastFinishedFullRunAsync(cancellationToken);
        var qualityVerified = lastQuality is { Status: "succeeded", FinishedAt: { } finished } && finished >= runStartedAt;

        var tolerance = dataSettings.QualityMaxMissingPercent;
        var states = sessions.ToDictionary(s => s, _ => new List<InstrumentSessionState>());
        foreach (var target in await targets.ListAsync(cancellationToken))
        {
            if (target.FirstBar is not { } firstBar)
            {
                continue;
            }

            var firstDay = IndiaTime.Today(firstBar);
            var have = await counts.CountAsync(target.InstrumentId, from, to, cancellationToken);
            var gapByDay = (await gaps.ListForAsync(target.InstrumentId, from, cancellationToken)).Where(g => g.Date >= from && g.Date <= to).ToDictionary(g => g.Date, g => g.Status);
            foreach (var session in sessions.Where(s => s > firstDay))
            {
                var dayStart = new DateTimeOffset(session.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset);
                var stale = dirty[target.InstrumentId].Any(d => d.From < dayStart.AddDays(1) && d.To >= dayStart);
                states[session].Add(RecoveryProofRules.Instrument(cal.ExpectedMinutes(session), have.GetValueOrDefault(session), gapByDay.TryGetValue(session, out var status) ? status : null, stale, tolerance));
            }
        }

        var proofs = sessions.Select(s => RecoveryProofRules.Session(s, states[s], qualityVerified)).ToList();
        var notRecovered = proofs.Where(p => p.State != SessionProofStates.Recovered).ToList();
        var reason = notRecovered.Count == 0
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"{proofs.Count - notRecovered.Count} of {proofs.Count} sessions recovered; not recovered: {string.Join(", ", notRecovered.Select(p => $"{p.Date:yyyy-MM-dd} {p.State}"))}");
        return new RecoveryProofResult(notRecovered.Count == 0, RecoveryProofDetail.Write(proofs, qualityVerified, runStartedAt), reason);
    }
}
