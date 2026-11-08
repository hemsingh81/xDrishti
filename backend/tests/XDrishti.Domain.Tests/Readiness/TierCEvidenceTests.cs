using XDrishti.Domain.Readiness;

namespace XDrishti.Domain.Tests.Readiness;

/// <summary>SC-S3 (ADR 0224): the hard fail "evidence only from tier C", judged on the Lab and the forward sets (the owner's reading of 2026-11-07), boundary by boundary.</summary>
public sealed class TierCEvidenceTests
{
    private static TierSet Set(string name, int a, int b, int c, int none) => TierSet.Of(name, new TierCounts(a, b, c, none));

    [Theory]
    [InlineData(0, 0, 0, 0, TierSetState.Absent)]
    [InlineData(5, 0, 0, 0, TierSetState.Mixed)]
    [InlineData(0, 5, 0, 0, TierSetState.Mixed)]
    [InlineData(1, 0, 9, 0, TierSetState.Mixed)]
    [InlineData(1, 0, 0, 9, TierSetState.Mixed)]
    [InlineData(0, 0, 10, 0, TierSetState.AllTierC)]
    [InlineData(0, 0, 1, 0, TierSetState.AllTierC)]
    [InlineData(0, 0, 9, 1, TierSetState.Unavailable)]
    [InlineData(0, 0, 0, 4, TierSetState.Unavailable)]
    public void A_set_is_classified_by_its_counts(int a, int b, int c, int none, TierSetState expected)
    {
        Set("x", a, b, c, none).State.ShouldBe(expected);
    }

    [Fact]
    public void Negative_counts_are_not_counts_and_read_unavailable()
    {
        Set("x", -1, 0, 5, 0).State.ShouldBe(TierSetState.Unavailable);
    }

    [Fact]
    public void An_unreadable_set_is_unavailable_with_its_reason()
    {
        var set = TierSet.Unreadable("Lab", "no block");

        set.State.ShouldBe(TierSetState.Unavailable);
        set.Line.ShouldBe("Lab: unavailable (no block)");
    }

    [Fact]
    public void Two_sets_that_are_not_only_tier_C_are_clear()
    {
        var (state, evidence) = TierCEvidence.Judge(Set("Lab", 3, 0, 7, 0), Set("forward", 0, 2, 0, 0));

        state.ShouldBe(FaultState.Clear);
        evidence.ShouldStartWith("clear:");
    }

    [Fact]
    public void Lab_only_tier_C_is_a_fault_triggered_by_the_Lab()
    {
        var (state, evidence) = TierCEvidence.Judge(Set("Lab", 0, 0, 7, 0), Set("forward", 5, 0, 0, 0));

        state.ShouldBe(FaultState.Fault);
        evidence.ShouldContain("triggered by: Lab.");
        evidence.ShouldContain("Lab: A 0, B 0, C 7, unavailable 0 (only tier C)");
        evidence.ShouldContain("forward: A 5, B 0, C 0, unavailable 0 (not only tier C)");
    }

    [Fact]
    public void Forward_only_tier_C_is_a_fault_triggered_by_the_forward_trades()
    {
        var (state, evidence) = TierCEvidence.Judge(Set("Lab", 5, 0, 0, 0), Set("forward", 0, 0, 3, 0));

        state.ShouldBe(FaultState.Fault);
        evidence.ShouldContain("triggered by: forward.");
    }

    [Fact]
    public void Both_only_tier_C_names_both()
    {
        var (_, evidence) = TierCEvidence.Judge(Set("Lab", 0, 0, 7, 0), Set("forward", 0, 0, 3, 0));

        evidence.ShouldContain("triggered by: Lab, forward.");
    }

    [Fact]
    public void One_set_absent_and_the_other_only_tier_C_is_a_fault()
    {
        TierCEvidence.Judge(Set("Lab", 0, 0, 0, 0), Set("forward", 0, 0, 3, 0)).State.ShouldBe(FaultState.Fault);
        TierCEvidence.Judge(Set("Lab", 0, 0, 3, 0), Set("forward", 0, 0, 0, 0)).State.ShouldBe(FaultState.Fault);
    }

    [Fact]
    public void No_trades_in_any_set_is_not_tier_C()
    {
        var (state, evidence) = TierCEvidence.Judge(Set("Lab", 0, 0, 0, 0), Set("forward", 0, 0, 0, 0));

        state.ShouldBe(FaultState.Clear);
        evidence.ShouldContain("no trades (not tier C)");
    }

    [Fact]
    public void An_unavailable_set_makes_the_term_unavailable_not_a_fault_and_not_clear()
    {
        TierCEvidence.Judge(TierSet.Unreadable("Lab", "x"), Set("forward", 5, 0, 0, 0)).State.ShouldBe(FaultState.Unavailable);
        TierCEvidence.Judge(Set("Lab", 5, 0, 0, 0), Set("forward", 0, 0, 3, 2)).State.ShouldBe(FaultState.Unavailable);
        TierCEvidence.Judge(Set("Lab", 0, 0, 0, 0), TierSet.Unreadable("forward", "x")).State.ShouldBe(FaultState.Unavailable);
    }

    [Fact]
    public void A_detected_fault_stands_over_an_unavailable_set()
    {
        TierCEvidence.Judge(TierSet.Unreadable("Lab", "x"), Set("forward", 0, 0, 3, 0)).State.ShouldBe(FaultState.Fault);
    }
}
