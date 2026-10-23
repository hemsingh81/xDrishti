using XDrishti.Domain.Planning;

namespace XDrishti.Domain.Tests.Planning;

/// <summary>The pure rules of the nightly pipeline (spec P7-08).</summary>
public sealed class NightlyRulesTests
{
    [Fact]
    public void The_review_cutoff_is_09_05_india_time_expressed_in_utc()
    {
        var cutoff = NightlyRules.ReviewCutoff(new DateOnly(2026, 10, 26));

        cutoff.ShouldBe(new DateTimeOffset(2026, 10, 26, 3, 35, 0, TimeSpan.Zero));
        cutoff.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void The_plan_key_is_the_same_for_the_same_session_and_builder()
    {
        NightlyRules.PlanKey(new DateOnly(2026, 10, 26), "skeleton").ShouldBe("nightly:2026-10-26:skeleton");
    }

    [Theory]
    [InlineData(5, 100, false)]
    [InlineData(6, 100, true)]
    [InlineData(0, 0, true)]
    [InlineData(0, 100, false)]
    public void Too_many_bad_is_a_share_above_five_percent_and_an_empty_universe_is_too_many(long bad, long total, bool tooMany)
    {
        NightlyRules.TooManyBad(bad, total).ShouldBe(tooMany);
    }

    [Theory]
    [InlineData(GateStatus.Pass, false)]
    [InlineData(GateStatus.Fail, true)]
    [InlineData(GateStatus.Unavailable, true)]
    public void Only_a_pass_does_not_block(GateStatus status, bool blocks)
    {
        new GateResult("g", status, "r").Blocks.ShouldBe(blocks);
    }
}
