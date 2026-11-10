using XDrishti.Domain.Baskets.Derived;

namespace XDrishti.Domain.Tests.Baskets.Derived;

/// <summary>Determinism, hash and validity (spec BK-02 acceptance criteria 17, 19).</summary>
public sealed class DerivedParamsTests
{
    [Fact]
    public void The_same_numbers_give_the_same_hash_and_any_change_gives_another()
    {
        var a = DerivedParams.Create(1).Value;
        var b = DerivedParams.Create(1).Value;
        var otherNumber = DerivedParams.Create(1, new Dictionary<string, double> { ["Basket1.TopK"] = 40 }).Value;
        var otherVersion = DerivedParams.Create(2).Value;

        a.Hash.ShouldBe(b.Hash);
        otherNumber.Hash.ShouldNotBe(a.Hash);
        otherVersion.Hash.ShouldNotBe(a.Hash);
    }

    [Fact]
    public void Every_number_of_the_defaults_changes_the_hash_when_changed()
    {
        var baseline = DerivedParams.Default();

        foreach (var (key, value) in baseline.Values)
        {
            var changed = DerivedParams.Create(1, new Dictionary<string, double> { [key] = value + 1 });

            changed.IsSuccess.ShouldBeTrue(key);
            changed.Value.Hash.ShouldNotBe(baseline.Hash, key);
        }
    }

    [Fact]
    public void The_spec_defaults_are_in_place()
    {
        var p = DerivedParams.Default();

        p["BufferFactor"].ShouldBe(1.5);
        p["MinDwellSessions"].ShouldBe(5);
        p["Basket1.TopK"].ShouldBe(50);
        p["Basket9.MinRatio"].ShouldBe(0.97);
    }

    [Theory]
    [InlineData("BufferFactor", 0.99)]
    [InlineData("Basket1.TopK", 0)]
    [InlineData("Basket3.Window", 1)]
    [InlineData("Basket1.AtrWindow", 1)]
    [InlineData("Basket6.Variant", 3)]
    [InlineData("Basket5.MinBeta", double.NaN)]
    [InlineData("Basket9.MinRatio", double.PositiveInfinity)]
    public void A_value_out_of_range_is_rejected_with_a_message_naming_the_key(string key, double value)
    {
        var result = DerivedParams.Create(1, new Dictionary<string, double> { [key] = value });

        result.IsFailure.ShouldBeTrue();
        result.Error.Message.ShouldContain(key);
    }

    [Fact]
    public void An_unknown_key_or_a_version_below_1_is_rejected()
    {
        DerivedParams.Create(1, new Dictionary<string, double> { ["Basket1.PlaceOrder"] = 1 }).IsFailure.ShouldBeTrue();
        DerivedParams.Create(0).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void No_engine_type_has_an_order_like_member()
    {
        var types = typeof(DerivedParams).Assembly.GetTypes().Where(t => t.Namespace == "XDrishti.Domain.Baskets.Derived");

        var names = types.SelectMany(t => t.GetMembers().Select(m => m.Name).Append(t.Name));

        names.ShouldNotContain(n => n.Contains("Order", StringComparison.OrdinalIgnoreCase) || n.Contains("Broker", StringComparison.OrdinalIgnoreCase));
    }
}
