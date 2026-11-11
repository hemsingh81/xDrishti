using XDrishti.Domain.Replay;

namespace XDrishti.Domain.Tests.Replay;

/// <summary>SR-01: the parameters refuse what cannot be replayed, and the hindsight labels say which parts of a result a trader could not have had.</summary>
public sealed class ReplayParamsTests
{
    private static readonly ReplayParams Good = new(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 30), new ReplayScope([Guid.NewGuid()], []), 1_000_000m, ReplayRiskProfile.Standard, null, ReplayVersionRule.Start, ReplayMode.Frozen);

    [Fact]
    public void Usable_parameters_have_no_problem()
    {
        Good.Problems().ShouldBeEmpty();
    }

    [Fact]
    public void A_start_after_the_end_an_empty_scope_a_capital_of_zero_or_a_broken_profile_are_refused()
    {
        (Good with { From = Good.To.AddDays(1) }).Problems().ShouldNotBeEmpty();
        (Good with { Scope = new ReplayScope([], []) }).Problems().ShouldNotBeEmpty();
        (Good with { Capital = 0m }).Problems().ShouldNotBeEmpty();
        (Good with { Capital = -5m }).Problems().ShouldNotBeEmpty();
        (Good with { Profile = ReplayRiskProfile.Standard with { RiskPerTradePct = 0 } }).Problems().ShouldNotBeEmpty();
        (Good with { Profile = ReplayRiskProfile.Standard with { MaxCapitalPerTradePct = 150 } }).Problems().ShouldNotBeEmpty();
        (Good with { Mode = (ReplayMode)9 }).Problems().ShouldNotBeEmpty();
    }

    [Fact]
    public void The_standard_profile_is_the_seeded_one()
    {
        ReplayRiskProfile.Standard.ShouldBe(new ReplayRiskProfile(0.5, 25, 3.0, 2, 1.5));
        ReplayRiskProfile.Standard.Problems().ShouldBeEmpty();
    }

    [Fact]
    public void Hindsight_labels_follow_the_parameters()
    {
        HindsightLabels.For(Good).ShouldBe([HindsightLabels.VersionsStart, HindsightLabels.SettingsNow, HindsightLabels.DataSeenBefore, HindsightLabels.BeforeCosts]);
        HindsightLabels.For(Good with { VersionRule = ReplayVersionRule.AsOf, CostSheetId = "sheet", Mode = ReplayMode.Learning })
            .ShouldBe([HindsightLabels.SettingsNow, HindsightLabels.DataSeenBefore, HindsightLabels.LifecycleAsRecorded]);
    }

    [Fact]
    public void The_two_switches_are_off_by_default_and_add_their_labels_when_on()
    {
        Good.FiltersWithoutHistory.ShouldBeFalse();
        Good.IncludeResearchCells.ShouldBeFalse();
        HindsightLabels.For(Good with { FiltersWithoutHistory = true, IncludeResearchCells = true }).ShouldContain(HindsightLabels.FiltersWithoutHistory);
        HindsightLabels.For(Good with { FiltersWithoutHistory = true, IncludeResearchCells = true }).ShouldContain(HindsightLabels.ResearchCells);
    }

    [Fact]
    public void The_closed_list_has_eight_reasons_each_with_its_own_code()
    {
        BlockReasons.All.Count.ShouldBe(8);
        BlockReasons.All.Select(BlockReasons.Code).Distinct().Count().ShouldBe(8);
    }
}
