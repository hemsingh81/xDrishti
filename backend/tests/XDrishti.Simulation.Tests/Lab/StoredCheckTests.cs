using XDrishti.Simulation.Replay;

namespace XDrishti.Simulation.Tests.Lab;

/// <summary>Criteria 1, 2 and 7 of P5-01 at the library: the check keeps every problem with its place, the hash ignores layout, and no text makes it throw.</summary>
public sealed class StoredCheckTests
{
    internal const string Valid = """
        strategy:
          schema: 1
          id: LAB_TEST
          style: intraday
          timeframes:
            - 15m
          sides:
            - BUY
            - SELL
          params:
            pull_atr: 0.5
            rr: 2
          setup:
            all:
              - "tf(\"1h\").ema(20) > tf(\"1h\").ema(50)"
          trigger:
            all:
              - "close > vwap and close > open"
              - "close[1] > vwap[1] and close[2] > vwap[2]"
          entry:
            type: at_close
          stop:
            type: swing_low
            lookback: 2
          exits:
            - policy: FIXED_RR
              rr: [rr]
          sell:
            setup:
              all:
                - "tf(\"1h\").ema(20) < tf(\"1h\").ema(50)"
            trigger:
              all:
                - "close < vwap and close < open"
                - "close[1] < vwap[1] and close[2] < vwap[2]"
            entry:
              type: at_close
            stop:
              type: swing_high
              lookback: 2
            exits:
              - policy: FIXED_RR
                rr: [rr]
          time_rules:
            first_entry: "10:00"
            last_entry: "14:30"
            square_off: "15:15"
        """;

    [Fact]
    public void A_valid_text_has_no_error_and_the_hash_of_inspect()
    {
        var check = StoredStrategies.Check(Valid);

        check.Issues.ShouldNotContain(i => i.Severity == "error");
        check.Description.ShouldNotBeNull();
        check.Description.Hash.ShouldBe(StoredStrategies.Inspect(Valid).Description!.Hash);
        check.Description.Sides.Count.ShouldBe(2);
    }

    [Fact]
    public void Layout_comments_line_ends_and_key_order_do_not_change_the_hash()
    {
        var reordered = "# a comment\r\n\r\n" + Valid.Replace("  id: LAB_TEST\n  style: intraday\n", "  style: intraday\n  id: LAB_TEST   # same id\n").Replace("\n", "\r\n") + "\r\n\r\n";
        reordered.ShouldNotBe(Valid);

        StoredStrategies.Check(reordered).Description!.Hash.ShouldBe(StoredStrategies.Check(Valid).Description!.Hash);
    }

    [Fact]
    public void A_byte_order_mark_is_ignored()
    {
        StoredStrategies.Check("﻿" + Valid).Description!.Hash.ShouldBe(StoredStrategies.Check(Valid).Description!.Hash);
    }

    [Fact]
    public void The_version_of_the_envelope_is_reported_and_is_not_in_the_hash()
    {
        var versioned = Valid.Replace("  id: LAB_TEST\n", "  id: LAB_TEST\n  version: 3\n");

        var check = StoredStrategies.Check(versioned);

        check.Version.ShouldBe(3);
        check.Description!.Hash.ShouldBe(StoredStrategies.Check(Valid).Description!.Hash);
        StoredStrategies.Check(Valid).Version.ShouldBeNull();
    }

    private static readonly string SellBlock = Valid[Valid.IndexOf("  sell:", StringComparison.Ordinal)..Valid.IndexOf("  time_rules:", StringComparison.Ordinal)];

    public static TheoryData<string, string, string, string, string> Mutations => new()
    {
        // name, before, after, code, path
        { "misspelt word in buy.trigger[1]", "close[1] > vwap[1] and", "clse[1] > vwap[1] and", "strategy.lint.unknown_name", "buy.trigger[1]" },
        { "a fifth param", "    rr: 2\n", "    rr: 2\n    a: 1\n    b: 2\n    c: 3\n", "strategy.model.too_many_params", "params" },
        { "BUY and SELL without a sell block", SellBlock, string.Empty, "strategy.model.sell_block_missing", "sell" },
        { "a swing SELL", "style: intraday", "style: swing", "strategy.model.swing_sell_unsupported", "sides[1]" },
        { "a filter with no data", "  time_rules:", "  filters: [no_such_filter]\n  time_rules:", "strategy.model.unknown_filter", "filters[0]" },
        { "an unknown key", "  entry:\n    type: at_close\n  stop", "  entri: 1\n  entry:\n    type: at_close\n  stop", "file.unknown_field", "entri" },
        { "a wrong type", "id: LAB_TEST", "id: [x]", "file.wrong_type", "id" },
        { "a duplicate key", "style: intraday", "style: intraday\n  style: swing", "file.duplicate_key", string.Empty },
        { "a scalar over its limit", "id: LAB_TEST", "id: " + new string('a', 5000), "file.scalar_too_long", string.Empty },
        { "an alias", "id: LAB_TEST", "id: &a LAB_TEST\n  version: *a", "file.alias_refused", string.Empty },
    };

    [Theory]
    [MemberData(nameof(Mutations))]
    public void Each_mutation_is_located_by_its_code_and_path_and_nothing_throws(string name, string before, string after, string code, string path)
    {
        var text = Valid.Replace(before, after);
        text.ShouldNotBe(Valid, name);

        var check = StoredStrategies.Check(text);

        check.Description.ShouldBeNull(name);
        var issue = check.Issues.First(i => i.Code == code);
        issue.Severity.ShouldBe("error");
        issue.Path.ShouldBe(path, name);
        issue.Line.ShouldNotBeNull(name);
    }

    [Fact]
    public void A_problem_of_the_rules_language_carries_its_item_relative_span_and_the_line_of_its_field()
    {
        var check = StoredStrategies.Check(Valid.Replace("close[1] > vwap[1] and", "clse[1] > vwap[1] and"));

        var issue = check.Issues.Single(i => i.Code == "strategy.lint.unknown_name");

        issue.Start.ShouldBe(0);
        issue.Length.ShouldBe(4);
        issue.Suggestion.ShouldBe("close");
        issue.Line.ShouldBe(Valid.Split('\n').ToList().FindIndex(l => l.Contains("close[1] > vwap[1] and", StringComparison.Ordinal)) + 1);
    }

    [Fact]
    public void Warnings_are_kept_beside_the_errors_and_do_not_refuse_a_text()
    {
        var withTest = Valid + "\ntest:\n  period: x";

        var check = StoredStrategies.Check(withTest);

        check.Description.ShouldNotBeNull();
        check.Issues.ShouldContain(i => i.Severity == "warning" && i.Code == "file.section_not_validated");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\0")]
    [InlineData("strategy:")]
    [InlineData("- - - [")]
    [InlineData("strategy: &a\n  id: [*a, *a]")]
    public void No_text_makes_the_check_throw_and_a_bad_one_has_an_error(string text)
    {
        var check = StoredStrategies.Check(text);

        check.Description.ShouldBeNull();
        check.Issues.ShouldContain(i => i.Severity == "error");
    }

    [Fact]
    public void A_very_long_single_line_is_a_problem_not_a_failure()
    {
        var check = StoredStrategies.Check(new string('x', 400_000));

        check.Description.ShouldBeNull();
        check.Issues.ShouldContain(i => i.Code == "file.too_large");
    }

    [Fact]
    public void Retarget_gives_the_text_another_id_and_therefore_another_hash()
    {
        var retargeted = StoredStrategies.Retarget(Valid, "LAB_COPY");

        retargeted.ShouldNotBeNull();
        var check = StoredStrategies.Check(retargeted);
        check.Description!.Id.ShouldBe("LAB_COPY");
        check.Description.Hash.ShouldNotBe(StoredStrategies.Check(Valid).Description!.Hash);
        StoredStrategies.Retarget("strategy:", "X").ShouldBeNull();
    }

    [Fact]
    public void A_warm_check_of_a_two_sided_text_takes_under_50_ms_at_the_95th_percentile()
    {
        for (var warm = 0; warm < 5; warm++)
        {
            StoredStrategies.Check(Valid);
        }

        var times = new List<double>();
        for (var run = 0; run < 40; run++)
        {
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            StoredStrategies.Check(Valid);
            times.Add(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }

        times.Order().ElementAt(37).ShouldBeLessThan(50);
    }

    [Fact]
    public void The_schema_is_the_json_schema_of_the_file()
    {
        StoredStrategies.SchemaJson().ShouldContain("https://json-schema.org/draft/2020-12/schema");
    }
}
