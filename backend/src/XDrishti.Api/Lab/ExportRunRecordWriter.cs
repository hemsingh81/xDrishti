using System.Buffers;
using System.Security.Cryptography;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Csv;

namespace XDrishti.Api.Lab;

/// <summary>The record text and file digests of a run, made with the export library exactly as the <c>simulate</c> verb makes them (<see cref="SimulateStore.Record(SimulationRun, string, string)"/>), so a Lab run and a verb run of one input are one stored run.</summary>
internal sealed class ExportRunRecordWriter : IRunRecordWriter
{
    public RunRecordText Write(SimulationRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return SimulateStore.Record(run, Digest(run.RunKey, run.Trades), Digest(run.RunKey, run.OpenAtEnd));
    }

    private static string Digest(string runKey, IReadOnlyList<RunTrade> trades)
    {
        var sink = new ArrayBufferWriter<byte>();
        TradesCsv.Write(sink, runKey, trades);
        return Convert.ToHexStringLower(SHA256.HashData(sink.WrittenSpan));
    }
}
