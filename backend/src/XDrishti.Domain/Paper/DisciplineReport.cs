using System.Globalization;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Reporting;

namespace XDrishti.Domain.Paper;

/// <summary>
/// The discipline report of a window (spec P10-03, R19): the seven categories of the primary tickets, the deviation lines (skipped, resized, R not taken), the added fills and the paper execution gap. A pure function of stored facts: the same
/// facts in any order give the same report. There is no composite score (P8-00 D10); every R has its n and its state.
/// </summary>
/// <param name="Lines">The lines in their fixed order.</param>
/// <param name="Execution">The execution waterfall rows.</param>
/// <param name="Outcomes">Where each primary ticket fell, in ticket order.</param>
/// <param name="PrimaryCount">The primary tickets of the window; the seven categories sum to it.</param>
public sealed record DisciplineReport(IReadOnlyList<DisciplineLine> Lines, IReadOnlyList<ExecutionRow> Execution, IReadOnlyList<TicketOutcome> Outcomes, int PrimaryCount)
{
    /// <summary>The fewest graded tickets a mean R needs (the insight minimum of doc 15 section 6).</summary>
    public const int MinGraded = 15;

    /// <summary>The count from which a mean is a conclusion rather than an indication.</summary>
    public const int Conclusion = 30;

    /// <summary>Sentence of the R-not-taken line.</summary>
    public const string NotTakenTitle = "R not taken (positive: leaving it cost you; negative: it saved you)";

    /// <summary>The reason of the R of an added trade.</summary>
    public const string NoPlanLevels = "no plan levels";

    /// <summary>Builds the report.</summary>
    /// <param name="tickets">The tickets of the window (any group).</param>
    /// <param name="trades">The stored paper trades (those of other tickets are ignored).</param>
    /// <param name="skips">The stored skips.</param>
    /// <param name="fills">The discretionary real fills of the window; null when they could not be read.</param>
    /// <param name="fillsUnavailable">Why, when <paramref name="fills"/> is null.</param>
    public static DisciplineReport Build(IReadOnlyList<ReviewTicket> tickets, IReadOnlyList<ReviewTrade> trades, IReadOnlyList<ReviewSkip> skips, IReadOnlyList<ReviewFill>? fills, string? fillsUnavailable)
    {
        ArgumentNullException.ThrowIfNull(tickets);
        ArgumentNullException.ThrowIfNull(trades);
        ArgumentNullException.ThrowIfNull(skips);
        var ordered = tickets.OrderBy(t => t.ForDate).ThenBy(t => t.ItemId).ToList();
        var tradesBy = trades.ToLookup(t => t.ItemId);
        var skipsBy = skips.ToLookup(s => s.ItemId);
        var outcomes = ordered.Where(t => t.Group == TicketGroup.Primary)
            .Select(t => DisciplineClassifier.Classify(t, [.. tradesBy[t.ItemId].OrderBy(x => x.AccountId)], [.. skipsBy[t.ItemId]]))
            .ToList();
        List<TicketOutcome> In(string category) => [.. outcomes.Where(o => o.Category == category)];

        var followed = In(DisciplineCategory.Followed);
        var sized = In(DisciplineCategory.SizedDown);
        var notEntered = In(DisciplineCategory.ApprovedNotEntered);
        var pending = In(DisciplineCategory.Pending);
        var rejected = In(DisciplineCategory.Rejected);
        var lapsed = In(DisciplineCategory.Lapsed);
        var invalidated = In(DisciplineCategory.InvalidatedPreopen);

        var lines = new List<DisciplineLine>
        {
            Taken(DisciplineCategory.Followed, "Followed: approved at full size, entered and closed", followed, WinsNote(followed)),
            Taken(DisciplineCategory.SizedDown, "Sized down: approved below the proposed size, entered and closed", sized, "the size effect is the deviation line below"),
            NoR(DisciplineCategory.ApprovedNotEntered, "Approved, not entered", notEntered, CodeNote(notEntered)),
            NoR(DisciplineCategory.Pending, "Pending", pending, PendingNote(pending)),
            Skipped(DisciplineCategory.Rejected, "Rejected", rejected, "graded on plan prices (simulated on plan prices)"),
            Skipped(DisciplineCategory.Lapsed, "Lapsed: not reviewed by the cutoff", lapsed, "graded on plan prices (simulated on plan prices)"),
            Skipped(DisciplineCategory.InvalidatedPreopen, "Invalidated at pre-open", invalidated, "dropped by the system at the pre-open check, not the owner's act; graded on plan prices"),
            Flag("confirmed_despite", "Approved despite a failed check (beside followed and sized down)", outcomes.Count(o => o.ClosedTrade && o.Ticket.ConfirmedDespite)),
            Flag("auto_approved", "Approved automatically (a primary ticket in any category)", outcomes.Count(o => o.Ticket.ApprovalKind == "auto")),
            Flag("reserve_approved", "Reserve tickets approved (not part of the seven)", ordered.Count(t => t.Group == TicketGroup.Reserve && t.Approved)),
        };

        var skippedGraded = rejected.Concat(lapsed).Select(o => o.Ticket.Grade).Where(IsGraded).Select(g => g!.R!.Value).ToList();
        var effects = sized.Where(o => o.SizeEffectR is not null).Select(o => o.SizeEffectR!.Value).ToList();
        var sizedApart = outcomes.Count(o => o.SizeReduced && !o.ClosedTrade && o.Category is DisciplineCategory.Pending or DisciplineCategory.ApprovedNotEntered);
        var notTaken = skippedGraded.Concat(effects).ToList();
        var takenR = followed.Concat(sized).Select(o => o.R!.Value).ToList();
        lines.Add(new DisciplineLine("skipped_graded", "deviation", "Skipped by you (rejected or lapsed), graded", skippedGraded.Count, Sum(skippedGraded, "no skipped ticket was graded"), Mean(skippedGraded), [],
            string.Create(CultureInfo.InvariantCulture, $"{NotTriggered(rejected.Concat(lapsed))} not triggered and {NotGraded(rejected.Concat(lapsed))} not graded are counted apart, never R 0")));
        lines.Add(new DisciplineLine("size_effect", "deviation", "Size effect of size-downs (in planned-size R)", effects.Count, Sum(effects, "no sized-down trade has closed"), Mean(effects), [],
            string.Create(CultureInfo.InvariantCulture, $"{sizedApart} size-down(s) still open or never entered add nothing; an accounting convenience, not a risk measure")));
        lines.Add(new DisciplineLine("r_not_taken", "total", NotTakenTitle, notTaken.Count, Sum(notTaken, "nothing to add"), Mean(notTaken), [], "skipped plus size effect, each ticket once"));
        lines.Add(new DisciplineLine("taken", "total", "Taken: closed paper trades (followed and sized down)", takenR.Count, Sum(takenR, "no closed paper trade"), Mean(takenR), Rupees(followed.Concat(sized)), WinsNote([.. followed, .. sized])));
        lines.Add(Added(fills, fillsUnavailable));

        var exits = followed.Concat(sized).Select(o => o.Trades.Where(t => t.Closed).OrderBy(t => t.AccountId).First().Exit!).ToList();
        return new DisciplineReport(lines, ExecutionGap.Rows(exits), outcomes, outcomes.Count);
    }

    /// <summary>A mean R: empty with nothing graded, insufficient below 15, provisional below 30.</summary>
    public static Metric Mean(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            return Metric.Empty("nothing graded");
        }

        if (values.Count < MinGraded)
        {
            return Metric.Insufficient(string.Create(CultureInfo.InvariantCulture, $"n {values.Count} < {MinGraded}"));
        }

        var mean = Metric.Of(values.Average());
        return values.Count < Conclusion ? mean.AsProvisional(string.Create(CultureInfo.InvariantCulture, $"n {values.Count} < {Conclusion}: an indication, not a conclusion")) : mean;
    }

    private static Metric Sum(List<double> values, string emptyReason) => values.Count == 0 ? Metric.Empty(emptyReason) : Metric.Of(values.Sum());

    private static bool IsGraded(TicketGrade? grade) => grade is { Status: GradeStatus.Graded, R: not null };

    private static int NotTriggered(IEnumerable<TicketOutcome> o) => o.Count(x => x.Ticket.Grade is { Status: GradeStatus.NotTriggered });

    private static int NotGraded(IEnumerable<TicketOutcome> o) => o.Count(x => x.Ticket.Grade is null or { Status: GradeStatus.NotGradable } || (x.Ticket.Grade is { Status: GradeStatus.Graded, R: null }));

    private static DisciplineLine Taken(string id, string title, List<TicketOutcome> list, string note)
    {
        var r = list.Select(o => o.R!.Value).ToList();
        return new DisciplineLine(id, "category", title, list.Count, Sum(r, "no closed trade"), Mean(r), Rupees(list), note);
    }

    private static DisciplineLine NoR(string id, string title, List<TicketOutcome> list, string note) =>
        new(id, "category", title, list.Count, Metric.Empty("no closed trade"), Metric.Empty("no closed trade"), [], note);

    private static DisciplineLine Skipped(string id, string title, List<TicketOutcome> list, string note)
    {
        var r = list.Select(o => o.Ticket.Grade).Where(IsGraded).Select(g => g!.R!.Value).ToList();
        var text = string.Create(CultureInfo.InvariantCulture, $"graded {r.Count}, not_triggered {NotTriggered(list)}, not_graded {NotGraded(list)}; {note}");
        return new DisciplineLine(id, "category", title, list.Count, Sum(r, "no ticket graded"), Mean(r), [], text);
    }

    private static DisciplineLine Flag(string id, string title, int n) =>
        new(id, "flag", title, n, Metric.Empty("a count only"), Metric.Empty("a count only"), [], string.Empty);

    private static IReadOnlyList<AccountRupees> Rupees(IEnumerable<TicketOutcome> list) =>
        [.. list.SelectMany(o => o.Trades).Where(t => t.Closed).GroupBy(t => t.AccountId).OrderBy(g => g.Key).Select(g => new AccountRupees(g.Key, g.Sum(t => t.Exit!.Result)))];

    private static string WinsNote(List<TicketOutcome> list) =>
        list.Count == 0 ? "no closed trade" : string.Create(CultureInfo.InvariantCulture, $"{list.Count(o => o.R > 0)} wins of {list.Count}");

    private static string CodeNote(List<TicketOutcome> list) =>
        list.Count == 0 ? "none" : string.Join(", ", list.GroupBy(o => o.SkipCode, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => string.Create(CultureInfo.InvariantCulture, $"{g.Key} {g.Count()}")));

    private static string PendingNote(List<TicketOutcome> list) =>
        list.Count == 0 ? "none" : string.Join(", ", list.GroupBy(o => o.PendingWhy, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => string.Create(CultureInfo.InvariantCulture, $"{g.Key} {g.Count()}")));

    private static DisciplineLine Added(IReadOnlyList<ReviewFill>? fills, string? unavailable)
    {
        const string title = "Added: real fills with no ticket";
        if (fills is null)
        {
            var why = "unavailable: " + (unavailable ?? "the journal could not be read");
            return new DisciplineLine("added", "deviation", title, 0, Metric.Unavailable(why), Metric.Unavailable(why), [], why);
        }

        if (fills.Count == 0)
        {
            return new DisciplineLine("added", "deviation", title, 0, Metric.Empty("no real fills in the window"), Metric.Empty("no real fills in the window"), [], "empty: no real fills in the window");
        }

        var noR = Metric.Unavailable(NoPlanLevels);
        var positions = fills.GroupBy(f => (f.AccountId, f.Symbol)).ToList();
        var finished = positions.Where(g => g.Where(f => f.IsBuy).Sum(f => f.Qty) == g.Where(f => !f.IsBuy).Sum(f => f.Qty)).ToList();
        var rupees = finished.GroupBy(g => g.Key.AccountId).OrderBy(g => g.Key)
            .Select(a => new AccountRupees(a.Key, a.Sum(g => g.Sum(f => (f.IsBuy ? -1 : 1) * f.Qty * f.Price - f.Charges)))).ToList();
        var note = string.Create(CultureInfo.InvariantCulture, $"{fills.Count} fills in {fills.Select(f => f.Symbol).Distinct(StringComparer.Ordinal).Count()} symbols; R unavailable: {NoPlanLevels}; ")
            + (finished.Count == 0 ? "rupees unavailable: no finished round trip in the journal" : string.Create(CultureInfo.InvariantCulture, $"rupees from {finished.Count} of {positions.Count} finished round trips"));
        return new DisciplineLine("added", "deviation", title, fills.Count, noR, noR, rupees, note);
    }
}
