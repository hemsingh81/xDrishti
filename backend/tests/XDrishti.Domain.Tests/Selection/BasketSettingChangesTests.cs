using XDrishti.Domain.Selection;

namespace XDrishti.Domain.Tests.Selection;

/// <summary>BK-08 criteria 10 and 11 (ADR 0260): only an owner actor makes a row, values are checked, and each change carries its direction.</summary>
public sealed class BasketSettingChangesTests
{
    private const string Owner = "owner:hem";

    private static readonly Guid Basket = new("00000000-0000-0000-0000-0000000000b1");

    private static BasketSettings Cap(double? cap, double rho = 0.70, string source = "tracked") => new(source, cap, rho);

    private static BasketSettingChange Single(BasketSettings current, string? source = null, double? cap = null, bool clear = false, double? rho = null)
    {
        var r = BasketSettingChanges.For(Owner, current, source, cap, clear, rho);
        r.IsSuccess.ShouldBeTrue(r.IsFailure ? r.Error.Message : "");
        return r.Value.Single();
    }

    [Fact]
    public void The_defaults_are_the_old_behaviour()
    {
        BasketSettings.Defaults.ShouldBe(new BasketSettings("tracked", null, 0.70));
        BasketSettings.Defaults.GateBasketId.ShouldBeNull();
    }

    [Theory]
    [InlineData("system:nightly")]
    [InlineData("rule:cluster")]
    [InlineData("cli")]
    [InlineData("")]
    [InlineData(null)]
    public void An_actor_that_is_not_an_owner_is_refused(string? actor)
    {
        var r = BasketSettingChanges.For(actor, BasketSettings.Defaults, null, 1.0, false, null);

        r.IsFailure.ShouldBeTrue();
        r.Error.Code.ShouldBe(BasketSettingChanges.OwnerOnlyCode);
    }

    [Fact]
    public void Setting_a_cap_from_off_or_lowering_it_is_tighten()
    {
        var fromOff = Single(BasketSettings.Defaults, cap: 1.5);
        var lowered = Single(Cap(1.5), cap: 1.0);

        (fromOff.Direction, fromOff.OldValue, fromOff.NewValue).ShouldBe(("tighten", "none", "1.5"));
        lowered.Direction.ShouldBe("tighten");
        lowered.Actor.ShouldBe(Owner);
        lowered.Hash.Length.ShouldBe(64);
    }

    [Fact]
    public void Raising_the_cap_or_clearing_it_is_loosen()
    {
        Single(Cap(1.0), cap: 1.5).Direction.ShouldBe("loosen");
        var cleared = Single(Cap(1.0), clear: true);

        (cleared.Direction, cleared.NewValue).ShouldBe(("loosen", "none"));
    }

    [Fact]
    public void Any_change_of_the_breadth_source_is_loosen_in_either_direction()
    {
        var toBasket = Single(BasketSettings.Defaults, source: BasketSettings.SourceOf(Basket));
        var back = Single(Cap(null, source: BasketSettings.SourceOf(Basket)), source: "tracked");

        toBasket.Direction.ShouldBe("loosen");
        back.Direction.ShouldBe("loosen");
    }

    [Fact]
    public void Raising_the_cut_is_loosen_and_lowering_it_tighten_while_the_cap_is_on()
    {
        Single(Cap(1.0), rho: 0.8).Direction.ShouldBe("loosen");
        Single(Cap(1.0), rho: 0.6).Direction.ShouldBe("tighten");
    }

    [Fact]
    public void A_cut_changed_while_the_cap_is_off_alters_nothing_in_effect_and_is_neutral()
    {
        Single(BasketSettings.Defaults, rho: 0.9).Direction.ShouldBe("neutral");
        var withCap = BasketSettingChanges.For(Owner, BasketSettings.Defaults, null, 1.0, false, 0.9).Value;

        withCap.Select(r => (r.Key, r.Direction)).ShouldBe([(BasketSettings.MaxClusterRiskKey, "tighten"), (BasketSettings.ClusterRhoKey, "loosen")]);
    }

    [Fact]
    public void Each_key_that_changes_is_a_row_and_a_value_in_force_changes_nothing()
    {
        var rows = BasketSettingChanges.For(Owner, BasketSettings.Defaults, BasketSettings.SourceOf(Basket), 1.0, false, 0.8).Value;
        var same = BasketSettingChanges.For(Owner, Cap(1.0), "tracked", 1.0, false, 0.70);

        rows.Count.ShouldBe(3);
        same.IsFailure.ShouldBeTrue();
        same.Error.Code.ShouldBe(BasketSettingChanges.InvalidCode);
    }

    [Theory]
    [InlineData("basket:not-a-guid", null, null)]
    [InlineData("basket:00000000-0000-0000-0000-000000000000", null, null)]
    [InlineData("tracked ", null, null)]
    [InlineData(null, 0.0, null)]
    [InlineData(null, 100.5, null)]
    [InlineData(null, double.NaN, null)]
    [InlineData(null, null, 0.05)]
    [InlineData(null, null, 1.1)]
    public void A_value_out_of_range_is_invalid(string? source, double? cap, double? rho)
    {
        var r = BasketSettingChanges.For(Owner, BasketSettings.Defaults, source, cap, false, rho);

        r.IsFailure.ShouldBeTrue();
        r.Error.Code.ShouldBe(BasketSettingChanges.InvalidCode);
    }

    [Fact]
    public void Clearing_the_cap_together_with_a_value_is_invalid()
    {
        BasketSettingChanges.For(Owner, Cap(1.0), null, 2.0, true, null).Error.Code.ShouldBe(BasketSettingChanges.InvalidCode);
    }

    [Fact]
    public void Stored_values_round_trip_and_an_unreadable_value_leaves_the_setting_alone()
    {
        var s = BasketSettings.Defaults.With(BasketSettings.MaxClusterRiskKey, "1.25").With(BasketSettings.ClusterRhoKey, "0.8").With(BasketSettings.BreadthSourceKey, BasketSettings.SourceOf(Basket));

        s.ShouldBe(new BasketSettings(BasketSettings.SourceOf(Basket), 1.25, 0.8));
        s.GateBasketId.ShouldBe(Basket);
        s.With(BasketSettings.MaxClusterRiskKey, "none").MaxClusterRiskPct.ShouldBeNull();
        s.With(BasketSettings.BreadthSourceKey, "garbage").BreadthSource.ShouldBe(s.BreadthSource);
        s.With("unknown.key", "1").ShouldBe(s);
    }
}
