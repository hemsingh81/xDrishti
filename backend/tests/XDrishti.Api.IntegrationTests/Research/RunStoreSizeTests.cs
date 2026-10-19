using System.Diagnostics;
using System.Globalization;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Research;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// Criterion 15 of P3-10: size and time are measured, not assumed. A synthetic costed run of 200,000 trades (the 20-instrument trades replicated with distinct keys) is stored in a scratch database; the test prints the sizes of the two tables and
/// their indexes, the milliseconds of the transaction, of one filtered page and of the whole stream. The figures are written into the spec's <i>Built</i> section and compared with D5; the bounds asserted here are the loose ones of <i>Non-functional</i>,
/// widened for a loaded machine, not a gate on speed.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class RunStoreSizeTests(ApiFactory factory)
{
    private const int Trades = 200_000;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static void Say(string line) => TestContext.Current.TestOutputHelper?.WriteLine(line);

    [Fact]
    public async Task A_run_of_200000_costed_trades_is_stored_read_and_measured()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_size_probe", Ct);
        var built = Stopwatch.StartNew();
        var run = StoredRunFixtures.Synthetic(Trades, costed: true);
        var toStore = StoreKit.ToStore(run);
        built.Stop();
        Say($"size-test: built {run.Trades.Count} trades in {built.ElapsedMilliseconds} ms; record text {toStore.Record.Json.Length} characters");

        var write = Stopwatch.StartNew();
        var stored = await StoreKit.WithStoreAsync(scratch.Host.Services, s => s.StoreAsync(toStore, Ct).AsTask());
        write.Stop();
        stored.IsSuccess.ShouldBeTrue(stored.IsFailure ? stored.Error.Message : string.Empty);

        await StoreKit.ExecuteAsync(scratch.ConnectionString, "VACUUM (ANALYZE) research.sim_trades", Ct);
        await StoreKit.ExecuteAsync(scratch.ConnectionString, "VACUUM (ANALYZE) research.sim_runs", Ct);
        var sizes = new Dictionary<string, long>();
        foreach (var (label, sql) in new[]
        {
            ("sim_trades table", "SELECT pg_table_size('research.sim_trades')"),
            ("sim_trades indexes", "SELECT pg_indexes_size('research.sim_trades')"),
            ("sim_trades total", "SELECT pg_total_relation_size('research.sim_trades')"),
            ("sim_trades pk", "SELECT pg_relation_size('research.pk_sim_trades')"),
            ("sim_trades unique trade key", "SELECT pg_relation_size('research.uq_sim_trades_run_trade_key')"),
            ("sim_runs total", "SELECT pg_total_relation_size('research.sim_runs')"),
        })
        {
            sizes[label] = await StoreKit.CountAsync(scratch.ConnectionString, sql, Ct);
        }

        var rows = run.Trades.Count;
        Say($"size-test: transaction {write.ElapsedMilliseconds} ms for {rows} rows (copy {stored.Value.CopyMilliseconds} ms), {rows * 1000L / Math.Max(1, write.ElapsedMilliseconds)} rows/s");
        foreach (var (label, bytes) in sizes)
        {
            Say(string.Create(CultureInfo.InvariantCulture, $"size-test: {label} {bytes / 1048576.0:F1} MiB ({bytes} bytes), {bytes / (double)rows:F0} bytes per trade"));
        }

        var page = Stopwatch.StartNew();
        var filtered = await StoreKit.WithStoreAsync(scratch.Host.Services, s => s.PageTradesAsync(run.RunKey, new TradePageFilter(Side: Side.Buy, ExitReason: ExitReason.Stop), new PageRequest(1000), Ct).AsTask());
        page.Stop();
        Say($"size-test: one filtered page of {filtered.Value.Items.Count} rows {page.ElapsedMilliseconds} ms");
        var cursorPage = Stopwatch.StartNew();
        var later = await StoreKit.WithStoreAsync(scratch.Host.Services, s => s.PageTradesAsync(run.RunKey, new TradePageFilter(Side: Side.Buy, ExitReason: ExitReason.Stop), new PageRequest(1000, filtered.Value.NextCursor), Ct).AsTask());
        cursorPage.Stop();
        Say($"size-test: second page of {later.Value.Items.Count} rows by cursor {cursorPage.ElapsedMilliseconds} ms");

        var stream = Stopwatch.StartNew();
        var count = 0;
        await StoreKit.WithStoreAsync(
            scratch.Host.Services,
            async s =>
            {
                await foreach (var unused in s.StreamTradesAsync(run.RunKey, false, Ct))
                {
                    count++;
                }

                return 0;
            });
        stream.Stop();
        Say($"size-test: streamed {count} rows in {stream.ElapsedMilliseconds} ms, {count * 1000L / Math.Max(1, stream.ElapsedMilliseconds)} rows/s");

        count.ShouldBe(run.Trades.Count);
        write.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(60), "the transaction for 200,000 costed trades stays under a minute");
        page.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
        (count * 1000L / Math.Max(1, stream.ElapsedMilliseconds)).ShouldBeGreaterThan(10_000, "the stream reads at least 10,000 rows a second even on a loaded machine");
    }
}
