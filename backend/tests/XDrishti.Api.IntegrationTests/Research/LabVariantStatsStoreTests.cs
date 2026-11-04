using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Lab;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// <c>lab.variant_stats</c> as the runtime role (CF-03, ADR 0205) on a throw-away server where <c>xd_app</c> exists with the default privileges of the init script: insert and select, never update, delete or truncate; the append is idempotent by run id and variant hash;
/// a figure that is not computable is null and a stored figure is finite.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class LabVariantStatsStoreTests(ApiFactory factory, ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> Host() => factory.WithWebHostBuilder(b => b.UseSetting("Database:ConnectionString", server.App));

    private static VariantStatsRow Row(string family, char hash, Guid? run = null, double? sr = 0.12, DateTimeOffset? at = null, string method = "sweep") =>
        new(family, run ?? Guid.NewGuid(), new string(hash, 64), 40, 0.1, 0.9, -0.2, 3.4, sr, method, at ?? new DateTimeOffset(2026, 11, 4, 6, 0, 0, TimeSpan.Zero));

    private static async Task<T> WithStoreAsync<T>(WebApplicationFactory<Program> host, Func<IVariantStatsStore, Task<T>> action)
    {
        using var scope = host.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IVariantStatsStore>());
    }

    private static async Task<string?> SqlStateAsync(string connectionString, string sql)
    {
        try
        {
            await StoreKit.ExecuteAsync(connectionString, sql, Ct);
            return null;
        }
        catch (PostgresException failure)
        {
            return failure.SqlState;
        }
    }

    [Fact]
    public async Task The_runtime_role_inserts_and_reads_and_can_never_update_delete_or_truncate()
    {
        await StoreKit.ExecuteAsync(server.Owner, "DELETE FROM lab.variant_stats", Ct);
        await using var host = Host();
        (await WithStoreAsync(host, s => s.AppendAsync([Row("GRANTS", 'a')], Ct).AsTask())).ShouldBe(1);

        (await SqlStateAsync(server.App, "UPDATE lab.variant_stats SET method = 'full'")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        (await SqlStateAsync(server.App, "DELETE FROM lab.variant_stats")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        (await SqlStateAsync(server.App, "TRUNCATE lab.variant_stats")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        (await StoreKit.CountAsync(server.App, "SELECT count(*) FROM lab.variant_stats", Ct)).ShouldBe(1);
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);
        foreach (var (privilege, expected) in new[] { ("SELECT", true), ("INSERT", true), ("UPDATE", false), ("DELETE", false), ("TRUNCATE", false) })
        {
            await using var command = new NpgsqlCommand($"SELECT has_table_privilege('xd_app', 'lab.variant_stats', '{privilege}')", connection);
            ((bool)(await command.ExecuteScalarAsync(Ct))!).ShouldBe(expected, privilege);
        }
    }

    [Fact]
    public async Task The_same_run_and_variant_twice_leaves_one_row_and_the_rows_read_back_newest_first_with_their_nulls()
    {
        await StoreKit.ExecuteAsync(server.Owner, "DELETE FROM lab.variant_stats", Ct);
        await using var host = Host();
        var run = Guid.NewGuid();
        var t = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

        var first = await WithStoreAsync(host, s => s.AppendAsync([Row("FAM", 'a', run, 0.12, t), Row("FAM", 'b', run, null, t.AddHours(1))], Ct).AsTask());
        var again = await WithStoreAsync(host, s => s.AppendAsync([Row("FAM", 'a', run, 0.99, t), Row("FAM", 'b', run, null, t.AddHours(1))], Ct).AsTask());
        await WithStoreAsync(host, s => s.AppendAsync([Row("OTHER", 'c')], Ct).AsTask());

        first.ShouldBe(2);
        again.ShouldBe(0, "a retry writes nothing");
        var rows = await WithStoreAsync(host, s => s.ListAsync("FAM", Ct).AsTask());
        rows.Count.ShouldBe(2);
        rows[0].VariantHash.ShouldBe(new string('a', 64), "one row per variant hash, in hash order");
        rows[0].SrPerTrade.ShouldBe(0.12, "the first write stands");
        rows[0].Skew.ShouldBe(-0.2);
        rows[0].CreatedAt.ShouldBe(t);
        rows[1].SrPerTrade.ShouldBeNull();
        (await WithStoreAsync(host, s => s.ListAsync("NONE", Ct).AsTask())).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("'NaN'")]
    [InlineData("'Infinity'")]
    [InlineData("'-Infinity'")]
    public async Task A_figure_that_is_not_finite_is_refused(string value)
    {
        var sql = $"INSERT INTO lab.variant_stats (strategy_id, run_id, variant_hash, n, sr_per_trade, method, created_at) VALUES ('X', gen_random_uuid(), '{new string('a', 64)}', 3, {value}::float8, 'sweep', now())";

        (await SqlStateAsync(server.Owner, sql)).ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task A_row_that_breaks_the_shape_is_refused()
    {
        var good = new string('a', 64);

        (await SqlStateAsync(server.Owner, $"INSERT INTO lab.variant_stats (strategy_id, run_id, variant_hash, n, method, created_at) VALUES ('X', gen_random_uuid(), '{good}', -1, 'sweep', now())")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(server.Owner, $"INSERT INTO lab.variant_stats (strategy_id, run_id, variant_hash, n, method, created_at) VALUES ('X', gen_random_uuid(), 'nothex', 1, 'sweep', now())")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(server.Owner, $"INSERT INTO lab.variant_stats (strategy_id, run_id, variant_hash, n, sd_r, method, created_at) VALUES ('X', gen_random_uuid(), '{good}', 1, -0.5, 'sweep', now())")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(server.Owner, $"INSERT INTO lab.variant_stats (strategy_id, run_id, variant_hash, n, method, created_at) VALUES ('X', gen_random_uuid(), '{good}', 1, 'guess', now())")).ShouldBe(PostgresErrorCodes.CheckViolation);
    }
}
