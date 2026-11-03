using XDrishti.Domain.Learning;
using XDrishti.Domain.Options.Remark;

namespace XDrishti.Domain.Tests.Options.Remark;

/// <summary>P14-08 (profile parameters): the four parameters of an option_buy profile, their canonical text and hash, and the refusals.</summary>
public sealed class OptionProfileParamsTests
{
    [Fact]
    public void The_four_parameters_make_one_profile_with_one_hash_and_read_back_the_same()
    {
        var made = OptionProfileParams.Create("itm1", "nearest_safe", 30m, 3).Value;

        var profile = made.ToProfile().Value;
        var again = OptionProfileParams.Create("itm1", "nearest_safe", 30.0m, 3).Value.ToProfile().Value;

        profile.Kind.ShouldBe(Expression.OptionBuy);
        profile.CanonicalText.ShouldBe("expression-profile/1\nkind=option_buy\nexpiry=nearest_safe\nmax_hold_sessions=3\npremium_stop_pct=30\nstrike=itm1");
        profile.Hash.ShouldBe(again.Hash);
        OptionProfileParams.FromProfile(profile).Value.ShouldBe(made);
        OptionProfileParams.Create("atm", "nearest_safe", null, 1).Value.ToProfile().Value.CanonicalText.ShouldContain("premium_stop_pct=none");
    }

    [Theory]
    [InlineData("otm1", "nearest_safe", 0, 1)]
    [InlineData("atm", "next", 0, 1)]
    [InlineData("atm", "nearest_safe", 0, 0)]
    [InlineData("atm", "nearest_safe", 0, 21)]
    [InlineData("atm", "nearest_safe", 100, 1)]
    [InlineData("atm", "nearest_safe", -5, 1)]
    public void A_value_outside_the_closed_lists_is_refused(string strike, string expiry, int stop, int hold)
    {
        var made = OptionProfileParams.Create(strike, expiry, stop == 0 ? null : stop, hold);

        made.IsFailure.ShouldBe(true);
        made.Error.Code.ShouldBe(OptionProfileParams.InvalidCode);
    }

    [Fact]
    public void A_future_profile_is_refused_for_want_of_futures_bars_and_another_kind_or_list_is_refused()
    {
        var future = ExpressionProfile.Create(Expression.Future, []).Value;
        var cash = ExpressionProfile.Cash;
        var partial = ExpressionProfile.Create(Expression.OptionBuy, [new("strike", "atm")]).Value;

        OptionProfileParams.FromProfile(future).Error.Code.ShouldBe(OptionProfileParams.NoFuturesBarsCode);
        OptionProfileParams.FromProfile(cash).Error.Code.ShouldBe(OptionProfileParams.InvalidCode);
        OptionProfileParams.FromProfile(partial).Error.Code.ShouldBe(OptionProfileParams.InvalidCode);
    }
}
