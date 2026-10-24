using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// The stored world of the report tests (P8-06), worked by hand. Strategy <c>RPTA</c> (intraday) ran over instruments RPT1 (baskets 1) and RPT2 (baskets 1 and 2), strategy <c>RPTB</c> (swing) over RPT3 (basket 3); all trades are BUY on the 15m timeframe, +1 R wins
/// (+1.5 R for RPT3) and -1 R losses, wins first on consecutive days. RPT1: 20 wins, 12 losses (total +8). RPT2: 12 wins, 18 losses (total -6). RPT3: 16 wins, 15 losses (total +9). So RPTA has 62 trades and +2 R, RPTB 31 trades and +9 R, all 93 trades +11 R, basket 1 has
/// 62 trades and +2 R, basket 2 30 trades and -6 R, basket 3 31 trades and +9 R (the baskets sum to 123 trades, since RPT2 is in two). A third run of RPTA over the same universe with an overlapping window exists to be refused.
/// </summary>
internal static class ReportFixture
{
    public static readonly Guid Basket1 = StoredRunFixtures.BasketOne;

    public static readonly Guid Basket2 = StoredRunFixtures.BasketTwo;

    public static readonly Guid Basket3 = new("aaaaaaaa-0000-0000-0000-000000000003");

    public static readonly Guid Rpt1 = new("00000000-0000-0000-0000-0000000000a1");

    public static readonly Guid Rpt2 = new("00000000-0000-0000-0000-0000000000a2");

    public static readonly Guid Rpt3 = new("00000000-0000-0000-0000-0000000000a3");

    /// <summary>Later than any run another test stores, so the runs here are the newest of their strategy and universe and the default set takes them.</summary>
    public static readonly DateTimeOffset At = StoreKit.At.AddDays(30);

    public static SimulationRun RunA { get; } = Build("RPTA", [(Rpt1, "NSE:RPT1", [Basket1], 20, 12, 1.0), (Rpt2, "NSE:RPT2", [Basket1, Basket2], 12, 18, 1.0)], new DateOnly(2026, 4, 30));

    public static SimulationRun RunB { get; } = Build("RPTB", [(Rpt3, "NSE:RPT3", [Basket3], 16, 15, 1.5)], new DateOnly(2026, 4, 30));

    /// <summary>The same strategy and universe as <see cref="RunA"/>, a window that overlaps it (it ends a day earlier), stored earlier: a second run of the same population.</summary>
    public static SimulationRun RunADuplicate { get; } = Build("RPTA", [(Rpt1, "NSE:RPT1", [Basket1], 6, 4, 1.0), (Rpt2, "NSE:RPT2", [Basket1, Basket2], 3, 3, 1.0)], new DateOnly(2026, 4, 29));

    private static SimulationRun Build(string strategy, (Guid Id, string Symbol, Guid[] Baskets, int Wins, int Losses, double Win)[] instruments, DateOnly to)
    {
        var ran = new List<InstrumentRun>();
        var trades = new List<SimTrade>();
        foreach (var (id, symbol, _, wins, losses, win) in instruments)
        {
            for (var j = 0; j < wins + losses; j++)
            {
                var isWin = j < wins;
                var r = isWin ? win : -1.0;
                trades.Add(FabricatedRun.Trade(id, symbol, Side.Buy, FabricatedRun.Monday.AddDays(j), isWin ? ExitReason.Target : ExitReason.Stop, entryMinute: 600, target: isWin ? 105.5 : null, rExact: r + 0.05, rFilled: r, strategy: strategy));
            }

            ran.Add(FabricatedRun.Ran(id, symbol, new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 30)) with { TradesInWindow = wins + losses, CandidatesInWindow = wins + losses });
        }

        var run = FabricatedRun.Assemble(FabricatedRun.List([.. instruments.Select(i => (i.Id, i.Symbol))]), ran, trades, [], to: to, strategy: strategy);
        var baskets = instruments.ToDictionary(i => i.Id, i => (IReadOnlyList<Guid>)i.Baskets);
        return run with { Trades = [.. run.Trades.Select(t => t with { BasketIds = baskets[t.Trade.InstrumentId] })] };
    }

    /// <summary>Stores the three runs, the styles of the two strategies and a comparison that gives the runs their exit labels (RPTA FIXED_RR{rr=2}, RPTB FIXED_RR{rr=3}). Idempotent: a second call writes nothing.</summary>
    public static async Task SeedAsync(ApiFactory factory, CancellationToken cancellationToken)
    {
        await using (var connection = new NpgsqlConnection(factory.AdminConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            foreach (var (id, style) in new[] { ("RPTA", "intraday"), ("RPTB", "swing") })
            {
                await using var command = new NpgsqlCommand("INSERT INTO lab.strategies (id, style, built_in, created_at) VALUES (@id, @style, false, now()) ON CONFLICT DO NOTHING", connection);
                command.Parameters.AddWithValue("id", id);
                command.Parameters.AddWithValue("style", style);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IRunStore>();
        foreach (var (run, at) in new[] { (RunADuplicate, At.AddDays(-1)), (RunA, At), (RunB, At) })
        {
            (await store.StoreAsync(StoreKit.ToStore(run, at), cancellationToken)).IsSuccess.ShouldBeTrue();
        }

        await using var admin = new NpgsqlConnection(factory.AdminConnectionString);
        await admin.OpenAsync(cancellationToken);

        // two comparisons give the labels: A and B are rr=2 and rr=3; the duplicate of A is rr=2 as well, so it carries the very labels of A (the same population twice) and B is rr=3 in both
        foreach (var (name, first) in new[] { ("report-fixture-comparison", RunA), ("report-fixture-comparison-duplicate", RunADuplicate) })
        {
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO research.sim_comparisons (comparison_key, entries_key, strategy_id, unit, common_entries, member_run_keys, member_candidate_ids, member_gated_blocked, comparison_csv, diagnostics_csv, record_sha256, created_at, created_by)
                VALUES (@key, @entries, 'RPTA', 'RFilled', 1, @runs, @candidates, @gated, 'a', 'b', @sha, now(), 'test')
                ON CONFLICT (comparison_key) DO NOTHING
                """,
                admin);
            insert.Parameters.AddWithValue("key", NpgsqlDbType.Char, StoreKit.Sha256(name));
            insert.Parameters.AddWithValue("entries", NpgsqlDbType.Char, StoreKit.Sha256(name + "-entries"));
            insert.Parameters.AddWithValue("runs", NpgsqlDbType.Array | NpgsqlDbType.Char, (string[])[first.RunKey, RunB.RunKey]);
            insert.Parameters.AddWithValue("candidates", NpgsqlDbType.Array | NpgsqlDbType.Varchar, (string[])["FIXED_RR{rr=2}", "FIXED_RR{rr=3}"]);
            insert.Parameters.AddWithValue("gated", NpgsqlDbType.Array | NpgsqlDbType.Bigint, (long[])[0, 0]);
            insert.Parameters.AddWithValue("sha", NpgsqlDbType.Char, StoreKit.Sha256(name + "-record"));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
