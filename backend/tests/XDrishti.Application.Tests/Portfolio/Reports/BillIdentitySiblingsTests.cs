using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Reports;
using XDrishti.Application.Tests.Portfolio.DividendImport;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.Reports;

/// <summary>
/// The identity of a bill-level trade does not depend on which of its siblings are pending (ADR 0030): a sibling that is left out - covered by the broker's own trades, or a BSE row of a security the account does not hold - still counts as sharing the identity, so the NSE trade keeps the
/// plain one and every other gets its exchange added, whatever else is in the file. Two securities that share a name and differ in the ticker in brackets give an NSE and a BSE row of one bill number the same plain identity while the broker's trades cover only one of them.
/// </summary>
public sealed class BillIdentitySiblingsTests
{
    private const string Nse = "INE902B01012";

    private const string Bse = "INE902B01099";

    private static readonly DateOnly Day = new(2026, 4, 7);

    private static readonly ShareName[] Shares =
    [
        DividendHoldings.Share("Orion Steel Limited", "ORIONSTL", Nse),
        DividendHoldings.Share("Orion Steel Limited", "ORIONSTB", Bse),
    ];

    private static SecurityIndex Index(params string[] held) => new([.. held.Select(isin => DividendHoldings.Held("Orion Steel Limited", isin == Nse ? "ORIONSTL" : "ORIONSTB", isin))], Shares, [], []);

    private static GtrRow Row(string name, string exchange, long buy = 10, long sell = 0, string bill = "443400") =>
        new(2, Day, name, exchange, bill, buy, buy * 10m, sell, sell * 10m, 0m);

    private static GtrFile File(params GtrRow[] rows) => new(rows.Length, rows, 0, [], 0, null, null, null, null);

    private static string Plain(GtrRow row, TradeSide side) => BillTrades.Identity(row, side, BillName.Parse(row.Name));

    private static string Salted(GtrRow row, TradeSide side) => BillTrades.Identity(row, side, BillName.Parse(row.Name), BillTrades.CollisionSalt(row));

    private static readonly GtrRow NseRow = Row("Orion Steel (ORIONSTL)", "NSE");

    private static readonly GtrRow BseRow = Row("Orion Steel (ORIONSTB)", "BSE");

    [Fact]
    public void With_both_siblings_pending_the_nse_trade_keeps_the_plain_identity_and_the_bse_one_adds_its_exchange()
    {
        var plan = BillPlanner.Plan(File(NseRow, BseRow), Index(Nse, Bse), []);

        plan.Identified.Select(i => i.ExternalId).ShouldBe([Plain(NseRow, TradeSide.Buy), Salted(BseRow, TradeSide.Buy)]);
    }

    [Fact]
    public void A_bse_trade_has_the_same_identity_when_its_nse_sibling_is_covered_by_the_brokers_trades()
    {
        // The broker's trades cover the NSE security's day, so the NSE row is left out - the BSE trade (another security of the same name) is the only one pending.
        var covered = new[] { new TradeGroup(Day, Nse, TradeSide.Buy, 10) };

        var plan = BillPlanner.Plan(File(NseRow, BseRow), Index(Nse, Bse), covered);

        plan.Covered.ShouldBe(1);
        plan.Identified.Select(i => i.ExternalId).ShouldBe([Salted(BseRow, TradeSide.Buy)]);
    }

    [Fact]
    public void A_bse_trade_has_the_same_identity_when_its_nse_sibling_is_pending_or_covered_and_in_either_order()
    {
        var pending = BillPlanner.Plan(File(NseRow, BseRow), Index(Nse, Bse), []).Identified.Single(i => i.Trade.Exchange == "BSE").ExternalId;
        TradeGroup[] groups = [new TradeGroup(Day, Nse, TradeSide.Buy, 10)];
        var covered = BillPlanner.Plan(File(BseRow, NseRow), Index(Nse, Bse), groups).Identified.Single().ExternalId;

        covered.ShouldBe(pending);
    }

    [Fact]
    public void A_nse_trade_keeps_the_plain_identity_when_its_bse_sibling_is_skipped_because_the_account_does_not_hold_it()
    {
        var alone = BillPlanner.Plan(File(NseRow), Index(Nse), []).Identified.Single().ExternalId;

        var withSkipped = BillPlanner.Plan(File(NseRow, BseRow), Index(Nse), []);

        withSkipped.File.SkippedByReason["bse"].ShouldBe(1);
        withSkipped.Identified.Single().ExternalId.ShouldBe(alone);
        alone.ShouldBe(Plain(NseRow, TradeSide.Buy));
    }

    [Fact]
    public void A_lone_trade_keeps_the_plain_identity_so_the_stored_ones_stay_valid()
    {
        var row = Row("Orion Steel (ORIONSTL)", "NSE", buy: 10, sell: 4, bill: "500");

        var plan = BillPlanner.Plan(File(row), Index(Nse), []);

        plan.Identified.Select(i => i.ExternalId).ShouldBe([Plain(row, TradeSide.Buy), Plain(row, TradeSide.Sell)]);
    }

    [Fact]
    public void Only_the_side_that_is_shared_is_salted()
    {
        var nse = Row("Orion Steel (ORIONSTL)", "NSE", buy: 10, sell: 4, bill: "500");
        var bse = Row("Orion Steel (ORIONSTB)", "BSE", buy: 3, sell: 0, bill: "500");

        TradeGroup[] covered = [new TradeGroup(Day, Nse, TradeSide.Buy, 10)];
        var plan = BillPlanner.Plan(File(nse, bse), Index(Nse, Bse), covered);

        // the NSE buy is covered; the BSE buy shares its identity with it (salted), the NSE sell shares with nobody (plain)
        plan.Identified.Select(i => i.ExternalId).Order().ShouldBe(new[] { Plain(nse, TradeSide.Sell), Salted(bse, TradeSide.Buy) }.Order());
    }

    [Fact]
    public void A_blank_exchange_trade_keeps_the_plain_identity_when_its_bse_sibling_is_skipped()
    {
        var blank = Row("Orion Steel (ORIONSTL)", "");
        var alone = BillPlanner.Plan(File(blank), Index(Nse), []).Identified.Single().ExternalId;

        var plan = BillPlanner.Plan(File(blank, BseRow), Index(Nse), []);

        alone.ShouldBe(Plain(blank, TradeSide.Buy));
        plan.File.SkippedByReason["bse"].ShouldBe(1);
        plan.Identified.Single().ExternalId.ShouldBe(alone);
    }

    [Fact]
    public void A_blank_exchange_trade_keeps_the_plain_identity_when_its_nse_sibling_is_covered_by_the_brokers_trades()
    {
        var blank = Row("Orion Steel (ORIONSTB)", "");

        var plan = BillPlanner.Plan(File(NseRow, blank), Index(Nse, Bse), [new TradeGroup(Day, Nse, TradeSide.Buy, 10)]);

        plan.Covered.ShouldBe(1);
        plan.Identified.Single().ExternalId.ShouldBe(Plain(blank, TradeSide.Buy)); // the rule of before: only a pending sibling salts a blank exchange
    }

    [Fact]
    public void A_blank_exchange_trade_still_adds_its_token_when_its_sibling_is_pending()
    {
        var blank = Row("Orion Steel (ORIONSTB)", "");

        var plan = BillPlanner.Plan(File(NseRow, blank), Index(Nse, Bse), []);

        plan.Identified.Select(i => i.ExternalId).ShouldBe([Plain(NseRow, TradeSide.Buy), Salted(blank, TradeSide.Buy)]);
    }

    [Fact]
    public void Two_bse_legs_that_share_an_identity_of_which_one_is_skipped_keep_the_plain_identity_of_the_pending_one()
    {
        // The account holds only the second security: the first BSE row is skipped, and there is no NSE row at all.
        var skipped = Row("Orion Steel (ORIONSTL)", "BSE");
        var alone = BillPlanner.Plan(File(BseRow), Index(Bse), []).Identified.Single().ExternalId;

        var plan = BillPlanner.Plan(File(skipped, BseRow), Index(Bse), []);

        plan.File.SkippedByReason["bse"].ShouldBe(1);
        plan.Identified.Single().ExternalId.ShouldBe(alone);
        alone.ShouldBe(Plain(BseRow, TradeSide.Buy));
    }
}
