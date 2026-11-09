using System.Globalization;
using System.Text;
using System.Text.Json;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Market.Quality;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Market;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Market.Bars.Eod;

/// <summary>
/// The proof of the end-of-day service's recovery steps (spec AU-03): per tracked instrument and missed session, the stored 1-minute bar count (<see cref="IBarDayCounts"/>: the daily aggregate, or the dirty range counted, never the
/// whole history) against the calendar's expected count, the gap record (<see cref="IBarGaps"/>), whether the aggregates are built (<see cref="IBarAggregates"/>), and the last full quality run. Two reads per instrument, bounded to
/// the step's sessions. A session is judged by <see cref="RecoveryProofRules"/>; before an instrument's first bar there was nothing to miss, and an instrument without bars is not evidence.
/// </summary>
internal sealed class EodRecoveryProof(
    IFetchTargets targets,
    IBarDayCounts counts,
    IBarGaps gaps,
    IBarAggregates aggregates,
    IQualityQueries quality,
    ITradingCalendar calendar) : IRecoveryProof
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
                states[session].Add(RecoveryProofRules.Instrument(cal.ExpectedMinutes(session), have.GetValueOrDefault(session), gapByDay.TryGetValue(session, out var status) ? status : null, stale));
            }
        }

        var proofs = sessions.Select(s => RecoveryProofRules.Session(s, states[s], qualityVerified)).ToList();
        var notRecovered = proofs.Where(p => p.State != SessionProofStates.Recovered).ToList();
        var reason = notRecovered.Count == 0
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"{proofs.Count - notRecovered.Count} of {proofs.Count} sessions recovered; not recovered: {string.Join(", ", notRecovered.Select(p => $"{p.Date:yyyy-MM-dd} {p.State}"))}");
        return new RecoveryProofResult(notRecovered.Count == 0, Detail(proofs, qualityVerified), reason);
    }

    /// <summary>The per-session state as JSON (written by hand: no reflection serialisation).</summary>
    internal static string Detail(IReadOnlyList<SessionProof> proofs, bool qualityVerified)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteBoolean("qualityVerified", qualityVerified);
            w.WriteStartArray("sessions");
            foreach (var p in proofs)
            {
                w.WriteStartObject();
                w.WriteString("date", p.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                w.WriteString("state", p.State);
                w.WriteNumber("instruments", p.Instruments);
                w.WriteNumber("complete", p.Complete);
                w.WriteNumber("unfillable", p.Unfillable);
                w.WriteNumber("missing", p.Missing);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
