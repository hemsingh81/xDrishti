using XDrishti.Application.Abstractions.Filings;
using XDrishti.Domain.Filings;

namespace XDrishti.Api.IntegrationTests.Filings;

/// <summary>Builders for calendar and shareholding rows in the P15-04 database tests.</summary>
internal static class CalendarTestKit
{
    public static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    public static readonly DateOnly Q1 = new(2026, 6, 30);

    public const string MeetingHeader = "symbol,period_end,meeting_date,purpose,status,intimation_ts\n";

    public const string HoldingHeader = "symbol,period_end,category,pct,pledged_pct,broadcast_ts\n";

    public static DateTimeOffset At(int month, int day, int hour = 0, int minute = 0) => new(2026, month, day, hour, minute, 0, Ist);

    public static NewMeeting Meeting(Guid instrument, string tag, DateOnly meeting, DateTimeOffset? intimated, string status = "scheduled", string purpose = "results", DateOnly? period = null)
    {
        var p = period ?? Q1;
        var sha = FilingTestKit.Sha("meeting-" + tag);
        var filing = new NewFiling(
            instrument,
            FilingKind.BoardMeeting,
            p,
            NewFiling.ToMicroseconds(intimated?.ToUniversalTime()),
            intimated is null ? BroadcastState.Unavailable : BroadcastState.Ok,
            intimated is null ? "missing" : null,
            FilingTestKit.Ingested,
            sha,
            10,
            "m.csv");
        return new NewMeeting(filing, meeting, purpose, status);
    }

    public static NewShareholding Holding(Guid instrument, string tag, DateOnly period, DateTimeOffset? ts, decimal promoter, decimal? pledged = null)
    {
        var sha = FilingTestKit.Sha("holding-" + tag);
        var filing = new NewFiling(
            instrument,
            FilingKind.Shareholding,
            period,
            NewFiling.ToMicroseconds(ts?.ToUniversalTime()),
            ts is null ? BroadcastState.Unavailable : BroadcastState.Ok,
            ts is null ? "missing" : null,
            FilingTestKit.Ingested,
            sha,
            10,
            "s.csv");
        return new NewShareholding(filing, [new ShareholdingLine("promoter", promoter, pledged), new ShareholdingLine("fii", 100m - promoter, null), new ShareholdingLine("dii", null, null)]);
    }
}
