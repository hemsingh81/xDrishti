using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;
using XDrishti.Domain.Paper;

namespace XDrishti.Application.Paper;

/// <summary>
/// Builds the weekly paper review of an ISO week and, when asked, stores it once per week and epoch (spec P10-03, ADR 0121). It reads stored records; the only thing it writes is the review text. It places no order and applies nothing.
/// </summary>
/// <param name="Week">The ISO week (<c>2026-W43</c>); null for the week of today (India time).</param>
/// <param name="EpochId">The epoch; null for the newest.</param>
/// <param name="Store">Store it (only when the week may be stored); false builds and shows it and stores nothing.</param>
/// <param name="Actor">Who asked (<c>system:paper-review</c>, <c>cli:xd</c>).</param>
public sealed record BuildPaperReviewCommand(string? Week, long? EpochId, bool Store, string Actor) : ICommand;

/// <summary>One stored review of a week and epoch (spec P10-03). Read only.</summary>
/// <param name="IsoWeek">The ISO week.</param>
/// <param name="EpochId">The epoch, or null.</param>
public sealed record GetPaperReviewQuery(string IsoWeek, long? EpochId) : IQuery<Result<PaperReviewView>>;

/// <summary>The stored reviews, newest week first (spec P10-03). Read only.</summary>
/// <param name="EpochId">One epoch, or null for all.</param>
/// <param name="Limit">1 to 52.</param>
public sealed record ListPaperReviewsQuery(long? EpochId, int Limit) : IQuery<Result<PaperReviewList>>;

/// <summary>The discipline report of a window (spec P10-03). Read only.</summary>
/// <param name="EpochId">The epoch, or null for the newest.</param>
/// <param name="From">The first day, or the epoch's start.</param>
/// <param name="To">The last day, or the last closed session.</param>
public sealed record GetDisciplineQuery(long? EpochId, DateOnly? From, DateOnly? To) : IQuery<Result<DisciplineView>>;

/// <summary>A weekly paper review as the API and the verb show it.</summary>
/// <param name="Id">The row id; null for a dry run that stored nothing.</param>
/// <param name="Week">The ISO week.</param>
/// <param name="EpochId">The epoch; null when there is none yet.</param>
/// <param name="BuiltAt">When it was built.</param>
/// <param name="BuiltBy">Who built it; null for a dry run.</param>
/// <param name="Stored">Whether this is the stored record.</param>
/// <param name="Repeated">The week already had a review: that one is shown and nothing was written.</param>
/// <param name="Text">The text.</param>
/// <param name="Sections">The sections.</param>
public sealed record PaperReviewView(long? Id, string Week, long? EpochId, DateTimeOffset BuiltAt, string? BuiltBy, bool Stored, bool Repeated, string Text, IReadOnlyList<ReviewSection> Sections);

/// <summary>One row of the list of stored reviews.</summary>
/// <param name="Id">The row id.</param>
/// <param name="Week">The ISO week.</param>
/// <param name="EpochId">The epoch.</param>
/// <param name="BuiltAt">When it was built.</param>
public sealed record PaperReviewListItem(long Id, string Week, long EpochId, DateTimeOffset BuiltAt);

/// <summary>The stored reviews, newest week first.</summary>
/// <param name="Items">The rows.</param>
public sealed record PaperReviewList(IReadOnlyList<PaperReviewListItem> Items);

/// <summary>Rupees of one account on a discipline line.</summary>
/// <param name="AccountId">The paper account.</param>
/// <param name="Amount">Rupees after the stored sheet's charges when charged.</param>
public sealed record DisciplineRupeesView(Guid AccountId, decimal Amount);

/// <summary>One line of the discipline report on the wire.</summary>
/// <param name="Category">The category or line name.</param>
/// <param name="Kind">category, flag, deviation or total.</param>
/// <param name="Title">The words.</param>
/// <param name="N">Tickets (or fills).</param>
/// <param name="SumR">Sum of R of the graded tickets.</param>
/// <param name="RMetric">Mean R: insufficient below 15 graded tickets, provisional below 30.</param>
/// <param name="Rupees">Rupees per account.</param>
/// <param name="Note">What is counted apart.</param>
public sealed record DisciplineLineView(string Category, string Kind, string Title, int N, MetricView SumR, MetricView RMetric, IReadOnlyList<DisciplineRupeesView> Rupees, string Note);

/// <summary>One row of the paper execution waterfall on the wire.</summary>
/// <param name="Id">plan, slippage, charges, net, missed_triggers or real_slippage.</param>
/// <param name="Label">The words.</param>
/// <param name="Value">Mean R.</param>
/// <param name="N">Trades.</param>
public sealed record ExecutionRowView(string Id, string Label, MetricView Value, int N);

/// <summary>What a discipline report rests on.</summary>
/// <param name="Sessions">Closed sessions counted.</param>
/// <param name="PrimaryTickets">Primary tickets of the window (the seven categories add up to this).</param>
/// <param name="ClosedTrades">Closed primary paper trades of the window, once per ticket.</param>
public sealed record DisciplineEvidenceView(int Sessions, int PrimaryTickets, int ClosedTrades);

/// <summary>The discipline report of a window (spec P10-03, R19). Every R has its n and its state; there is no composite score.</summary>
/// <param name="Basis">The label of the record.</param>
/// <param name="EpochId">The epoch; null when there is none yet.</param>
/// <param name="From">The first day of the window.</param>
/// <param name="To">The last day of the window.</param>
/// <param name="Lines">The lines in their fixed order.</param>
/// <param name="Execution">The execution waterfall.</param>
/// <param name="Evidence">What it rests on.</param>
/// <param name="Unavailable">Why the report could not be built; null when it was.</param>
/// <param name="Limits">The honest limits.</param>
public sealed record DisciplineView(string Basis, long? EpochId, DateOnly From, DateOnly To, IReadOnlyList<DisciplineLineView> Lines, IReadOnlyList<ExecutionRowView> Execution, DisciplineEvidenceView Evidence, string? Unavailable, IReadOnlyList<string> Limits);

/// <summary>Maps the discipline report to the wire.</summary>
internal static class DisciplineViews
{
    public const string Basis = "paper only: skipped tickets are graded on plan prices (simulated on plan prices), taken trades are simulated on the official 1-minute bars; no order was placed or exists";

    public static IReadOnlyList<string> Limits { get; } =
    [
        "A skipped ticket's R is a modelled grade, not a fill; a mean needs at least 15 graded tickets and 30 for a conclusion. Read the lines as questions, not verdicts.",
        "There is no composite discipline score: none is defined (P8-00 D10).",
        "Added trades are real fills with no ticket; they have no plan levels, so no R.",
        "Paper cannot validate execution: slippage is modelled and nothing real was filled.",
    ];

    public static DisciplineView Of(long? epochId, DateOnly from, DateOnly to, int sessions, DisciplineReport report) =>
        new(Basis, epochId, from, to, [.. report.Lines.Select(Line)], [.. report.Execution.Select(r => new ExecutionRowView(r.Id, r.Label, ReportViews.Metric(r.Value), r.N))],
            new DisciplineEvidenceView(sessions, report.PrimaryCount, report.Outcomes.Count(o => o.ClosedTrade)), null, Limits);

    public static DisciplineView Empty(long? epochId, DateOnly from, DateOnly to, string? unavailable) =>
        new(Basis, epochId, from, to, [], [], new DisciplineEvidenceView(0, 0, 0), unavailable, Limits);

    private static DisciplineLineView Line(DisciplineLine l) =>
        new(l.Id, l.Kind, l.Title, l.N, ReportViews.Metric(l.SumR), ReportViews.Metric(l.MeanR), [.. l.Rupees.Select(r => new DisciplineRupeesView(r.AccountId, r.Amount))], l.Note);
}
