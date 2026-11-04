using XDrishti.Domain.Learning;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Options.Evaluation;
using XDrishti.Domain.Options.Structures;

namespace XDrishti.Domain.Tests.Options.Structures;

/// <summary>P14-10 criteria 4 and 5: the profile parameters are a closed list with one hash per choice; a stock width of 4 is refused at validation; a credit spread is for an index; the four condor variants are N = 4 with their factor; each family has its own primary profile.</summary>
public sealed class StructureParamsTests
{
    [Fact]
    public void A_spread_profile_round_trips_through_the_registered_profile_and_one_choice_has_one_hash()
    {
        var made = SpreadParams.Create(Expression.DebitSpread, "itm1", 4, "nearest_safe", 40m, 3).Value;

        var profile = made.ToProfile().Value;
        var back = SpreadParams.FromProfile(profile).Value;

        back.ShouldBe(made);
        profile.Hash.ShouldBe(SpreadParams.Create(Expression.DebitSpread, "itm1", 4, "nearest_safe", 40m, 3).Value.ToProfile().Value.Hash);
        profile.Hash.ShouldNotBe(SpreadParams.Create(Expression.DebitSpread, "itm1", 2, "nearest_safe", 40m, 3).Value.ToProfile().Value.Hash);
        profile.Hash.ShouldNotBe(SpreadParams.Create(Expression.CreditSpread, "itm1", 4, "nearest_safe", null, 3).Value.ToProfile().Value.Hash);
    }

    [Theory]
    [InlineData("otm1", 2, "nearest_safe", 0, 1)]
    [InlineData("atm", 3, "nearest_safe", 0, 1)]
    [InlineData("atm", 2, "farthest", 0, 1)]
    [InlineData("atm", 2, "nearest_safe", 0, 0)]
    [InlineData("atm", 2, "nearest_safe", 0, 21)]
    [InlineData("atm", 2, "nearest_safe", 100, 1)]
    public void A_value_outside_the_closed_lists_is_refused_without_throwing(string strike, int width, string expiry, int stop, int hold)
    {
        var made = SpreadParams.Create(Expression.DebitSpread, strike, width, expiry, stop == 0 ? null : stop, hold);

        made.IsFailure.ShouldBeTrue();
        made.Error.Code.ShouldBe(SpreadParams.InvalidCode);
    }

    [Fact]
    public void A_credit_spread_has_no_structure_stop_and_a_profile_of_another_kind_is_refused()
    {
        SpreadParams.Create(Expression.CreditSpread, "atm", 2, "nearest_safe", 40m, 1).IsFailure.ShouldBeTrue();
        SpreadParams.FromProfile(CondorParams.Primary.ToProfile().Value).IsFailure.ShouldBeTrue();
        CondorParams.FromProfile(StructureKit.Debit().ToProfile().Value).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_stock_debit_spread_with_a_width_of_4_is_refused_at_validation_as_width_outside_window_and_a_credit_spread_is_for_an_index()
    {
        var wide = StructureKit.Debit(width: 4);

        wide.ValidateFor("NSE:RELIANCE").Error.Code.ShouldBe(SpreadParams.WidthOutsideWindowCode);
        wide.ValidateFor("NSE:NIFTY").IsSuccess.ShouldBeTrue();
        wide.ValidateFor("NSE:BANKNIFTY").IsSuccess.ShouldBeTrue();
        StructureKit.Debit().ValidateFor("NSE:RELIANCE").IsSuccess.ShouldBeTrue();
        StructureKit.Credit().ValidateFor("NSE:RELIANCE").Error.Code.ShouldBe(SpreadParams.IndexOnlyCode);
    }

    [Fact]
    public void The_condor_primary_is_k6_m1_with_two_step_wings_and_the_four_variants_make_N_4_with_a_factor_of_1_5()
    {
        CondorParams.Primary.ShouldBe(new CondorParams(6, 1, 2));
        CondorParams.Variants.Count.ShouldBe(4);
        CondorParams.Variants.Select(v => (v.K, v.M)).ShouldBe([(6, 1), (6, 2), (4, 1), (4, 2)]);
        CondorParams.Variants.Select(v => v.ToProfile().Value.Hash).Distinct().Count().ShouldBe(4);
        XDrishti.Domain.Lab.MultipleTesting.Factor(4).ShouldBe(1.5, 1e-12);
        CondorParams.Create(5, 1, 2).IsFailure.ShouldBeTrue();
        CondorParams.Create(6, 3, 2).IsFailure.ShouldBeTrue();
        CondorParams.Create(6, 1, 3).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Each_family_has_its_own_primary_profile_of_its_own_kind_and_no_two_share_a_hash()
    {
        string[] families = ["expr:option_buy", "expr:debit_spread", "expr:credit_spread", "expr:iron_condor"];

        var hashes = families.Select(ExpressionProfiles.PrimaryHashOf).ToList();

        hashes.ShouldAllBe(h => h != null && h.Length == 64);
        hashes.Distinct().Count().ShouldBe(4);
        families.Select(f => ExpressionProfiles.KindOf(f)).ShouldBe([Expression.OptionBuy, Expression.DebitSpread, Expression.CreditSpread, Expression.IronCondor]);
        ExpressionProfiles.PrimaryHashOf("expr:pairs").ShouldBeNull();
        ExpressionProfiles.PrimaryHashOf("expr:option_buy").ShouldBe(ExpressionProfiles.PrimaryHash);
    }

    [Fact]
    public void The_trial_of_one_family_never_equals_the_trial_of_another_for_the_same_profile_and_run()
    {
        var profile = new string('a', 64);
        var run = new string('b', 64);
        var from = new DateOnly(2023, 1, 1);
        var to = new DateOnly(2025, 12, 31);

        var hashes = ExpressionFamilies.All.Select(f => ExpressionTrial.Of(f, profile, run, from, to).Hash).ToList();

        hashes.Distinct().Count().ShouldBe(4);
        ExpressionTrial.CondorText(profile, run).ShouldContain("protocol:R");
        ExpressionTrial.Text(profile, run).ShouldContain("protocol:O");
    }
}
