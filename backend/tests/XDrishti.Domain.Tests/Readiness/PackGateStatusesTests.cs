using XDrishti.Domain.Readiness;

namespace XDrishti.Domain.Tests.Readiness;

/// <summary>CF-11 row 15 (ADR 0226): the gate's words and the one rule for what counts.</summary>
public sealed class PackGateStatusesTests
{
    [Fact]
    public void The_five_statuses_are_the_gates_own_spellings()
    {
        PackGateStatuses.All.ShouldBe(["PASS", "PASS_WITH_DATA_FINDINGS", "RERUN_NEEDED", "DATA_REPAIR_NEEDED", "BLOCKED_DEFECT"]);
        PackGateStatuses.All.ShouldAllBe(s => PackGateStatuses.IsKnown(s));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pass")]
    [InlineData("MET")]
    public void Anything_else_is_unknown(string? status) => PackGateStatuses.IsKnown(status).ShouldBeFalse();

    [Theory]
    [InlineData("PASS", 20, true)]
    [InlineData("PASS", 19, false)]
    [InlineData("PASS", 0, false)]
    [InlineData("PASS_WITH_DATA_FINDINGS", 500, false)]
    [InlineData("BLOCKED_DEFECT", 500, false)]
    [InlineData(null, 500, false)]
    public void Only_a_pass_over_twenty_trades_counts(string? status, int trades, bool expected) => PackGateStatuses.Counts(status, trades).ShouldBe(expected);
}
