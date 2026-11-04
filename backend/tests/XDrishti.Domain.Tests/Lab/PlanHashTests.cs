using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Tests.Lab;

/// <summary>CF-06, D2 and AC3: the plan hash is deterministic, changes with any one input, and ignores the order of the parameters, values and exit flags.</summary>
public sealed class PlanHashTests
{
    private static readonly string H1 = new('a', 64);
    private static readonly string H2 = new('b', 64);

    private static PlanInput Plan() => new(
        H1,
        "basket members at the time of the post",
        H2,
        new DateOnly(2024, 1, 1),
        new DateOnly(2026, 6, 30),
        "default|" + H2 + "|checked=yes",
        [new PlanSweep("period", [10, 14, 20]), new PlanSweep("mult", [1.5, 2])],
        ["FIXED_RR:rr=2|1.5|2.5|3"],
        "modelled",
        H1);

    [Fact]
    public void The_same_plan_gives_the_same_hash_of_64_hex_characters()
    {
        PlanHash.Of(Plan()).ShouldBe(PlanHash.Of(Plan()));
        PlanHash.Of(Plan()).ShouldMatch("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Changing_any_one_input_changes_the_hash()
    {
        var baseline = PlanHash.Of(Plan());
        var changed = new[]
        {
            Plan() with { StrategyHash = H2 },
            Plan() with { UniverseLabel = "symbols as listed" },
            Plan() with { UniverseMembersHash = H1 },
            Plan() with { From = new DateOnly(2024, 1, 2) },
            Plan() with { To = new DateOnly(2026, 7, 1) },
            Plan() with { CostSheet = "default|" + H2 + "|checked=no" },
            Plan() with { Sweeps = [new PlanSweep("period", [10, 14, 21]), new PlanSweep("mult", [1.5, 2])] },
            Plan() with { Sweeps = [new PlanSweep("period", [10, 14, 20])] },
            Plan() with { ExitFlags = ["FIXED_RR:rr=2|1.5|2.5"] },
            Plan() with { SlippagePreset = "conservative" },
            Plan() with { RegistrationHash = "none" },
        };

        foreach (var plan in changed)
        {
            PlanHash.Of(plan).ShouldNotBe(baseline);
        }

        changed.Select(PlanHash.Of).Distinct().Count().ShouldBe(changed.Length, "no two single changes collide");
    }

    [Fact]
    public void The_order_of_parameters_values_and_exit_flags_does_not_change_it()
    {
        var shuffled = Plan() with
        {
            Sweeps = [new PlanSweep("mult", [2, 1.5]), new PlanSweep("period", [20, 10, 14])],
            ExitFlags = ["FIXED_RR:rr=2|1.5|2.5|3"],
        };

        PlanHash.Of(shuffled).ShouldBe(PlanHash.Of(Plan()));
    }

    [Fact]
    public void Any_permutation_of_a_larger_plan_gives_the_same_hash()
    {
        var random = new Random(20261104);
        var sweeps = Enumerable.Range(0, 4).Select(i => new PlanSweep("p" + i, [.. Enumerable.Range(1, 4).Select(v => v * (i + 1) * 0.5)])).ToList();
        var exits = new[] { "FIXED_RR:rr=2", "ATR_TRAIL:mult=2", "EMA_TRAIL:length=20" };
        var baseline = PlanHash.Of(Plan() with { Sweeps = sweeps, ExitFlags = exits });

        for (var i = 0; i < 50; i++)
        {
            var permuted = Plan() with
            {
                Sweeps = [.. sweeps.OrderBy(_ => random.Next()).Select(s => new PlanSweep(s.Parameter, [.. s.Values.OrderBy(_ => random.Next())]))],
                ExitFlags = [.. exits.OrderBy(_ => random.Next())],
            };

            PlanHash.Of(permuted).ShouldBe(baseline);
        }
    }

    [Fact]
    public void A_quote_in_a_text_cannot_make_two_plans_read_alike()
    {
        PlanHash.Of(Plan() with { UniverseLabel = "a\",\"members\":\"x" }).ShouldNotBe(PlanHash.Of(Plan() with { UniverseLabel = "a", UniverseMembersHash = "x" }));
    }
}
