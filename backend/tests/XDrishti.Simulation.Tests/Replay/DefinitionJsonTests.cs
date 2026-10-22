using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Tests.Replay;

/// <summary>Criterion 10 of P3-04: the canonical JSON of a definition and its SHA-256.</summary>
public sealed class DefinitionJsonTests
{
    /// <summary>The SHA-256 of the golden text, computed with an independent tool (Python 3 <c>hashlib</c>) on 2026-10-18.</summary>
    private const string OrbGoldenSha256 = "0ba11bc69d2f1cfa24d8801f78f8fcc111415b39fa1454a1a09e59eeebf8574c";

    private static string Golden() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "orb-v1.definition.golden.txt")).TrimEnd('\r', '\n');

    private static StrategyRecord RecordOf(StrategyDefinition definition) => Rep.Prepare(definition).Record;

    private static StrategyDefinition WithTriggers(string buy, string sell) =>
        OrbV1.Create() with
        {
            Buy = OrbV1.Create().Buy! with { Trigger = TestStrategies.All(buy) },
            Sell = OrbV1.Create().Sell! with { Trigger = TestStrategies.All(sell) },
        };

    [Fact]
    public void The_json_of_the_orb_draft_equals_the_golden_text_checked_in_with_the_tests()
    {
        RecordOf(OrbV1.Create()).DefinitionJson.ShouldBe(Golden());
    }

    [Fact]
    public void Two_builds_give_equal_text_and_the_hash_is_the_sha256_of_its_utf8_bytes_and_a_known_answer()
    {
        var a = RecordOf(OrbV1.Create());
        var b = RecordOf(OrbV1.Create());

        a.DefinitionJson.ShouldBe(b.DefinitionJson);
        a.DefinitionSha256.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(a.DefinitionJson))));
        a.DefinitionSha256.ShouldBe(OrbGoldenSha256);
        Encoding.UTF8.GetBytes(a.DefinitionJson)[0].ShouldBe((byte)'{', "no byte order mark");
    }

    [Fact]
    public void Another_opening_range_gives_another_text_and_another_hash()
    {
        var thirty = RecordOf(OrbV1.Create(30));

        thirty.DefinitionJson.ShouldContain("\"value\":30");
        thirty.DefinitionJson.ShouldNotBe(RecordOf(OrbV1.Create()).DefinitionJson);
        thirty.DefinitionSha256.ShouldNotBe(OrbGoldenSha256);
    }

    [Fact]
    public void White_space_and_redundant_brackets_in_a_condition_do_not_change_the_text_or_the_hash()
    {
        var plain = RecordOf(OrbV1.Create());
        var spaced = RecordOf(WithTriggers("  close   >  ( or_high(or_min) )  and  ( close[1] <= or_high(or_min) )  ", OrbV1.SellTrigger));

        spaced.DefinitionJson.ShouldBe(plain.DefinitionJson);
        spaced.DefinitionSha256.ShouldBe(plain.DefinitionSha256);
    }

    [Fact]
    public void Two_spellings_of_one_feature_give_two_hashes_because_the_text_is_the_version()
    {
        var implicitLength = RecordOf(TestStrategies.WithBuy(OrbV1.Create(), b => b with { Setup = TestStrategies.All("rsi() > 50") }));
        var explicitLength = RecordOf(TestStrategies.WithBuy(OrbV1.Create(), b => b with { Setup = TestStrategies.All("rsi(14) > 50") }));

        implicitLength.DefinitionJson.ShouldContain("rsi() > 50");
        explicitLength.DefinitionJson.ShouldContain("rsi(14) > 50");
        implicitLength.DefinitionSha256.ShouldNotBe(explicitLength.DefinitionSha256);
    }

    [Fact]
    public void A_condition_with_greater_than_and_less_than_is_written_as_it_is_not_escaped()
    {
        var record = RecordOf(WithTriggers("close > 100 and close[1] <= 100", "close < 90 and close[1] >= 90"));

        record.DefinitionJson.ShouldContain("close > 100 and close[1] <= 100");
        record.DefinitionJson.ShouldContain("close < 90 and close[1] >= 90");
        record.DefinitionJson.ShouldNotContain("\\u003");
    }

    [Fact]
    public void Only_the_quote_the_backslash_and_control_characters_are_escaped()
    {
        var definition = OrbV1.Create() with { Id = "a\"b\\c\nd\u0001e<f>g&h'ié" };

        var json = DefinitionJson.Write(definition);

        json.ShouldContain("\"id\":\"a\\\"b\\\\c\\nd\\u0001e<f>g&h'ié\"");
    }

    [Fact]
    public void An_absent_member_is_left_out_and_an_empty_list_is_written()
    {
        var json = DefinitionJson.Write(TestStrategies.OrbBuyOnly());

        json.ShouldNotContain("\"sell\"");
        json.ShouldNotContain("\"setup\"");
        json.ShouldNotContain("\"invalidate\"");
        json.ShouldNotContain("\"overlays\"");
        json.ShouldNotContain("\"max_hold_sessions\"");
        json.ShouldContain("\"sides\":[\"BUY\"]");
        json.ShouldContain("\"filters\":[]");
    }

    [Fact]
    public void A_quantity_is_a_number_or_the_name_of_a_parameter_and_a_negative_zero_is_written_zero()
    {
        var definition = OrbV1.Create() with
        {
            Buy = OrbV1.Create().Buy! with { Stop = new StopSpec(StopKind.OrOpposite, Minutes: Quantity.Of(15)) },
            Params = [new StrategyParam("or_min", -0.0), new StrategyParam("x", 1e-7)],
        };

        var json = DefinitionJson.Write(definition);

        json.ShouldContain("\"stop\":{\"kind\":\"or_opposite\",\"minutes\":15}");
        json.ShouldContain("\"params\":[{\"name\":\"or_min\",\"value\":0},{\"name\":\"x\",\"value\":1E-07}]");
    }

    [Fact]
    public void The_exit_policies_and_kinds_are_spelt_as_doc_11_spells_them()
    {
        var definition = OrbV1.Create() with
        {
            Buy = OrbV1.Create().Buy! with
            {
                Setup = TestStrategies.All("close > 1"),
                Invalidate = new ConditionList(ListMode.Any, ["close < 1"]),
                Entry = new EntrySpec(EntryKind.StopAboveHigh),
                Stop = new StopSpec(StopKind.SwingLow, Timeframe.M15, Lookback: Quantity.Of(10)),
                Exits = [new ExitSpec(ExitPolicy.PartialTrail, [new ExitParameter("book_pct", [Quantity.Of(50)])]), new ExitSpec(ExitPolicy.AtrTrail, [])],
                Overlays = [new OverlaySpec(OverlayKind.TimeStop, [new ExitParameter("minutes", [Quantity.Of(60), Quantity.Named("m")])])],
            },
            Style = Style.Swing,
            TimeRules = new TimeRules(null, null, null, 5),
        };

        var json = DefinitionJson.Write(definition);

        json.ShouldContain("\"style\":\"swing\"");
        json.ShouldContain("\"setup\":{\"mode\":\"all\",\"items\":[\"close > 1\"]}");
        json.ShouldContain("\"invalidate\":{\"mode\":\"any\",\"items\":[\"close < 1\"]}");
        json.ShouldContain("\"entry\":{\"kind\":\"stop_above_high\"}");
        json.ShouldContain("\"stop\":{\"kind\":\"swing_low\",\"timeframe\":\"15m\",\"lookback\":10}");
        json.ShouldContain("{\"policy\":\"PARTIAL_TRAIL\",\"parameters\":[{\"name\":\"book_pct\",\"values\":[50]}]}");
        json.ShouldContain("{\"policy\":\"ATR_TRAIL\",\"parameters\":[]}");
        json.ShouldContain("\"overlays\":[{\"kind\":\"time_stop\",\"parameters\":[{\"name\":\"minutes\",\"values\":[60,\"m\"]}]}]");
        json.ShouldContain("\"time_rules\":{\"max_hold_sessions\":5}");
    }

    [Fact]
    public void The_text_and_the_hash_are_identical_under_a_culture_with_other_separators()
    {
        var before = CultureInfo.CurrentCulture;
        var comma = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        comma.NumberFormat.NumberDecimalSeparator = ",";
        comma.NumberFormat.NumberGroupSeparator = ".";
        comma.NumberFormat.NegativeSign = "~";
        try
        {
            CultureInfo.CurrentCulture = comma;

            var record = RecordOf(OrbV1.Create(30, 2.5));

            record.DefinitionJson.ShouldContain("\"value\":30");
            record.DefinitionJson.ShouldContain("{\"name\":\"rr\",\"value\":2.5}");
            RecordOf(OrbV1.Create()).DefinitionJson.ShouldBe(Golden());
            RecordOf(OrbV1.Create()).DefinitionSha256.ShouldBe(OrbGoldenSha256);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void A_condition_that_does_not_parse_is_written_as_it_stands_and_never_throws()
    {
        var definition = OrbV1.Create() with { Sell = OrbV1.Create().Sell! with { Setup = TestStrategies.All("close >> 1") }, Sides = [Side.Buy] };

        var json = DefinitionJson.Write(definition);

        json.ShouldContain("\"close >> 1\"");
    }
}
