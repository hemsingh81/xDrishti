using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Domain.Paper;

namespace XDrishti.Application.Paper;

/// <summary>
/// Reads criterion I of the paper phase check on its own (CF-11, ADR 0225): the newest epoch's trading sessions up to the last closed session, their capture rows, and the very function of the phase check that judges them (<see cref="PaperPhaseCheck.CaptureIntegrity"/>).
/// It names only the calendar, the epoch read and the capture read, so the readiness scorecard (which the cell lifecycle can reach) does not reach the whole check or the schedule reader. Read only. A port that cannot be read throws; the scorecard reads that as unavailable.
/// </summary>
internal sealed class GetCaptureIntegrityHandler(ITradingCalendar tradingCalendar, IPaperLedgerData ledger, IPaperPhaseData phaseData, TimeProvider clock) : IQueryHandler<GetCaptureIntegrityQuery, CaptureIntegrityView>
{
    public async ValueTask<CaptureIntegrityView> HandleAsync(GetCaptureIntegrityQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var calendar = await tradingCalendar.GetAsync(cancellationToken);
        var through = calendar.LastClosedSession(clock.GetUtcNow());
        var epoch = await ledger.EpochAsync(null, cancellationToken);
        if (epoch is null)
        {
            var none = PaperPhaseCheck.CaptureIntegrity([]);
            return new CaptureIntegrityView(none.Verdict, "no paper epoch yet, so no capture run: " + none.Label, 0, null, none.Facts);
        }

        var dates = new List<DateOnly>();
        for (var d = epoch.StartedOn; d <= through; d = d.AddDays(1))
        {
            if (calendar.IsTradingDay(d))
            {
                dates.Add(d);
            }
        }

        IEnumerable<CaptureRunView> rows = dates.Count == 0 ? [] : await phaseData.CaptureRunsAsync(epoch.StartedOn, through, cancellationToken);
        var captures = rows.ToLookup(r => r.SessionDate);
        var sessions = dates.Select(date => CaptureSessionFacts.Of(date, captures[date])).ToList();
        var criterion = PaperPhaseCheck.CaptureIntegrity(sessions);
        return new CaptureIntegrityView(criterion.Verdict, criterion.Label, sessions.Sum(s => s.CaptureRuns), through, criterion.Facts);
    }
}
