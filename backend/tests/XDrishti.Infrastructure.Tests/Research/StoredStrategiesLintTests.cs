using XDrishti.Infrastructure.Research;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.File;
using XDrishti.Strategies.Lint;

namespace XDrishti.Infrastructure.Tests.Research;

/// <summary>Spec P3-02f criterion 5: the rule for comparisons that cannot be true does not fire on the real, working text of the fifteen stored strategies.</summary>
public sealed class StoredStrategiesLintTests
{
    public static TheoryData<string> Ids { get; } = [.. StrategySeeder.EmbeddedTexts().Select(t => t.Name)];

    [Theory]
    [MemberData(nameof(Ids))]
    public void A_stored_strategy_compiles_with_no_never_true_warning(string name)
    {
        var definition = StrategyFile.Read(StrategySeeder.EmbeddedTexts().Single(t => t.Name == name).Text).Definition.ShouldNotBeNull();

        foreach (var timeframe in definition.Timeframes)
        {
            foreach (var side in definition.Sides)
            {
                var result = StrategyCompiler.Compile(definition, timeframe, side);

                result.Problems.ShouldNotContain(p => p.Problem.Code == LintCodes.NeverTrue, name + " " + timeframe + " " + side);
            }
        }
    }

    [Fact]
    public void There_are_fifteen_stored_strategies()
    {
        Ids.Count.ShouldBe(15);
    }
}
