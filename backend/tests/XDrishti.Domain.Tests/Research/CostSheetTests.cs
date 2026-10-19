using System.Globalization;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Domain.Tests.Research;

/// <summary>
/// Criteria 1 to 3 of P3-03c: the charge sheet fails closed (every field of D4 omitted in turn, every bad kind of value, a repeated name), its canonical form and hashes do not depend on the spelling, and the rates in force on a date are the
/// last schedule's that starts on or before it.
/// </summary>
public sealed class CostSheetTests
{
    private static (string Path, string Code)[] Problems(string text)
    {
        var result = CostSheetJson.Parse(text);
        result.IsFailure.ShouldBeTrue("the text should be refused: " + text);
        result.Error.Code.ShouldBe(CostErrors.SheetInvalidCode);
        return [.. result.Error.Fields!.Select(f => (f.Field, f.Code!))];
    }

    private static void ShouldRefuseOneProblem(SheetSpec spec, string path, string code)
    {
        CostSheetJson.Parse(new SheetSpec().Render()).IsSuccess.ShouldBeTrue("the sheet with the problem repaired parses");
        Problems(spec.Render()).ShouldBe([(path, code)]);
    }

    [Fact]
    public void A_valid_sheet_parses_and_a_flat_fee_costs_the_fee_on_any_turnover()
    {
        var spec = new SheetSpec();
        spec.Schedules[0].Intraday["brokerage_pct"] = "0";
        spec.Schedules[0].Intraday["brokerage_cap"] = "20";
        spec.Schedules[0].Intraday["brokerage_min"] = "20";

        var sheet = CostSheetJson.Parse(spec.Render());

        sheet.IsSuccess.ShouldBeTrue();
        foreach (var turnover in new[] { 1m, 1_000m, 25_000_000m })
        {
            CostModel.Leg(sheet.Value, CostSegment.IntradayEquity, Side.Buy, turnover, new DateOnly(2026, 3, 2)).Value.Brokerage.ShouldBe(20m);
        }
    }

    [Theory]
    [InlineData("intraday_equity")]
    [InlineData("delivery_equity")]
    public void Every_line_omitted_in_turn_is_required_with_its_path(string segment)
    {
        foreach (var name in SheetSpec.RateNames)
        {
            var spec = new SheetSpec();
            (segment == "intraday_equity" ? spec.Schedules[0].Intraday : spec.Schedules[0].Delivery)[name] = null;

            ShouldRefuseOneProblem(spec, $"schedules[0].{segment}.{name}", CostErrors.Required);
        }
    }

    [Theory]
    [InlineData("intraday_equity")]
    [InlineData("delivery_equity")]
    public void A_json_null_a_string_a_negative_and_a_non_finite_number_are_each_refused_on_every_line(string segment)
    {
        foreach (var name in SheetSpec.RateNames)
        {
            foreach (var (raw, code) in new[] { ("null", CostErrors.Required), ("\"0.03\"", CostErrors.NotANumber), ("NaN", CostErrors.NotANumber), ("Infinity", CostErrors.NotANumber), ("-Infinity", CostErrors.NotANumber), ("-1", CostErrors.Negative), ("true", CostErrors.NotANumber) })
            {
                var spec = new SheetSpec();
                (segment == "intraday_equity" ? spec.Schedules[0].Intraday : spec.Schedules[0].Delivery)[name] = raw;

                ShouldRefuseOneProblem(spec, $"schedules[0].{segment}.{name}", code);
            }
        }
    }

    [Fact]
    public void A_line_above_its_bound_is_too_large_and_a_line_at_its_bound_is_fine()
    {
        var bounds = new Dictionary<string, decimal>
        {
            ["brokerage_pct"] = 5,
            ["brokerage_cap"] = 10_000,
            ["brokerage_min"] = 0,
            ["stt_buy_pct"] = 5,
            ["stt_sell_pct"] = 5,
            ["exchange_txn_pct"] = 5,
            ["sebi_per_crore"] = 1_000,
            ["stamp_buy_pct"] = 5,
            ["other_pct"] = 5,
            ["dp_charge_per_sell"] = 1_000,
        };

        foreach (var (name, bound) in bounds.Where(b => b.Key != "brokerage_min"))
        {
            var at = new SheetSpec();
            at.Schedules[0].Delivery[name] = bound.ToString(CultureInfo.InvariantCulture);
            if (name == "brokerage_pct")
            {
                at.Schedules[0].Delivery["brokerage_cap"] = "20";
            }

            CostSheetJson.Parse(at.Render()).IsSuccess.ShouldBeTrue(name + " at its bound");

            var above = new SheetSpec();
            above.Schedules[0].Delivery[name] = (bound + 0.01m).ToString(CultureInfo.InvariantCulture);
            if (name == "brokerage_pct")
            {
                above.Schedules[0].Delivery["brokerage_cap"] = "20";
            }

            Problems(above.Render()).ShouldBe([($"schedules[0].delivery_equity.{name}", CostErrors.TooLarge)]);
        }

        var minAbove = new SheetSpec();
        minAbove.Schedules[0].Intraday["brokerage_min"] = "10001";
        Problems(minAbove.Render()).ShouldContain(("schedules[0].intraday_equity.brokerage_min", CostErrors.TooLarge));
        var gst = new SheetSpec();
        gst.Schedules[0].Gst = "100.5";
        ShouldRefuseOneProblem(gst, "schedules[0].gst_pct", CostErrors.TooLarge);
    }

    [Fact]
    public void A_rate_with_more_than_six_decimals_is_too_precise_and_trailing_zeros_do_not_count()
    {
        var spec = new SheetSpec();
        spec.Schedules[0].Intraday["exchange_txn_pct"] = "0.0000001";
        ShouldRefuseOneProblem(spec, "schedules[0].intraday_equity.exchange_txn_pct", CostErrors.TooPrecise);

        var padded = new SheetSpec();
        padded.Schedules[0].Intraday["exchange_txn_pct"] = "0.005000000000";
        CostSheetJson.Parse(padded.Render()).IsSuccess.ShouldBeTrue();
        var six = new SheetSpec();
        six.Schedules[0].Intraday["exchange_txn_pct"] = "0.000001";
        CostSheetJson.Parse(six.Render()).IsSuccess.ShouldBeTrue();
        var gst = new SheetSpec();
        gst.Schedules[0].Gst = "18.1234567";
        ShouldRefuseOneProblem(gst, "schedules[0].gst_pct", CostErrors.TooPrecise);
    }

    [Theory]
    [InlineData("1e-29")]
    [InlineData("0.00000000000000000000000000001")]
    [InlineData("1E-30")]
    [InlineData("5e-400")]
    public void A_number_that_underflows_to_zero_is_too_precise_and_never_a_zero_rate(string token)
    {
        var spec = new SheetSpec();
        spec.Schedules[0].Intraday["other_pct"] = token;

        ShouldRefuseOneProblem(spec, "schedules[0].intraday_equity.other_pct", CostErrors.TooPrecise);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.0")]
    [InlineData("0e5")]
    [InlineData("0.000000000000000000000000000000")]
    [InlineData("-0")]
    public void A_true_zero_in_any_spelling_is_still_a_zero(string token)
    {
        var spec = new SheetSpec();
        spec.Schedules[0].Intraday["other_pct"] = token;

        CostSheetJson.Parse(spec.Render()).Value.Schedules[0].IntradayEquity.OtherPct.ShouldBe(0m);
    }

    [Fact]
    public void An_unknown_key_is_shown_in_the_error_path_truncated_and_without_control_characters()
    {
        var longName = new string('k', 15_000);
        var spec = new SheetSpec { Extra = "\"" + longName + "\":1, \"a\\u0007b\\nc\":2" };

        var fields = CostSheetJson.Parse(spec.Render()).Error.Fields!;

        fields.Count.ShouldBe(2);
        fields[0].Field.Length.ShouldBeLessThanOrEqualTo(83);
        fields[0].Field.ShouldEndWith("...");
        fields[1].Field.ShouldBe("a\\u0007b\\u000ac");
        fields.ShouldAllBe(f => !f.Field.Any(char.IsControl));
        fields.ShouldAllBe(f => f.Code == CostErrors.UnknownField);
    }

    [Fact]
    public void The_schedules_are_a_read_only_view_and_not_the_internal_array()
    {
        var sheet = TestCostSheet.Sheet;

        sheet.Schedules.ShouldNotBeOfType<ChargeSchedule[]>();
        ((IList<ChargeSchedule>)sheet.Schedules).IsReadOnly.ShouldBeTrue();
        Should.Throw<NotSupportedException>(() => ((IList<ChargeSchedule>)sheet.Schedules)[0] = sheet.Schedules[1]);
        ReferenceEquals(sheet.Schedules, sheet.Schedules).ShouldBeTrue();
    }

    [Fact]
    public void Dates_that_repeat_descend_or_lie_outside_2000_to_2099_are_refused()
    {
        var repeated = new SheetSpec();
        repeated.Schedules.Add(new SheetSpec.ScheduleSpec("\"2026-01-01\""));
        ShouldRefuseOneProblem(repeated, "schedules[1].effective_from", CostErrors.Duplicate);

        var descending = new SheetSpec();
        descending.Schedules.Add(new SheetSpec.ScheduleSpec("\"2025-12-31\""));
        ShouldRefuseOneProblem(descending, "schedules[1].effective_from", CostErrors.NotAscending);

        foreach (var date in new[] { "\"1999-12-31\"", "\"2100-01-01\"" })
        {
            var spec = new SheetSpec();
            spec.Schedules[0].From = date;
            ShouldRefuseOneProblem(spec, "schedules[0].effective_from", CostErrors.OutOfRange);
        }

        foreach (var date in new[] { "\"2026-1-1\"", "\"01/01/2026\"", "20260101", "\"2026-02-30\"" })
        {
            var spec = new SheetSpec();
            spec.Schedules[0].From = date;
            Problems(spec.Render()).Single().Path.ShouldBe("schedules[0].effective_from");
        }

        foreach (var year in new[] { "\"2000-01-01\"", "\"2099-12-31\"" })
        {
            var edge = new SheetSpec();
            edge.Schedules[0].From = year;
            CostSheetJson.Parse(edge.Render()).IsSuccess.ShouldBeTrue(year);
        }
    }

    [Fact]
    public void Zero_schedules_and_thirteen_schedules_are_refused_and_twelve_are_fine()
    {
        var none = new SheetSpec();
        none.Schedules.Clear();
        ShouldRefuseOneProblem(none, "schedules", CostErrors.Required);

        var twelve = new SheetSpec();
        twelve.Schedules.Clear();
        for (var i = 0; i < 12; i++)
        {
            twelve.Schedules.Add(new SheetSpec.ScheduleSpec($"\"{2020 + i}-01-01\""));
        }

        CostSheetJson.Parse(twelve.Render()).IsSuccess.ShouldBeTrue();
        twelve.Schedules.Add(new SheetSpec.ScheduleSpec("\"2032-01-01\""));
        Problems(twelve.Render()).ShouldBe([("schedules", CostErrors.TooLarge)]);
    }

    [Fact]
    public void An_unknown_field_is_refused_at_the_sheet_the_schedule_and_the_line_level()
    {
        var top = new SheetSpec { Extra = "\"colour\":1" };
        ShouldRefuseOneProblem(top, "colour", CostErrors.UnknownField);
        var schedule = new SheetSpec();
        schedule.Schedules[0].Extra = "\"colour\":1";
        ShouldRefuseOneProblem(schedule, "schedules[0].colour", CostErrors.UnknownField);
        var line = new SheetSpec();
        line.Schedules[0].IntradayExtra = "\"stt_other_pct\":1";
        ShouldRefuseOneProblem(line, "schedules[0].intraday_equity.stt_other_pct", CostErrors.UnknownField);
    }

    [Fact]
    public void A_property_name_repeated_in_one_object_is_refused_because_the_second_value_would_win()
    {
        var top = new SheetSpec { Extra = "\"label\":\"second\"" };
        ShouldRefuseOneProblem(top, "label", CostErrors.DuplicateProperty);
        var line = new SheetSpec();
        line.Schedules[0].IntradayExtra = "\"stt_sell_pct\":0.9";
        ShouldRefuseOneProblem(line, "schedules[0].intraday_equity.stt_sell_pct", CostErrors.DuplicateProperty);
        var schedule = new SheetSpec();
        schedule.Schedules[0].Extra = "\"gst_pct\":0";
        ShouldRefuseOneProblem(schedule, "schedules[0].gst_pct", CostErrors.DuplicateProperty);
    }

    [Theory]
    [InlineData("\"Upper\"")]
    [InlineData("\"-x\"")]
    [InlineData("\"a b\"")]
    [InlineData("\".dot\"")]
    [InlineData("\"\"")]
    [InlineData("\"a1234567890123456789012345678901234567890\"")]
    public void An_id_outside_the_pattern_is_refused_and_one_of_forty_characters_is_fine(string id)
    {
        var spec = new SheetSpec { Id = id };

        ShouldRefuseOneProblem(spec, "id", CostErrors.InvalidValue);
        CostSheetJson.Parse(new SheetSpec { Id = "\"" + new string('a', 40) + "\"" }.Render()).IsSuccess.ShouldBeTrue();
        CostSheetJson.Parse(new SheetSpec { Id = "\"0a._-\"" }.Render()).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void The_texts_are_required_and_bounded()
    {
        ShouldRefuseOneProblem(new SheetSpec { Id = null }, "id", CostErrors.Required);
        ShouldRefuseOneProblem(new SheetSpec { Label = null }, "label", CostErrors.Required);
        ShouldRefuseOneProblem(new SheetSpec { Source = "null" }, "source", CostErrors.Required);
        ShouldRefuseOneProblem(new SheetSpec { Label = "\"" + new string('x', 81) + "\"" }, "label", CostErrors.TooLong);
        ShouldRefuseOneProblem(new SheetSpec { Source = "\"" + new string('x', 201) + "\"" }, "source", CostErrors.TooLong);
        ShouldRefuseOneProblem(new SheetSpec { Checked = "\"" + new string('x', 201) + "\"" }, "checked", CostErrors.TooLong);
        ShouldRefuseOneProblem(new SheetSpec { Label = "5" }, "label", CostErrors.InvalidValue);
        CostSheetJson.Parse(new SheetSpec { Label = "\"" + new string('x', 80) + "\"", Source = "\"" + new string('x', 200) + "\"", Checked = "\"" + new string('x', 200) + "\"" }.Render()).IsSuccess.ShouldBeTrue();
        CostSheetJson.Parse(new SheetSpec { Checked = "null" }.Render()).Value.Checked.ShouldBeNull("null is an absent note");
    }

    [Fact]
    public void A_cap_of_zero_with_a_percent_or_a_minimum_above_zero_is_brokerage_cap_required_and_never_means_no_cap()
    {
        var percent = new SheetSpec();
        percent.Schedules[0].Intraday["brokerage_cap"] = "0";
        ShouldRefuseOneProblem(percent, "schedules[0].intraday_equity.brokerage_cap", CostErrors.BrokerageCapRequired);

        var minimum = new SheetSpec();
        minimum.Schedules[0].Delivery["brokerage_min"] = "5";
        ShouldRefuseOneProblem(minimum, "schedules[0].delivery_equity.brokerage_cap", CostErrors.BrokerageCapRequired);

        var allZero = new SheetSpec();
        allZero.Schedules[0].Intraday["brokerage_pct"] = "0";
        allZero.Schedules[0].Intraday["brokerage_cap"] = "0";
        CostSheetJson.Parse(allZero.Render()).IsSuccess.ShouldBeTrue("a cap of 0 with a percent of 0 and a minimum of 0 is no brokerage");
    }

    [Fact]
    public void A_minimum_above_a_non_zero_cap_is_min_above_cap()
    {
        var spec = new SheetSpec();
        spec.Schedules[0].Intraday["brokerage_min"] = "21";

        ShouldRefuseOneProblem(spec, "schedules[0].intraday_equity.brokerage_min", CostErrors.MinAboveCap);
    }

    [Fact]
    public void Text_over_16000_characters_is_too_long_before_it_is_parsed_and_a_canonical_text_over_8000_is_too_long()
    {
        var long16 = new SheetSpec { Label = "\"" + new string('x', 15_950) + "\"" }.Render();
        long16.Length.ShouldBeGreaterThan(CostErrors.MaxInputLength);

        var tooLong = CostSheetJson.Parse(long16);

        tooLong.IsFailure.ShouldBeTrue();
        tooLong.Error.Code.ShouldBe(CostErrors.SheetTooLongCode);

        var big = new SheetSpec { Label = "\"" + new string('x', 80) + "\"", Source = "\"" + new string('x', 200) + "\"", Checked = "\"" + new string('x', 200) + "\"" };
        big.Schedules.Clear();
        for (var i = 0; i < 12; i++)
        {
            var schedule = new SheetSpec.ScheduleSpec($"\"{2020 + i}-01-01\"");
            foreach (var name in SheetSpec.RateNames)
            {
                schedule.Intraday[name] = name switch { "sebi_per_crore" => "999.999999", "dp_charge_per_sell" => "999.999999", "brokerage_cap" => "9999.999999", "brokerage_min" => "9999.999999", _ => "4.999999" };
                schedule.Delivery[name] = schedule.Intraday[name];
            }

            big.Schedules.Add(schedule);
        }

        var text = big.Render();
        text.Length.ShouldBeLessThan(CostErrors.MaxInputLength);
        var canonical = CostSheetJson.Parse(text);
        canonical.IsFailure.ShouldBeTrue();
        canonical.Error.Code.ShouldBe(CostErrors.SheetTooLongCode);
    }

    [Fact]
    public void Text_that_is_not_json_and_values_of_the_wrong_kind_are_refused_with_a_path()
    {
        foreach (var text in new[] { "", "{", "[1,2]", "null", "{\"id\":}", "{\"id\":\"a\",}", "{'id':1}", "{\"a\":1} x", "{\"id\":\"\\q\"}" })
        {
            var result = CostSheetJson.Parse(text);

            result.IsFailure.ShouldBeTrue(text);
            result.Error.Code.ShouldBe(CostErrors.SheetInvalidCode, text);
            result.Error.Fields.ShouldNotBeNull().ShouldNotBeEmpty(text);
        }

        Problems(new SheetSpec { SchedulesRaw = "5" }.Render()).ShouldBe([("schedules", CostErrors.InvalidValue)]);
        var nested = new SheetSpec();
        nested.Schedules[0].IntradayRaw = "[]";
        ShouldRefuseOneProblem(nested, "schedules[0].intraday_equity", CostErrors.InvalidValue);
        var rounding = new SheetSpec();
        rounding.Schedules[0].Rounding = "\"banker\"";
        ShouldRefuseOneProblem(rounding, "schedules[0].rounding", CostErrors.InvalidValue);
        var deep = new string('[', 40) + new string(']', 40);
        CostSheetJson.Parse(deep).IsFailure.ShouldBeTrue("nesting is bounded");
    }

    [Fact]
    public void All_problems_are_listed_together_up_to_twenty()
    {
        var spec = new SheetSpec();
        spec.Schedules.Add(new SheetSpec.ScheduleSpec("\"2026-02-01\""));
        foreach (var schedule in spec.Schedules)
        {
            foreach (var name in SheetSpec.RateNames)
            {
                schedule.Intraday[name] = "-1";
                schedule.Delivery[name] = "-1";
            }
        }

        var problems = Problems(spec.Render());

        problems.Length.ShouldBe(CostErrors.MaxFieldErrors);
        problems.ShouldAllBe(p => p.Code == CostErrors.Negative);
        problems[0].Path.ShouldBe("schedules[0].intraday_equity.brokerage_pct");
    }

    [Fact]
    public void A_null_rate_never_reaches_the_model_because_the_rates_type_has_no_way_to_hold_one()
    {
        typeof(ChargeRates).GetProperties().Where(p => p.PropertyType != typeof(decimal) && p.Name != "EqualityContract").ShouldBeEmpty();
        typeof(ChargeRates).GetConstructors().ShouldBeEmpty("only Create builds one");
        ChargeRates.Create(0.03m, 20m, 0m, 0m, 0.04m, 0.005m, 50m, 0.02m, 0m, 0m).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void The_test_sheet_round_trips_and_is_a_test_sheet_by_its_id()
    {
        var sheet = TestCostSheet.Sheet;

        var back = CostSheetJson.Parse(sheet.ToCanonicalJson());

        back.IsSuccess.ShouldBeTrue();
        back.Value.ShouldBe(sheet);
        back.Value.ToCanonicalJson().ShouldBe(sheet.ToCanonicalJson());
        sheet.Id.ShouldBe("test-synthetic-1");
        sheet.IsTest.ShouldBeTrue();
        sheet.Label.ShouldBe("TEST sheet (synthetic numbers)");
        sheet.Checked.ShouldBeNull();
        new SheetSpec { Id = "\"dhan-x\"" }.Render().ShouldNotBeNull();
        CostSheetJson.Parse(new SheetSpec { Id = "\"dhan-x\"" }.Render()).Value.IsTest.ShouldBeFalse();
    }

    [Fact]
    public void The_canonical_text_is_pinned_for_the_test_sheet()
    {
        TestCostSheet.Json.ShouldBe(
            "{\"id\":\"test-synthetic-1\",\"label\":\"TEST sheet (synthetic numbers)\",\"source\":\"invented for tests; not a broker's sheet\",\"schedules\":["
            + "{\"effective_from\":\"2026-01-01\",\"gst_pct\":18,\"rounding\":\"half_away_from_zero\","
            + "\"intraday_equity\":{\"brokerage_pct\":0.03,\"brokerage_cap\":20,\"brokerage_min\":0,\"stt_buy_pct\":0,\"stt_sell_pct\":0.04,\"exchange_txn_pct\":0.005,\"sebi_per_crore\":50,\"stamp_buy_pct\":0.02,\"other_pct\":0,\"dp_charge_per_sell\":0},"
            + "\"delivery_equity\":{\"brokerage_pct\":0,\"brokerage_cap\":0,\"brokerage_min\":0,\"stt_buy_pct\":0.08,\"stt_sell_pct\":0.08,\"exchange_txn_pct\":0.004,\"sebi_per_crore\":50,\"stamp_buy_pct\":0.03,\"other_pct\":0,\"dp_charge_per_sell\":10}},"
            + "{\"effective_from\":\"2026-03-03\",\"gst_pct\":18,\"rounding\":\"half_away_from_zero\","
            + "\"intraday_equity\":{\"brokerage_pct\":0.03,\"brokerage_cap\":20,\"brokerage_min\":0,\"stt_buy_pct\":0,\"stt_sell_pct\":0.035,\"exchange_txn_pct\":0.005,\"sebi_per_crore\":50,\"stamp_buy_pct\":0.02,\"other_pct\":0,\"dp_charge_per_sell\":0},"
            + "\"delivery_equity\":{\"brokerage_pct\":0,\"brokerage_cap\":0,\"brokerage_min\":0,\"stt_buy_pct\":0.08,\"stt_sell_pct\":0.09,\"exchange_txn_pct\":0.004,\"sebi_per_crore\":50,\"stamp_buy_pct\":0.03,\"other_pct\":0,\"dp_charge_per_sell\":10}}]}");
    }

    [Fact]
    public void The_rates_hash_ignores_the_spelling_of_the_input_and_the_descriptive_fields_and_follows_a_digit_or_the_id()
    {
        var baseline = CostSheetJson.Parse(TestCostSheet.Json).Value;
        var spaced = SpelledDifferently(TestCostSheet.Json);

        var respelt = CostSheetJson.Parse(spaced).Value;

        respelt.RatesSha256.ShouldBe(baseline.RatesSha256);
        respelt.ToCanonicalJson().ShouldBe(baseline.ToCanonicalJson());
        baseline.RatesSha256.Length.ShouldBe(64);
        baseline.RatesSha256.ShouldBe(CanonicalText.Sha256Hex(baseline.RatesJson()));
        baseline.RatesJson().ShouldNotContain("label");
        baseline.RatesJson().ShouldNotContain("source");

        var digit = TestCostSheet.Json.Replace("\"exchange_txn_pct\":0.005,\"sebi_per_crore\":50,\"stamp_buy_pct\":0.02", "\"exchange_txn_pct\":0.006,\"sebi_per_crore\":50,\"stamp_buy_pct\":0.02", StringComparison.Ordinal);
        CostSheetJson.Parse(digit).Value.RatesSha256.ShouldNotBe(baseline.RatesSha256);
        CostSheetJson.Parse(TestCostSheet.Json.Replace("test-synthetic-1", "test-synthetic-2", StringComparison.Ordinal)).Value.RatesSha256.ShouldNotBe(baseline.RatesSha256);
    }

    [Fact]
    public void Writing_the_label_the_source_or_the_note_changes_the_text_and_not_the_rates_hash()
    {
        var schedules = TestCostSheet.Sheet.Schedules;
        var hash = TestCostSheet.Sheet.RatesSha256;

        var relabelled = CostSheet.Create(TestCostSheet.Id, "another label", TestCostSheet.Sheet.Source, null, schedules).Value;
        var resourced = CostSheet.Create(TestCostSheet.Id, TestCostSheet.Sheet.Label, "another source", null, schedules).Value;
        var checkedSheet = CostSheet.Create(TestCostSheet.Id, TestCostSheet.Sheet.Label, TestCostSheet.Sheet.Source, "bills of 2026-10", schedules).Value;

        foreach (var changed in new[] { relabelled, resourced, checkedSheet })
        {
            changed.RatesSha256.ShouldBe(hash);
            changed.ToCanonicalJson().ShouldNotBe(TestCostSheet.Json);
        }

        checkedSheet.ToCanonicalJson().ShouldContain("\"checked\":\"bills of 2026-10\"");
        TestCostSheet.Json.ShouldNotContain("checked");
        CostSheetJson.Parse(checkedSheet.ToCanonicalJson()).Value.ShouldBe(checkedSheet);
    }

    [Fact]
    public void The_text_is_the_same_under_a_culture_with_german_separators_and_zero_is_never_written_minus_zero()
    {
        // The test host runs in globalization-invariant mode, so de-DE cannot be loaded: an invariant clone with German separators stands in for it.
        var german = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        german.NumberFormat.NumberDecimalSeparator = ",";
        german.NumberFormat.NumberGroupSeparator = ".";
        var before = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = german;
            CultureInfo.CurrentUICulture = german;
            var parsed = CostSheetJson.Parse(TestCostSheet.Json);

            parsed.IsSuccess.ShouldBeTrue();
            parsed.Value.ToCanonicalJson().ShouldBe(TestCostSheet.Json);
            parsed.Value.RatesSha256.ShouldBe(TestCostSheet.Sheet.RatesSha256);
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = before;
        }

        var negativeZero = new SheetSpec();
        negativeZero.Schedules[0].Intraday["other_pct"] = "-0";
        negativeZero.Schedules[0].Intraday["stt_buy_pct"] = "-0.000";
        var text = CostSheetJson.Parse(negativeZero.Render()).Value.ToCanonicalJson();
        text.ShouldNotContain(":-0", Case.Sensitive);
        text.ShouldContain("\"other_pct\":0,");
    }

    [Fact]
    public void The_rates_in_force_on_a_date_are_those_of_the_last_schedule_that_starts_on_or_before_it()
    {
        var sheet = TestCostSheet.Sheet;

        var before = sheet.RatesOn(new DateOnly(2025, 12, 31));

        before.IsFailure.ShouldBeTrue();
        before.Error.Code.ShouldBe(CostErrors.NoRatesForDateCode);
        sheet.RatesOn(new DateOnly(2026, 1, 1)).Value.EffectiveFrom.ShouldBe(TestCostSheet.ScheduleA);
        sheet.RatesOn(new DateOnly(2026, 3, 2)).Value.EffectiveFrom.ShouldBe(TestCostSheet.ScheduleA);
        sheet.RatesOn(new DateOnly(2026, 3, 3)).Value.EffectiveFrom.ShouldBe(TestCostSheet.ScheduleB);
        sheet.RatesOn(new DateOnly(2099, 12, 31)).Value.EffectiveFrom.ShouldBe(TestCostSheet.ScheduleB);
        sheet.RatesOn(new DateOnly(2026, 3, 2)).Value.IntradayEquity.SttSellPct.ShouldBe(0.04m);
        sheet.RatesOn(new DateOnly(2026, 3, 3)).Value.IntradayEquity.SttSellPct.ShouldBe(0.035m);
    }

    [Fact]
    public void Create_refuses_what_the_parser_refuses_and_a_null_list()
    {
        var a = TestCostSheet.Schedule(TestCostSheet.ScheduleA, TestCostSheet.IntradayA, TestCostSheet.DeliveryA);

        CostSheet.Create("UPPER", "l", "s", null, [a]).Error.Fields!.Single().Code.ShouldBe(CostErrors.InvalidValue);
        CostSheet.Create("ok", "l", "s", null, []).Error.Fields!.Single().Code.ShouldBe(CostErrors.Required);
        CostSheet.Create("ok", "l", "s", null, [a, a]).Error.Fields!.Single().Code.ShouldBe(CostErrors.Duplicate);
        Should.Throw<ArgumentNullException>(() => CostSheet.Create("ok", "l", "s", null, null!));
        Should.Throw<ArgumentNullException>(() => CostSheetJson.Parse(null!));
        ChargeRates.Create(0.03m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m).Error.Fields!.Single().Field.ShouldBe("brokerage_cap");
        ChargeSchedule.Create(new DateOnly(1999, 1, 1), 18m, ChargeRounding.Down, TestCostSheet.IntradayA, TestCostSheet.DeliveryA).Error.Fields!.Single().Code.ShouldBe(CostErrors.OutOfRange);
        ChargeSchedule.Create(a.EffectiveFrom, 18m, (ChargeRounding)9, TestCostSheet.IntradayA, TestCostSheet.DeliveryA).IsFailure.ShouldBeTrue();
        Should.Throw<ArgumentOutOfRangeException>(() => a.RatesOf((CostSegment)9));
    }

    [Fact]
    public void Equality_of_two_sheets_is_by_value_and_a_changed_note_is_a_different_sheet()
    {
        var one = CostSheetJson.Parse(TestCostSheet.Json).Value;
        var two = CostSheetJson.Parse(TestCostSheet.Json).Value;
        var noted = CostSheet.Create(one.Id, one.Label, one.Source, "x", one.Schedules).Value;

        one.ShouldBe(two);
        one.GetHashCode().ShouldBe(two.GetHashCode());
        one.ShouldNotBe(noted);
        one.Equals(null).ShouldBeFalse();
    }

    private static string SpelledDifferently(string canonical)
    {
        // the first two members swapped, 0.030 for 0.03, 18.00 for 18, white space everywhere
        return canonical
            .Replace("{\"id\":\"test-synthetic-1\",\"label\":\"TEST sheet (synthetic numbers)\"", "{\"label\":\"TEST sheet (synthetic numbers)\",\"id\":\"test-synthetic-1\"", StringComparison.Ordinal)
            .Replace("\"brokerage_pct\":0.03,", "\"brokerage_pct\":0.030,", StringComparison.Ordinal)
            .Replace("\"gst_pct\":18,", "\"gst_pct\":18.00,", StringComparison.Ordinal)
            .Replace(",", " ,\n  ", StringComparison.Ordinal)
            .Replace(":", " : ", StringComparison.Ordinal);
    }
}
