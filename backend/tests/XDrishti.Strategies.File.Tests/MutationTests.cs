using System.Text.Json.Nodes;
using Json.Schema;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.File.Tests.Support;

namespace XDrishti.Strategies.File.Tests;

/// <summary>
/// Criteria 4 and 6 of P3-02c: twelve mutations of the example, each reported with path, line, column and code; the JSON Schema rejects the shape errors and the compile the others. The table says which.
/// </summary>
public sealed class MutationTests
{
    private static readonly JsonSchema Schema = JsonSchema.FromText(StrategyFileSchema.Json());

    private static string Hundred() => "all:\n" + string.Concat(Enumerable.Repeat("      - a\n", 101));

    public static TheoryData<string, string, string, string?, string?, bool> Cases => new()
    {
        // name, text before, text after, file code, path, schema rejects (false: the compile or the YAML reader judges it)
        { "misspelt key", "trigger:", "triger:", FileCodes.UnknownField, "triger", true },
        { "wrong type", "id: VWAP_RSI_TREND", "id: [VWAP]", FileCodes.WrongType, "id", true },
        { "missing required", "  style: intraday\n", string.Empty, FileCodes.MissingField, "style", true },
        { "unknown policy", "policy: FIXED_RR", "policy: FIXED_RRR", FileCodes.BadValue, "buy.exits[0].policy", true },
        { "out-of-range book_pct", "book_pct: 50", "book_pct: 500", null, null, false },
        { "unknown timeframe", "timeframes: [15m]", "timeframes: [17m]", FileCodes.BadValue, "timeframes[0]", true },
        { "list over its limit", "all:\n      - crosses_above(close, vwap)\n", Hundred(), FileCodes.TooManyItems, null, true },
        { "all and any together", "trigger:\n    all:", "trigger:\n    any: [a]\n    all:", FileCodes.BadConditionList, "buy.trigger", true },
        { "bad time", "square_off: \"15:15\"", "square_off: \"25:99\"", FileCodes.BadTime, "time_rules.square_off", true },
        { "negative number", "square_off: \"15:15\" }", "square_off: \"15:15\", max_hold_sessions: -1 }", FileCodes.BadValue, "time_rules.max_hold_sessions", true },
        { "duplicate key", "style: intraday", "style: intraday\n  style: swing", FileCodes.DuplicateKey, null, false },
        { "field not used by its kind", "type: swing_low", "type: fixed_pct, pct: 1", null, null, false },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Each_mutation_is_located_by_the_reader_the_schema_or_the_compile(string name, string before, string after, string? code, string? path, bool schemaRejects)
    {
        var text = Example.Text.Replace(before, after);
        text.ShouldNotBe(Example.Text, name);

        var result = StrategyFile.Read(text);

        if (code is not null)
        {
            var problem = result.Problems.First(p => p.Code == code);
            problem.Severity.ShouldBe(ProblemSeverity.Error);
            problem.Line.ShouldBeGreaterThan(0);
            problem.Column.ShouldBeGreaterThan(0);
            if (path is not null)
            {
                problem.Path.ShouldBe(path);
            }

            result.Definition.ShouldBeNull();
        }
        else
        {
            result.HasErrors.ShouldBeFalse(name);
            StrategyCompiler.Compile(result.Definition!, Timeframe.M15, Side.Buy).Errors.ShouldNotBeEmpty(name + ": the compile rejects it");
        }

        var json = YamlJson.Convert(text);
        if (json is not null)
        {
            Schema.Evaluate(json).IsValid.ShouldBe(!schemaRejects, name);
        }
    }

    [Fact]
    public void All_the_problems_of_a_file_are_reported_in_one_pass_in_file_order()
    {
        var text = Example.Text.Replace("style: intraday", "style: monthly").Replace("timeframes: [15m]", "timeframes: [17m]").Replace("rsi_min: 55", "rsi_min: x1").Replace("type: at_close", "type: later");

        var errors = StrategyFile.Read(text).Problems.Where(p => p.Severity == ProblemSeverity.Error).ToList();

        errors.Select(p => p.Path).ShouldBe(["style", "timeframes[0]", "params.rsi_min", "buy.entry.type"]);
        errors.Select(p => p.Line).ShouldBe([.. errors.Select(p => p.Line).Order()]);
    }

    [Fact]
    public void The_example_and_the_writers_output_for_the_corpus_pass_the_schema()
    {
        Schema.Evaluate(YamlJson.Convert(Example.Text)).IsValid.ShouldBeTrue();
        Schema.Evaluate(YamlJson.Convert(Example.Perturbed)).IsValid.ShouldBeTrue();
        foreach (var definition in Corpus.Definitions(seed: 11, count: 200))
        {
            var text = StrategyFile.Write(definition);
            var evaluation = Schema.Evaluate(YamlJson.Convert(text), new EvaluationOptions { OutputFormat = OutputFormat.List });
            evaluation.IsValid.ShouldBeTrue(text);
        }
    }

    [Fact]
    public void The_schema_is_a_valid_draft_2020_12_schema_and_equals_the_golden_file()
    {
        MetaSchemas.Draft202012.Evaluate(JsonNode.Parse(StrategyFileSchema.Json())).IsValid.ShouldBeTrue();
        GoldenFile.Check("strategy-schema-1.json", StrategyFileSchema.Json());
        StrategyFileSchema.Json().ShouldBeSameAs(StrategyFileSchema.Json(), "generated once per process");
    }

    [Fact]
    public void Random_mutations_of_the_example_never_throw_and_give_a_definition_or_a_problem()
    {
        var random = new Random(3);
        var alphabet = ":-[]{}\"' \n#,&*!|>0aZ_.";
        for (var i = 0; i < 2000; i++)
        {
            var chars = Example.Text.ToCharArray();
            for (var k = random.Next(1, 4); k > 0; k--)
            {
                chars[random.Next(chars.Length)] = alphabet[random.Next(alphabet.Length)];
            }

            var result = StrategyFile.Read(new string(chars));

            (result.Definition is not null || result.Problems.Any(p => p.Severity == ProblemSeverity.Error)).ShouldBeTrue();
        }
    }

    [Fact]
    public void Random_text_never_throws()
    {
        var random = new Random(5);
        for (var i = 0; i < 2000; i++)
        {
            var chars = new char[random.Next(0, 300)];
            for (var k = 0; k < chars.Length; k++)
            {
                chars[k] = (char)random.Next(0, 0x300);
            }

            var result = StrategyFile.Read(new string(chars));

            (result.Definition is not null || result.HasErrors).ShouldBeTrue();
        }
    }
}
