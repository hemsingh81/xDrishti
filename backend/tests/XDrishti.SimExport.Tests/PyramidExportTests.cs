using System.Text.Json;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Csv;
using XDrishti.SimExport.Json;
using XDrishti.SimExport.Tests.Support;

namespace XDrishti.SimExport.Tests;

/// <summary>Criterion 8 of P4-04 (D7, D16 of P4-01): a pyramid with an add is written with its legs in the CSV and in run.json, spelt <c>entry</c>, <c>add</c> and <c>exit</c> and in order, and its card shows the three legs with their charges.</summary>
public sealed class PyramidExportTests
{
    private static SimTrade Pyramid(SimTrade trade) => trade with
    {
        Legs = new ValueList<Leg>(
        [
            new Leg(trade.EntryEpochSeconds, trade.EntryPrice, trade.EntryFilledPrice, 0.5m, LegKind.Entry, string.Empty),
            new Leg(Day.Epoch(CardFixtures.Date, 620), 102.00, 102.05, 0.3m, LegKind.Add, string.Empty),
            new Leg(trade.ExitEpochSeconds, trade.ExitPrice, trade.ExitFilledPrice, 0.8m, LegKind.Exit, "SquareOff"),
        ]),
    };

    private static (SimulationRun Run, TradeCardData Data) Card(bool costed)
    {
        var (trade, bars) = CardFixtures.BuyAtTarget();
        var pyramid = Pyramid(trade);
        if (!costed)
        {
            return CardFixtures.Card(pyramid, bars);
        }

        var context = TestCostSheet.Context();
        var applied = TradeCostApplier.Apply(pyramid, context, CostSegment.IntradayEquity).Value.Trade.ShouldNotBeNull();
        return CardFixtures.Card(applied, bars, costs: CostRecord.Of(context));
    }

    [Fact]
    public void The_csv_and_the_json_spell_the_add_leg_between_the_entry_and_the_exit()
    {
        var (run, _) = Card(costed: true);

        var rows = Rfc4180Reader.Read(Sinks.Render(s => TradesCsv.Write(s, run.RunKey, run.Trades)));
        var csvLegs = rows[1][rows[0].IndexOf("legs")].Split(';');
        var document = JsonDocument.Parse(Sinks.Render(s => SimExportJson.Write(s, run, RunLabels.Of(run, FillRules.Default), "xd-sim-export-3")));
        var trade = document.RootElement.GetProperty("run").GetProperty("trades")[0].GetProperty("trade");

        csvLegs.Select(l => l.Split('|')[1]).ShouldBe(["entry", "add", "exit"]);
        csvLegs[1].ShouldBe($"{Day.Epoch(CardFixtures.Date, 620)}|add|102|102.05|0.3|");
        trade.GetProperty("legs").EnumerateArray().Select(l => l.GetProperty("kind").GetString()).ShouldBe(["entry", "add", "exit"]);
        trade.GetProperty("costing").GetProperty("leg_charges").GetArrayLength().ShouldBe(3, "one order for the entry, one for the add and one for the exit");
    }

    [Fact]
    public void The_card_shows_the_entry_the_add_and_the_exit_with_the_quantity_each_was_charged_on()
    {
        var (run, data) = Card(costed: true);

        var html = CardFixtures.Render(run, data);

        html.ShouldContain("<h2>legs</h2>");
        html.ShouldContain("<td>entry</td>");
        html.ShouldContain("<td>add</td>");
        html.ShouldContain("<td>exit (SquareOff)</td>");
        html.ShouldContain("<td>0.3</td>");
    }
}
