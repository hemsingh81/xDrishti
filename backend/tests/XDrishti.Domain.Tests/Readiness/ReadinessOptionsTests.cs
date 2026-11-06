using XDrishti.Domain.Readiness;

namespace XDrishti.Domain.Tests.Readiness;

/// <summary>CF-11 part 1 (ADR 0221): the settings are the spec's proposals at the loose end and can only be tightened; the hash changes with any value.</summary>
public sealed class ReadinessOptionsTests
{
    [Fact]
    public void The_defaults_are_the_specs_numbers_and_usable()
    {
        var o = new ReadinessOptions();

        (o.MinNEff, o.MinTradesPerCell, o.ExpectancyR, o.TStatistic, o.MinQuarterShare, o.MaxYearShare, o.MaxInstrumentShare, o.DrawdownBudgetR, o.BuildingFrom, o.UsableFrom, o.ReadyFrom)
            .ShouldBe((300.0, 30, 0.15, 2.0, 0.70, 0.60, 0.15, 20.0, 40.0, 70.0, 85.0));
        o.Problems().ShouldBeEmpty();
    }

    public static TheoryData<ReadinessOptions> Looser => new()
    {
        new ReadinessOptions(MinNEff: 299),
        new ReadinessOptions(MinTradesPerCell: 29),
        new ReadinessOptions(ExpectancyR: 0.14),
        new ReadinessOptions(TStatistic: 1.9),
        new ReadinessOptions(MinQuarterShare: 0.69),
        new ReadinessOptions(MaxYearShare: 0.61),
        new ReadinessOptions(MaxInstrumentShare: 0.16),
        new ReadinessOptions(DrawdownBudgetR: 21),
        new ReadinessOptions(BuildingFrom: 39),
        new ReadinessOptions(UsableFrom: 69),
        new ReadinessOptions(ReadyFrom: 84),
    };

    [Theory]
    [MemberData(nameof(Looser))]
    public void A_value_looser_than_the_spec_is_refused(ReadinessOptions options)
    {
        options.Problems().ShouldNotBeEmpty();
    }

    [Fact]
    public void A_stricter_value_is_accepted_at_every_setting()
    {
        new ReadinessOptions(400, 40, 0.2, 2.5, 0.8, 0.5, 0.1, 15, 50, 75, 90).Problems().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_value_that_is_not_a_finite_number_is_refused(double value)
    {
        new ReadinessOptions(MinNEff: value).Problems().ShouldNotBeEmpty();
        new ReadinessOptions(ReadyFrom: value).Problems().ShouldNotBeEmpty();
    }

    [Fact]
    public void The_label_cut_offs_must_increase()
    {
        new ReadinessOptions(BuildingFrom: 75, UsableFrom: 75).Problems().ShouldNotBeEmpty();
        new ReadinessOptions(UsableFrom: 90, ReadyFrom: 90).Problems().ShouldNotBeEmpty();
    }

    [Fact]
    public void The_hash_is_stable_and_changes_with_any_value()
    {
        var hash = new ReadinessOptions().SnapshotHash();

        hash.ShouldBe(new ReadinessOptions().SnapshotHash());
        hash.Length.ShouldBe(64);
        new ReadinessOptions(MinNEff: 301).SnapshotHash().ShouldNotBe(hash);
        new ReadinessOptions(ReadyFrom: 86).SnapshotHash().ShouldNotBe(hash);
        new ReadinessOptions().KeyText().ShouldStartWith(ReadinessOptions.RulesVersion);
    }
}
