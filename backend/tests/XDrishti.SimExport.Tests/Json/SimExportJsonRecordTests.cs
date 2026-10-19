using System.Text.Json;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Json;
using XDrishti.SimExport.Tests.Support;

namespace XDrishti.SimExport.Tests.Json;

/// <summary>Criterion 14b of P3-10: <c>SimExportJson.WriteRecord</c>, the canonical record text the result store keeps.</summary>
public sealed class SimExportJsonRecordTests
{
    private static string Record(SimulationRun run) => Sinks.Render(s => SimExportJson.WriteRecord(s, run));

    private static JsonElement RunObject(SimulationRun run) =>
        JsonDocument.Parse(Sinks.Render(s => SimExportJson.Write(s, run, RunLabels.Of(run, FillRules.Default), "xd-sim-export-2"))).RootElement.GetProperty("run");

    [Fact]
    public void Two_calls_give_equal_bytes()
    {
        var run = FabricatedRun.Standard();

        Sinks.Bytes(s => SimExportJson.WriteRecord(s, run)).ShouldBe(Sinks.Bytes(s => SimExportJson.WriteRecord(s, run)));
    }

    [Fact]
    public void The_record_has_no_trades_and_no_open_at_end_member()
    {
        var names = JsonDocument.Parse(Record(FabricatedRun.Standard())).RootElement.EnumerateObject().Select(p => p.Name).ToList();

        names.ShouldNotContain("trades");
        names.ShouldNotContain("open_at_end");
        names.ShouldContain("run_key");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_record_is_the_run_object_of_run_json_less_the_two_trade_members(bool costed)
    {
        var run = costed ? FabricatedRun.CostedStandard() : FabricatedRun.Standard();

        var fromFile = RunObject(run).EnumerateObject().Where(p => p.Name is not ("trades" or "open_at_end")).Select(p => (p.Name, p.Value.GetRawText())).ToList();
        var record = JsonDocument.Parse(Record(run)).RootElement.EnumerateObject().Select(p => (p.Name, p.Value.GetRawText())).ToList();

        record.ShouldBe(fromFile);
    }

    [Fact]
    public void The_text_of_the_record_is_the_bytes_of_the_file_less_the_two_members()
    {
        var run = FabricatedRun.Standard();
        var raw = RunObject(run).GetRawText();
        var record = Record(run);

        record.Length.ShouldBeLessThan(raw.Length);
        record.ShouldStartWith("{\"schema_version\":");
        record.ShouldNotContain("\n");
    }

    [Fact]
    public void A_missing_argument_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => SimExportJson.WriteRecord(null!, FabricatedRun.Standard()));
        Should.Throw<ArgumentNullException>(() => SimExportJson.WriteRecord(new System.Buffers.ArrayBufferWriter<byte>(), null!));
    }
}
