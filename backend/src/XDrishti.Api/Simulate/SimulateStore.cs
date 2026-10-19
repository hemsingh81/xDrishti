using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Research;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Json;
using XDrishti.SimExport.Manifest;

namespace XDrishti.Api.Simulate;

/// <summary>
/// The <c>--store</c> step of the verb (spec P3-10, D12): after the export files are written, hands the run, the canonical record text and the two digests to the store-run handler and prints one line. The record text and the digests
/// come from the in-memory run (the text from <see cref="SimExportJson.WriteRecord"/>, the file digests from the hashes the writer took of the very bytes it rendered), never from files read back from the folder. Exit codes: 0 stored or already
/// stored, 1 refused by validation, 3 a conflict or a database failure; the files stay in every case.
/// </summary>
internal static class SimulateStore
{
    /// <summary>Who is recorded as the author of a stored run.</summary>
    public const string Actor = "cli:simulate";

    /// <summary>The record text without trades and the two digests, for the run and the entries the writer produced.</summary>
    public static RunRecordText Record(SimulationRun run, IReadOnlyList<ManifestEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(entries);

        var sink = new ArrayBufferWriter<byte>();
        SimExportJson.WriteRecord(sink, run);
        var recordSha = Convert.ToHexStringLower(SHA256.HashData(sink.WrittenSpan));
        var text = new UTF8Encoding(false, true).GetString(sink.WrittenSpan);
        var trades = entries.Single(e => e.Name == "trades.csv").Sha256;
        var open = entries.Single(e => e.Name == "open_at_end.csv").Sha256;
        return new RunRecordText(text, recordSha, TradesDigest(trades, open));
    }

    /// <summary><c>SHA-256(sha256(trades.csv) ":" sha256(open_at_end.csv))</c> over the lower-case hex of the two figures the manifest holds.</summary>
    public static string TradesDigest(string tradesSha256, string openAtEndSha256) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(tradesSha256 + ":" + openAtEndSha256)));

    /// <returns>The exit code: <see cref="SimulateCommand.Done"/>, <see cref="SimulateCommand.Refused"/> or <see cref="SimulateCommand.Failed"/>.</returns>
    public static async Task<int> StoreAsync(SimulationRun run, ExportOutcome outcome, IServiceProvider provider, SimulateConsole console, ILogger logger, CancellationToken cancellationToken)
    {
        var record = Record(run, outcome.Entries);
        var handler = provider.GetRequiredService<ICommandHandler<StoreRunCommand, StoreOutcome>>();
        var stored = await handler.HandleAsync(new StoreRunCommand(run, record, Actor), cancellationToken);
        var shortKey = run.RunKey[..12];
        if (stored.IsSuccess)
        {
            await console.Out.WriteLineAsync(stored.Value.WasAlreadyStored
                ? "already stored: run " + shortKey
                : "stored: run " + shortKey + ", " + run.Trades.Count.ToString(CultureInfo.InvariantCulture) + " trades, " + run.OpenAtEnd.Count.ToString(CultureInfo.InvariantCulture) + " open at end");
            return SimulateCommand.Done;
        }

        var error = stored.Error;
        SimulateLog.Refused(logger, error.Code, error.Fields?.Count ?? 1);
        if (error.Code == StoreErrors.RunInvalidCode)
        {
            await console.Error.WriteLineAsync("not stored (the export files are kept): " + error.Code);
            foreach (var field in (error.Fields ?? []).Take(SimulateCommand.MaxProblems))
            {
                await console.Error.WriteLineAsync("  " + field.Field + ": " + field.Message);
            }

            return SimulateCommand.Refused;
        }

        await console.Error.WriteLineAsync("not stored (the export files are kept): " + error.Code + ": " + error.Message);
        return SimulateCommand.Failed;
    }
}
