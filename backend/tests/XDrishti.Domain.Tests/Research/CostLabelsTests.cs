using System.Text.RegularExpressions;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Domain.Tests.Research;

/// <summary>
/// Criterion 21 of P3-03c: the labels (<see cref="CostDescription"/>, <c>SimulationHeader.LabelsOf</c> lives in the simulation tests), the sizing line, the ten limits and the wording rule of D15: no whole word of a statistic in any text the slice
/// produces. The scan is word-bounded: <c>rates</c> and <c>window</c> are not hits, <c>profit</c> and <c>total</c> are.
/// </summary>
public sealed partial class CostLabelsTests
{
    private static readonly string[] ExtraWords = ["return", "returns", "gain", "gains", "edge", "performance"];

    private static readonly string[] ExtraPhrases = ["after costs", "net profit"];

    /// <summary>The whole words and phrases of D15 found in the text, lower case.</summary>
    private static List<string> Hits(string text)
    {
        var found = ForbiddenWords.In(text).ToList();
        var words = new HashSet<string>(WordPattern().Matches(text).Select(m => m.Value.ToLowerInvariant()), StringComparer.Ordinal);
        found.AddRange(ExtraWords.Where(words.Contains));
        found.AddRange(ExtraPhrases.Where(p => text.Contains(p, StringComparison.OrdinalIgnoreCase)));
        return found;
    }

    [GeneratedRegex(@"\b\w+\b", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();

    private static CostRecord Record(string id = "dhan-x", string? checkedNote = null, decimal capital = 1_000_000m, decimal maxCostR = 0.10m)
    {
        var sheet = CostSheet.Create(id, "Dhan equity charges", "read from the broker's page", checkedNote, TestCostSheet.Sheet.Schedules).Value;
        return CostRecord.Of(CostContext.Create(sheet, SizingProfile.Standard, capital, maxCostR).Value);
    }

    [Fact]
    public void The_scan_catches_a_sentinel_with_profit_and_one_with_total_and_lets_rates_and_window_through()
    {
        Hits("a profit of 5").ShouldContain("profit");
        Hits("the total cost").ShouldContain("total");
        Hits("the edge of the market").ShouldContain("edge");
        Hits("returns").ShouldContain("returns");
        Hits("R after costs").ShouldContain("after costs");
        Hits("figures at the rates of the window; knowledge is a challenge").ShouldBeEmpty();
    }

    [Fact]
    public void The_cost_label_says_none_for_a_gross_run_and_names_the_sheet_the_test_note_and_the_unchecked_note()
    {
        CostDescription.Label(null).ShouldBe("costs: none");
        CostDescription.Label(Record()).ShouldBe("costs: sheet dhan-x (not checked against bills)");
        CostDescription.Label(Record(checkedNote: "bills of 2026-10")).ShouldBe("costs: sheet dhan-x");
        CostDescription.Label(CostRecord.Of(TestCostSheet.Context())).ShouldBe("costs: sheet test-synthetic-1 (TEST sheet: synthetic rates, not the broker's) (not checked against bills)");
        CostDescription.None.ShouldBe(RunLabels.FixedLabels[0]);
    }

    [Fact]
    public void The_label_of_a_stored_sheet_is_the_label_of_a_run_that_used_it()
    {
        var sheet = FixtureSheet.Sheet;
        var checkedSheet = FixtureSheet.Build(FixtureSheet.Id, "check 2026-10-15: ok");

        CostDescription.SheetLabel(sheet).ShouldBe("costs: sheet fixture-a (not checked against bills)");
        CostDescription.SheetLabel(checkedSheet).ShouldBe("costs: sheet fixture-a");
        CostDescription.SheetLabel(TestCostSheet.Sheet).ShouldBe(CostDescription.Label(CostRecord.Of(TestCostSheet.Context())));
        CostDescription.SheetLabel(sheet).ShouldBe(CostDescription.Label(CostRecord.Of(TestCostSheet.Context(sheet: sheet))));
        Should.Throw<ArgumentNullException>(() => CostDescription.SheetLabel(null!));
    }

    [Fact]
    public void The_sizing_line_gives_the_capital_the_profile_the_caps_and_what_is_skipped()
    {
        var line = CostDescription.SizingLabel(Record());

        line.ShouldBe("sizing: fixed simulated capital 1000000, profile standard (risk 0.5 %, cap 25 % of capital per trade); a trade under 1 share or whose charge is above 0.1 of its risk taken is skipped and counted");
        Should.Throw<ArgumentNullException>(() => CostDescription.SizingLabel(null!));
    }

    [Fact]
    public void The_record_of_a_context_carries_the_id_the_hash_the_text_without_checked_the_note_the_capital_the_profile_and_the_limit()
    {
        var sheet = CostSheet.Create("dhan-x", "Dhan equity charges", "read from the broker's page", "bills of 2026-10", TestCostSheet.Sheet.Schedules).Value;
        var context = CostContext.Create(sheet, SizingProfile.Conservative, 500_000m, 0.08m).Value;

        var record = CostRecord.Of(context);

        record.SheetId.ShouldBe("dhan-x");
        record.RatesSha256.ShouldBe(sheet.RatesSha256);
        record.SheetJson.ShouldBe(sheet.ToCanonicalJson().Replace(",\"checked\":\"bills of 2026-10\"", string.Empty, StringComparison.Ordinal));
        record.SheetJson.ShouldNotContain("checked");
        record.Checked.ShouldBe("bills of 2026-10");
        (record.Capital, record.Profile, record.MaxCostR).ShouldBe((500_000m, SizingProfile.Conservative, 0.08m));
        CostSheetJson.Parse(record.SheetJson).Value.RatesSha256.ShouldBe(record.RatesSha256, "the record can be recomputed from its own text");
        Should.Throw<ArgumentNullException>(() => CostRecord.Of(null!));
    }

    [Fact]
    public void The_ten_limits_are_listed_in_order_each_with_a_text_and_the_swing_gate_is_one_of_them()
    {
        CostLimits.NotModelled.Select(l => l.Code).ShouldBe(
        [
            "fixed_capital", "no_margin_or_leverage", "no_liquidity_cap", "one_order_per_leg", "cash_equity_only", "no_trading_tax", "rates_as_sheet", "dp_charge_is_flat", "no_flat_order_fees", "swing_gate_before_costing",
        ]);
        CostLimits.NotModelled.ShouldAllBe(l => !string.IsNullOrWhiteSpace(l.Text));
        CostLimits.NotModelled.Select(l => l.Code).Intersect(FillLimits.NotModelled.Select(l => l.Code)).ShouldBeEmpty("a code is declared once");
    }

    [Fact]
    public void No_text_the_slice_produces_holds_a_word_of_a_statistic()
    {
        var texts = new List<string>
        {
            CostDescription.Label(null),
            CostDescription.Label(Record()),
            CostDescription.Label(Record(checkedNote: "bills")),
            CostDescription.Label(CostRecord.Of(TestCostSheet.Context())),
            CostDescription.SizingLabel(Record()),
            CostErrors.SheetTooLong("text", 20_000, 16_000).Message,
            CostErrors.NoRatesForDate("x", new DateOnly(2025, 1, 1)).Message,
            CostErrors.SizingInputInvalid("capital").Message,
            CostErrors.SheetInvalid([]).Message,
        };
        texts.AddRange(CostLimits.NotModelled.Select(l => l.Text));
        texts.AddRange(ProblemMessages());

        foreach (var text in texts)
        {
            Hits(text).ShouldBeEmpty(text);
        }

        texts.Count.ShouldBeGreaterThan(40);
    }

    /// <summary>The message of every kind of field error the parser can raise, from a corpus of broken sheets.</summary>
    private static IEnumerable<string> ProblemMessages()
    {
        var specs = new List<SheetSpec>();
        void Add(Action<SheetSpec> corrupt)
        {
            var spec = new SheetSpec();
            corrupt(spec);
            specs.Add(spec);
        }

        Add(s => s.Id = "\"UPPER\"");
        Add(s => s.Id = null);
        Add(s => s.Label = "\"" + new string('x', 90) + "\"");
        Add(s => s.Label = "5");
        Add(s => s.Extra = "\"colour\":1");
        Add(s => s.Extra = "\"id\":\"b\"");
        Add(s => s.SchedulesRaw = "5");
        Add(s => s.Schedules.Clear());
        Add(s => s.Schedules.Add(new SheetSpec.ScheduleSpec("\"2026-01-01\"")));
        Add(s => s.Schedules.Add(new SheetSpec.ScheduleSpec("\"2025-01-01\"")));
        Add(s => s.Schedules[0].From = "\"1999-01-01\"");
        Add(s => s.Schedules[0].From = "\"nope\"");
        Add(s => s.Schedules[0].Rounding = "\"banker\"");
        Add(s => s.Schedules[0].Gst = "101");
        Add(s => s.Schedules[0].Gst = "null");
        Add(s => s.Schedules[0].IntradayRaw = "[]");
        Add(s => s.Schedules[0].Intraday["brokerage_cap"] = "0");
        Add(s => s.Schedules[0].Intraday["brokerage_min"] = "30");
        Add(s => s.Schedules[0].Intraday["stt_sell_pct"] = "\"0.1\"");
        Add(s => s.Schedules[0].Intraday["stt_sell_pct"] = "-1");
        Add(s => s.Schedules[0].Intraday["stt_sell_pct"] = "6");
        Add(s => s.Schedules[0].Intraday["stt_sell_pct"] = "0.1234567");
        Add(s => s.Schedules[0].Intraday["stt_sell_pct"] = null);
        foreach (var spec in specs)
        {
            var result = CostSheetJson.Parse(spec.Render());
            result.IsFailure.ShouldBeTrue();
            yield return result.Error.Message;
            foreach (var field in result.Error.Fields ?? [])
            {
                yield return field.Message;
            }
        }

        foreach (var text in new[] { "", "{", "{\"id\":}" })
        {
            foreach (var field in CostSheetJson.Parse(text).Error.Fields!)
            {
                yield return field.Message;
            }
        }
    }
}
