using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>P14-06 criterion 3 (ADR 0193): a profile's hash does not depend on the order of its parameters, changes with any parameter, and the cash profile's hash is pinned.</summary>
public sealed class ExpressionProfileTests
{
    private static KeyValuePair<string, object> P(string name, object value) => new(name, value);

    private static ExpressionProfile Make(params KeyValuePair<string, object>[] parameters) => ExpressionProfile.Create(Expression.OptionBuy, parameters).Value;

    [Fact]
    public void The_cash_profile_has_a_fixed_text_and_hash()
    {
        ExpressionProfile.Cash.CanonicalText.ShouldBe("expression-profile/1\nkind=cash");
        ExpressionProfile.Cash.Hash.ShouldBe("7d1d22d885f25128a83f8b6c9d7935787e273ae278e507e968a3e1cc037399e2");
        ExpressionProfile.Create(Expression.Cash, []).Value.Hash.ShouldBe(ExpressionProfile.Cash.Hash);
    }

    [Fact]
    public void The_same_parameters_in_another_order_give_the_same_text_and_hash()
    {
        var a = Make(P("strike_rule", "atm"), P("premium_stop", 0.5m), P("min_open_interest", 1000));
        var b = Make(P("min_open_interest", 1000L), P("strike_rule", "atm"), P("premium_stop", 0.50m));

        a.CanonicalText.ShouldBe(b.CanonicalText);
        a.Hash.ShouldBe(b.Hash);
        a.CanonicalText.ShouldBe("expression-profile/1\nkind=option_buy\nmin_open_interest=1000\npremium_stop=0.5\nstrike_rule=atm");
        a.Hash.Length.ShouldBe(64);
        a.Hash.ShouldBe(a.Hash.ToLowerInvariant());
    }

    [Fact]
    public void Any_change_of_kind_name_or_value_changes_the_hash()
    {
        var baseline = Make(P("strike_rule", "atm"), P("premium_stop", 0.5m));
        var others = new[]
        {
            Make(P("strike_rule", "atm"), P("premium_stop", 0.6m)),
            Make(P("strike_rule", "otm1"), P("premium_stop", 0.5m)),
            Make(P("strike_rule", "atm")),
            Make(P("strike_rule", "atm"), P("premium_stop", 0.5m), P("extra", true)),
            Make(P("strike_rule", "atm"), P("premium_stops", 0.5m)),
            ExpressionProfile.Create(Expression.DebitSpread, [P("strike_rule", "atm"), P("premium_stop", 0.5m)]).Value,
        };

        others.Select(o => o.Hash).Append(baseline.Hash).Distinct().Count().ShouldBe(others.Length + 1);
    }

    [Fact]
    public void Numbers_are_written_the_same_in_every_culture_and_without_trailing_zeros()
    {
        Make(P("a", 1.50m), P("b", 2.0d), P("c", 0m), P("d", -0.0d), P("e", 12L)).CanonicalText
            .ShouldBe("expression-profile/1\nkind=option_buy\na=1.5\nb=2\nc=0\nd=0\ne=12");
    }

    [Fact]
    public void A_name_or_value_that_is_not_canonical_is_refused_not_thrown()
    {
        ExpressionProfile.Create(Expression.OptionBuy, [P("Strike", "atm")]).IsFailure.ShouldBeTrue();
        ExpressionProfile.Create(Expression.OptionBuy, [P("1a", "atm")]).IsFailure.ShouldBeTrue();
        ExpressionProfile.Create(Expression.OptionBuy, [P("a", "x\ny")]).IsFailure.ShouldBeTrue();
        ExpressionProfile.Create(Expression.OptionBuy, [P("a", "")]).IsFailure.ShouldBeTrue();
        ExpressionProfile.Create(Expression.OptionBuy, [P("a", new string('x', 65))]).IsFailure.ShouldBeTrue();
        ExpressionProfile.Create(Expression.OptionBuy, [P("a", double.NaN)]).IsFailure.ShouldBeTrue();
        ExpressionProfile.Create(Expression.OptionBuy, [P("a", new object())]).IsFailure.ShouldBeTrue();
        ExpressionProfile.Create(Expression.OptionBuy, [P("a", 1), P("a", 2)]).IsFailure.ShouldBeTrue();
        ExpressionProfile.Create(Expression.Cash, [P("a", 1)]).IsFailure.ShouldBeTrue();
        ExpressionProfile.Create((Expression)99, []).Error.Code.ShouldBe(ExpressionProfile.InvalidCode);
        ExpressionProfile.Create(Expression.OptionBuy, Enumerable.Range(0, 33).Select(i => P("p" + i, i))).IsFailure.ShouldBeTrue();
    }
}
