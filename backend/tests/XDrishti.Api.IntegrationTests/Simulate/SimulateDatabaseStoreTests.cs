using System.Buffers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.SimExport.Json;
using XDrishti.SimExport.Manifest;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>Criteria 13 and 14 of P3-10: <c>simulate … --store</c> over the real host and the real adapter on the container: the files as without the flag, then the store, idempotent, refused on a conflict, the files kept on every failure, the digests from the in-memory run.</summary>
public sealed partial class SimulateDatabaseTests
{
    private const string StoreArgs = "--strategy ORB --symbols ABB,TCS " + Window;

    /// <summary>A store handler that records the command and answers with a fixed result, or throws.</summary>
    private sealed class FakeStoreHandler(Func<StoreRunCommand, Result<StoreOutcome>> answer) : ICommandHandler<StoreRunCommand, StoreOutcome>
    {
        public List<StoreRunCommand> Commands { get; } = [];

        public ValueTask<Result<StoreOutcome>> HandleAsync(StoreRunCommand command, CancellationToken cancellationToken)
        {
            Commands.Add(command);
            return ValueTask.FromResult(answer(command));
        }
    }

    private async Task SeedStoreInstrumentsAsync()
    {
        await InstrumentAsync("ABB", A());
        await InstrumentAsync("TCS", B());
    }

    private static Dictionary<string, string> FilesOf(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(folder, f), f => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f))), StringComparer.Ordinal);

    private Task<long> ResearchCountAsync(string sql, params (string, object)[] parameters) => StoreKit.CountAsync(Factory.AdminConnectionString, sql, Ct, parameters);

    [Fact]
    public async Task With_store_the_export_is_written_as_without_the_flag_then_the_run_is_stored_and_a_second_invocation_says_already_stored()
    {
        await SeedStoreInstrumentsAsync();
        using var plain = new TempFolder();
        using var kept = new TempFolder();

        var without = await VerbAsync(StoreArgs, plain);
        var first = await VerbAsync(StoreArgs + " --store", kept);
        var second = await VerbAsync(StoreArgs + " --store", kept);

        without.Exit.ShouldBe(0, without.Error);
        first.Exit.ShouldBe(0, first.Error);
        second.Exit.ShouldBe(0, second.Error);
        FilesOf(RunFolder(kept)).ShouldBe(FilesOf(RunFolder(plain)), "the flag does not change a byte of the export");
        var runKey = ExportManifest.ParseBytes(File.ReadAllBytes(Path.Combine(RunFolder(kept), "manifest.json"))).Value.RunKey;
        first.Out.ShouldContain("stored: run " + runKey[..12] + ", ");
        first.Out.ShouldNotContain("already stored");
        second.Out.ShouldContain("already stored: run " + runKey[..12]);
        (await ResearchCountAsync("SELECT count(*) FROM research.sim_runs WHERE run_key = @k", ("k", runKey))).ShouldBe(1);
        (await ResearchCountAsync("SELECT count(*) FROM research.sim_trades t JOIN research.sim_runs r ON r.id = t.run_id WHERE r.run_key = @k", ("k", runKey))).ShouldBe(Rows(RunFolder(kept)).Count + Rows(RunFolder(kept), "open_at_end.csv").Count);
    }

    [Fact]
    public async Task Without_store_nothing_is_written_to_the_research_tables_and_the_store_is_never_asked_for()
    {
        await SeedStoreInstrumentsAsync();
        using var temp = new TempFolder();
        var before = await ResearchCountAsync("SELECT (SELECT count(*) FROM research.sim_runs) + (SELECT count(*) FROM research.sim_trades)");

        var outcome = await VerbAsync(StoreArgs, temp, configure: services => services.AddScoped<IRunStore>(_ => throw new InvalidOperationException("the store was resolved without --store")));

        outcome.Exit.ShouldBe(0, outcome.Error);
        outcome.Out.ShouldNotContain("stored: run");
        outcome.Out.ShouldNotContain("already stored");
        (await ResearchCountAsync("SELECT (SELECT count(*) FROM research.sim_runs) + (SELECT count(*) FROM research.sim_trades)")).ShouldBe(before);
    }

    [Fact]
    public async Task A_store_refused_by_validation_exits_1_with_the_files_kept_and_the_problems_listed()
    {
        await SeedStoreInstrumentsAsync();
        using var temp = new TempFolder();
        var fake = new FakeStoreHandler(_ => Result.Failure<StoreOutcome>(StoreErrors.RunInvalid([new FieldError("run.runKey", "The run key is 64 lower-case hex characters.")])));

        var outcome = await VerbAsync(StoreArgs + " --store", temp, configure: services => services.AddScoped<ICommandHandler<StoreRunCommand, StoreOutcome>>(_ => fake));

        outcome.Exit.ShouldBe(1);
        outcome.Error.ShouldContain("store.run_invalid");
        outcome.Error.ShouldContain("run.runKey");
        File.Exists(Path.Combine(RunFolder(temp), "manifest.json")).ShouldBeTrue();
    }

    [Fact]
    public async Task A_conflict_exits_3_with_the_files_kept_and_the_message_names_the_code()
    {
        await SeedStoreInstrumentsAsync();
        using var temp = new TempFolder();
        (await VerbAsync(StoreArgs + " --store", temp)).Exit.ShouldBe(0);
        var runKey = ExportManifest.ParseBytes(File.ReadAllBytes(Path.Combine(RunFolder(temp), "manifest.json"))).Value.RunKey;
        await StoreKit.ExecuteAsync(Factory.AdminConnectionString, "UPDATE research.sim_runs SET trades_sha256 = repeat('e', 64) WHERE run_key = @k", Ct, ("k", runKey));

        var outcome = await VerbAsync(StoreArgs + " --store", temp);

        outcome.Exit.ShouldBe(3);
        outcome.Error.ShouldContain("store.run_conflict");
        File.Exists(Path.Combine(RunFolder(temp), "manifest.json")).ShouldBeTrue();
        outcome.Logs.ShouldContain(l => l.StartsWith("Error", StringComparison.Ordinal) && l.Contains("store.conflict", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_database_failure_while_storing_exits_3_after_the_files_are_written()
    {
        await SeedStoreInstrumentsAsync();
        using var temp = new TempFolder();
        var fake = new FakeStoreHandler(_ => Result.Failure<StoreOutcome>(StoreErrors.WriteFailed("The database could not be used: NpgsqlException; nothing was stored.")));

        var failed = await VerbAsync(StoreArgs + " --store", temp, configure: services => services.AddScoped<ICommandHandler<StoreRunCommand, StoreOutcome>>(_ => fake));
        var thrown = await VerbAsync(StoreArgs + " --store", temp, configure: services => services.AddScoped<IRunStore>(_ => throw new NpgsqlException("connection refused")));

        failed.Exit.ShouldBe(3);
        failed.Error.ShouldContain("store.write_failed");
        thrown.Exit.ShouldBe(3);
        thrown.Error.ShouldContain("failed:");
        File.Exists(Path.Combine(RunFolder(temp), "manifest.json")).ShouldBeTrue();
    }

    [Fact]
    public async Task The_record_text_and_the_digests_handed_to_the_store_are_those_of_the_exported_bytes_and_come_from_the_run_in_memory()
    {
        await SeedStoreInstrumentsAsync();
        using var temp = new TempFolder();
        string? tradesCsvBefore = null;
        StoreRunCommand? seen = null;
        var fake = new FakeStoreHandler(command =>
        {
            seen = command;
            var folder = RunFolder(temp);
            tradesCsvBefore = File.ReadAllText(Path.Combine(folder, "trades.csv"));
            File.AppendAllText(Path.Combine(folder, "trades.csv"), "tampered after the write\n");
            return Result.Success(StoreOutcome.Stored(1, 0));
        });

        var outcome = await VerbAsync(StoreArgs + " --store", temp, configure: services => services.AddScoped<ICommandHandler<StoreRunCommand, StoreOutcome>>(_ => fake));

        outcome.Exit.ShouldBe(0, outcome.Error);
        var command = seen.ShouldNotBeNull();
        var run = command.Run!;
        var folder = RunFolder(temp);
        var manifest = ExportManifest.ParseBytes(File.ReadAllBytes(Path.Combine(folder, "manifest.json"))).Value;
        var sinkRecord = new ArrayBufferWriter<byte>();
        SimExportJson.WriteRecord(sinkRecord, run);
        command.Record.Json.ShouldBe(System.Text.Encoding.UTF8.GetString(sinkRecord.WrittenSpan));
        command.Record.RecordSha256.ShouldBe(StoreKit.Sha256(command.Record.Json));
        var tradesEntry = manifest.Entries.Single(e => e.Name == "trades.csv");
        var openEntry = manifest.Entries.Single(e => e.Name == "open_at_end.csv");
        command.Record.TradesSha256.ShouldBe(SimulateStore.TradesDigest(tradesEntry.Sha256, openEntry.Sha256));
        tradesEntry.Sha256.ShouldBe(StoreKit.Sha256(tradesCsvBefore!), "the digest is that of the bytes the writer rendered, not of the file as it is now");
        command.Record.TradesSha256.ShouldBe(StoreKit.Record(run).TradesSha256, "the same figure from the run in memory");
        using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "run.json")));
        var fromFile = json.RootElement.GetProperty("run").EnumerateObject().Where(p => p.Name is not ("trades" or "open_at_end")).Select(p => (p.Name, p.Value.GetRawText())).ToList();
        JsonDocument.Parse(command.Record.Json).RootElement.EnumerateObject().Select(p => (p.Name, p.Value.GetRawText())).ToList().ShouldBe(fromFile);
        command.Actor.ShouldBe("cli:simulate");
    }
}
