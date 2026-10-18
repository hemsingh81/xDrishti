using XDrishti.Simulation.Scanning;
using XDrishti.Simulation.Tests.Support;

namespace XDrishti.Simulation.Tests.Scanning;

/// <summary>Criterion 5 of P3-03a: setup, invalidate and the order of D2 (the latch, the window, eligibility, the fate of an eligible step), with the counts of the tables.</summary>
public sealed class OrderOfDecisionTests
{
    private static EntryScanner Buy(string name)
    {
        var fixture = Fixtures.All.Single(f => f.Name == name);
        var scanner = Scan.Both(fixture.Definition, fixture.Bars, fixture.Timeframe)[0];
        Identities.ShouldHold(scanner);
        return scanner;
    }

    [Fact]
    public void A_false_setup_gates_the_trigger_f20()
    {
        var scanner = Buy("F20");

        scanner.Candidates.ShouldBeEmpty();
        var c = scanner.Counters;
        Identities.Of(c, "buy.setup[0]").ShouldBe((7, 2, 0, 0));
        Identities.Of(c, "buy.trigger[0]").ShouldBe((1, 8, 0, 0));
        c.EligibleSteps.ShouldBe(0);
    }

    [Fact]
    public void An_invalidation_that_is_true_at_the_step_of_the_trigger_blocks_the_entry_at_that_step_f19()
    {
        var scanner = Buy("F19");

        scanner.Candidates.ShouldBeEmpty();
        var c = scanner.Counters;
        (c.BlockedByInvalidation, c.InvalidatedDays, c.LaterTriggers, c.Candidates).ShouldBe((1, 1, 0, 0));
        Identities.Of(c, "buy.invalidate[0]").ShouldBe((1, 24, 0, 0));
        Identities.Of(c, "buy.trigger[0]").ShouldBe((1, 8, 0, 0));
    }

    [Fact]
    public void An_invalidation_after_the_entry_makes_a_later_trigger_a_later_trigger_not_a_blocked_one_f19b()
    {
        var scanner = Buy("F19b");

        scanner.Candidates.Select(c => c.DecisionBarIndex).ShouldBe([29]);
        var c = scanner.Counters;
        (c.LaterTriggers, c.BlockedByInvalidation, c.InvalidatedDays).ShouldBe((1, 0, 1));
        Identities.Of(c, "buy.invalidate[0]").ShouldBe((1, 24, 0, 0));
    }

    [Fact]
    public void An_unknown_invalidation_is_no_entry_and_the_latch_is_set_by_the_later_true_f23()
    {
        var scanner = Buy("F23");

        scanner.Candidates.ShouldBeEmpty();
        var c = scanner.Counters;
        (c.UnknownInvalidation, c.InvalidatedDays, c.BlockedByInvalidation).ShouldBe((1, 1, 0));
        Identities.Of(c, "buy.invalidate[0]").ShouldBe((1, 22, 2, 0));
        Identities.Of(c, "buy.trigger[0]").ShouldBe((1, 8, 0, 0));
    }

    [Fact]
    public void A_stop_without_a_value_keeps_the_date_open_and_the_candidate_comes_at_the_first_step_with_one_f22()
    {
        var scanner = Buy("F22");

        scanner.Candidates.Select(c => Render.Instant(c.DecisionEpochSeconds)).ShouldBe(["2026-03-02 10:00"]);
        var c = scanner.Counters;
        (c.StopNotReady, c.Candidates, c.LaterTriggers).ShouldBe((2, 1, 6));
    }

    /// <summary>
    /// An eligible step is a window step where the setup and the trigger are both true, counted by hand from the fixture and not from the counters of the fates:
    /// F1 one break (trigger True 1, no setup list); F11 two breaks (trigger True 2); F19 one break, the invalidation true at that step (1); F19b the bars of F11, two breaks (2);
    /// F20 the setup (time at or after 10:00) is true on 7 of 9 window steps and the trigger on the one at 09:45, never together (0); F22 the trigger `close > 0` is true on all 9 window steps
    /// of the table of F1 (WindowSteps 9: StopNotReady 2 + candidate 1 + later 6 is the same 9 seen from the fates, which is why it is not asserted from them); F23 one break (1).
    /// </summary>
    [Theory]
    [InlineData("F1", 1)]
    [InlineData("F11", 2)]
    [InlineData("F19", 1)]
    [InlineData("F19b", 2)]
    [InlineData("F20", 0)]
    [InlineData("F22", 9)]
    [InlineData("F23", 1)]
    public void The_eligible_steps_are_the_window_steps_where_setup_and_trigger_are_both_true_counted_by_hand(string name, int eligible)
    {
        Buy(name).Counters.EligibleSteps.ShouldBe(eligible);
    }

    [Fact]
    public void Each_condition_path_of_the_fixture_strategies_is_true_at_least_once_in_some_fixture()
    {
        var shown = new HashSet<string>();
        foreach (var fixture in Fixtures.All)
        {
            foreach (var scanner in Scan.Both(fixture.Definition, fixture.Bars, fixture.Timeframe))
            {
                foreach (var condition in scanner.Counters.Conditions.Where(x => x.True > 0))
                {
                    shown.Add(fixture.Definition.Id + ":" + condition.Path);
                }
            }
        }

        foreach (var path in new[] { "ORB:buy.trigger[0]", "ORB:sell.trigger[0]", "STALE_TEST:buy.trigger[0]", "CROSS_VWAP_TEST:buy.trigger[0]", "STOP_NOT_READY_TEST:buy.trigger[0]" })
        {
            shown.ShouldContain(path);
        }
    }

    [Fact]
    public void The_conditions_of_the_variants_that_add_a_list_show_true_in_their_fixtures()
    {
        Identities.Of(Buy("F19").Counters, "buy.invalidate[0]").True.ShouldBeGreaterThan(0);
        Identities.Of(Buy("F20").Counters, "buy.setup[0]").True.ShouldBeGreaterThan(0);
        Identities.Of(Buy("F23").Counters, "buy.invalidate[0]").True.ShouldBeGreaterThan(0);
    }
}
