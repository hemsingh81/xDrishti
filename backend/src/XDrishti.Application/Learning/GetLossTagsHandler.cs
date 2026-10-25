using System.Globalization;
using System.Text;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// The loss tags on the report population (P8-13): resolves the runs (named, or the default set with the labels the runs record), refuses a set above the report limit, takes the place in the <see cref="ReportGate"/>, and wraps
/// <see cref="AnalyzeLossTagsHandler"/>. Computed on read: the rules are pure and one pass (ADR 0111).
/// </summary>
internal sealed class GetLossTagsHandler(
    IReportTradeSource source,
    ReportGate gate,
    IQueryHandler<AnalyzeLossTagsQuery, Result<LossTagReport>> analyze) : IQueryHandler<GetLossTagsQuery, Result<LossTagsView>>
{
    private const int MaxWorst = 25;

    /// <summary>
    /// The most closed trades one loss-tag read takes (by the run headers, before reading). Lower than the report engine's cap because this read holds whole simulated trades, not report rows, and the pure pass alone takes about 0.9 s at 500,000 (ADR 0111).
    /// Shortcut: compute on read; store as a learning_reports kind when P8-12 lands. The problem code stays reports.too_large.
    /// </summary>
    public const int MaxClosedTrades = 150_000;

    private static readonly string[] Unavailable =
    [
        "Regime of each trade (against_regime tag): no market context is stored (P3-01d/e).",
        "Execution gap and decision drag: need real fills and recorded decisions (P10/P11).",
    ];

    public async ValueTask<Result<LossTagsView>> HandleAsync(GetLossTagsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var all = await source.ListRunsAsync(cancellationToken);
        var isDefault = query.Runs.Count == 0;
        var skipped = new List<string>();
        List<CellSource> sources;
        List<ReportRunInfo> used;
        if (isDefault)
        {
            var (chosen, notes) = RunSelection.Default(all);
            skipped.AddRange(notes.OrderBy(n => n.Key, StringComparer.Ordinal).Select(n => Short(n.Key) + ": " + n.Value));
            sources = [];
            used = [];
            foreach (var run in chosen)
            {
                if (run.Style is null || run.Exit is null)
                {
                    skipped.Add(Short(run.RunKey) + ": style or exit is not recorded for this run; name it as run=KEY:STYLE:EXIT");
                    continue;
                }

                sources.Add(new CellSource(run.RunKey, run.Style, run.Exit));
                used.Add(run);
            }
        }
        else
        {
            sources = [.. query.Runs];
            used = [];
            foreach (var s in sources)
            {
                var run = all.FirstOrDefault(r => string.Equals(r.RunKey, s.RunKey, StringComparison.Ordinal));
                if (run is not null)
                {
                    used.Add(run);
                }
            }
        }

        var closed = used.Sum(r => (long)r.ClosedTrades);
        if (closed > MaxClosedTrades)
        {
            return Result.Failure<LossTagsView>(Error.Validation("reports.too_large", $"The runs hold {closed} closed trades; loss tags read at most {MaxClosedTrades} in one request."));
        }

        if (sources.Count == 0)
        {
            return Result.Success(Build(null, [], isDefault, skipped));
        }

        using var lease = await gate.EnterAsync(cancellationToken);
        var report = await analyze.HandleAsync(new AnalyzeLossTagsQuery(sources, new LossTagOptions(ShowWorst: MaxWorst)), cancellationToken);
        if (report.IsFailure)
        {
            return Result.Failure<LossTagsView>(report.Error);
        }

        return Result.Success(Build(report.Value, used, isDefault, skipped));
    }

    private static string Short(string key) => key[..Math.Min(12, key.Length)];

    private static LossTagsView Build(LossTagReport? report, List<ReportRunInfo> used, bool isDefault, List<string> skipped)
    {
        var runs = used.OrderBy(r => r.RunKey, StringComparer.Ordinal)
            .Select(r => new RunUsedView(r.RunKey, r.StrategyId, r.UniverseKind + ": " + r.UniverseLabel, r.WindowStart, r.WindowEnd, r.Costed, r.CostsSheetId, r.Style, r.Exit, r.ClosedTrades, r.OpenAtEnd))
            .ToList();
        var basis = used.Count == 0 ? "unknown" : used.All(r => r.Costed) ? "net" : used.All(r => !r.Costed) ? "gross" : "mixed";
        if (report is null)
        {
            var evidence = new LossTagsEvidenceView(basis, 0, runs, isDefault, skipped, Unavailable);
            return new LossTagsView("empty", 0, 0, [], [], 0, 0, LossTagReport.NotBuilt, evidence, []);
        }

        var kinds = Enum.GetValues<LossTagKind>();
        var window = report.Options.GrowingWindow;
        var cells = report.Cells.Select(c => new CellLossTagsView(
            c.CellKey,
            c.Trades,
            c.Losses,
            c.Untagged,
            [.. kinds.Select((k, i) => new TagCountView(Snake(k), c.Counts[i], c.Losses == 0 ? null : (double)c.Counts[i] / c.Losses, c.LostR?[i]))],
            c.Losses >= 2 * window ? "evaluated" : "not_evaluable",
            c.Losses >= 2 * window ? null : string.Create(CultureInfo.InvariantCulture, $"needs {2 * window} losses (two windows of {window})"),
            [.. c.Growing.Select(g => new GrowingTagView(Snake(g.Kind), g.RecentCount, g.EarlierCount, g.Window))])).ToList();
        var worst = report.Worst.Select(w => new WorstLossView(w.Key, w.CellKey, w.R, [.. w.Tags.Select(t => new LossTagDetailView(Snake(t.Kind), t.Detail))])).ToList();
        var limits = new List<string>(report.Limits) { "Tags overlap: the R lost per tag adds to more than the total, and shares are of the cell's losses and do not add to 100 per cent." };
        return new LossTagsView("ok", report.Trades, report.Losses, cells, worst, report.Comparisons, report.Comparisons * LossTagAnalyzer.ChanceFlagRate, LossTagReport.NotBuilt, new LossTagsEvidenceView(basis, report.Trades, runs, isDefault, skipped, Unavailable), limits);
    }

    /// <summary>GapThroughStop becomes gap_through_stop.</summary>
    private static string Snake(LossTagKind kind)
    {
        var name = kind.ToString();
        var sb = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                sb.Append('_');
            }

            sb.Append(char.ToLowerInvariant(name[i]));
        }

        return sb.ToString();
    }
}
