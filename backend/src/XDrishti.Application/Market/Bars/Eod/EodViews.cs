using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Bars.Eod;

/// <summary>
/// An automatic (scheduled) run as the Data screen shows it. <c>status</c>: running · succeeded · failed · skipped; <c>message</c> is the reason a run failed or was skipped
/// (for example the Data account was not connected). <c>trigger</c> is always <c>daily</c> here.
/// </summary>
public sealed record EodRunView(Guid JobId, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, string Status, string Trigger, string? Message)
{
    public static EodRunView From(DataJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return new EodRunView(job.Id, job.StartedAt, job.FinishedAt, job.Status.ToString().ToLowerInvariant(), job.Trigger.ToCode(), job.Error);
    }
}

/// <summary>
/// The scheduled end-of-day run (ADR 0017 §2): <c>enabled</c> (<c>Data:Eod:Enabled</c>), <c>timeIst</c> (<c>HH:mm</c>, India time), <c>nextRunAt</c> (the next trading day's run;
/// null when switched off) and <c>lastRun</c> (the latest automatic run, null before the first).
/// </summary>
public sealed record EodOverview(bool Enabled, string TimeIst, DateTimeOffset? NextRunAt, EodRunView? LastRun);

/// <summary>
/// Something about the data pipeline the owner should know, derived from the stored state like the account notices of the bell. <c>id</c> includes the situation (a new failed
/// run or a changed number of open gaps is a new notice, hence unread again). <c>kind</c>: data_run_failed · data_gaps_open · data_quality_failed (instruments fail the data quality checks: warn within the allowed share, error when the gate blocks the nightly plan) · data_corporate_actions_pending (suggested splits or bonus issues wait for a decision; warn). <c>severity</c>: error · warn.
/// </summary>
public sealed record DataNotice(string Id, string Kind, string Severity, string Text, DateTimeOffset? At);

public sealed record DataNotices(IReadOnlyList<DataNotice> Items);
