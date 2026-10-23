using System.Globalization;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// The learning overview (spec P6-18): counts and the newest stored verdicts, read from the stores of P6-01 to P6-16. It counts the newest <see cref="CountLimit"/> records of each kind and says so when there may be more. An empty system is an answer.
/// </summary>
internal sealed class GetLearningOverviewHandler(ICellStatsStore cells, IModelRegistry models, IExperimentLedger experiments, IHypothesisLog hypotheses, ILessonLog lessons, ILearningChangelog changelog)
    : IQueryHandler<GetLearningOverviewQuery, Result<LearningOverviewView>>
{
    /// <summary>The records of each kind that are counted.</summary>
    public const int CountLimit = 200;

    private static readonly string[] Limits =
    [
        "Counts are of the newest 200 records of each kind; a section marked truncated has more.",
        "Every verdict is as stored when the record was made: nothing is recomputed here. Verdicts rest on about 2.1 years of data with few folds and modelled fills (ISS-19, ISS-18); a model is advisory until P7.",
    ];

    public async ValueTask<Result<LearningOverviewView>> HandleAsync(GetLearningOverviewQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        // Shortcut: this loads every latest cell row only to count them and take the newest date; a count/max query on ICellStatsStore would avoid that if the table grows.
        var cellRows = await cells.ListLatestAsync(null, null, cancellationToken);
        var modelRows = await models.ListAsync(null, CountLimit, cancellationToken);
        var champions = await models.ListAsync(ModelRole.Champion, 1, cancellationToken);
        var champion = champions.Count == 0 ? null : champions[0];
        var experimentRows = await experiments.ListAsync(CountLimit, cancellationToken);
        var hypothesisRows = await hypotheses.ListAsync(null, CountLimit, cancellationToken);
        var lessonRows = await lessons.ListAsync(null, CountLimit, cancellationToken);
        var changelogRows = await changelog.ListAsync(null, null, CountLimit, cancellationToken);

        var view = new LearningOverviewView(
            cellRows.Count,
            cellRows.Count == 0 ? null : cellRows.Max(r => r.Row.AsOf),
            Section(modelRows, r => r.Role.Code(), r => Headline(r), champion is null ? null : Headline(champion)),
            Section(experimentRows, r => r.Experiment.Outcome.ToString(), Headline),
            Section(hypothesisRows, r => r.Row.State, r => new OverviewHeadline(r.Id, r.Row.KnobName, r.Row.State, r.Row.Rationale, r.Row.CreatedAt)),
            Section(lessonRows, r => r.Latest.Row.State, r => new OverviewHeadline(r.Latest.Id, r.Latest.Row.Subject, r.Latest.Row.State, r.Latest.Row.Problem, r.OpenedAt)),
            Section(changelogRows, r => r.Entry.Kind, r => new OverviewHeadline(r.Id, r.Entry.Kind, r.Entry.ToState, r.Entry.Subject, r.Entry.At)),
            Limits);
        return Result.Success(view);
    }

    private static OverviewSection Section<T>(IReadOnlyList<T> rows, Func<T, string> name, Func<T, OverviewHeadline> headline, OverviewHeadline? champion = null) =>
        new(
            rows.Count,
            rows.Count >= CountLimit,
            [.. rows.GroupBy(name, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => new OverviewCount(g.Key, g.Count()))],
            rows.Count == 0 ? null : headline(rows[0]),
            champion);

    private static OverviewHeadline Headline(StoredModel s)
    {
        var m = s.Model;
        return new OverviewHeadline(
            s.Id,
            s.Role.Code(),
            m.Verdict.ToString(),
            string.Create(CultureInfo.InvariantCulture, $"beat both baselines in {m.FoldsBeatingBoth} of {m.UsedFolds} folds ({m.FoldsNeeded} needed); pooled calibration error {m.PooledEcePp:F2} pp; hold-out {(m.HoldOutStart is { } h ? "from " + h.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "none kept")}"),
            m.CreatedAt);
    }

    private static OverviewHeadline Headline(StoredExperiment s)
    {
        var e = s.Experiment;
        return new OverviewHeadline(s.Id, e.Knob.Name, e.Outcome.ToString(), string.Create(CultureInfo.InvariantCulture, $"better in {e.FoldsBetter} of {e.UsedFolds} folds ({e.FoldsNeeded} needed); {e.Trials} trials in 30 days"), e.CreatedAt);
    }
}
