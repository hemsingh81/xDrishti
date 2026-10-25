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
/// <param name="UnmatchedFills">Approved tickets that have a stored fill on the day and no journal entry yet.</param>
internal sealed record PlanNoticePlan(long Id, DateOnly ForDate, DateTimeOffset ReviewCutoff, PlanState State, int Undecided, int Lapsed, int Armed, int Invalidated, int UnmatchedFills);

/// <summary>The latest recorded attempt of the newest planned session.</summary>
/// <param name="ForDate">The planned session.</param>
/// <param name="Outcome">published, failed_closed or dry_run.</param>
/// <param name="FailedGate">The gate that failed.</param>
/// <param name="Reason">Why, in words.</param>
/// <param name="FinishedAt">When the attempt ended.</param>
internal sealed record PlanNoticeRun(DateOnly ForDate, string Outcome, string? FailedGate, string Reason, DateTimeOffset FinishedAt);

/// <summary>
/// What the bell says about the plan (P8-15, ADR 0101). Pure: the same facts and clock give the same list, so an item resolves by itself when the stored state changes and nothing has to be reconciled or stored.
/// The notices are counts and words only; none carries a symbol, a size or a strategy text, and none has a command.
/// </summary>
internal static class PlanNoticeRules
{
    /// <summary>Minutes to the cutoff at or below which the reminder is a warning.</summary>
    public const int UrgentMinutes = 30;

    private const int MaxReasonLength = 160;

    public static IReadOnlyList<PlanNotice> Derive(DateTimeOffset now, IReadOnlyList<PlanNoticePlan> plans, PlanNoticeRun? latestRun)
    {
        ArgumentNullException.ThrowIfNull(plans);
        var today = IndiaTime.Today(now);
        var items = new List<(PlanNotice Notice, DateOnly Session)>();
        if (latestRun is { Outcome: "failed_closed" })
        {
            var date = latestRun.ForDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var gate = string.IsNullOrWhiteSpace(latestRun.FailedGate) ? "a gate" : "the " + latestRun.FailedGate + " gate";
            items.Add((new PlanNotice($"plan:nightly:{date}", "plan_nightly_failed", "error", $"The nightly plan for {Day(latestRun.ForDate)} failed closed at {gate}: {Short(latestRun.Reason)} No plan was published.", "/plans", null, latestRun.FinishedAt), latestRun.ForDate));
        }

        foreach (var plan in plans)
        {
            var id = plan.Id.ToString(CultureInfo.InvariantCulture);
            var route = $"/plans?plan={id}";
            if (plan.State is PlanState.Published or PlanState.InReview && plan.Undecided > 0 && now < plan.ReviewCutoff)
            {
                var minutes = (int)Math.Ceiling((plan.ReviewCutoff - now).TotalMinutes);
                var severity = minutes <= UrgentMinutes ? "warning" : "info";
                items.Add((new PlanNotice($"plan:review:{id}", "plan_awaiting_review", severity, $"The plan for {Day(plan.ForDate)} awaits your review: {Tickets(plan.Undecided)} undecided, {Minutes(minutes)} to the cutoff at {Time(plan.ReviewCutoff)}. Unreviewed tickets lapse.", route, plan.Id, plan.ReviewCutoff), plan.ForDate));
            }

            var live = plan.ForDate >= today && plan.State != PlanState.Expired;
            if (live && plan.Lapsed > 0)
            {
                items.Add((new PlanNotice($"plan:lapsed:{id}", "plan_tickets_lapsed", "warning", $"{Tickets(plan.Lapsed)} of the plan for {Day(plan.ForDate)} lapsed: the cutoff passed with no decision. Lapsing is never approval.", route, plan.Id, plan.ReviewCutoff), plan.ForDate));
            }

            if (live && plan.Armed > 0)
            {
                items.Add((new PlanNotice($"plan:armed:{id}", "plan_tickets_armed", "info", $"{Tickets(plan.Armed)} of the plan for {Day(plan.ForDate)} armed at the pre-open check: watched, no order is placed.", route, plan.Id, null), plan.ForDate));
            }

            if (live && plan.Invalidated > 0)
            {
                items.Add((new PlanNotice($"plan:invalidated:{id}", "plan_tickets_invalidated", "warning", $"{Tickets(plan.Invalidated)} of the plan for {Day(plan.ForDate)} invalidated at the pre-open check: a gap, an event or a missing input voided it.", route, plan.Id, null), plan.ForDate));
            }

            if (plan.UnmatchedFills > 0)
            {
                items.Add((new PlanNotice($"plan:fills:{id}", "plan_fills_unmatched", "info", $"{Tickets(plan.UnmatchedFills)} approved for {Day(plan.ForDate)} have a fill from the Dhan sync that is not matched yet. The journal matches it to the ticket.", route, plan.Id, null), plan.ForDate));
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
        var line = reason.ReplaceLineEndings(" ").Trim();
        if (line.Length == 0)
        {
            return "see the plan runs.";
        }

        var cut = line.Length <= MaxReasonLength ? line : line[..MaxReasonLength] + "…";
        return cut.EndsWith('.') ? cut : cut + ".";
    }
}
