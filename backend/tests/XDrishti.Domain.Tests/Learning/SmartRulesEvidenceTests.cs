using XDrishti.Domain.Learning;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>The counting of smart-rule codes on stored plans (P8-13) and the R lost per loss tag.</summary>
public sealed class SmartRulesEvidenceTests
{
    private static PlanItemFacts Item(string group, string[]? reasons = null, string[]? unavailable = null) => new(group, reasons ?? [], unavailable ?? []);

    private static PlanRuleFacts Plan(long id, int day, params PlanItemFacts[] items) => new(id, new DateOnly(2026, 3, day), items);

    [Fact]
    public void Three_plans_are_counted_once_per_code_and_the_flagged_plan_is_named()
    {
        var plans = new[]
        {
            Plan(1, 2, Item("primary", unavailable: ["gate_unavailable"]), Item("primary", unavailable: ["gate_unavailable"])),
            Plan(2, 3, Item("primary"), Item("reserve", ["same_bet_better_ticket"]), Item("reserve", ["same_bet_better_ticket", "gate_reduced_cut"]), Item("reserve", ["same_bet_open_trade"], ["correlation_partial"])),
            Plan(3, 4, Item("primary", unavailable: ["rule_removed_half", "throttle_unavailable"])),
        };

        var counts = SmartRulesEvidence.Build(plans);

        counts.PlansRead.ShouldBe(3);
        (counts.From, counts.To).ShouldBe((new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 4)));
        var gate = counts.Rules.Single(r => r.Id == "gate");
        (gate.UnavailableOn, gate.Moved).ShouldBe((1, 1));
        var corr = counts.Rules.Single(r => r.Id == "correlation");
        (corr.UnavailableOn, corr.PartialOn, corr.Moved).ShouldBe((0, 1, 3));
        corr.Reasons.Single(r => r.Code == "same_bet_better_ticket").Count.ShouldBe(2);
        counts.Rules.Single(r => r.Id == "throttle").UnavailableOn.ShouldBe(1);
        counts.Flagged.ShouldBe([new FlaggedPlan(3, new DateOnly(2026, 3, 4), "rule_removed_half")]);
    }

    [Fact]
    public void Meta_and_windows_are_not_recorded_and_no_plan_gives_empty_counts()
    {
        var counts = SmartRulesEvidence.Build([]);

        counts.PlansRead.ShouldBe(0);
        counts.From.ShouldBeNull();
        counts.Flagged.ShouldBeEmpty();
        counts.Rules.Select(r => r.Id).ShouldBe(["gate", "throttle", "meta", "windows", "correlation"]);
        counts.Rules.Where(r => r.Id is "meta" or "windows").ShouldAllBe(r => !r.Recorded);
    }

    [Fact]
    public void A_reason_on_a_primary_item_is_not_counted_as_moved()
    {
        var counts = SmartRulesEvidence.Build([Plan(1, 2, Item("primary", ["gate_no_trade"]))]);

        counts.Rules.Single(r => r.Id == "gate").Moved.ShouldBe(0);
    }

    [Fact]
    public void R_lost_per_tag_sums_the_negative_R_of_the_losses_carrying_the_tag()
    {
        var start = new DateOnly(2026, 1, 5);
        var gap = FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Buy, start, ExitReason.Stop, entryMinute: 600, entry: 100, stop: 99, target: 102, exitPrice: 98.2, rExact: -1.8, rFilled: -1.8);
        gap = gap with { MaeR = 1.8, MfeR = 0.1, ExitEpochSeconds = gap.EntryEpochSeconds + 3600 };
        var normal = FabricatedRun.Trade(FabricatedRun.IdA, "NSE:B", Side.Buy, start.AddDays(1), ExitReason.Stop, entryMinute: 600, entry: 100, stop: 99, target: 102, exitPrice: 99.4, rExact: -0.6, rFilled: -0.6);
        normal = normal with { MaeR = 0.6, MfeR = 0.2, ExitEpochSeconds = normal.EntryEpochSeconds + 3600 };

        var report = LossTagAnalyzer.Analyze([new LossTagInput("A", gap, -1.8), new LossTagInput("A", normal, -0.6)], new LossTagOptions()).Value;

        var cell = report.Cells.Single();
        cell.LostR.ShouldNotBeNull();
        cell.LostR.Count.ShouldBe(cell.Counts.Count);
        cell.LostR[(int)LossTagKind.NormalVariance].ShouldBe(-0.6, 1e-9);
        for (var i = 0; i < cell.Counts.Count; i++)
        {
            (cell.Counts[i] == 0).ShouldBe(cell.LostR[i] == 0);
        }

        cell.Counts.Sum().ShouldBeGreaterThanOrEqualTo(cell.Losses - cell.Untagged);
    }
}
