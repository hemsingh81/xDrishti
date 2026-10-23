using XDrishti.Domain.Research;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Scanning;

/// <summary>
/// The exit candidate of a strategy that a cell's exit label names (P7-04). A candidate is a policy and one value of each of its parameters, spelt like <see cref="ExitCandidate.Id"/> (<c>FIXED_RR{rr=2}</c>). A label matches a candidate by
/// that spelling, or by the policy name alone when the strategy has exactly one candidate of that policy. Anything else matches nothing: the ticket then has no target (never the first exit).
/// </summary>
internal sealed record ExitMatch(string Id, ExitPolicy Policy, IReadOnlyDictionary<string, double> Values)
{
    /// <summary>The reward to risk when the candidate is a fixed multiple of the risk with an explicit <c>rr</c>.</summary>
    public double? RewardRisk => Policy == ExitPolicy.FixedRr && Values.TryGetValue("rr", out var rr) && rr > 0 ? rr : null;

    public static ExitMatch? Find(IReadOnlyList<ExitPlan> plans, string label)
    {
        var wanted = Normalize(label);
        var all = plans.SelectMany(Candidates).ToList();
        var byId = all.Where(c => Normalize(c.Id) == wanted).ToList();
        if (byId.Count == 1)
        {
            return byId[0];
        }

        var byPolicy = all.Where(c => Normalize(Name(c.Policy)) == wanted).ToList();
        return byPolicy.Count == 1 && all.Count(c => c.Policy == byPolicy[0].Policy) == 1 ? byPolicy[0] : null;
    }

    private static string Normalize(string text) => text.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    private static List<ExitMatch> Candidates(ExitPlan plan)
    {
        var names = plan.Values.Keys.Order(StringComparer.Ordinal).ToList();
        IEnumerable<List<(string Name, double Value)>> combos = [[]];
        foreach (var name in names)
        {
            combos = combos.SelectMany(prefix => plan.Values[name].Select(v => new List<(string, double)>([.. prefix, (name, v)])));
        }

        var result = new List<ExitMatch>();
        foreach (var combo in combos)
        {
            var id = Name(plan.Policy) + "{" + string.Join(',', combo.Select(c => c.Name + "=" + CanonicalText.Number(c.Value))) + "}";
            result.Add(new ExitMatch(id, plan.Policy, combo.ToDictionary(c => c.Name, c => c.Value, StringComparer.Ordinal)));
        }

        return result;
    }

    private static string Name(ExitPolicy policy) => policy switch
    {
        ExitPolicy.FixedRr => "FIXED_RR",
        ExitPolicy.PartialBe => "PARTIAL_BE",
        ExitPolicy.PartialTrail => "PARTIAL_TRAIL",
        ExitPolicy.AtrTrail => "ATR_TRAIL",
        ExitPolicy.StructureTrail => "STRUCTURE_TRAIL",
        ExitPolicy.EmaTrail => "EMA_TRAIL",
        _ => "PYRAMID",
    };
}
