using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// Opens lessons from the replayed monitors (spec P6-16, ADR 0078): the growing loss tags and the drift alarms still on at the end of the replay. Idempotent: a drift finding with the same monitor, subject and evidence key is never opened twice; a growing tag opens no new lesson while one of the same monitor, subject and tag kind is open or linked. A finding whose subject or key does not fit its column is listed as skipped. Reads
/// stored runs only; execution gap and decision drag are listed as not built. Nothing is applied.
/// </summary>
internal sealed class OpenLessonsHandler(
    IQueryHandler<AnalyzeLossTagsQuery, Result<LossTagReport>> lossTags,
    IQueryHandler<ReplayDriftQuery, Result<DriftReport>> drift,
    ILessonLog log,
    TimeProvider time,
    ILogger<OpenLessonsHandler> logger) : ICommandHandler<OpenLessonsCommand, LessonScanOutcome>
{
    public async ValueTask<Result<LessonScanOutcome>> HandleAsync(OpenLessonsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var problems = new List<FieldError>();
        if (command.Sources is null || command.Drift is null || command.LossTags is null || command.MonitorMonths is < 1 or > 120)
        {
            problems.Add(new FieldError("request", "Sources, drift options, loss-tag options and monitor months of 1 to 120 are required."));
        }

        if (!HypothesisRows.ValidActor(command.Actor))
        {
            problems.Add(new FieldError("actor", "The actor is required and at most 100 characters."));
        }

        if (problems.Count > 0)
        {
            return Refuse(LearningErrors.Invalid(problems));
        }

        var tags = await lossTags.HandleAsync(new AnalyzeLossTagsQuery(command.Sources!, command.LossTags!), cancellationToken);
        if (tags.IsFailure)
        {
            return Refuse(tags.Error);
        }

        var replay = await drift.HandleAsync(new ReplayDriftQuery(command.Sources!, command.Drift!, command.MonitorMonths, command.ModelId, new CellStatsOptions(), null), cancellationToken);
        if (replay.IsFailure)
        {
            return Refuse(replay.Error);
        }

        var findings = LessonFindings.FromLossTags(tags.Value).Concat(LessonFindings.FromDrift(replay.Value)).ToList();
        var items = new List<LessonScanItem>();
        var opened = 0;
        var existing = 0;
        foreach (var f in findings)
        {
            var code = f.Monitor.Code();
            var item = await ScanOneAsync(f, code, command, cancellationToken);
            items.Add(item);
            opened += item.Status == "opened" ? 1 : 0;
            existing += item.Status == "existing" ? 1 : 0;
        }

        LessonLog.Scanned(logger, findings.Count, opened, existing, command.DryRun);
        return Result.Success(new LessonScanOutcome(items, LessonMonitors.NotBuilt));
    }

    private async ValueTask<LessonScanItem> ScanOneAsync(LessonFinding f, string code, OpenLessonsCommand command, CancellationToken cancellationToken)
    {
        var problem = f.Problem.Length <= LessonRows.MaxProblem ? f.Problem : f.Problem[..LessonRows.MaxProblem];
        if (f.Subject.Length > LessonRows.MaxSubject || f.EvidenceKey.Length > LessonRows.MaxEvidenceKey)
        {
            // Too long for its column: skipped and said so, never an aborted scan or a clipped key that could collide with another finding.
            var shown = f.Subject.Length <= LessonRows.MaxSubject ? f.Subject : f.Subject[..LessonRows.MaxSubject];
            return new LessonScanItem(code, shown, problem, "skipped", 0);
        }

        if (command.DryRun)
        {
            return await log.FindIdByEvidenceAsync(code, f.Subject, f.EvidenceKey, cancellationToken) is { } known
                ? new LessonScanItem(code, f.Subject, problem, "existing", known)
                : new LessonScanItem(code, f.Subject, problem, "found", 0);
        }

        var row = new NewLessonRow(null, LessonState.Open.Code(), null, code, f.Subject, f.EvidenceKey, problem, null, null, null, null, null, null, null, null, "found by the replayed monitors", command.Actor, time.GetUtcNow());
        var appended = await log.OpenAsync(row, cancellationToken);
        if (appended.Status == LessonAppendStatus.Created)
        {
            LessonLog.Opened(logger, appended.Id, code);
            return new LessonScanItem(code, f.Subject, problem, "opened", appended.Id);
        }

        return new LessonScanItem(code, f.Subject, problem, "existing", appended.Id);
    }

    private Result<LessonScanOutcome> Refuse(Error error)
    {
        LessonLog.Refused(logger, error.Code);
        return Result.Failure<LessonScanOutcome>(error);
    }
}
