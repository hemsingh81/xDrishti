using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Platform.Services;

/// <summary>The status words of a row of the Recovery screen (spec AU-04); <c>running</c> is a step in progress.</summary>
public static class RecoveryRowStatus
{
    public const string Missed = "missed";
    public const string Recovered = "recovered";
    public const string Partial = "partial";
    public const string StillFailing = "still_failing";
    public const string Blocked = "blocked";
    public const string Waiting = "waiting";
    public const string Running = "running";
    public const string NotRecoverable = "not_recoverable";
    public const string Superseded = "superseded";
    public const string OutOfReach = "out_of_reach";
}

/// <summary>What was missed, recovered, partial and still failing in the last <see cref="Days"/> days (1 to 90, default 14). Read-only.</summary>
public sealed record GetRecoveryStatusQuery(int Days = GetRecoveryStatusQuery.DefaultDays) : IQuery<Result<RecoveryStatusView>>
{
    public const int DefaultDays = 14;
    public const int MaxDays = 90;
}

public sealed record RecoveryPassView(string Trigger, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, string Status);

/// <summary>Headline counts: missed (missed, waiting, running, not recoverable, out of reach), recovered, still failing (still failing, blocked, partial).</summary>
public sealed record RecoveryCounts(int Missed, int Recovered, int StillFailing);

/// <summary>One service at one session. <c>Session</c> is the last session of the step; <c>From</c> to <c>To</c> are the sessions the step covers; <c>MissingSessions</c> are those a partial recovery did not fill.</summary>
public sealed record RecoveryRowView(string ServiceId, string ServiceName, string Status, string? Reason, int Attempts, DateOnly From, DateOnly To, Guid? RunId, bool CanRetry, IReadOnlyList<DateOnly> MissingSessions);

public sealed record RecoverySessionView(DateOnly Session, IReadOnlyList<RecoveryRowView> Items);

/// <summary><c>CheckedAt</c> is when this was read; <c>Running</c> is true while a pass runs; <c>Truncated</c> when the ledger held more rows than are shown (the newest sessions are kept). Sessions are oldest first.</summary>
public sealed record RecoveryStatusView(DateTimeOffset CheckedAt, bool Running, RecoveryPassView? LatestPass, int Days, RecoveryCounts Counts, bool Truncated, IReadOnlyList<RecoverySessionView> Sessions);

internal sealed class GetRecoveryStatusHandler(IRecoveryLedger ledger, RecoveryRunner runner, ServiceRegistry registry, TimeProvider clock)
    : IQueryHandler<GetRecoveryStatusQuery, Result<RecoveryStatusView>>
{
    private const int MaxRows = 500;

    public async ValueTask<Result<RecoveryStatusView>> HandleAsync(GetRecoveryStatusQuery query, CancellationToken cancellationToken)
    {
        if (query.Days is < 1 or > GetRecoveryStatusQuery.MaxDays)
        {
            return Result.Failure<RecoveryStatusView>(ServiceErrors.InvalidField("days", $"Days must be between 1 and {GetRecoveryStatusQuery.MaxDays}."));
        }

        var now = clock.GetUtcNow();
        var since = IndiaTime.Today(now).AddDays(-query.Days);
        var pass = await ledger.LatestPassAsync(cancellationToken);
        var fetched = await ledger.ListSinceAsync(since, MaxRows + 1, cancellationToken);
        var truncated = fetched.Count > MaxRows;
        var items = fetched.Take(MaxRows).ToList();
        var rows = new List<(DateOnly Session, RecoveryRowView Row)>();
        foreach (var item in items)
        {
            rows.Add((item.SessionTo, Row(item)));
        }

        // what a pass would do now and the ledger does not yet cover: missed, never made up, superseded, out of reach
        var plan = await runner.PreviewAsync(cancellationToken);
        foreach (var missed in plan?.Items ?? [])
        {
            if (missed.Session < since || items.Any(i => i.ServiceId == missed.ServiceId && i.SessionFrom <= missed.Session && missed.Session <= i.SessionTo))
            {
                continue;
            }

            var status = missed.Status switch
            {
                RecoveryItemStatus.Planned => RecoveryRowStatus.Missed,
                RecoveryItemStatus.Superseded => RecoveryRowStatus.Superseded,
                RecoveryItemStatus.NotRecoverable => RecoveryRowStatus.NotRecoverable,
                _ => RecoveryRowStatus.OutOfReach,
            };
            rows.Add((missed.Session, new RecoveryRowView(missed.ServiceId, registry.NameOf(missed.ServiceId), status, Clip(missed.Reason), 0, missed.Session, missed.Session, null, status == RecoveryRowStatus.Missed, [])));
        }

        var sessions = rows.GroupBy(r => r.Session).OrderBy(g => g.Key)
            .Select(g => new RecoverySessionView(g.Key, [.. g.Select(r => r.Row).OrderBy(r => r.ServiceName, StringComparer.Ordinal)]))
            .ToList();
        var all = rows.Select(r => r.Row.Status).ToList();
        var counts = new RecoveryCounts(
            all.Count(s => s is RecoveryRowStatus.Missed or RecoveryRowStatus.Waiting or RecoveryRowStatus.Running or RecoveryRowStatus.NotRecoverable or RecoveryRowStatus.OutOfReach),
            all.Count(s => s == RecoveryRowStatus.Recovered),
            all.Count(s => s is RecoveryRowStatus.StillFailing or RecoveryRowStatus.Blocked or RecoveryRowStatus.Partial));
        var latest = pass is null ? null : new RecoveryPassView(pass.Trigger, pass.StartedAt, pass.FinishedAt, pass.Status);
        return Result.Success(new RecoveryStatusView(now, runner.IsRunning, latest, query.Days, counts, truncated, sessions));
    }

    private RecoveryRowView Row(RecoveryItemRow item)
    {
        var status = item.State switch
        {
            RecoveryStates.Succeeded => RecoveryRowStatus.Recovered,
            RecoveryStates.Partial => RecoveryRowStatus.Partial,
            RecoveryStates.Failed => RecoveryRowStatus.StillFailing,
            RecoveryStates.Blocked => RecoveryRowStatus.Blocked,
            RecoveryStates.Waiting => RecoveryRowStatus.Waiting,
            RecoveryStates.Superseded => RecoveryRowStatus.Superseded,
            _ => RecoveryRowStatus.Running,
        };
        IReadOnlyList<DateOnly> missing = status == RecoveryRowStatus.Partial && RecoveryProofDetail.Read(item.Detail) is { } proof
            ? [.. proof.Sessions.Where(s => s.State is SessionProofStates.StillMissing or SessionProofStates.Unfillable).Select(s => s.Date).OrderBy(d => d)]
            : [];
        var canRetry = status is RecoveryRowStatus.StillFailing or RecoveryRowStatus.Blocked or RecoveryRowStatus.Partial or RecoveryRowStatus.Waiting;
        return new RecoveryRowView(item.ServiceId, registry.NameOf(item.ServiceId), status, Clip(item.Reason), item.Attempts, item.SessionFrom, item.SessionTo, item.RunId, canRetry, missing);
    }

    private static string? Clip(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? null : reason.Length <= 300 ? reason : string.Concat(reason.AsSpan(0, 299), "…");
}
