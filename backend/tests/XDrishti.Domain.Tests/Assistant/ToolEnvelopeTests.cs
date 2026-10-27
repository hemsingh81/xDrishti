using System.Text.Json;
using System.Text.Json.Nodes;
using XDrishti.Domain.Assistant;

namespace XDrishti.Domain.Tests.Assistant;

/// <summary>Spec P9-02 AC 6: the envelope has one JSON shape, and a result is cut to fit the model's limit by dropping rows from the end.</summary>
public sealed class ToolEnvelopeTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 27, 5, 0, 0, TimeSpan.Zero);

    private static ToolEnvelope WithRows(int count, int width = 1) =>
        new(true, "t", At, ["n", "s"], [.. Enumerable.Range(0, count).Select(i => (IReadOnlyList<object?>)[(long)i, new string('x', width)])], null, null, false, null, [], null);

    [Fact]
    public void The_json_has_the_documented_fields_and_values()
    {
        var envelope = new ToolEnvelope(
            true, "run_sql", At, ["a", "b", "c", "d"], [[1L, "x", null, new List<object?> { 1.5d, "y" }]], new JsonObject { ["elapsedMs"] = 5 }, 7, true,
            new ToolEvidence("basis", ["aiv"], null, 1, ["limit"], [new ToolUnavailable("in", "why")], "abc"), ["note"], null, 2, 1);

        var json = JsonNode.Parse(envelope.ToJson())!.AsObject();

        json["ok"]!.GetValue<bool>().ShouldBeTrue();
        json["tool"]!.GetValue<string>().ShouldBe("run_sql");
        json["asOf"]!.GetValue<string>().ShouldBe("2026-10-27T05:00:00Z");
        json["rows"]![0]!["a"]!.GetValue<long>().ShouldBe(1);
        json["rows"]![0]!["c"].ShouldBeNull();
        json["rows"]![0]!["d"]![0]!.GetValue<double>().ShouldBe(1.5);
        json["data"]!["elapsedMs"]!.GetValue<int>().ShouldBe(5);
        json["rowCount"]!.GetValue<int>().ShouldBe(1);
        json["totalRows"]!.GetValue<int>().ShouldBe(7);
        json["truncated"]!.GetValue<bool>().ShouldBeTrue();
        json["evidence"]!["basis"]!.GetValue<string>().ShouldBe("basis");
        json["evidence"]!["sqlHash"]!.GetValue<string>().ShouldBe("abc");
        json["evidence"]!["unavailable"]![0]!["reason"]!.GetValue<string>().ShouldBe("why");
        json["notes"]![0]!.GetValue<string>().ShouldBe("note");
        json["error"].ShouldBeNull();
        json["redactions"]!.GetValue<int>().ShouldBe(2);
        json["injectionFlags"]!.GetValue<int>().ShouldBe(1);
    }

    [Fact]
    public void A_failure_has_no_rows_and_names_the_error()
    {
        var json = JsonNode.Parse(ToolEnvelope.Failure("t", At, ToolErrorCodes.Disabled, "off").ToJson())!.AsObject();

        json["ok"]!.GetValue<bool>().ShouldBeFalse();
        json["rows"].ShouldBeNull();
        json["error"]!["code"]!.GetValue<string>().ShouldBe("assistant.disabled");
    }

    [Fact]
    public void Numbers_that_are_not_finite_and_decimals_are_written_as_text_and_amounts_in_paise_are_exact()
    {
        var envelope = new ToolEnvelope(true, "t", At, ["a", "b", "c"], [[double.NaN, 12.50m, 12_345_678_901L]], null, null, false, null, [], null);

        var row = JsonNode.Parse(envelope.ToJson())!["rows"]![0]!;

        row["a"]!.GetValue<string>().ShouldBe("NaN");
        row["b"]!.GetValue<string>().ShouldBe("12.50");
        row["c"]!.GetValue<long>().ShouldBe(12_345_678_901L);
    }

    [Fact]
    public void Null_cells_are_left_out_only_when_asked()
    {
        var kept = new ToolEnvelope(true, "t", At, ["a", "b"], [[1L, null]], null, null, false, null, [], null);

        kept.ToJson().ShouldContain("\"b\":null");
        (kept with { OmitNullCells = true }).ToJson().ShouldNotContain("\"b\"");
    }

    [Fact]
    public void A_result_that_fits_is_unchanged()
    {
        var (fitted, json) = WithRows(10).Fit();

        fitted.RowCount.ShouldBe(10);
        fitted.Truncated.ShouldBeFalse();
        json.Length.ShouldBeLessThanOrEqualTo(ToolLimits.MaxModelChars);
    }

    [Fact]
    public void Rows_are_dropped_from_the_end_until_the_text_fits_and_the_total_is_kept()
    {
        var (fitted, json) = WithRows(200, width: 60).Fit();

        json.Length.ShouldBeLessThanOrEqualTo(ToolLimits.MaxModelChars);
        fitted.Truncated.ShouldBeTrue();
        fitted.TotalRows.ShouldBe(200);
        fitted.RowCount.ShouldBeGreaterThan(0);
        fitted.RowCount.ShouldBeLessThan(200);
        fitted.Rows![0][0].ShouldBe(0L);
        fitted.Rows[^1][0].ShouldBe((long)(fitted.RowCount - 1));

        // One more row would not have fitted.
        (WithRows(fitted.RowCount + 1, width: 60) with { TotalRows = 200, Truncated = true }).ToJson().Length.ShouldBeGreaterThan(ToolLimits.MaxModelChars);
    }

    [Fact]
    public void An_envelope_that_cannot_fit_even_without_rows_becomes_a_too_large_failure()
    {
        var huge = new ToolEnvelope(true, "t", At, [], null, null, null, false, null, [new string('n', 7000)], null);

        var (fitted, json) = huge.Fit();

        fitted.Ok.ShouldBeFalse();
        fitted.Error!.Code.ShouldBe(ToolErrorCodes.TooLarge);
        json.Length.ShouldBeLessThanOrEqualTo(ToolLimits.MaxModelChars);
    }

    [Fact]
    public void The_text_is_valid_json_in_every_case()
    {
        foreach (var width in new[] { 1, 40, 400 })
        {
            var (_, json) = WithRows(200, width).Fit();
            Should.NotThrow(() => JsonDocument.Parse(json));
        }
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(300, 300)]
    [InlineData(301, 300)]
    [InlineData(1000, 300)]
    public void A_cell_is_cut_to_300_characters_with_an_ellipsis(int length, int expected)
    {
        var cut = ToolLimits.CutCell(new string('a', length));

        cut.Length.ShouldBe(expected);
        (length > 300).ShouldBe(cut.EndsWith('…'));
    }
}
