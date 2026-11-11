using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Replay;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Tests.Replay;

/// <summary>SR-01: what the live builder writes on a plan item that was not taken is mapped to the one reason of the closed list; the engine code is kept beside it.</summary>
public sealed class BlockReasonMapTests
{
    private static NewPlanItem Item(TicketGroup group, params string[] reasons) =>
        new(1, group, "AAA", "BUY", "S1", ReplayWorld.Hash1, "t", "s", "g", "v", reasons, new PlanEvidence(ReplayWorld.Cell.Key, 1, "r", "b", null, null, [], null), []);

    [Theory]
    [InlineData(TicketGroup.Reserve, "gate_no_trade", BlockReason.Gate)]
    [InlineData(TicketGroup.Reserve, "gate_reduced_cut", BlockReason.Gate)]
    [InlineData(TicketGroup.Reserve, "same_bet_better_ticket", BlockReason.SmartRule)]
    [InlineData(TicketGroup.Reserve, "same_bet_open_trade", BlockReason.SmartRule)]
    [InlineData(TicketGroup.Reserve, "rule_removed_half", BlockReason.SmartRule)]
    [InlineData(TicketGroup.Reserve, "open_positions:fail", BlockReason.MaxPositions)]
    [InlineData(TicketGroup.Reserve, "entries_per_day:fail", BlockReason.MaxPositions)]
    [InlineData(TicketGroup.Reserve, "plan_items:fail", BlockReason.MaxPositions)]
    [InlineData(TicketGroup.Reserve, "not_taken", BlockReason.MaxPositions)]
    [InlineData(TicketGroup.Reserve, "total_risk:fail", BlockReason.TotalRisk)]
    [InlineData(TicketGroup.Reserve, "per_trade_risk:fail", BlockReason.TotalRisk)]
    [InlineData(TicketGroup.Reserve, "net_exposure:unavailable", BlockReason.TotalRisk)]
    [InlineData(TicketGroup.Reserve, "cluster_cap:fail", BlockReason.ClusterCap)]
    [InlineData(TicketGroup.Reserve, "basket_budget", BlockReason.Capital)]
    [InlineData(TicketGroup.Reserve, "sector:fail", BlockReason.SmartRule)]
    [InlineData(TicketGroup.Reserve, "correlation:fail", BlockReason.SmartRule)]
    [InlineData(TicketGroup.Reserve, "book:unavailable", BlockReason.SmartRule)]
    [InlineData(TicketGroup.Rejected, "win_rate:fail", BlockReason.Gate)]
    [InlineData(TicketGroup.Rejected, "cell_state:fail", BlockReason.Gate)]
    [InlineData(TicketGroup.Rejected, "event:unavailable", BlockReason.Gate)]
    [InlineData(TicketGroup.Rejected, "conflict_lost", BlockReason.SmartRule)]
    [InlineData(TicketGroup.Rejected, "merged_duplicate", BlockReason.SmartRule)]
    [InlineData(TicketGroup.Rejected, "an_unknown_code", BlockReason.Gate)] // never a pass: an unknown rejection is a gate
    public void An_engine_code_maps_to_one_reason_and_is_kept_as_the_detail(TicketGroup group, string code, BlockReason expected)
    {
        var blocked = BlockReasonMap.Of(Item(group, code));

        blocked.Reason.ShouldBe(expected);
        blocked.Detail.ShouldBe(code);
        (blocked.Symbol, blocked.CellKey).ShouldBe(("AAA", ReplayWorld.Cell.Key));
    }

    [Fact]
    public void The_first_reason_of_the_engine_decides()
    {
        BlockReasonMap.Of(Item(TicketGroup.Reserve, "total_risk:fail", "open_positions:fail")).Reason.ShouldBe(BlockReason.TotalRisk);
    }
}
