using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Learning;

/// <summary>
/// Builds and stores the weekly digest (spec P7-15, ADR 0094). It reads the stored records through the read ports of the learning stores, the stored grades and the phase check (a read-only handler), hands them to the pure <see cref="DigestBuilder"/>
/// and stores the result once per ISO week. The only write port it holds is <see cref="IDigestStore"/>: no lesson, hypothesis, changelog, approval or order is written, so nothing is applied (Q6, Q16).
/// </summary>
internal sealed class BuildDigestHandler(
    ICellStatsStore cells,
    ILearningChangelog changelog,
    ILessonLog lessons,
    IHypothesisLog hypotheses,
    IExperimentLedger experiments,
    IModelRegistry models,
    IGradeStore grades,
    IQueryHandler<EvaluatePhaseCheckQuery, Result<PhaseCheckView>> phaseCheck,
    IDigestStore store,
    TimeProvider time,
    ILogger<BuildDigestHandler> logger) : ICommandHandler<BuildDigestCommand, DigestView>
{
    /// <summary>The records of each kind read.</summary>
    public const int ReadLimit = 200;

    private static readonly string[] DriftMonitors = ["rolling_band", "cusum", "calibration"];

    public async ValueTask<Result<DigestView>> HandleAsync(BuildDigestCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = time.GetUtcNow();
        var today = IndiaTime.Today(now);
        var problems = new List<FieldError>();
        var week = command.Week is null ? DigestWeek.Of(today) : DigestWeek.TryParse(command.Week) ?? default;
        if (command.Week is not null && week == default)
        {
            problems.Add(new FieldError("week", "The week is written 2026-W43."));
        }
        else if (week.Start > today)
        {
            problems.Add(new FieldError("week", "That week has not started."));
        }

        if (string.IsNullOrWhiteSpace(command.Actor) || command.Actor.Length > 100)
        {
            problems.Add(new FieldError("actor", "The actor is required, at most 100 characters."));
        }

        if (problems.Count > 0)
        {
            return Result.Failure<DigestView>(LearningErrors.Invalid(problems));
        }

        if (!command.DryRun && await store.FindByWeekAsync(week.Key, cancellationToken) is { } existing)
        {
            DigestLog.Repeat(logger, week.Key, existing.Id);
            return Result.Success(Of(existing, true));
        }

        var digest = DigestBuilder.Build(await GatherAsync(week, cancellationToken));
        if (command.DryRun)
        {
            DigestLog.DryRun(logger, week.Key);
            return Result.Success(new DigestView(null, week.Key, week.Start, week.End, digest.Sections, digest.Text, null, null, false));
        }

        var (stored, created) = await store.InsertAsync(new NewDigest(digest, command.Actor, now), cancellationToken);
        if (created)
        {
            DigestLog.Stored(logger, week.Key, stored.Id, stored.Digest.Sections.Count);
        }
        else
        {
            DigestLog.Repeat(logger, week.Key, stored.Id);
        }

        return Result.Success(Of(stored, !created));
    }

    /// <summary>The view of a stored digest.</summary>
    internal static DigestView Of(StoredDigest s, bool repeated) =>
        new(s.Id, s.Digest.Week.Key, s.Digest.Week.Start, s.Digest.Week.End, s.Digest.Sections, s.Digest.Text, s.BuiltBy, s.CreatedAt, repeated);

    private async ValueTask<DigestInput> GatherAsync(DigestWeek week, CancellationToken cancellationToken)
    {
        var from = new DateTimeOffset(week.Start.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset);
        var to = from.AddDays(7);
        bool InWeek(DateTimeOffset at) => at >= from && at < to;
        var truncated = new List<string>();

        var asOf = await cells.NewestAsOfAsync(cancellationToken);

        var log = await changelog.ListAsync("cell", null, ReadLimit, cancellationToken);
        Flag(truncated, "changelog", log.Count);
        var cellChanges = log.Where(r => InWeek(r.Entry.At)).OrderBy(r => r.Id)
            .Select(r => new DigestItem(r.Id, r.Entry.Kind, r.Entry.Subject, r.Entry.FromState + " -> " + r.Entry.ToState, r.Entry.Reason)).ToList();

        var lessonRows = await lessons.ListAsync(null, ReadLimit, cancellationToken);
        Flag(truncated, "lessons", lessonRows.Count);
        var live = lessonRows.Where(l => l.Latest.Row.State is "open" or "linked").OrderBy(l => l.Latest.LessonId).ToList();
        DigestItem Lesson(LessonListed l) => new(l.Latest.LessonId, l.Latest.Row.MonitorKind, l.Latest.Row.Subject, l.Latest.Row.State, l.Latest.Row.Problem);
        var drift = live.Where(l => DriftMonitors.Contains(l.Latest.Row.MonitorKind, StringComparer.Ordinal)).Select(Lesson).ToList();
        var lossTags = live.Where(l => l.Latest.Row.MonitorKind == "growing_tag").Select(Lesson).ToList();
        var resolved = lessonRows.Where(l => l.Latest.Row.State is "closed" or "dismissed" && InWeek(l.Latest.Row.CreatedAt)).OrderBy(l => l.Latest.LessonId)
            .Select(l => Lesson(l) with { Text = l.Latest.Row.Result is { } res ? res + ": " + l.Latest.Row.Problem : l.Latest.Row.Problem }).ToList();

        var hypothesisRows = await hypotheses.ListAsync(null, ReadLimit, cancellationToken);
        Flag(truncated, "hypotheses", hypothesisRows.Count);
        var awaiting = hypothesisRows.Where(h => h.Row.State is "proposed" or "judged").OrderBy(h => h.HypothesisId)
            .Select(h => new DigestItem(h.HypothesisId, "hypothesis", h.Row.CellKey ?? string.Empty, h.Row.State,
                string.Create(CultureInfo.InvariantCulture, $"{h.Row.KnobName} {h.Row.BaselineValue} -> {h.Row.CandidateValue}{(h.Row.ExperimentId is { } e ? $" (experiment #{e})" : string.Empty)}: {h.Row.Rationale}"))).ToList();
        var inProgress = hypothesisRows.Count(h => h.Row.State is "approved" or "running");

        var experimentRows = await experiments.ListAsync(ReadLimit, cancellationToken);
        Flag(truncated, "experiments", experimentRows.Count);
        var weekExperiments = experimentRows.Where(x => InWeek(x.Experiment.CreatedAt)).OrderBy(x => x.Id)
            .Select(x => new DigestItem(x.Id, "experiment", x.Experiment.Knob.Name, x.Experiment.Outcome.ToString(),
                string.Create(CultureInfo.InvariantCulture, $"better in {x.Experiment.FoldsBetter} of {x.Experiment.UsedFolds} folds ({x.Experiment.FoldsNeeded} needed); {x.Experiment.Trials} trials in 30 days"))).ToList();

        var modelRows = await models.ListAsync(null, ReadLimit, cancellationToken);
        Flag(truncated, "models", modelRows.Count);
        var modelItems = modelRows.Where(m => m.Role != ModelRole.Retired).OrderBy(m => m.Role == ModelRole.Champion ? 0 : 1).ThenByDescending(m => m.Id)
            .Select(m => new DigestItem(m.Id, m.Role.Code(), string.Empty, m.Model.Verdict.ToString(),
                string.Create(CultureInfo.InvariantCulture, $"beat both baselines in {m.Model.FoldsBeatingBoth} of {m.Model.UsedFolds} folds ({m.Model.FoldsNeeded} needed); pooled calibration error {m.Model.PooledEcePp:F2} pp; hold-out {(m.Model.HoldOutStart is { } h ? "from " + h.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "none kept")}"))).ToList();

        var page = await grades.ReportItemsAsync(week.Start, week.End, GetDecisionsReportHandlerLimit, cancellationToken);
        var plans = page.Items.Count == 0 ? null : GradeReport.Build(page.Items, page.Truncated);

        var phase = await phaseCheck.HandleAsync(new EvaluatePhaseCheckQuery(null), cancellationToken);
        if (phase.IsFailure)
        {
            DigestLog.PhaseUnavailable(logger, week.Key, phase.Error.Code);
        }

        return new DigestInput(
            week,
            asOf,
            cellChanges,
            drift,
            lossTags,
            resolved,
            lessonRows.Count(l => l.Latest.Row.State == "open"),
            lessonRows.Count(l => l.Latest.Row.State == "linked"),
            awaiting,
            inProgress,
            weekExperiments,
            plans,
            modelItems,
            phase.IsSuccess ? phase.Value.Report.Criteria : null,
            phase.IsFailure ? phase.Error.Code : null,
            phase.IsSuccess ? phase.Value.Report.Limits : [],
            truncated);
    }

    private const int GetDecisionsReportHandlerLimit = 5000;

    private static void Flag(List<string> truncated, string kind, int count)
    {
        if (count >= ReadLimit)
        {
            truncated.Add(kind);
        }
    }
}
