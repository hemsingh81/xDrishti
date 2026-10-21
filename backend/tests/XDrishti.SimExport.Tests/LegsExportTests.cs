using System.Text.Json;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Cards;
using XDrishti.SimExport.Csv;
using XDrishti.SimExport.Json;
using XDrishti.SimExport.Tests.Support;

namespace XDrishti.SimExport.Tests;

/// <summary>Criteria 11 and 12 of P4-01 (D15, D16): a trade with legs in the CSV, in run.json and on its card; a trade without legs writes what it wrote before (the goldens), and no file name says how a trade ended.</summary>
public sealed class LegsExportTests
{
    private static SimTrade WithLegs(SimTrade trade) => trade with
    {
        Legs = new ValueList<Leg>(
        [
            new Leg(trade.EntryEpochSeconds, trade.EntryPrice, trade.EntryFilledPrice, 1m, LegKind.Entry, string.Empty),
            new Leg(Day.Epoch(CardFixtures.Date, 620), 103.00, 102.95, 0.5m, LegKind.Exit, "LADDER"),
            new Leg(trade.ExitEpochSeconds, trade.ExitPrice, trade.ExitFilledPrice, 0.5m, LegKind.Exit, "Target"),
        ]),
    };

    private static (SimulationRun Run, TradeCardData Data) Card(bool costed)
    {
        var (trade, bars) = CardFixtures.BuyAtTarget();
        var legs = WithLegs(trade);
        if (!costed)
        {
            return CardFixtures.Card(legs, bars);
        }

        var context = TestCostSheet.Context();
        var applied = TradeCostApplier.Apply(legs, context, CostSegment.IntradayEquity).Value.Trade.ShouldNotBeNull();
        return CardFixtures.Card(applied, bars, costs: CostRecord.Of(context));
    }

    [Fact]
    public void The_csv_row_of_a_trade_with_legs_lists_them_in_the_legs_cell_and_a_trade_without_legs_leaves_it_empty()
    {
        var (run, _) = Card(costed: false);
        var plain = CardFixtures.BuyAtTarget().Trade;
        var plainRun = CardFixtures.Card(plain, CardFixtures.BuyAtTarget().Bars).Run;

        var rows = Rfc4180Reader.Read(Sinks.Render(s => TradesCsv.Write(s, run.RunKey, run.Trades)));
        var plainRows = Rfc4180Reader.Read(Sinks.Render(s => TradesCsv.Write(s, plainRun.RunKey, plainRun.Trades)));

        var legs = rows[1][rows[0].IndexOf("legs")];
        legs.Split(';').Length.ShouldBe(3);
        legs.Split(';')[0].ShouldBe($"{run.Trades[0].Trade.EntryEpochSeconds}|entry|100.5|100.55|1|");
        legs.Split(';')[1].ShouldBe($"{Day.Epoch(CardFixtures.Date, 620)}|exit|103|102.95|0.5|LADDER");
        legs.Split(';')[2].ShouldBe($"{run.Trades[0].Trade.ExitEpochSeconds}|exit|105.5|105.45|0.5|Target");
        plainRows[1][plainRows[0].IndexOf("legs")].ShouldBeEmpty();
        rows[1][rows[0].IndexOf("exit_price")].ShouldBe("105.5", "the top-level exit is that of the last leg");
    }

    [Fact]
    public void The_json_of_a_trade_with_legs_holds_them_spelt_and_in_order_and_the_leg_charges_of_a_costed_one()
    {
        var (run, _) = Card(costed: true);

        var document = JsonDocument.Parse(Sinks.Render(s => SimExportJson.Write(s, run, RunLabels.Of(run, FillRules.Default), "xd-sim-export-3")));
        var trade = document.RootElement.GetProperty("run").GetProperty("trades")[0].GetProperty("trade");

        var legs = trade.GetProperty("legs");
        legs.GetArrayLength().ShouldBe(3);
        legs.EnumerateArray().Select(l => l.GetProperty("kind").GetString()).ShouldBe(["entry", "exit", "exit"]);
        legs.EnumerateArray().Select(l => l.GetProperty("reason").GetString()).ShouldBe(["", "LADDER", "Target"]);
        legs[1].GetProperty("share").GetRawText().ShouldBe("0.5");
        var charges = trade.GetProperty("costing").GetProperty("leg_charges");
        charges.GetArrayLength().ShouldBe(3);
        charges.EnumerateArray().Select(c => c.GetProperty("quantity").GetInt64()).Sum().ShouldBe(trade.GetProperty("costing").GetProperty("quantity").GetInt64() * 2, "the entry order plus the two exit orders each way");
    }

    [Fact]
    public void The_json_of_a_trade_without_legs_says_null()
    {
        var (trade, bars) = CardFixtures.BuyAtTarget();
        var run = CardFixtures.Card(trade, bars).Run;

        var document = JsonDocument.Parse(Sinks.Render(s => SimExportJson.Write(s, run, RunLabels.Of(run, FillRules.Default), "xd-sim-export-3")));

        document.RootElement.GetProperty("run").GetProperty("trades")[0].GetProperty("trade").GetProperty("legs").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public void The_card_of_a_trade_with_legs_shows_every_leg_with_its_quantity_and_charge_and_a_card_without_legs_has_no_legs_section()
    {
        var (run, data) = Card(costed: true);
        var (plainRun, plainData) = CardFixtures.Card(CardFixtures.BuyAtTarget().Trade, CardFixtures.BuyAtTarget().Bars);

        var html = CardFixtures.Render(run, data);

        html.ShouldContain("<h2>legs</h2>");
        html.ShouldContain("entry");
        html.ShouldContain("exit (LADDER)");
        html.ShouldContain("exit (Target)");
        html.ShouldContain("<td>0.5</td>");
        html.IndexOf("<h2>legs</h2>", StringComparison.Ordinal).ShouldBeGreaterThan(html.IndexOf("<h2>costs</h2>", StringComparison.Ordinal));
        CardFixtures.Render(plainRun, plainData).ShouldNotContain("<h2>legs</h2>");
    }

    [Fact]
    public void The_file_name_of_a_card_of_a_trade_with_legs_is_made_of_the_fields_it_always_used()
    {
        var (run, _) = Card(costed: false);
        var plain = CardFixtures.BuyAtTarget().Trade;

        CardNames.Of(1, 1, run.Trades[0].Trade).ShouldBe(CardNames.Of(1, 1, plain));
    }
}
