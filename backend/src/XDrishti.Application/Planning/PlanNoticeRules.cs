using System.Globalization;
using XDrishti.Domain.Market;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>A plan as the notice rules read it: its stored cutoff and state and how many tickets (primary and reserve) stand in each state that matters.</summary>
/// <param name="Id">The plan.</param>
/// <param name="ForDate">The session.</param>
/// <param name="ReviewCutoff">The stored cutoff.</param>
/// <param name="State">The plan state.</param>
/// <param name="Undecided">Tickets still proposed or modified.</param>
/// <param name="Lapsed">Tickets that lapsed at the cutoff.</param>
/// <param name="Armed">Tickets armed (watched, no order).</param>
/// <param name="Invalidated">Tickets the pre-open check voided.</param>
/// <param name="UnmatchedFills">Approved tickets with a stored fill that the journal would link as the ticket's entry (the pure matcher, after the approval) and no stored journal entry yet.</param>
/// <param name="AmbiguousFills">Fills of the day that fit more than one approved ticket: the journal refuses to guess, so it will never link them.</param>
internal sealed record PlanNoticePlan(long Id, DateOnly ForDate, DateTimeOffset ReviewCutoff, PlanState State, int Undecided, int Lapsed, int Armed, int Invalidated, int UnmatchedFills, int AmbiguousFills = 0);

/// <summary>The latest recorded attempt of the newest planned session.</summary>
/// <param name="ForDate">The planned session.</param>
/// <param name="Outcome">published, failed_closed or dry_run.</param>
/// <param name="FailedGate">The gate that failed.</param>
/// <param name="Reason">Why, in words.</param>
/// <param name="FinishedAt">When the attempt ended.</param>
/// <param name="Attempt">1 for the first attempt of the session, then 2, 3: a new failed attempt is a new notice.</param>
internal sealed record PlanNoticeRun(DateOnly ForDate, string Outcome, string? FailedGate, string Reason, DateTimeOffset FinishedAt, int Attempt = 1);

/// <summary>
/// What the bell says about the plan (P8-15, ADR 0101). Pure: the same facts and clock give the same list, so an item resolves by itself when the stored state changes and nothing has to be reconciled or stored.
/// The notices are counts and words only; none carries a symbol, a size or a strategy text, and none has a command. The text that is not ours is the stored reason and gate of a failed run (written by the pipeline, trusted, shown clipped to one line).
/// <b>The id carries what changes</b> (the severity of the reminder, the count of tickets, the attempt of a run): the bell keeps what is read by id, so a situation that gets worse or bigger is a new id and unread again (the convention of <c>deriveNotifications</c>).
/// </summary>
internal static class PlanNoticeRules
{
    /// <summary>Minutes to the cutoff at or below which the reminder is a warning.</summary>
    public const int UrgentMinutes = 30;

    private const int MaxReasonLength = 160;
    private const int MaxGateLength = 40;

    public static IReadOnlyList<PlanNotice> Derive(DateTimeOffset now, IReadOnlyList<PlanNoticePlan> plans, PlanNoticeRun? latestRun)
    {
        ArgumentNullException.ThrowIfNull(plans);
        var today = IndiaTime.Today(now);
        var items = new List<(PlanNotice Notice, DateOnly Session)>();
        if (latestRun is { Outcome: "failed_closed" })
        {
            var date = latestRun.ForDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var gate = string.IsNullOrWhiteSpace(latestRun.FailedGate) ? "a gate" : "the " + Clip(latestRun.FailedGate, MaxGateLength) + " gate";
            items.Add((new PlanNotice($"plan:nightly:{date}:{latestRun.Attempt.ToString(CultureInfo.InvariantCulture)}", "plan_nightly_failed", "error", $"The nightly plan for {Day(latestRun.ForDate)} failed closed at {gate}: {Short(latestRun.Reason)} No plan was published.", "/plans", null, latestRun.FinishedAt), latestRun.ForDate));
        }

        foreach (var plan in plans)
        {
            var id = plan.Id.ToString(CultureInfo.InvariantCulture);
            var route = $"/plans?plan={id}";
            if (plan.State is PlanState.Published or PlanState.InReview && plan.Undecided > 0 && now < plan.ReviewCutoff)
            {
                var minutes = (int)Math.Ceiling((plan.ReviewCutoff - now).TotalMinutes);
                var severity = minutes <= UrgentMinutes ? "warning" : "info";
                items.Add((new PlanNotice($"plan:review:{id}:{severity}", "plan_awaiting_review", severity, $"The plan for {Day(plan.ForDate)} awaits your review: {Tickets(plan.Undecided)} undecided, {Minutes(minutes)} to the cutoff at {Time(plan.ReviewCutoff)}. Unreviewed tickets lapse.", route, plan.Id, plan.ReviewCutoff), plan.ForDate));
            }

            var live = plan.ForDate >= today && plan.State != PlanState.Expired;
            if (live && plan.Lapsed > 0)
            {
                items.Add((new PlanNotice($"plan:lapsed:{id}:{plan.Lapsed.ToString(CultureInfo.InvariantCulture)}", "plan_tickets_lapsed", "warning", $"{Tickets(plan.Lapsed)} of the plan for {Day(plan.ForDate)} lapsed: the cutoff passed with no decision. Lapsing is never approval.", route, plan.Id, plan.ReviewCutoff), plan.ForDate));
            }

            if (live && plan.Armed > 0)
            {
                items.Add((new PlanNotice($"plan:armed:{id}:{plan.Armed.ToString(CultureInfo.InvariantCulture)}", "plan_tickets_armed", "info", $"{Tickets(plan.Armed)} of the plan for {Day(plan.ForDate)} armed at the pre-open check: watched, no order is placed.", route, plan.Id, null), plan.ForDate));
            }

            if (live && plan.Invalidated > 0)
            {
                items.Add((new PlanNotice($"plan:invalidated:{id}:{plan.Invalidated.ToString(CultureInfo.InvariantCulture)}", "plan_tickets_invalidated", "warning", $"{Tickets(plan.Invalidated)} of the plan for {Day(plan.ForDate)} invalidated at the pre-open check: a gap, an event or a missing input voided it.", route, plan.Id, null), plan.ForDate));
            }

            if (plan.UnmatchedFills > 0)
            {
                items.Add((new PlanNotice($"plan:fills:{id}:{plan.UnmatchedFills.ToString(CultureInfo.InvariantCulture)}", "plan_fills_unmatched", "info", $"{Tickets(plan.UnmatchedFills)} approved for {Day(plan.ForDate)} {(plan.UnmatchedFills == 1 ? "has" : "have")} a fill from the Dhan sync that the journal will link to the ticket when it runs (xd plan journal).", route, plan.Id, null), plan.ForDate));
            }

            if (plan.AmbiguousFills > 0)
            {
                items.Add((new PlanNotice($"plan:fills-ambiguous:{id}:{plan.AmbiguousFills.ToString(CultureInfo.InvariantCulture)}", "plan_fills_unmatched", "info", $"{(plan.AmbiguousFills == 1 ? "1 fill" : plan.AmbiguousFills.ToString(CultureInfo.InvariantCulture) + " fills")} of {Day(plan.ForDate)} fit more than one approved ticket. The journal will not guess, so it will not link {(plan.AmbiguousFills == 1 ? "it" : "them")}.", route, plan.Id, null), plan.ForDate));
            }
        }

        return [.. items
            .OrderBy(i => Rank(i.Notice.Severity))
            .ThenByDescending(i => i.Session)
            .ThenBy(i => i.Notice.Id, StringComparer.Ordinal)
            .Select(i => i.Notice)];
    }

    private static int Rank(string severity) => severity switch { "error" => 0, "warning" => 1, _ => 2 };

    private static string Day(DateOnly date) => date.ToString("d MMM", CultureInfo.InvariantCulture);

    private static string Time(DateTimeOffset at) => IndiaTime.ToIst(at).ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string Tickets(int n) => n == 1 ? "1 ticket" : n.ToString(CultureInfo.InvariantCulture) + " tickets";

    private static string Minutes(int n) => n == 1 ? "1 minute" : n.ToString(CultureInfo.InvariantCulture) + " minutes";

    private static string Short(string reason)
    {
        var line = Clip(reason, MaxReasonLength);
        if (line.Length == 0)
        {
            return "see the plan runs.";
        }

        return line.EndsWith('.') ? line : line + ".";
    }

    /// <summary>One line, trimmed, at most <paramref name="max"/> characters (and an ellipsis when cut).</summary>
    private static string Clip(string text, int max)
    {
        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length <= max ? line : line[..max] + "…";
    }
}
