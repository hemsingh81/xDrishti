using System.Globalization;
using XDrishti.Domain.Market;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Reporting;

namespace XDrishti.Domain.Paper;

/// <summary>
/// Builds the weekly paper review (spec P10-03) from stored facts: where the epoch stands, what happened in the week, the discipline report, the paper execution gap, the configuration changes and the backtest line. A pure function: it reads no
/// clock and no store, applies nothing, and the same input gives the same sections and text.
/// </summary>
public static class PaperReviewBuilder
{
    /// <summary>The days of the paper phase.</summary>
    public const int PhaseDays = 84;

    /// <summary>The counted trades the phase needs.</summary>
    public const int TargetTrades = 60;

    /// <summary>The fewest counted trades an ETA needs.</summary>
    public const int EtaMinTrades = 10;

    /// <summary>The fewest elapsed days an ETA needs (three weeks).</summary>
    public const int EtaMinDays = 21;

    private const string StandingLimits = "The pace is a straight line over the days elapsed and trading is lumpy; an ETA is a straight-line guess, not a forecast.";

    private const string WeekLimits = "Fills are the simulator's: touch is fill, with no queue, latency, partial fill or rejection; a session captured after this review was built shows as pending.";

    private const string DisciplineLimits = "A skipped ticket's R is a modelled grade on plan prices, not a fill; with fewer than 15 graded tickets a line has no mean, and under 30 it is an indication. Read the lines as questions, not verdicts.";

    private const string ExecutionLimits = "Paper cannot validate execution: slippage is modelled and nothing real was filled.";

    private const string ConfigLimits = "Changes are detected and labelled from stored configuration snapshots; nothing is applied or blocked.";

    private const string ComparisonLimits = "The comparison is summarised in one line; its band and drawdown are not recomputed here.";

    /// <summary>Builds the review.</summary>
    public static PaperReview Build(PaperReviewInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var week = input.Week;
        var title = string.Create(CultureInfo.InvariantCulture, $"Weekly paper review {week.Key} ({week.Start:yyyy-MM-dd} to {week.End:yyyy-MM-dd})");
        var built = ReviewText.BuiltLine(input.BuiltAt, week.End);
        if (input.Epoch is null)
        {
            var none = new ReviewSection("epoch", "No paper epoch", ["No paper epoch exists yet: nothing was captured, so there is nothing to review."], [], "A review needs the first paper capture to have run.");
            return new PaperReview([none], ReviewText.Compose(title, built, [none]), null);
        }

        DisciplineReport? discipline = null;
        ReviewSection standing, session, disc, execution;
        if (input.Record is null)
        {
            var why = "unavailable: " + (input.RecordUnavailable ?? "the paper record could not be read");
            standing = Unavailable("standing", "Where it stands", why, StandingLimits);
            session = Unavailable("week", "This week", why, WeekLimits);
            disc = Unavailable("discipline", "Discipline: skipped, added and resized", why, DisciplineLimits);
            execution = Unavailable("execution", "Execution gap (paper)", why, ExecutionLimits);
        }
        else
        {
            discipline = DisciplineReport.Build(input.Record.Tickets, input.Record.Trades, input.Record.Skips, input.Record.Fills, input.Record.FillsUnavailable);
            standing = Standing(input, input.Record);
            session = Week(input, input.Record, discipline);
            disc = Discipline(discipline);
            execution = Execution(discipline);
        }

        var sections = new List<ReviewSection> { standing, session, disc, execution, Config(input), Comparison(input) };
        return new PaperReview(sections, ReviewText.Compose(title, built, sections), discipline);
    }

    private static ReviewSection Unavailable(string id, string title, string why, string limits) => new(id, title, [why], [], limits);

    /// <summary>The trades that count toward the 60: closed, primary, measured, once per ticket.</summary>
    public static IReadOnlyList<ReviewTrade> Counted(IReadOnlyList<ReviewTrade> trades)
    {
        ArgumentNullException.ThrowIfNull(trades);
        return [.. trades.Where(t => t.Closed && t.Measured && t.TicketGroup == "primary").GroupBy(t => t.TradeKey, StringComparer.Ordinal).Select(g => g.OrderBy(t => t.AccountId).First()).OrderBy(t => t.Exit!.ExitAt).ThenBy(t => t.TradeKey, StringComparer.Ordinal)];
    }

    private static ReviewSection Standing(PaperReviewInput input, PaperRecordFacts record)
    {
        var epoch = input.Epoch!;
        var counted = Counted(record.Trades).Count;
        var asOf = DateOnly.FromDateTime(IndiaTime.ToIst(input.BuiltAt).DateTime) < input.Week.End ? DateOnly.FromDateTime(IndiaTime.ToIst(input.BuiltAt).DateTime) : input.Week.End;
        var days = Math.Max(0, asOf.DayNumber - epoch.StartedOn.DayNumber + 1);
        var weekNo = Math.Max(1, (days + 6) / 7);
        var weeks = days / 7.0;
        var pace = days >= 7 ? Metric.Of(counted / weeks) : Metric.Unavailable("less than a week of the epoch has elapsed");
        Metric eta;
        string etaLine;
        if (counted >= TargetTrades)
        {
            eta = Metric.Of(0);
            etaLine = string.Create(CultureInfo.InvariantCulture, $"ETA to {TargetTrades}: reached ({counted} counted).");
        }
        else if (counted < EtaMinTrades || days < EtaMinDays)
        {
            var reason = string.Create(CultureInfo.InvariantCulture, $"needs at least {EtaMinTrades} counted trades and {EtaMinDays / 7} weeks elapsed (counted {counted}, day {days})");
            eta = Metric.Unavailable(reason);
            etaLine = "ETA to " + TargetTrades.ToString(CultureInfo.InvariantCulture) + ": unavailable: " + reason + ".";
        }
        else
        {
            var perWeek = counted / weeks;
            var toGo = (TargetTrades - counted) / perWeek;
            eta = Metric.Of(toGo);
            var date = asOf.AddDays((int)Math.Ceiling(toGo * 7));
            etaLine = string.Create(CultureInfo.InvariantCulture, $"ETA to {TargetTrades}: about {toGo:0.0} weeks, around {date:yyyy-MM-dd}. A straight-line guess.");
        }

        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"Epoch {epoch.Id} ({epoch.Kind}) started {epoch.StartedOn:yyyy-MM-dd}: {epoch.Reason}"),
            string.Create(CultureInfo.InvariantCulture, $"Day {days} of {PhaseDays}; week {weekNo} of {PhaseDays / 7}."),
            string.Create(CultureInfo.InvariantCulture, $"Counted trades: {counted} of {TargetTrades} (closed, primary, measured, once per ticket)."),
            pace.State == MetricState.Value ? string.Create(CultureInfo.InvariantCulture, $"Pace: {pace.Number:0.00} counted trades per week.") : "Pace: unavailable: " + pace.Reason + ".",
            etaLine,
        };
        var metrics = new List<ReviewMetric>
        {
            ReviewMetric.Of("counted", "Counted trades", Metric.Of(counted), counted),
            ReviewMetric.Of("days", "Days elapsed of 84", Metric.Of(days)),
            ReviewMetric.Of("pace", "Counted trades per week", pace, counted),
            ReviewMetric.Of("eta_weeks", "Weeks to 60 (straight line)", eta, counted),
        };
        return new ReviewSection("standing", "Where it stands", lines, metrics, StandingLimits);
    }

    private static ReviewSection Week(PaperReviewInput input, PaperRecordFacts record, DisciplineReport discipline)
    {
        var primary = discipline.Outcomes;
        var entered = primary.Count(o => o.Trades.Count > 0);
        var closedTickets = primary.Count(o => o.ClosedTrade);
        var armed = primary.Count(o => o.Trades.Count > 0 || o.Ticket.State is TicketState.Armed or TicketState.Triggered or TicketState.Expired or TicketState.Closed);
        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"Window {input.From:yyyy-MM-dd} to {input.To:yyyy-MM-dd}: {input.SessionsCounted} of {input.SessionsInWeek} sessions of the week counted (sessions not yet closed are not)."),
            string.Create(CultureInfo.InvariantCulture, $"Funnel of primary tickets: proposed {primary.Count}, approved {primary.Count(o => o.Ticket.Approved)}, armed {armed}, entered {entered}, closed {closedTickets}."),
        };
        var rows = record.Trades.Where(t => t.Closed && IndiaTime.Today(t.Exit!.ExitAt) >= input.From && IndiaTime.Today(t.Exit.ExitAt) <= input.To)
            .OrderBy(t => t.Exit!.ExitAt).ThenBy(t => t.Symbol, StringComparer.Ordinal).ThenBy(t => t.AccountId)
            .Select(t => (IReadOnlyList<string>)[
                IndiaTime.Today(t.Exit!.ExitAt).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                t.Symbol,
                t.Side,
                t.Qty.ToString(CultureInfo.InvariantCulture),
                ReviewText.Plus(t.Exit.RSim) + "R",
                ReviewText.Inr(t.Exit.Result),
                t.Exit.ExitReason])
            .ToList();
        lines.Add(rows.Count == 0 ? "Closed paper trades this window: none." : string.Create(CultureInfo.InvariantCulture, $"Closed paper trades this window: {rows.Count} (one row per account)."));
        foreach (var row in rows)
        {
            lines.Add("- " + string.Join(' ', row));
        }

        var table = new ReviewTable(["Day", "Symbol", "Side", "Qty", "R", "Rupees (after stored charges)", "Exit"], rows);
        return new ReviewSection("week", "This week", lines, [], WeekLimits, table);
    }

    private static ReviewSection Discipline(DisciplineReport report)
    {
        var lines = new List<string> { string.Create(CultureInfo.InvariantCulture, $"Primary tickets in the window: {report.PrimaryCount}; the seven categories add up to it.") };
        var metrics = new List<ReviewMetric>();
        foreach (var line in report.Lines)
        {
            lines.Add(Text(line));
            if (line.Kind != "flag")
            {
                metrics.Add(ReviewMetric.Of(line.Id + "_sum", line.Title + " (sum R)", line.SumR, line.N));
                metrics.Add(ReviewMetric.Of(line.Id + "_mean", line.Title + " (mean R)", line.MeanR, line.N));
            }
        }

        return new ReviewSection("discipline", "Discipline: skipped, added and resized", lines, metrics, DisciplineLimits);
    }

    /// <summary>One line of text for a discipline line.</summary>
    public static string Text(DisciplineLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.Kind == "flag")
        {
            return string.Create(CultureInfo.InvariantCulture, $"- {line.Title}: {line.N}");
        }

        var rupees = line.Rupees.Count == 0 ? string.Empty : "; rupees " + string.Join(", ", line.Rupees.Select(r => ReviewText.Inr(r.Amount) + " (account " + r.AccountId.ToString("N", CultureInfo.InvariantCulture)[..8] + ")"));
        var note = line.Note.Length == 0 ? string.Empty : "; " + line.Note;
        return string.Create(CultureInfo.InvariantCulture, $"- {line.Title}: n {line.N}; sum {ReviewText.R(line.SumR)}; mean {ReviewText.R(line.MeanR)}{rupees}{note}");
    }

    private static ReviewSection Execution(DisciplineReport report)
    {
        var lines = report.Execution.Select(r => string.Create(CultureInfo.InvariantCulture, $"- {r.Label}: {Mean(r.Value)}{(r.N > 0 ? $" (n {r.N})" : string.Empty)}")).ToList();
        var metrics = report.Execution.Select(r => ReviewMetric.Of(r.Id, r.Label, r.Value, r.N)).ToList();
        return new ReviewSection("execution", "Execution gap (paper)", lines, metrics, ExecutionLimits);
    }

    private static string Mean(Metric metric) => metric.State is MetricState.Value or MetricState.Provisional ? metric.Number!.Value.ToString("+0.0000;-0.0000;0.0000", CultureInfo.InvariantCulture) + "R" + (metric.State == MetricState.Provisional ? $" (provisional: {metric.Reason})" : string.Empty) : ReviewText.R(metric);

    private static ReviewSection Config(PaperReviewInput input)
    {
        if (input.Config is null)
        {
            return Unavailable("config", "Configuration changes", "unavailable: " + (input.ConfigUnavailable ?? "the governance record could not be read"), ConfigLimits);
        }

        var lines = new List<string>();
        if (input.Config.Started.Count == 0)
        {
            lines.Add("No new epoch started this week.");
        }

        foreach (var epoch in input.Config.Started.OrderBy(e => e.Id))
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"Epoch {epoch.Id} ({epoch.Kind}) started {epoch.StartedOn:yyyy-MM-dd}: {epoch.Reason}"));
        }

        lines.Add(string.Create(CultureInfo.InvariantCulture, $"Noted in the current epoch: {input.Config.RiskDown} risk-down change(s); {input.Config.Unexplained} change(s) with no explanation and no note."));
        return new ReviewSection("config", "Configuration changes", lines, [], ConfigLimits);
    }

    private static ReviewSection Comparison(PaperReviewInput input) =>
        new("comparison", "Backtest versus paper", [input.Comparison], [], ComparisonLimits);
}
