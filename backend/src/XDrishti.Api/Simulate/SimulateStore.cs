using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Csv;
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
        ArgumentNullException.ThrowIfNull(entries);
        return Record(run, entries.Single(e => e.Name == "trades.csv").Sha256, entries.Single(e => e.Name == "open_at_end.csv").Sha256);
    }

    /// <summary>As above for the files of a candidate of a comparison, whose names the caller knows (P4-05).</summary>
    public static RunRecordText Record(SimulationRun run, string tradesSha256, string openAtEndSha256)
    {
        ArgumentNullException.ThrowIfNull(run);

        var sink = new ArrayBufferWriter<byte>();
        SimExportJson.WriteRecord(sink, run);
        var recordSha = Convert.ToHexStringLower(SHA256.HashData(sink.WrittenSpan));
        var text = new UTF8Encoding(false, true).GetString(sink.WrittenSpan);
        return new RunRecordText(text, recordSha, TradesDigest(tradesSha256, openAtEndSha256));
    }

    /// <summary>The text of a comparison for the store: <c>comparison.csv</c>, a line feed and <c>diagnostics.csv</c> in the canonical form of the export, and the SHA-256 of that text.</summary>
    public static ComparisonRecordText ComparisonRecord(ComparisonReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var table = new ArrayBufferWriter<byte>();
        ComparisonCsv.Write(table, report);
        var diagnostics = new ArrayBufferWriter<byte>();
        ComparisonCsv.WriteDiagnostics(diagnostics, report);
        var utf8 = new UTF8Encoding(false, true);
        var tableText = utf8.GetString(table.WrittenSpan);
        var diagnosticsText = utf8.GetString(diagnostics.WrittenSpan);
        return new ComparisonRecordText(tableText, diagnosticsText, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(tableText + "\n" + diagnosticsText))));
    }

    /// <summary>
    /// The <c>--store</c> step of a comparison (spec P4-05, D10): every candidate is stored as the run it is, then the comparison names them. A refusal or a failure of a member stops there (the comparison is not stored without its members); the files
    /// stay in every case. One line is printed for each candidate and one for the comparison.
    /// </summary>
    /// <returns>The exit code: <see cref="SimulateCommand.Done"/>, <see cref="SimulateCommand.Refused"/> or <see cref="SimulateCommand.Failed"/>.</returns>
    public static async Task<int> StoreComparisonAsync(ComparisonRun comparison, ExportOutcome outcome, IServiceProvider provider, SimulateConsole console, ILogger logger, CancellationToken cancellationToken)
    {
        var runs = provider.GetRequiredService<ICommandHandler<StoreRunCommand, StoreOutcome>>();
        for (var i = 0; i < comparison.Candidates.Count; i++)
        {
            var run = comparison.Candidates[i].Run;
            var names = SimulateCompare.Names(i, comparison.Candidates[i].Id);
            var record = Record(run, outcome.Entries.Single(e => e.Name == names.Trades).Sha256, outcome.Entries.Single(e => e.Name == names.Open).Sha256);
            var stored = await runs.HandleAsync(new StoreRunCommand(run, record, Actor), cancellationToken);
            if (stored.IsFailure)
            {
                return await ReportAsync(stored.Error, "run " + run.RunKey[..12], console, logger);
            }

            await console.Out.WriteLineAsync((stored.Value.WasAlreadyStored ? "already stored: run " : "stored: run ") + run.RunKey[..12] + " (" + comparison.Candidates[i].Id + ")");
        }

        var handler = provider.GetRequiredService<ICommandHandler<StoreComparisonCommand, StoreOutcome>>();
        var header = await handler.HandleAsync(new StoreComparisonCommand(comparison, ComparisonRecord(comparison.Report), Actor), cancellationToken);
        if (header.IsFailure)
        {
            return await ReportAsync(header.Error, "comparison " + comparison.ComparisonKey[..12], console, logger);
        }

        await console.Out.WriteLineAsync((header.Value.WasAlreadyStored ? "already stored: comparison " : "stored: comparison ") + comparison.ComparisonKey[..12] + ", " + comparison.Candidates.Count.ToString(CultureInfo.InvariantCulture) + " members, entries key " + comparison.EntriesKey[..12]);
        return SimulateCommand.Done;
    }

    private static async Task<int> ReportAsync(Error error, string what, SimulateConsole console, ILogger logger)
    {
        SimulateLog.Refused(logger, error.Code, error.Fields?.Count ?? 1);
        if (error.Code == StoreErrors.RunInvalidCode)
        {
            await console.Error.WriteLineAsync("not stored (the export files are kept): " + what + ": " + error.Code);
            foreach (var field in (error.Fields ?? []).Take(SimulateCommand.MaxProblems))
            {
                await console.Error.WriteLineAsync("  " + field.Field + ": " + field.Message);
            }

            return SimulateCommand.Refused;
        }

        await console.Error.WriteLineAsync("not stored (the export files are kept): " + what + ": " + error.Code + ": " + error.Message);
        return SimulateCommand.Failed;
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
