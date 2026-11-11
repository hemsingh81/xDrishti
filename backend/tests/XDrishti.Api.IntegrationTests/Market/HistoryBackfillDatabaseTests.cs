using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Application.Abstractions.Market.Actions;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.History;
using XDrishti.Application.Abstractions.SimAll;
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Market.History;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Bars;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Market;

/// <summary>
/// CF-04 Part B criteria 7, 10 and 18 on a real TimescaleDB: a backfill leaves the stored CSV rows byte-identical (a Dhan bar at or after the first CSV bar is dropped and counted), a probe leaves the bar table untouched, the ledger resumes, and the migration is reversible.
/// The Dhan source is a fake; everything else is the real store.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class HistoryBackfillDatabaseTests(ApiFactory factory) : DataTestBase(factory)
{
    private const string Before = "20261111133052_AddOpeningBarsRetention";

    private static readonly DateTimeOffset Seam = new(2024, 4, 1, 9, 15, 0, IndiaTime.Offset);

    private static Bar At(DateTimeOffset when, double close) => new(when.ToUnixTimeSeconds(), close, close + 1, close - 1, close, 10);

    private async Task LoadCsvAsync(Guid id, params Bar[] bars)
    {
        using var scope = Factory.Services.CreateScope();
        await using var session = await scope.ServiceProvider.GetRequiredService<IBarStore>().BeginMergeAsync(id, BarSource.Csv, Ct);
        await session.AddAsync(bars, Ct);
        await session.CompleteAsync(new CsvFileMark("abc.csv", bars.Length, DateTimeOffset.UnixEpoch, bars.Length, 0, 0, bars[0].Start, bars[^1].Start), Ct);
    }

    private async Task<string> HashAsync(Guid id, string? source = null)
    {
        var filter = source is null ? string.Empty : $" AND source = '{source}'";
        return await ScalarAsync<string>($"SELECT coalesce(md5(string_agg(ts::text || '|' || open || '|' || high || '|' || low || '|' || close || '|' || volume || '|' || source, ';' ORDER BY ts)), '') FROM market.bars_1m WHERE instrument_id = @id{filter}", ("id", id));
    }

    private sealed class FixedSettings : IBackfillSettings
    {
        public bool Enabled => true;

        public int Years => 5;

        public int ChunkDays => 90;

        public int MaxRetries => 3;

        public int DiskHeadroomFactor => 2;

        public int BytesPerRowEstimate => 60;

        public TimeOnly RunWindowFrom => new(20, 0);

        public TimeOnly RunWindowTo => new(6, 0);

        public int ProbeInstruments => 20;

        public int ProbeSessions => 10;

        public double ProbeMedianBps => 5;

        public double ProbeMaxBps => 50;

        public double ProbeMinWithinMaxPct => 99;
    }

    private sealed class FixedTargets(FetchTarget target) : IFetchTargets
    {
        public ValueTask<IReadOnlyList<FetchTarget>> ListAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<FetchTarget>>([target]);
    }

    private sealed class Account : IDataAccountStatus
    {
        public ValueTask<DataAccountState> GetAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new DataAccountState(Guid.NewGuid(), "Primary", DataAccountStatuses.Connected, null));

        public ValueTask<SecretValue?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult<SecretValue?>(null);
    }

    private sealed class Calendar : IBackfillCalendar
    {
        public ValueTask<IReadOnlySet<DateOnly>> KnownDaysAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlySet<DateOnly>>(Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(i => from.AddDays(i)).ToHashSet());
    }

    private sealed class Disk : IFreeDiskProbe
    {
        public long? FreeBytes() => long.MaxValue;
    }

    private sealed class Source(Func<IntradayTarget, IReadOnlyList<Bar>> respond) : IIntradayBarSource
    {
        public int Requests { get; private set; }

        public ValueTask<SourceAvailability> CheckAsync(CancellationToken cancellationToken) => ValueTask.FromResult(SourceAvailability.Ready);

        public async IAsyncEnumerable<IntradayResult> FetchAsync(IntradayFetchPlan plan, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var target in plan.Targets)
            {
                Requests++;
                await Task.Yield();
                yield return new IntradayResult(target.InstrumentId, respond(target), 1);
            }
        }
    }

    private RunBackfillHandler Handler(IServiceScope scope, Guid id, Source source) =>
        new(
            new FixedSettings(),
            Factory.Services.GetRequiredService<IDataSettings>(),
            source,
            new Account(),
            new FixedTargets(new FetchTarget(id, "ABC", "share", "1333", "NSE_EQ", null)),
            scope.ServiceProvider.GetRequiredService<IBarStore>(),
            scope.ServiceProvider.GetRequiredService<IBackfillStore>(),
            new Calendar(),
            new Disk(),
            scope.ServiceProvider.GetRequiredService<ICorporateActionQueries>(),
            scope.ServiceProvider.GetRequiredService<IAdjustmentFactors>(),
            Factory.Services.GetRequiredService<TimeProvider>(),
            NullLogger<RunBackfillHandler>.Instance);

    [Fact]
    public async Task A_backfill_writes_only_before_the_first_csv_bar_and_leaves_the_csv_rows_byte_identical()
    {
        var id = await SeedAsync("ABC");
        await LoadCsvAsync(id, At(Seam, 300), At(Seam.AddMinutes(1), 301), At(Seam.AddDays(1), 302));
        var csvHash = await HashAsync(id, "csv");
        var source = new Source(t =>
            t.Windows[0].To == new DateTimeOffset(2024, 4, 1, 0, 0, 0, IndiaTime.Offset)
                ? [At(Seam.AddDays(-3), 250), At(Seam, 999), At(Seam.AddMinutes(1), 999), At(Seam.AddDays(1), 999)] // the last three would replace the CSV bars if they were written
                : [At(t.Windows[0].From.AddHours(9).AddMinutes(15), 100)]);
        using var scope = Factory.Services.CreateScope();

        var result = await Handler(scope, id, source).HandleAsync(new RunBackfillCommand(), Ct);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : null);
        result.Value.BeyondSeam.ShouldBe(3);
        result.Value.ChunksDone.ShouldBe(11, "the test clock is 2026-10-12: 902 days back to the seam");
        (await HashAsync(id, "csv")).ShouldBe(csvHash, "the stored CSV rows are byte-identical");
        (await ScalarAsync<long>("SELECT count(*) FROM market.bars_1m WHERE instrument_id = @id AND source = 'dhan' AND ts >= @seam", ("id", id), ("seam", Seam.ToUniversalTime()))).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM market.bars_1m WHERE instrument_id = @id AND source = 'dhan'", ("id", id))).ShouldBe(11);
        var store = scope.ServiceProvider.GetRequiredService<IBackfillStore>();
        (await store.CurrentSeamAsync(id, Ct)).ShouldBe(Seam, "the seam is the oldest csv bar, although Dhan bars are older");

        // A second complete run is idempotent: no request, no row.
        var again = new Source(_ => []);
        var second = await Handler(scope, id, again).HandleAsync(new RunBackfillCommand(), Ct);
        second.Value.ChunksPending.ShouldBe(0);
        again.Requests.ShouldBe(0);
        (await HashAsync(id, "csv")).ShouldBe(csvHash);
    }

    [Fact]
    public async Task A_probe_leaves_the_bar_table_untouched_and_stores_only_the_seam_row()
    {
        var id = await SeedAsync("ABC");
        await LoadCsvAsync(id, At(Seam, 100), At(Seam.AddMinutes(1), 100.1), At(Seam.AddMinutes(2), 99.9));
        var before = await HashAsync(id);
        var count = await ScalarAsync<long>("SELECT count(*) FROM market.bars_1m");
        using var scope = Factory.Services.CreateScope();
        var handler = new ProbeSeamHandler(
            new FixedSettings(),
            new Source(_ => [At(Seam, 100), At(Seam.AddMinutes(1), 100.1), At(Seam.AddMinutes(2), 99.95)]),
            new Account(),
            new FixedTargets(new FetchTarget(id, "ABC", "share", "1333", "NSE_EQ", null)),
            scope.ServiceProvider.GetRequiredService<IBarStore>(),
            scope.ServiceProvider.GetRequiredService<IBackfillStore>(),
            scope.ServiceProvider.GetRequiredService<ICorporateActionQueries>(),
            scope.ServiceProvider.GetRequiredService<IAdjustmentFactors>(),
            Factory.Services.GetRequiredService<TimeProvider>(),
            NullLogger<ProbeSeamHandler>.Instance);

        var result = await handler.HandleAsync(new ProbeSeamCommand(), Ct);

        result.Value.Passed.ShouldBe(1);
        (await HashAsync(id)).ShouldBe(before);
        (await ScalarAsync<long>("SELECT count(*) FROM market.bars_1m")).ShouldBe(count);
        (await scope.ServiceProvider.GetRequiredService<IBackfillStore>().ListSeamsAsync([id], Ct))[id].ProbeStatus.ShouldBe("passed");
    }

    [Fact]
    public async Task The_ledger_resets_a_chunk_whose_window_changed_drops_a_stale_planned_one_and_the_gate_sees_unfinished_work()
    {
        var id = await SeedAsync("ABC");
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IBackfillStore>();
        var progress = scope.ServiceProvider.GetRequiredService<IBackfillProgress>();
        await store.PlanChunksAsync(id, [(new DateOnly(2024, 1, 2), new DateOnly(2024, 4, 1)), (new DateOnly(2023, 10, 4), new DateOnly(2024, 1, 2))], Ct);
        await store.SaveChunkAsync(new ChunkRecord(id, new DateOnly(2024, 1, 2), new DateOnly(2024, 4, 1), "done", 500, 1, null), Ct);

        (await progress.UnfinishedInstrumentsAsync(Ct)).ShouldBe(1);

        await store.PlanChunksAsync(id, [(new DateOnly(2024, 1, 2), new DateOnly(2024, 4, 5))], Ct); // the seam moved later: the done chunk's window changed, the other planned chunk is stale

        var chunks = await store.ListChunksAsync(id, Ct);
        chunks.Count.ShouldBe(1);
        chunks[0].ShouldBe(new ChunkRecord(id, new DateOnly(2024, 1, 2), new DateOnly(2024, 4, 5), "planned", 0, 0, null));
        await store.SaveChunkAsync(chunks[0] with { Status = "empty" }, Ct);
        (await progress.UnfinishedInstrumentsAsync(Ct)).ShouldBe(0);
        await store.SaveChunkAsync(chunks[0] with { Status = "failed", Attempts = 3, Error = "x" }, Ct);
        (await progress.UnfinishedInstrumentsAsync(Ct)).ShouldBe(1, "a failed chunk keeps the AU-12 gate closed (fail closed)");
        await store.SaveSeamAsync(new SeamRecord(id, Seam.ToUniversalTime(), "unresolved", null, null, null, null, false, null), Ct);
        (await progress.UnfinishedInstrumentsAsync(Ct)).ShouldBe(0, "an unresolved seam counts as complete without pre-seam bars");
        (await scope.ServiceProvider.GetRequiredService<IBackfillSeams>().SeamOfAsync(id, Ct)).ShouldBe(Seam);
    }

    [Fact]
    public async Task The_cut_is_a_high_water_mark_a_seam_that_moves_earlier_never_lowers_it_and_the_sql_function_agrees()
    {
        var id = await SeedAsync("ABC");
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IBackfillStore>();
        var seams = scope.ServiceProvider.GetRequiredService<IBackfillSeams>();
        (await ScalarAsync<DateTime>("SELECT market.backfill_cut(@id)", ("id", id))).ShouldBe(DateTime.MinValue.ToUniversalTime(), "no row: -infinity reads as the minimum");
        (await seams.SeamOfAsync(id, Ct)).ShouldBeNull();

        await store.SaveSeamAsync(new SeamRecord(id, Seam.ToUniversalTime(), "none", null, null, null, null, false, null), Ct);
        await store.SaveSeamAsync(new SeamRecord(id, Seam.AddDays(-30).ToUniversalTime(), "unresolved", null, null, null, null, false, null), Ct); // a CSV import reached 30 days earlier

        (await seams.SeamOfAsync(id, Ct)).ShouldBe(Seam, "the high-water mark stays at the latest seam ever recorded: raw Dhan bars before it are not admitted");
        (await store.ListSeamsAsync([id], Ct))[id].SeamAt.ShouldBe(Seam.AddDays(-30));
        (await ScalarAsync<DateTime>("SELECT market.backfill_cut(@id)", ("id", id))).ShouldBe(Seam.UtcDateTime);

        await store.SaveSeamAsync(new SeamRecord(id, Seam.AddDays(10).ToUniversalTime(), "none", null, null, null, null, false, null), Ct);

        (await seams.SeamOfAsync(id, Ct)).ShouldBe(Seam.AddDays(10));
    }

    [Fact]
    public async Task Down_removes_both_tables_and_up_again_is_clean()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(Factory, "xd_backfill_down_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await using var source = NpgsqlDataSource.Create(scratch.ConnectionString);
        scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().Database.HasPendingModelChanges().ShouldBeFalse("the migration and the model snapshot agree");

        await migrator.MigrateAsync(Before, Ct);

        (await Count(source)).ShouldBe(0);
        await using (var index = source.CreateCommand("SELECT count(*) FROM pg_indexes WHERE indexname IN ('ix_bars_1m_csv') OR indexname LIKE 'ix_bars_1m_csv%'"))
        {
            Convert.ToInt64(await index.ExecuteScalarAsync(Ct), System.Globalization.CultureInfo.InvariantCulture).ShouldBe(0);
        }

        await migrator.MigrateAsync(null, Ct);

        (await Count(source)).ShouldBe(2);
        await using var present = source.CreateCommand("SELECT count(*) FROM pg_proc WHERE proname = 'backfill_cut'");
        Convert.ToInt64(await present.ExecuteScalarAsync(Ct), System.Globalization.CultureInfo.InvariantCulture).ShouldBe(1);

        static async Task<long> Count(NpgsqlDataSource data)
        {
            await using var command = data.CreateCommand("SELECT count(*) FROM information_schema.tables WHERE table_schema = 'market' AND table_name IN ('backfill_chunks', 'backfill_seams')");
            return Convert.ToInt64(await command.ExecuteScalarAsync(Ct), System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
