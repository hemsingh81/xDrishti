using System.Text.Json;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Cards;
using XDrishti.SimExport.Csv;
using XDrishti.SimExport.Json;
using XDrishti.SimExport.Tests.Support;
using XDrishti.SimExport.Verification;

namespace XDrishti.SimExport.Tests;

/// <summary>Criterion 8 of P4-03 for the export: the new exit reason <see cref="ExitReason.EmaExit"/> is spelt <c>ema_exit</c> everywhere a reason is written (CSV, JSON, the counter, the card, the name of a card, the check log) and has a place in the strata and the edge table.</summary>
public sealed class EmaExitExportTests
{
    private static SimTrade EmaTrade() => CardFixtures.BuyAtTarget().Trade with { ExitReason = ExitReason.EmaExit, TargetPrice = null };

    [Fact]
    public void The_spelling_is_ema_exit_and_a_card_name_carries_it()
    {
        ExportSpellings.Of(ExitReason.EmaExit).ShouldBe("ema_exit");
        CardNames.Of(1, 10, EmaTrade()).ShouldEndWith("-ema_exit.html");
        XDrishti.SimExport.Manifest.ExportNames.IsValid(CardNames.Of(1, 10, EmaTrade())).ShouldBeTrue();
    }

    [Fact]
    public void The_csv_row_and_the_json_counter_say_ema_exit()
    {
        var (run, _) = CardFixtures.Card(EmaTrade(), CardFixtures.BuyAtTarget().Bars);

        var csv = Rfc4180Reader.Read(Sinks.Render(s => TradesCsv.Write(s, run.RunKey, run.Trades)));
        csv[1][csv[0].IndexOf("exit_reason")].ShouldBe("ema_exit");
        csv[1][csv[0].IndexOf("target_price")].ShouldBe(string.Empty);

        var json = JsonDocument.Parse(Sinks.Render(s => SimExportJson.Write(s, run, RunLabels.Of(run, FillRules.Default), "xd-sim-export-1"))).RootElement.GetRawText();
        json.ShouldContain("\"exits_ema_exit\":");
    }

    [Fact]
    public void The_card_names_the_reason_and_the_strata_and_the_edge_table_know_it()
    {
        var (run, data) = CardFixtures.Card(EmaTrade(), CardFixtures.BuyAtTarget().Bars);

        CardFixtures.Render(run, data).ShouldContain("ema_exit");
        EdgeCategories.ExitReasonHandling.Keys.ShouldContain(ExitReason.EmaExit);
        CardSelection.StratumNames.ShouldContain("ema_exit-buy");
        CardSelection.StratumNames.ShouldContain("ema_exit-sell");
    }
}
