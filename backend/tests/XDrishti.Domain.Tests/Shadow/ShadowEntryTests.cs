using XDrishti.Domain.Shadow;

namespace XDrishti.Domain.Tests.Shadow;

/// <summary>CF-08, D3 and D4 with review fix 2: the row hash is the SHA-256 of the canonical JSON and includes the plan id.</summary>
public sealed class ShadowEntryTests
{
    private static readonly DateOnly Session = new(2026, 11, 3);

    private static ShadowEntry Entry() =>
        new(Session, 7, 70, "ORB|NSE:PAPA|Buy|2026-11-03", "primary", "ORB|15m|BUY|intraday|FIXED_RR{rr=2}|*", new string('a', 64), "NSE:PAPA", "BUY", 100m, 98m, 104m, "09:30-11:30", new string('b', 64), null, 0.712, null, null, true, 3);

    [Fact]
    public void The_row_hash_is_64_hex_characters_and_the_same_for_the_same_row()
    {
        Entry().RowHash.ShouldMatch("^[0-9a-f]{64}$");
        Entry().RowHash.ShouldBe(Entry().RowHash);
    }

    [Fact]
    public void The_canonical_json_has_a_fixed_field_order_and_four_decimal_levels()
    {
        Entry().CanonicalJson.ShouldBe(
            "{\"session_date\":\"2026-11-03\",\"plan_id\":7,\"plan_item_id\":70,\"trade_key\":\"ORB|NSE:PAPA|Buy|2026-11-03\",\"ticket_group\":\"primary\",\"cell_key\":\"ORB|15m|BUY|intraday|FIXED_RR{rr=2}|*\","
            + "\"strategy_hash\":\"" + new string('a', 64) + "\",\"symbol\":\"NSE:PAPA\",\"side\":\"BUY\",\"entry_limit\":\"100.0000\",\"stop\":\"98.0000\",\"target\":\"104.0000\",\"time_rule\":\"09:30-11:30\","
            + "\"config_hash\":\"" + new string('b', 64) + "\",\"model_id\":null,\"p_win\":\"0.712\",\"feature_version\":null,\"features\":null,\"counts_for_edge\":true,\"edge_epoch\":3}");
    }

    [Fact]
    public void The_plan_id_is_part_of_the_hash()
    {
        (Entry() with { PlanId = 8 }).RowHash.ShouldNotBe(Entry().RowHash);
    }

    [Fact]
    public void Every_other_field_is_part_of_the_hash()
    {
        var baseline = Entry().RowHash;
        ShadowEntry[] changed =
        [
            Entry() with { SessionDate = Session.AddDays(1) },
            Entry() with { PlanItemId = 71 },
            Entry() with { TradeKey = "x" },
            Entry() with { TicketGroup = "reserve" },
            Entry() with { CellKey = "x" },
            Entry() with { StrategyHash = new string('c', 64) },
            Entry() with { Symbol = "NSE:OTHER" },
            Entry() with { Side = "SELL" },
            Entry() with { EntryLimit = 100.0001m },
            Entry() with { Stop = null },
            Entry() with { Target = 105m },
            Entry() with { TimeRule = "x" },
            Entry() with { ConfigHash = null },
            Entry() with { ModelId = "m1" },
            Entry() with { PWin = 0.713 },
            Entry() with { FeatureVersion = "v1", Features = new Dictionary<string, double> { ["rsi"] = 51 } },
            Entry() with { CountsForEdge = false },
            Entry() with { EdgeEpoch = null },
        ];

        foreach (var entry in changed)
        {
            entry.RowHash.ShouldNotBe(baseline);
        }
    }

    [Fact]
    public void A_level_hashes_the_same_whatever_its_scale()
    {
        (Entry() with { EntryLimit = 100m }).RowHash.ShouldBe((Entry() with { EntryLimit = 100.0000m }).RowHash);
        (Entry() with { Stop = 98.5m }).RowHash.ShouldBe((Entry() with { Stop = 98.50m }).RowHash);
    }

    [Fact]
    public void Features_hash_the_same_in_any_order_of_their_keys()
    {
        var a = Entry() with { FeatureVersion = "v1", Features = new Dictionary<string, double> { ["rsi"] = 51.5, ["atr"] = 2 } };
        var b = Entry() with { FeatureVersion = "v1", Features = new Dictionary<string, double> { ["atr"] = 2, ["rsi"] = 51.5 } };

        a.RowHash.ShouldBe(b.RowHash);
        a.CanonicalJson.ShouldContain("\"features\":{\"atr\":\"2\",\"rsi\":\"51.5\"}");
    }
}
