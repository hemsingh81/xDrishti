using System.Buffers;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Csv;
using XDrishti.SimExport.Json;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>Criteria 3, 4, 5 and 6 of P3-10 over the real adapter on a real TimescaleDB: the round trip equals the in-memory run, the csv files from the store equal the ones from memory, the store is idempotent by run key and another record under a key is refused.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class RunStoreRoundTripTests(ApiFactory factory)
{
    private static int _keys;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A window end no other test uses, so that the run key is the test's own.</summary>
    internal static DateOnly NextTo() => new DateOnly(2031, 1, 1).AddDays(Interlocked.Increment(ref _keys));

    private Task<T> WithStore<T>(Func<IRunStore, Task<T>> action) => StoreKit.WithStoreAsync(factory.Services, action);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_stored_run_reads_back_equal_to_the_one_in_memory(bool costed)
    {
        var run = StoredRunFixtures.Run(costed, to: NextTo());
        run.Trades.Count.ShouldBeGreaterThan(50);
        run.OpenAtEnd.Count.ShouldBeGreaterThan(0);
        var toStore = StoreKit.ToStore(run);

        var stored = await WithStore(s => s.StoreAsync(toStore, Ct).AsTask());
        var header = await WithStore(s => s.FindAsync(run.RunKey, Ct).AsTask());
        var trades = await WithStore(async s => await s.StreamTradesAsync(run.RunKey, false, Ct).ToListAsync(Ct));
        var open = await WithStore(async s => await s.StreamTradesAsync(run.RunKey, true, Ct).ToListAsync(Ct));
        var record = await WithStore(s => s.ReadRecordTextAsync(run.RunKey, Ct).AsTask());

        stored.IsSuccess.ShouldBeTrue(stored.IsFailure ? stored.Error.Message : string.Empty);
        stored.Value.WasAlreadyStored.ShouldBeFalse();
        SameTrade.ShouldBeSame(trades, run.Trades, "trades");
        SameTrade.ShouldBeSame(open, run.OpenAtEnd, "open_at_end");
        var sink = new ArrayBufferWriter<byte>();
        SimExportJson.WriteRecord(sink, run);
        record.ShouldBe(Encoding.UTF8.GetString(sink.WrittenSpan));
        StoreKit.Sha256(record!).ShouldBe(header!.RecordSha256);
        header.RunKey.ShouldBe(run.RunKey);
        (header.EngineVersion, header.RecordSchemaVersion, header.TradeSchemaVersion).ShouldBe((run.EngineVersion, 6, 5));
        (header.From, header.To, header.StrategyId, header.DefinitionSha256).ShouldBe((run.From, run.To, run.Strategy.Id, run.Strategy.DefinitionSha256));
        (header.UniverseKind, header.UniverseLabel).ShouldBe(("instrumentlist", run.Universe.Label));
        (header.Instruments, header.InstrumentsRun, header.TradesInWindow, header.CandidatesInWindow).ShouldBe((run.Totals.Instruments, run.Totals.InstrumentsRun, run.Totals.TradesInWindow, run.Totals.CandidatesInWindow));
        (header.TradeCount, header.OpenAtEndCount, header.Fills, header.AcknowledgeCsvAdjusted).ShouldBe((run.Trades.Count, run.OpenAtEnd.Count, run.Fills, false));
        (header.CostsSheetId, header.CostsRatesSha256).ShouldBe(costed ? (FixtureSheet.Id, run.Costs!.RatesSha256) : (null, null));
        (header.CreatedAt, header.CreatedBy, header.RecordJsonLength).ShouldBe((StoreKit.At, "cli:simulate", record!.Length));
        if (costed)
        {
            trades.ShouldAllBe(t => t.Trade.Costing != null && t.Trade.Costing.SheetId == FixtureSheet.Id);
        }
    }

    [Fact]
    public async Task A_stored_run_with_EmaExit_trades_reads_back_equal_to_the_one_in_memory()
    {
        // P4-03: the new reason is a new name in the check constraint of research.sim_trades (migration AddEmaExitReason) and a name the store reads back
        var run = StoredRunFixtures.Run(costed: false, to: NextTo(), emaExits: true);
        run.Trades.Count(t => t.Trade.ExitReason == ExitReason.EmaExit).ShouldBeGreaterThan(10);

        var stored = await WithStore(s => s.StoreAsync(StoreKit.ToStore(run), Ct).AsTask());
        var trades = await WithStore(async s => await s.StreamTradesAsync(run.RunKey, false, Ct).ToListAsync(Ct));

        stored.IsSuccess.ShouldBeTrue(stored.IsFailure ? stored.Error.Message : string.Empty);
        SameTrade.ShouldBeSame(trades, run.Trades, "trades");
        trades.Count(t => t.Trade.ExitReason == ExitReason.EmaExit).ShouldBe(run.Trades.Count(t => t.Trade.ExitReason == ExitReason.EmaExit));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_stored_run_with_legs_reads_back_with_its_legs_and_the_charges_of_its_fills_equal_in_every_field(bool costed)
    {
        // P4-05, D9: the legs and the charges of a fill are rows of their own; a trade with legs comes back with them, through the stream and through the pages
        var run = StoredRunFixtures.Run(costed, to: NextTo(), legs: true);
        var withLegs = run.Trades.Where(t => t.Trade.Legs is not null).ToList();
        withLegs.Count.ShouldBeGreaterThan(20);
        if (costed)
        {
            withLegs.ShouldAllBe(t => t.Trade.Costing!.LegCharges != null && t.Trade.Costing.LegCharges.Count >= 2);
        }

        var stored = await WithStore(s => s.StoreAsync(StoreKit.ToStore(run), Ct).AsTask());
        var trades = await WithStore(async s => await s.StreamTradesAsync(run.RunKey, false, Ct).ToListAsync(Ct));
        var open = await WithStore(async s => await s.StreamTradesAsync(run.RunKey, true, Ct).ToListAsync(Ct));
        var paged = new List<RunTrade>();
        string? cursor = null;
        do
        {
            var page = await WithStore(s => s.PageTradesAsync(run.RunKey, new TradePageFilter(), new PageRequest(7, cursor), Ct).AsTask());
            page.IsSuccess.ShouldBeTrue(page.IsFailure ? page.Error.Message : string.Empty);
            paged.AddRange(page.Value.Items);
            cursor = page.Value.NextCursor;
        }
        while (cursor is not null);

        stored.IsSuccess.ShouldBeTrue(stored.IsFailure ? stored.Error.Message : string.Empty);
        SameTrade.ShouldBeSame(trades, run.Trades, "trades");
        SameTrade.ShouldBeSame(open, run.OpenAtEnd, "open_at_end");
        SameTrade.ShouldBeSame(paged.Where(t => t.Trade.ExitReason != ExitReason.EndOfData).ToList(), run.Trades, "paged trades");
        (await StoreKit.CountAsync(factory.AdminConnectionString, "SELECT count(*) FROM research.sim_trade_legs l JOIN research.sim_runs r ON r.id = l.run_id WHERE r.run_key = @k", Ct, ("k", run.RunKey))).ShouldBe(withLegs.Sum(t => t.Trade.Legs!.Count));
        Csv(run.RunKey, trades).ShouldBe(StoreKit.TradesCsvText(run, false), "the legs column of the csv is the same from the store");
    }

    [Fact]
    public async Task A_run_with_legs_stored_twice_is_a_no_op_the_second_time_and_writes_no_second_set_of_legs()
    {
        var run = StoredRunFixtures.Run(costed: true, to: NextTo(), legs: true);
        var legCount = run.Trades.Sum(t => t.Trade.Legs?.Count ?? 0);
        legCount.ShouldBeGreaterThan(60);

        var first = await WithStore(s => s.StoreAsync(StoreKit.ToStore(run), Ct).AsTask());
        var second = await WithStore(s => s.StoreAsync(StoreKit.ToStore(run, StoreKit.At.AddMilliseconds(1), "cli:other"), Ct).AsTask());

        first.Value.WasAlreadyStored.ShouldBeFalse();
        second.Value.WasAlreadyStored.ShouldBeTrue();
        (await StoreKit.CountAsync(factory.AdminConnectionString, "SELECT count(*) FROM research.sim_trade_legs l JOIN research.sim_runs r ON r.id = l.run_id WHERE r.run_key = @k", Ct, ("k", run.RunKey))).ShouldBe(legCount);
        (await StoreKit.CountAsync(factory.AdminConnectionString, "SELECT count(*) FROM research.sim_trade_leg_charges c JOIN research.sim_runs r ON r.id = c.run_id WHERE r.run_key = @k", Ct, ("k", run.RunKey))).ShouldBe(run.Trades.Sum(t => t.Trade.Costing?.LegCharges?.Count ?? 0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_csv_files_made_from_the_stored_trades_are_byte_identical_to_the_ones_from_memory(bool costed)
    {
        var run = StoredRunFixtures.Run(costed, to: NextTo());
        var toStore = StoreKit.ToStore(run);
        (await WithStore(s => s.StoreAsync(toStore, Ct).AsTask())).IsSuccess.ShouldBeTrue();

        var trades = await WithStore(async s => await s.StreamTradesAsync(run.RunKey, false, Ct).ToListAsync(Ct));
        var open = await WithStore(async s => await s.StreamTradesAsync(run.RunKey, true, Ct).ToListAsync(Ct));

        Csv(run.RunKey, trades).ShouldBe(StoreKit.TradesCsvText(run, false));
        Csv(run.RunKey, open).ShouldBe(StoreKit.TradesCsvText(run, true));
        SimulateStore.TradesDigest(StoreKit.Sha256(Csv(run.RunKey, trades)), StoreKit.Sha256(Csv(run.RunKey, open))).ShouldBe(toStore.Record.TradesSha256);
    }

    private static string Csv(string runKey, IReadOnlyList<RunTrade> trades)
    {
        var sink = new ArrayBufferWriter<byte>();
        TradesCsv.Write(sink, runKey, trades);
        return new UTF8Encoding(false, true).GetString(sink.WrittenSpan);
    }

    [Fact]
    public async Task Storing_a_run_twice_is_a_no_op_the_second_time_and_keeps_the_first_rows()
    {
        var run = StoredRunFixtures.Run(to: NextTo());
        var first = await WithStore(s => s.StoreAsync(StoreKit.ToStore(run), Ct).AsTask());
        var before = await StoreKit.FingerprintAsync(factory.AdminConnectionString, run.RunKey, Ct);

        var second = await WithStore(s => s.StoreAsync(StoreKit.ToStore(run, StoreKit.At.AddMilliseconds(1), "cli:other"), Ct).AsTask());

        first.Value.WasAlreadyStored.ShouldBeFalse();
        second.IsSuccess.ShouldBeTrue();
        second.Value.WasAlreadyStored.ShouldBeTrue();
        second.Value.Id.ShouldBe(first.Value.Id);
        (await StoreKit.FingerprintAsync(factory.AdminConnectionString, run.RunKey, Ct)).ShouldBe(before);
        before.ShouldStartWith("1|" + (run.Trades.Count + run.OpenAtEnd.Count) + "|");
        before.ShouldContain("cli:simulate");
    }

    [Fact]
    public async Task Two_concurrent_stores_of_one_key_give_one_stored_one_already_stored_and_no_doubled_rows()
    {
        var run = StoredRunFixtures.Run(to: NextTo());

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => WithStore(s => s.StoreAsync(StoreKit.ToStore(run), Ct).AsTask())));

        results.ShouldAllBe(r => r.IsSuccess);
        results.Count(r => r.Value.WasAlreadyStored).ShouldBe(1);
        (await StoreKit.FingerprintAsync(factory.AdminConnectionString, run.RunKey, Ct)).ShouldStartWith("1|" + (run.Trades.Count + run.OpenAtEnd.Count) + "|");
    }

    [Fact]
    public async Task A_run_with_no_trades_stores_a_header_only_and_is_already_stored_the_second_time()
    {
        var empty = StoredRunFixtures.Run(to: NextTo(), tradesEach: 0, fixtureEdges: false);
        empty = empty with { OpenAtEnd = [] };

        var first = await WithStore(s => s.StoreAsync(StoreKit.ToStore(empty), Ct).AsTask());
        var second = await WithStore(s => s.StoreAsync(StoreKit.ToStore(empty), Ct).AsTask());

        first.Value.WasAlreadyStored.ShouldBeFalse();
        second.Value.WasAlreadyStored.ShouldBeTrue();
        (await StoreKit.FingerprintAsync(factory.AdminConnectionString, empty.RunKey, Ct)).ShouldStartWith("1|0|");
        (await WithStore(async s => await s.StreamTradesAsync(empty.RunKey, false, Ct).ToListAsync(Ct))).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_run_with_only_open_at_end_trades_round_trips()
    {
        var source = StoredRunFixtures.Run(to: NextTo(), fixtureEdges: false);
        var run = source with { Trades = [] };

        (await WithStore(s => s.StoreAsync(StoreKit.ToStore(run), Ct).AsTask())).IsSuccess.ShouldBeTrue();

        (await WithStore(async s => await s.StreamTradesAsync(run.RunKey, false, Ct).ToListAsync(Ct))).ShouldBeEmpty();
        SameTrade.ShouldBeSame(await WithStore(async s => await s.StreamTradesAsync(run.RunKey, true, Ct).ToListAsync(Ct)), run.OpenAtEnd, "open_at_end");
    }

    // criterion 6: another record under a stored key is a corruption signal

    public static TheoryData<string> Differences() => new() { "record", "trades", "trade_count", "engine_version" };

    [Theory]
    [MemberData(nameof(Differences))]
    public async Task Another_record_under_a_stored_key_is_a_conflict_and_nothing_is_written(string difference)
    {
        var run = StoredRunFixtures.Run(to: NextTo());
        (await WithStore(s => s.StoreAsync(StoreKit.ToStore(run), Ct).AsTask())).IsSuccess.ShouldBeTrue();
        var before = await StoreKit.FingerprintAsync(factory.AdminConnectionString, run.RunKey, Ct);
        var good = StoreKit.Record(run);
        var other = difference switch
        {
            "record" => new RunToStore(run, new RunRecordText(good.Json + " ", StoreKit.Sha256(good.Json + " "), good.TradesSha256), "cli:simulate", StoreKit.At),
            "trades" => new RunToStore(run, new RunRecordText(good.Json, good.RecordSha256, new string('c', 64)), "cli:simulate", StoreKit.At),
            "trade_count" => new RunToStore(run with { Trades = [.. run.Trades.Skip(1)] }, good, "cli:simulate", StoreKit.At),
            _ => new RunToStore(run with { EngineVersion = "sim-9" }, good, "cli:simulate", StoreKit.At),
        };

        var result = await WithStore(s => s.StoreAsync(other, Ct).AsTask());

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("store.run_conflict");
        result.Error.Fields!.Select(f => f.Field).ShouldContain(difference);
        (await StoreKit.FingerprintAsync(factory.AdminConnectionString, run.RunKey, Ct)).ShouldBe(before);
    }

    [Fact]
    public async Task A_conflict_through_the_handler_is_logged_once_at_error_with_no_trade_value_and_no_record_text()
    {
        var run = StoredRunFixtures.Run(to: NextTo());
        using (var scope = factory.Services.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<XDrishti.Application.Abstractions.Messaging.ICommandHandler<XDrishti.Application.Research.StoreRunCommand, StoreOutcome>>();
            (await handler.HandleAsync(new XDrishti.Application.Research.StoreRunCommand(run, StoreKit.Record(run), "cli:simulate"), Ct)).IsSuccess.ShouldBeTrue();
        }

        var tampered = StoreKit.Record(run);
        tampered = new RunRecordText(tampered.Json, tampered.RecordSha256, new string('d', 64));
        using (var scope = factory.Services.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<XDrishti.Application.Abstractions.Messaging.ICommandHandler<XDrishti.Application.Research.StoreRunCommand, StoreOutcome>>();

            var result = await handler.HandleAsync(new XDrishti.Application.Research.StoreRunCommand(run, tampered, "cli:simulate"), Ct);

            result.Error.Code.ShouldBe("store.run_conflict");
        }

        var lines = factory.Logs.All.Where(l => l.Contains(run.RunKey[..12], StringComparison.Ordinal)).ToList();
        lines.Count(l => l.StartsWith("Error", StringComparison.Ordinal) && l.Contains("store.conflict", StringComparison.Ordinal)).ShouldBe(1);
        lines.Count(l => l.Contains("store.started", StringComparison.Ordinal)).ShouldBe(2);
        lines.ShouldAllBe(l => !l.Contains("schema_version", StringComparison.Ordinal) && !l.Contains("SYM0", StringComparison.Ordinal) && !l.Contains("\"id\"", StringComparison.Ordinal));
    }
}
