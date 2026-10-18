using XDrishti.Domain.Market.Bars;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Tests.Scanning;

/// <summary>Criterion 13 of P3-03a (the scanner's part): the scanner knows nothing of exits, so the candidates, traces included, are equal whatever the exit policy of the strategy.</summary>
public sealed class ScannerSeamTests
{
    private static readonly string[] Days = ["F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12", "F13"];

    private static string Candidates(StrategyDefinition definition, Bar[] bars) =>
        string.Join("\n", Scan.Both(definition, bars).SelectMany(s => s.Candidates).Select(c => Render.Candidate(c)));

    private static ExitSpec Exit(ExitPolicy policy, params (string Name, double Value)[] parameters) =>
        new(policy, [.. parameters.Select(p => new ExitParameter(p.Name, [Quantity.Of(p.Value)]))]);

    [Fact]
    public void The_candidates_of_f1_to_f13_are_equal_for_a_reward_risk_of_1_5_2_and_3()
    {
        foreach (var name in Days)
        {
            var bars = Fixtures.OrbDays.Single(f => f.Name == name).Bars;

            var reference = Candidates(OrbV1.Create(rewardRisk: 2.0), bars);

            Candidates(OrbV1.Create(rewardRisk: 1.5), bars).ShouldBe(reference, name);
            Candidates(OrbV1.Create(rewardRisk: 3.0), bars).ShouldBe(reference, name);
        }
    }

    [Fact]
    public void The_candidates_are_equal_for_a_trailing_exit_and_a_partial_trail_exit_run_on_the_scanner()
    {
        var trail = OrbV1.Create().WithBuy(b => b with { Exits = [Exit(ExitPolicy.AtrTrail, ("atr_mult", 2))] }) with { Sell = OrbV1.Create().Sell! with { Exits = [Exit(ExitPolicy.AtrTrail, ("atr_mult", 2))] } };
        var partial = OrbV1.Create().WithBuy(b => b with { Exits = [Exit(ExitPolicy.PartialTrail, ("book_pct", 50), ("t1_rr", 1), ("trail_atr_mult", 2))] })
            with
        { Sell = OrbV1.Create().Sell! with { Exits = [Exit(ExitPolicy.PartialTrail, ("book_pct", 50), ("t1_rr", 1), ("trail_atr_mult", 2))] } };

        foreach (var name in Days)
        {
            var bars = Fixtures.OrbDays.Single(f => f.Name == name).Bars;
            var reference = Candidates(OrbV1.Create(), bars);

            Candidates(trail, bars).ShouldBe(reference, name + " with ATR_TRAIL");
            Candidates(partial, bars).ShouldBe(reference, name + " with PARTIAL_TRAIL");
        }
    }

    [Fact]
    public void The_scanner_has_no_member_that_takes_or_returns_an_exit()
    {
        var members = typeof(XDrishti.Simulation.Scanning.EntryScanner).GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly);

        members.Select(m => m.Name).ShouldNotContain(n => n.Contains("Exit", StringComparison.OrdinalIgnoreCase) || n.Contains("Target", StringComparison.OrdinalIgnoreCase));
        typeof(XDrishti.Simulation.EntryCandidate).GetProperties().Select(p => p.Name).ShouldNotContain(n => n.Contains("Exit", StringComparison.OrdinalIgnoreCase) || n.Contains("Target", StringComparison.OrdinalIgnoreCase));
    }
}
