using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Selection;

namespace XDrishti.Application.Selection;

/// <summary>
/// Cell-level view of selection (spec P7-01, ADR 0082). It reads the newest stored statistics (all-time window) as of the decision date and each cell's state from the changelog as it stood before that day, makes one candidate per cell with the placeholder symbol <c>*</c> and runs the pure filters and ranker.
/// No instrument-level input exists yet (reward to risk, cost, turnover come with the ticket builder, P7-04), so those filters read unavailable and a cell is held, never passed on a guess. No merge (it needs symbols). Writes nothing.
/// </summary>
internal sealed class ShowSelectionHandler(ICellStatsStore stats, ILearningChangelog changelog, ILogger<ShowSelectionHandler> logger) : IQueryHandler<ShowSelectionQuery, Result<SelectionView>>
{
    private const int MaxRows = 200;

    private static readonly TimeSpan IndiaOffset = TimeSpan.FromHours(5.5);

    public async ValueTask<Result<SelectionView>> HandleAsync(ShowSelectionQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var rules = new SelectionRules(MinState: query.MinState);
        var problems = rules.Problems();
        if (problems.Count > 0)
        {
            SelectionLog.Refused(logger, LearningErrors.InvalidCode);
            return Result.Failure<SelectionView>(LearningErrors.Invalid([.. problems.Select(p => new FieldError("rules", p))]));
        }

        // As of the decision date: the newest snapshot with an as-of date before it, and each cell's state from the changelog rows written before that day began (India time).
        var rows = await stats.ListLatestBeforeAsync(query.Date, null, CellWindow.All.Code(), cancellationToken);
        var cut = new DateTimeOffset(query.Date.ToDateTime(TimeOnly.MinValue), IndiaOffset);
        var evaluated = new List<Eligibility>(rows.Count);
        foreach (var stored in rows)
        {
            // Shortcut: one changelog read per cell (N+1). Fine for the cell counts of the lab (hundreds); batch the read when selection runs nightly with many more.
            var history = await changelog.HistoryAsync(stored.Row.Cell.Key, cancellationToken);
            var state = CellHistory.Replay([.. history.Where(h => h.Entry.At < cut)]).State;
            evaluated.Add(EligibilityFilters.Evaluate(new PlanCandidate("*", stored.Row.Cell, state, stored.Row, query.Date), rules));
        }

        var eligible = evaluated.Where(e => e.Verdict == EligibilityVerdict.Eligible).ToList();
        var ranked = CandidateRanker.Rank(eligible.Select(e => new MergedCandidate(e, 0, [])), rules);
        var rejected = evaluated.Count(e => e.Verdict == EligibilityVerdict.Rejected);
        var held = evaluated.Count(e => e.Verdict == EligibilityVerdict.Held);
        SelectionLog.Shown(logger, query.Date, ranked.Count, rejected, held, evaluated.Count);
        return Result.Success(new SelectionView(ranked.Count, rejected, held, Print(query, rules, rows.Count == 0 ? null : rows[0].Row.AsOf, evaluated, ranked)));
    }

    private static string Print(ShowSelectionQuery query, SelectionRules rules, DateOnly? asOf, List<Eligibility> evaluated, IReadOnlyList<RankedCandidate> ranked)
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine(string.Create(c, $"selection for {query.Date:yyyy-MM-dd}, minimum cell state {rules.MinState.Code()}, statistics as of {(asOf is { } d ? d.ToString("yyyy-MM-dd", c) : "none stored")}"));
        sb.AppendLine(string.Create(c, $"{evaluated.Count} cells: {ranked.Count} eligible, {evaluated.Count(e => e.Verdict == EligibilityVerdict.Rejected)} rejected, {evaluated.Count(e => e.Verdict == EligibilityVerdict.Held)} held"));
        foreach (var r in ranked.Take(MaxRows))
        {
            var e = r.Merged.Winner;
            var caveats = r.Caveats.Count == 0 ? string.Empty : " [loosened, unavailable: " + string.Join(", ", r.Caveats) + "]";
            sb.AppendLine(string.Create(c, $"  #{r.Rank} {e.Candidate.Cell.Key} {r.Explanation}{caveats}{(r.Provisional ? " (provisional: cell not yet paper-proven)" : string.Empty)}"));
        }

        if (ranked.Count > MaxRows)
        {
            sb.AppendLine(string.Create(c, $"  ... {ranked.Count - MaxRows} more not shown"));
        }

        var others = evaluated.Where(e => e.Verdict != EligibilityVerdict.Eligible)
            .OrderBy(e => e.Verdict).ThenByDescending(e => StrengthOrderValue(e)).ThenBy(e => e.Candidate.Cell.Key, StringComparer.Ordinal).ToList();
        foreach (var e in others.Take(MaxRows))
        {
            var why = string.Join("; ", e.Reasons.Select(x => x.Code + (x.Outcome == FilterOutcome.Unavailable ? " unavailable" : " fail") + (x.Value is { } v ? string.Create(c, $" {v:0.###}") : string.Empty)));
            sb.AppendLine(string.Create(c, $"  {e.Verdict.ToString().ToLowerInvariant()} {e.Candidate.Cell.Key}: {why}{(e.Verdict == EligibilityVerdict.Held && e.Provisional ? " (provisional: cell not yet paper-proven)" : string.Empty)}"));
        }

        if (others.Count > MaxRows)
        {
            sb.AppendLine(string.Create(c, $"  ... {others.Count - MaxRows} more not shown"));
        }

        sb.AppendLine("limits: a cell is not a ticket. Reward to risk, cost and turnover belong to an instrument and exist only once the ticket builder (P7-04) is built, so they are unavailable here and every cell is held; this view shows the evidence filters.");
        sb.AppendLine("limits: win rates are shrunk replayed-history estimates (about 2.1 years, no hold-out, modelled fills); events, F&O ban and surveillance data do not exist yet, so those filters are unavailable and hold a cell (never clear) unless the owner loosens them; no model is used. State is as the changelog stood before the decision day; statistics are the newest snapshot as of a date before it.");
        return sb.ToString();
    }

    private static double StrengthOrderValue(Eligibility e) => e.ConservativeExpectancyR ?? double.NegativeInfinity;
}
