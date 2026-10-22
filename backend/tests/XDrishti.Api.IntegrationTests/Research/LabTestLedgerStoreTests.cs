using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Lab;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// The test ledger as the runtime role (spec P5-03, criteria 1 to 3; ADR 0059) on a throw-away server where <c>xd_app</c> exists with the default privileges of the init script: insert and select, never update, delete or truncate; the append is
/// idempotent by run id; the variant count is the number of distinct hashes.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class LabTestLedgerStoreTests(ApiFactory factory, ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> Host() => factory.WithWebHostBuilder(b => b.UseSetting("Database:ConnectionString", server.App));

    private static LedgerRow Row(string family, char hash, string universe = "symbols as listed", Guid? run = null, DateTimeOffset? at = null) =>
        new(run ?? Guid.NewGuid(), new string('k', 64), family, 1, new string(hash, 64), null, universe, new DateOnly(2025, 5, 1), new DateOnly(2026, 4, 30), "quick", at ?? DateTimeOffset.UtcNow);

    private static async Task<T> WithLedgerAsync<T>(WebApplicationFactory<Program> host, Func<ITestLedger, Task<T>> action)
    {
        using var scope = host.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ITestLedger>());
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
        await StoreKit.ExecuteAsync(server.Owner, "DELETE FROM lab.test_ledger", Ct);
        await using var host = Host();
        var row = Row("GRANTS", 'a');
        (await WithLedgerAsync(host, l => l.AppendAsync(row, Ct).AsTask())).ShouldBeTrue();

        (await SqlStateAsync(server.App, "UPDATE lab.test_ledger SET method = 'x'")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        (await SqlStateAsync(server.App, "DELETE FROM lab.test_ledger")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        (await SqlStateAsync(server.App, "TRUNCATE lab.test_ledger")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        (await StoreKit.CountAsync(server.App, "SELECT count(*) FROM lab.test_ledger", Ct)).ShouldBe(1);
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);
        foreach (var (privilege, expected) in new[] { ("SELECT", true), ("INSERT", true), ("UPDATE", false), ("DELETE", false), ("TRUNCATE", false) })
        {
            await using var command = new NpgsqlCommand($"SELECT has_table_privilege('xd_app', 'lab.test_ledger', '{privilege}')", connection);
            ((bool)(await command.ExecuteScalarAsync(Ct))!).ShouldBe(expected, privilege);
        }
    }

    [Fact]
    public async Task The_same_run_id_twice_leaves_one_row_and_the_variant_count_is_the_distinct_hashes_of_the_family()
    {
        await StoreKit.ExecuteAsync(server.Owner, "DELETE FROM lab.test_ledger", Ct);
        await using var host = Host();
        var run = Guid.NewGuid();
        var t = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

        var first = await WithLedgerAsync(host, l => l.AppendAsync(Row("FAM", 'a', run: run, at: t), Ct).AsTask());
        var second = await WithLedgerAsync(host, l => l.AppendAsync(Row("FAM", 'a', run: run, at: t), Ct).AsTask());
        await WithLedgerAsync(host, l => l.AppendAsync(Row("FAM", 'a', "basket members at the time of the post", at: t.AddHours(1)), Ct).AsTask());
        await WithLedgerAsync(host, l => l.AppendAsync(Row("FAM", 'b', at: t.AddHours(2)), Ct).AsTask());
        await WithLedgerAsync(host, l => l.AppendAsync(Row("OTHER", 'c', at: t.AddHours(3)), Ct).AsTask());

        first.ShouldBeTrue();
        second.ShouldBeFalse();
        (await WithLedgerAsync(host, l => l.CountVariantsAsync("FAM", Ct).AsTask())).ShouldBe(2, "the same hash on two universes is one variant");
        (await WithLedgerAsync(host, l => l.CountVariantsAsync("NONE", Ct).AsTask())).ShouldBe(0);
        var rows = await WithLedgerAsync(host, l => l.ListAsync("FAM", Ct).AsTask());
        rows.Count.ShouldBe(3, "every run is a row");
        rows[0].StrategyHash.ShouldBe(new string('b', 64), "newest first");
        rows.Single(r => r.RunId == run).Universe.ShouldBe("symbols as listed");
    }
}
