using System.Text.Json;

namespace XDrishti.Strategies.File.Tests;

/// <summary>Spec P9-07 C3 and C6: the strategy text as plain JSON goes through the same safe reader as the strategy file, and everything the reader refuses is null.</summary>
public sealed class StrategyTextJsonTests
{
    [Fact]
    public void A_text_becomes_a_tree_of_strings()
    {
        var json = StrategyTextJson.ToJson("strategy:\n  id: X\n  params:\n    rr: 2\n  exits:\n    - policy: FIXED_RR\n      rr: [rr]\n");

        json.ShouldNotBeNull();
        using var document = JsonDocument.Parse(json);
        var strategy = document.RootElement.GetProperty("strategy");
        strategy.GetProperty("id").GetString().ShouldBe("X");
        strategy.GetProperty("params").GetProperty("rr").GetString().ShouldBe("2");
        strategy.GetProperty("exits")[0].GetProperty("policy").GetString().ShouldBe("FIXED_RR");
    }

    [Theory]
    [InlineData("a: &x 1\nb: *x\n")]
    [InlineData("a: 1\na: 2\n")]
    [InlineData("a: !!str 1\n")]
    [InlineData("a: 1\n---\nb: 2\n")]
    [InlineData("a: \u0007\n")]
    [InlineData("a: [")]
    public void What_the_reader_refuses_is_null(string text) => StrategyTextJson.ToJson(text).ShouldBeNull();

    [Fact]
    public void A_text_over_the_limit_is_null() => StrategyTextJson.ToJson("a: " + new string('x', 300 * 1024)).ShouldBeNull();
}
