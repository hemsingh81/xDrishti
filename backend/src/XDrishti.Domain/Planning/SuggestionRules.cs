using System.Globalization;

namespace XDrishti.Domain.Planning;

/// <summary>
/// The deterministic suggestion rules (spec P7-09, ADR 0092): rules only, no model. The same plan gives the same suggestions in the same order, whatever order the tickets come in.
/// A suggestion is advice to the owner; nothing here changes a plan, approves a ticket or reaches a broker.
/// </summary>
public static class SuggestionRules
{
    /// <summary>Statistics older than this many calendar days before the plan date are called stale (the weekly refresh makes 7 normal; the nightly gate already refuses older than 5 sessions).</summary>
    public const int StaleAfterDays = 7;

    /// <summary>Most tickets named in one suggestion text.</summary>
    public const int MaxNamed = 8;

    private const string UnavailableSuffix = ":unavailable";

    /// <summary>Evaluates the rules.</summary>
    public static IReadOnlyList<NewSuggestion> Evaluate(SuggestionInput plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var result = new List<NewSuggestion>();

        if (plan.Basis.Contains(PlanWording.GateUnavailableMarker, StringComparison.Ordinal))
        {
            result.Add(new NewSuggestion(SuggestionKinds.MarketGateUnavailable, "plan", "The market gate had no data (VIX, breadth, event calendar): this plan was not treated as a normal day. Judge the market yourself before approving anything."));
        }

        if (plan.Basis.Contains(PlanWording.OpenBookUnavailableMarker, StringComparison.Ordinal))
        {
            result.Add(new NewSuggestion(SuggestionKinds.OpenBookUnavailable, "plan", "Open book unavailable: no primary tickets. The system keeps no position record, so the portfolio limits cannot be checked. To let the plan use an empty book, the owner sets Planning:OpenBook:AssumedEmpty to true in the configuration."));
        }

        if (!plan.Items.Any(i => i.Group == TicketGroup.Primary))
        {
            result.Add(new NewSuggestion(SuggestionKinds.NoPrimaryTickets, "plan", "No primary ticket today. See the reserve and rejected lists for the reasons; nothing needs approving."));
        }

        // One suggestion per unavailable input: "tickets held because cost data is unavailable: AAA, BBB". Reason codes look like "cost:unavailable".
        var byInput = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var item in plan.Items.Where(i => i.Group != TicketGroup.Primary))
        {
            foreach (var reason in item.Reasons.Where(r => r.EndsWith(UnavailableSuffix, StringComparison.Ordinal)))
            {
                var code = reason[..^UnavailableSuffix.Length];
                if (!byInput.TryGetValue(code, out var symbols))
                {
                    byInput[code] = symbols = new SortedSet<string>(StringComparer.Ordinal);
                }

                symbols.Add(item.Symbol);
            }
        }

        foreach (var (code, symbols) in byInput)
        {
            var named = string.Join(", ", symbols.Take(MaxNamed)) + (symbols.Count > MaxNamed ? string.Create(CultureInfo.InvariantCulture, $" and {symbols.Count - MaxNamed} more") : string.Empty);
            result.Add(new NewSuggestion(
                SuggestionKinds.TicketsHeldUnavailableInput,
                code,
                string.Create(CultureInfo.InvariantCulture, $"{symbols.Count} ticket(s) held because '{code}' is unavailable (no data source): {named}. Nothing was assumed; a source for this input would let them through.")));
        }

        if (plan.StatsAsOf is not { } asOf)
        {
            result.Add(new NewSuggestion(SuggestionKinds.CellStatsStale, "plan", "No cell statistics are stored before the plan date."));
        }
        else if (plan.ForDate.DayNumber - asOf.DayNumber > StaleAfterDays)
        {
            result.Add(new NewSuggestion(SuggestionKinds.CellStatsStale, "plan", string.Create(CultureInfo.InvariantCulture, $"The cell statistics are from {asOf:yyyy-MM-dd}, more than {StaleAfterDays} days before the plan date {plan.ForDate:yyyy-MM-dd}. Refresh them so the plan rests on recent trades.")));
        }

        return result;
    }
}
