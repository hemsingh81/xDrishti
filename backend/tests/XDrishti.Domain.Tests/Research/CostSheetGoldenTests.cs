using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Domain.Tests.Research;

/// <summary>
/// P14-07 criterion 1: a sheet without option or futures lines keeps its canonical text and its rates hash, byte for byte. The expected values below were taken from the code before the optional segments were added; a change to them is a changed
/// hash of every stored sheet and run key, and is a defect.
/// </summary>
public sealed class CostSheetGoldenTests
{
    private const string TestJson = "{\"id\":\"test-synthetic-1\",\"label\":\"TEST sheet (synthetic numbers)\",\"source\":\"invented for tests; not a broker's sheet\",\"schedules\":[{\"effective_from\":\"2026-01-01\",\"gst_pct\":18,\"rounding\":\"half_away_from_zero\",\"intraday_equity\":{\"brokerage_pct\":0.03,\"brokerage_cap\":20,\"brokerage_min\":0,\"stt_buy_pct\":0,\"stt_sell_pct\":0.04,\"exchange_txn_pct\":0.005,\"sebi_per_crore\":50,\"stamp_buy_pct\":0.02,\"other_pct\":0,\"dp_charge_per_sell\":0},\"delivery_equity\":{\"brokerage_pct\":0,\"brokerage_cap\":0,\"brokerage_min\":0,\"stt_buy_pct\":0.08,\"stt_sell_pct\":0.08,\"exchange_txn_pct\":0.004,\"sebi_per_crore\":50,\"stamp_buy_pct\":0.03,\"other_pct\":0,\"dp_charge_per_sell\":10}},{\"effective_from\":\"2026-03-03\",\"gst_pct\":18,\"rounding\":\"half_away_from_zero\",\"intraday_equity\":{\"brokerage_pct\":0.03,\"brokerage_cap\":20,\"brokerage_min\":0,\"stt_buy_pct\":0,\"stt_sell_pct\":0.035,\"exchange_txn_pct\":0.005,\"sebi_per_crore\":50,\"stamp_buy_pct\":0.02,\"other_pct\":0,\"dp_charge_per_sell\":0},\"delivery_equity\":{\"brokerage_pct\":0,\"brokerage_cap\":0,\"brokerage_min\":0,\"stt_buy_pct\":0.08,\"stt_sell_pct\":0.09,\"exchange_txn_pct\":0.004,\"sebi_per_crore\":50,\"stamp_buy_pct\":0.03,\"other_pct\":0,\"dp_charge_per_sell\":10}}]}";

    private const string TestHash = "14169419a10787f993a969ab63dd3c04db9f9e641fc19b1abd54f2c31309de19";

    private const string FixtureJson = "{\"id\":\"fixture-a\",\"label\":\"FIXTURE sheet (invented numbers)\",\"source\":\"invented for tests; not a broker's sheet\",\"schedules\":[{\"effective_from\":\"2026-01-01\",\"gst_pct\":18,\"rounding\":\"half_away_from_zero\",\"intraday_equity\":{\"brokerage_pct\":0.03,\"brokerage_cap\":20,\"brokerage_min\":0,\"stt_buy_pct\":0,\"stt_sell_pct\":0.04,\"exchange_txn_pct\":0.005,\"sebi_per_crore\":50,\"stamp_buy_pct\":0.02,\"other_pct\":0,\"dp_charge_per_sell\":0},\"delivery_equity\":{\"brokerage_pct\":0,\"brokerage_cap\":0,\"brokerage_min\":0,\"stt_buy_pct\":0.08,\"stt_sell_pct\":0.08,\"exchange_txn_pct\":0.004,\"sebi_per_crore\":50,\"stamp_buy_pct\":0.03,\"other_pct\":0,\"dp_charge_per_sell\":10}},{\"effective_from\":\"2026-03-03\",\"gst_pct\":18,\"rounding\":\"half_away_from_zero\",\"intraday_equity\":{\"brokerage_pct\":0.03,\"brokerage_cap\":20,\"brokerage_min\":0,\"stt_buy_pct\":0,\"stt_sell_pct\":0.035,\"exchange_txn_pct\":0.005,\"sebi_per_crore\":50,\"stamp_buy_pct\":0.02,\"other_pct\":0,\"dp_charge_per_sell\":0},\"delivery_equity\":{\"brokerage_pct\":0,\"brokerage_cap\":0,\"brokerage_min\":0,\"stt_buy_pct\":0.08,\"stt_sell_pct\":0.09,\"exchange_txn_pct\":0.004,\"sebi_per_crore\":50,\"stamp_buy_pct\":0.03,\"other_pct\":0,\"dp_charge_per_sell\":10}}]}";

    private const string FixtureHash = "d633fe489b486808941bed118add484e22f620695fa868a7acc1f7b5e8a5bb9d";

    [Fact]
    public void The_test_sheet_without_option_lines_has_the_canonical_text_and_hash_it_had_before_P14_07()
    {
        TestCostSheet.Sheet.ToCanonicalJson().ShouldBe(TestJson);
        TestCostSheet.Sheet.RatesSha256.ShouldBe(TestHash);
    }

    [Fact]
    public void The_fixture_sheet_without_option_lines_has_the_canonical_text_and_hash_it_had_before_P14_07()
    {
        FixtureSheet.Sheet.ToCanonicalJson().ShouldBe(FixtureJson);
        FixtureSheet.Sheet.RatesSha256.ShouldBe(FixtureHash);
    }

    [Fact]
    public void The_stored_text_parses_back_to_the_same_sheet_and_the_same_hash()
    {
        var parsed = CostSheetJson.Parse(FixtureJson);

        parsed.IsSuccess.ShouldBeTrue();
        parsed.Value.ToCanonicalJson().ShouldBe(FixtureJson);
        parsed.Value.RatesSha256.ShouldBe(FixtureHash);
        parsed.Value.ShouldBe(FixtureSheet.Sheet);
    }
}
