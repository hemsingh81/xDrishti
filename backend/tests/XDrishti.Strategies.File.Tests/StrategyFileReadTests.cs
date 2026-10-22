using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.File.Tests.Support;
using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.File.Tests;

/// <summary>Criteria 1, 2 and 7 of P3-02c: the example, the round trip and the lexical rules of the scalars.</summary>
public sealed class StrategyFileReadTests
{
    [Fact]
    public void The_example_reads_to_the_hand_built_model_with_two_warnings_and_no_errors()
    {
        var result = StrategyFile.Read(Example.Text);

        result.HasErrors.ShouldBeFalse(string.Join("; ", result.Problems.Select(p => p.Code + " " + p.Path + " " + p.Message)));
        result.Definition.ShouldBe(Example.Model());
        result.Envelope.Version.ShouldBe(1);
        result.Problems.Select(p => (p.Code, p.Path, p.Severity)).ShouldBe(
            [(FileCodes.SectionNotValidated, "universe", ProblemSeverity.Warning), (FileCodes.SectionNotValidated, "test", ProblemSeverity.Warning)]);
    }

    [Fact]
    public void The_example_compiles_for_its_15m_buy_side()
    {
        var result = StrategyCompiler.Compile(StrategyFile.Read(Example.Text).Definition!, Timeframe.M15, Side.Buy);

        result.IsSuccess.ShouldBeTrue(string.Join("; ", result.Errors.Select(e => e.Path + " " + e.Problem.Code)));
    }

    [Fact]
    public void Fields_are_located_by_their_model_path()
    {
        var result = StrategyFile.Read(Example.Text);

        result.Locate("time_rules.last_entry")!.Value.Line.ShouldBe(22);
        result.Locate("buy.trigger[0]")!.Value.Line.ShouldBe(16);
        result.Locate("buy.stop.lookback").ShouldNotBeNull();
        result.Locate("exits[9]").ShouldBeNull();
    }

    [Fact]
    public void Five_hundred_seeded_definitions_survive_write_then_read_and_writing_again_is_a_fixed_point()
    {
        foreach (var definition in Corpus.Definitions(seed: 7, count: 500))
        {
            var text = StrategyFile.Write(definition);
            var result = StrategyFile.Read(text);

            result.HasErrors.ShouldBeFalse(text + string.Join("; ", result.Problems.Select(p => p.Code + " " + p.Path + " " + p.Message)));
            result.Definition.ShouldBe(definition, text);
            StrategyFile.Write(result.Definition!).ShouldBe(text);
        }
    }

    [Fact]
    public void The_writer_keeps_a_fixed_layout()
    {
        var text = StrategyFile.Write(Example.Model());

        text.ShouldStartWith("strategy:\n  schema: 1\n  id: VWAP_RSI_TREND\n  style: intraday\n  timeframes:\n    - 15m\n");
        text.ShouldContain("      - \"time >= \\\"09:45\\\"\"\n");
        text.ShouldContain("  time_rules:\n    last_entry: \"14:30\"\n    square_off: \"15:15\"\n");
        text.ShouldEndWith("\n");
        text.ShouldNotContain("\r");
    }

    [Theory]
    [InlineData("0x10")]
    [InlineData("1_000")]
    [InlineData(".inf")]
    [InlineData(".nan")]
    [InlineData("+1")]
    [InlineData("1e999")]
    [InlineData("yes")]
    [InlineData("on")]
    public void A_number_that_is_not_a_plain_decimal_is_a_bad_number_at_that_scalar(string spelling)
    {
        var result = StrategyFile.Read(Example.Text.Replace("rsi_min: 55", "rsi_min: " + spelling));

        var problem = result.Problems.Single(p => p.Severity == ProblemSeverity.Error);
        problem.Code.ShouldBe(FileCodes.BadNumber);
        problem.Path.ShouldBe("params.rsi_min");
        result.Definition.ShouldBeNull();
    }

    [Fact]
    public void A_quoted_number_is_a_wrong_type()
    {
        var result = StrategyFile.Read(Example.Text.Replace("rsi_min: 55", "rsi_min: \"2\""));

        result.Problems.Single(p => p.Severity == ProblemSeverity.Error).Code.ShouldBe(FileCodes.WrongType);
    }

    [Theory]
    [InlineData("last_entry: 14:30")]
    [InlineData("last_entry: \"14:30\"")]
    public void A_time_reads_quoted_or_not(string spelling)
    {
        var result = StrategyFile.Read(Example.Text.Replace("last_entry: \"14:30\"", spelling));

        result.HasErrors.ShouldBeFalse();
        result.Definition!.TimeRules!.LastEntry.ShouldBe(new TimeOnly(14, 30));
    }

    [Fact]
    public void A_quantity_may_name_a_parameter_but_not_a_reserved_word()
    {
        StrategyFile.Read(Example.Text).Definition!.Buy!.Stop!.Lookback.ShouldBe(Quantity.Named("swing_lookback"));
        StrategyFile.Read(Example.Text.Replace("lookback: swing_lookback", "lookback: yes")).Problems.ShouldContain(p => p.Code == FileCodes.BadNumber && p.Path == "buy.stop.lookback");
    }

    [Theory]
    [InlineData("", FileCodes.Empty)]
    [InlineData("# only a comment\n", FileCodes.Empty)]
    [InlineData("strategy:\n", FileCodes.WrongType)]
    [InlineData("- a\n- b\n", FileCodes.WrongType)]
    [InlineData("other: 1\n", FileCodes.UnknownField)]
    [InlineData("strategy:\n\tid: X\n", FileCodes.YamlSyntax)]
    [InlineData("strategy: [unclosed\n", FileCodes.YamlSyntax)]
    [InlineData("schema: 2\n", FileCodes.UnknownField)]
    public void Edge_inputs_give_one_located_code(string text, string code)
    {
        var result = StrategyFile.Read(text);

        result.Definition.ShouldBeNull();
        result.Problems.ShouldContain(p => p.Code == code);
    }

    [Fact]
    public void Only_strategy_with_an_unsupported_schema_is_refused_by_name()
    {
        var result = StrategyFile.Read("strategy:\n  schema: 2\n  id: X\n  style: swing\n");

        result.Problems.ShouldContain(p => p.Code == FileCodes.UnsupportedSchema && p.Path == "schema");
    }

    [Fact]
    public void Crlf_line_ends_and_a_byte_order_mark_read_like_lf()
    {
        var crlf = "﻿" + Example.Text.Replace("\n", "\r\n");

        StrategyFile.Read(crlf).Definition.ShouldBe(Example.Model());
    }

    [Fact]
    public void A_condition_of_4096_characters_reads_and_one_more_is_refused()
    {
        var ok = "strategy:\n  id: X\n  style: swing\n  trigger:\n    all:\n      - \"" + new string('a', 4096) + "\"\n";
        var tooLong = ok.Replace(new string('a', 4096), new string('a', 4097));

        StrategyFile.Read(ok).HasErrors.ShouldBeFalse();
        StrategyFile.Read(tooLong).Problems.ShouldContain(p => p.Code == FileCodes.ScalarTooLong);
    }

    [Fact]
    public void Non_ascii_in_a_condition_is_accepted_for_the_language_to_judge()
    {
        var result = StrategyFile.Read("strategy:\n  id: X\n  style: swing\n  trigger:\n    any:\n      - \"close > 1 # ₹\"\n");

        result.Definition!.Buy!.Trigger!.Items.ShouldBe(["close > 1 # ₹"]);
    }

    [Fact]
    public void Codes_are_stable_strings()
    {
        typeof(FileCodes).GetFields().Select(f => (string)f.GetRawConstantValue()!).ShouldBe(
        [
            "file.yaml_syntax", "file.empty", "file.too_large", "file.too_deep", "file.too_many_nodes", "file.scalar_too_long", "file.too_many_keys", "file.too_many_items", "file.alias_refused",
            "file.tag_refused", "file.multiple_documents", "file.duplicate_key", "file.bad_character", "file.bad_key", "file.unsupported_schema", "file.unknown_field", "file.missing_field",
            "file.wrong_type", "file.bad_number", "file.bad_value", "file.bad_time", "file.bad_condition_list", "file.section_not_validated",
        ]);
    }
}
