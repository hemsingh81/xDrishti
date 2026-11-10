using XDrishti.Domain.Selection.SizeScaling;

namespace XDrishti.Domain.Tests.Selection.SizeScaling;

/// <summary>BK-10 criterion 9: only an owner actor, values checked, each change recorded with its direction; and the defaults are everything OFF.</summary>
public sealed class SizingChangesTests
{
    private const string Owner = "owner:hem";

    private static readonly Guid Basket = new("00000000-0000-0000-0000-0000000000c1");

    [Fact]
    public void With_no_row_everything_is_off_and_neutral()
    {
        SizingSettings.Defaults.ShouldBe(new SizingSettings(false, 0.5, false, 0.01));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("system:nightly")]
    [InlineData("auto:event-filter")]
    [InlineData("assistant")]
    public void An_actor_that_is_not_an_owner_is_refused_before_anything_else(string? actor)
    {
        var settings = SizingChanges.ForSettings(actor, SizingSettings.Defaults, true, null, null, null);
        var budget = SizingChanges.ForBudget(actor, Basket, null, 40);

        settings.IsFailure.ShouldBeTrue();
        settings.Error.Code.ShouldBe("selection.owner_only");
        budget.IsFailure.ShouldBeTrue();
        budget.Error.Code.ShouldBe("selection.owner_only");
    }

    [Fact]
    public void Enabling_is_tighten_and_disabling_is_loosen_for_both_switches()
    {
        var on = SizingChanges.ForSettings(Owner, SizingSettings.Defaults, true, null, true, null).Value;
        var off = SizingChanges.ForSettings(Owner, new SizingSettings(true, 0.5, true, 0.01), false, null, false, null).Value;

        on.Select(r => (r.Key, r.Direction)).ShouldBe([("sizing.vol_target.enabled", "tighten"), ("sizing.capacity.enabled", "tighten")]);
        off.ShouldAllBe(r => r.Direction == "loosen");
    }

    [Fact]
    public void Floor_and_capacity_percent_follow_the_spec_direction_rule()
    {
        var current = new SizingSettings(true, 0.5, true, 0.01);

        SizingChanges.ForSettings(Owner, current, null, 0.25, null, null).Value.Single().Direction.ShouldBe("loosen"); // lowered
        SizingChanges.ForSettings(Owner, current, null, 0.75, null, null).Value.Single().Direction.ShouldBe("tighten"); // raised
        SizingChanges.ForSettings(Owner, current, null, null, null, 0.02).Value.Single().Direction.ShouldBe("loosen"); // raised
        SizingChanges.ForSettings(Owner, current, null, null, null, 0.005).Value.Single().Direction.ShouldBe("tighten"); // lowered
    }

    [Theory]
    [InlineData(0.24)]
    [InlineData(1.01)]
    [InlineData(0.0)]
    [InlineData(double.NaN)]
    public void The_floor_is_validated_from_a_quarter_to_one(double floor)
    {
        var result = SizingChanges.ForSettings(Owner, SizingSettings.Defaults, null, floor, null, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("selection.invalid");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.1)]
    [InlineData(0.11)]
    public void The_capacity_percent_is_validated(double pct)
    {
        SizingChanges.ForSettings(Owner, SizingSettings.Defaults, null, null, null, pct).Error.Code.ShouldBe("selection.invalid");
    }

    [Fact]
    public void A_change_that_changes_nothing_is_refused_not_written()
    {
        SizingChanges.ForSettings(Owner, SizingSettings.Defaults, false, 0.5, false, 0.01).Error.Code.ShouldBe("selection.invalid");
    }

    [Fact]
    public void A_row_carries_old_and_new_value_actor_and_a_stable_hash()
    {
        var a = SizingChanges.ForSettings(Owner, SizingSettings.Defaults, true, null, null, null).Value.Single();
        var b = SizingChanges.ForSettings(Owner, SizingSettings.Defaults, true, null, null, null).Value.Single();

        (a.OldValue, a.NewValue, a.Actor).ShouldBe(("false", "true", Owner));
        a.Hash.ShouldBe(b.Hash);
        a.Hash.Length.ShouldBe(64);
    }

    [Fact]
    public void Budget_directions_add_and_lower_are_tighten_raise_and_remove_are_loosen()
    {
        SizingChanges.ForBudget(Owner, Basket, null, 40).Value.Direction.ShouldBe("tighten");
        SizingChanges.ForBudget(Owner, Basket, 40, 30).Value.Direction.ShouldBe("tighten");
        SizingChanges.ForBudget(Owner, Basket, 40, 50).Value.Direction.ShouldBe("loosen");
        var removed = SizingChanges.ForBudget(Owner, Basket, 40, null).Value;
        removed.Direction.ShouldBe("loosen");
        removed.NewValue.ShouldBe("none");
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(101.0)]
    [InlineData(double.NaN)]
    public void A_budget_outside_one_to_one_hundred_is_refused(double pct)
    {
        SizingChanges.ForBudget(Owner, Basket, null, pct).Error.Code.ShouldBe("selection.invalid");
    }

    [Fact]
    public void The_same_budget_or_none_to_none_changes_nothing()
    {
        SizingChanges.ForBudget(Owner, Basket, 40, 40).IsFailure.ShouldBeTrue();
        SizingChanges.ForBudget(Owner, Basket, null, null).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void An_empty_basket_id_is_refused()
    {
        SizingChanges.ForBudget(Owner, Guid.Empty, null, 40).Error.Code.ShouldBe("selection.invalid");
    }
}
