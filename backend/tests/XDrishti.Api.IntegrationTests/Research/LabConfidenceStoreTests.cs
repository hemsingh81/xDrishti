using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Lab;
using XDrishti.Domain.Lab;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// CF-06 on a real database as the runtime role (throw-away server, <c>xd_app</c> with the default privileges of the init script): the four new tables are insert-only (same standing check as <c>ResearchGrantsTests</c>), the registration is written once, the plan freeze
/// counts re-plans, the count of looks adds up from the ledger columns, the block and the unlock round-trip their jsonb, and two fresh looks at one block cannot both be stored (the partial unique index).
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class LabConfidenceStoreTests(ApiFactory factory, ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private static readonly string[] Tables = ["registrations", "plan_freezes", "holdout_blocks", "holdout_unlocks"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> Host() => factory.WithWebHostBuilder(b => b.UseSetting("Database:ConnectionString", server.App));

    private static async Task<T> WithAsync<TService, T>(WebApplicationFactory<Program> host, Func<TService, Task<T>> action)
        where TService : notnull
    {
        using var scope = host.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
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

    private static HoldOutResultView Result(string strategy, int version, bool reused) =>
        new(strategy, version, "strategy", "owner", reused ? "reused" : "used", new DateTimeOffset(2026, 11, 4, 6, 0, 0, TimeSpan.Zero), new DateOnly(2026, 5, 1), new DateOnly(2026, 10, 30), reused, false, !reused, reused ? "pass" : "pass", "ok",
            0.2, 0.12, 0.6, 45, new string('f', 64), new string('a', 64), "owner:x", reused ? ["note"] : []);

    private static StoredUnlock Unlock(string strategy, int version, bool reused) =>
        new(strategy, version, "strategy", "owner", "owner:x", new DateTimeOffset(2026, 11, 4, 6, 0, 0, TimeSpan.Zero), new string('a', 64), reused, new DateOnly(2026, 5, 1), Guid.NewGuid(), Result(strategy, version, reused));

    [Fact]
    public async Task The_runtime_role_reads_and_inserts_the_four_new_tables_and_can_neither_change_delete_nor_truncate_them()
    {
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);
        foreach (var name in Tables)
        {
            foreach (var (privilege, expected) in new[] { ("SELECT", true), ("INSERT", true), ("UPDATE", false), ("DELETE", false), ("TRUNCATE", false) })
            {
                await using var check = new NpgsqlCommand("SELECT has_table_privilege('xd_app', @t, @p)", connection);
                check.Parameters.AddWithValue("t", "lab." + name);
                check.Parameters.AddWithValue("p", privilege);
                ((bool)(await check.ExecuteScalarAsync(Ct))!).ShouldBe(expected, $"{privilege} on lab.{name}");
            }
        }

        (await SqlStateAsync(server.App, "UPDATE lab.registrations SET actor = 'x'")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        (await SqlStateAsync(server.App, "DELETE FROM lab.plan_freezes")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        (await SqlStateAsync(server.App, "UPDATE lab.holdout_blocks SET length_months = 1")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        (await SqlStateAsync(server.App, "DELETE FROM lab.holdout_unlocks")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task A_registration_is_written_once_and_read_back_with_its_text_hash()
    {
        await using var host = Host();
        var stored = new StoredRegistration("REGSTORE", 1, Registration.Create("who loses", "signs", "tiers", "edge", "falsifier").Value, new string('b', 64), DateTimeOffset.UtcNow, "owner:x");

        var first = await WithAsync<IRegistrationStore, bool>(host, s => s.InsertAsync(stored, Ct).AsTask());
        var second = await WithAsync<IRegistrationStore, bool>(host, s => s.InsertAsync(stored, Ct).AsTask());
        var read = await WithAsync<IRegistrationStore, StoredRegistration?>(host, s => s.GetAsync("REGSTORE", 1, Ct).AsTask());

        (first, second).ShouldBe((true, false));
        read.ShouldNotBeNull().TextHash.ShouldBe(new string('b', 64));
        read.Registration.Counterparty.ShouldBe("who loses");
    }

    [Fact]
    public async Task A_plan_freeze_counts_the_other_plans_the_version_had_and_the_first_one_is_remembered()
    {
        await using var host = Host();
        var run = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var first = await WithAsync<IPlanFreezeStore, PlanFreezeOutcome>(host, s => s.FreezeAsync("FRZ", 1, new string('1', 64), run, now, Ct).AsTask());
        var same = await WithAsync<IPlanFreezeStore, PlanFreezeOutcome>(host, s => s.FreezeAsync("FRZ", 1, new string('1', 64), run, now.AddMinutes(1), Ct).AsTask());
        var replan = await WithAsync<IPlanFreezeStore, PlanFreezeOutcome>(host, s => s.FreezeAsync("FRZ", 1, new string('2', 64), Guid.NewGuid(), now.AddMinutes(2), Ct).AsTask());
        var firstHash = await WithAsync<IPlanFreezeStore, string?>(host, s => s.FirstAsync("FRZ", 1, Ct).AsTask());
        var none = await WithAsync<IPlanFreezeStore, string?>(host, s => s.FirstAsync("FRZ", 2, Ct).AsTask());

        first.ShouldBe(new PlanFreezeOutcome(true, 0));
        same.ShouldBe(new PlanFreezeOutcome(false, 0));
        replan.ShouldBe(new PlanFreezeOutcome(true, 1));
        firstHash.ShouldBe(new string('1', 64));
        none.ShouldBeNull();
    }

    [Fact]
    public async Task The_count_of_looks_adds_up_hashes_sweep_points_extra_exits_re_plans_and_nulls_from_the_ledger()
    {
        await using var host = Host();
        var t = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        LedgerRow Row(char hash, string method, string kind = "run", int extra = 0) =>
            new(Guid.NewGuid(), new string('k', 64), "CNT", 1, new string(hash, 64), null, "symbols as listed", new DateOnly(2025, 5, 1), new DateOnly(2026, 4, 30), method, t, kind, extra);
        foreach (var row in new[] { Row('a', "full", extra: 3), Row('a', "robust", "replan"), Row('b', "quick"), Row('c', "sweep"), Row('d', "sweep"), Row('b', "sweep"), Row('a', "null", "null") })
        {
            (await WithAsync<ITestLedger, bool>(host, l => l.AppendAsync(row, Ct).AsTask())).ShouldBeTrue();
        }

        var parts = await WithAsync<ITestLedger, TrialCounts>(host, l => l.CountTrialsAsync("CNT", Ct).AsTask());

        parts.ShouldBe(new TrialCounts(2, 2, 3, 1, 1));
        parts.DistinctHashes.ShouldBe(await WithAsync<ITestLedger, int>(host, l => l.CountVariantsAsync("CNT", Ct).AsTask()), "the old count is unchanged");
        parts.Total.ShouldBe(9);
    }

    [Fact]
    public async Task The_block_and_the_unlock_round_trip_and_only_one_fresh_look_at_a_block_is_stored()
    {
        await using var host = Host();
        var block = new HoldOutBlock(new DateOnly(2026, 5, 1), new DateOnly(2024, 10, 1), new DateOnly(2026, 10, 30), 6, new string('d', 64), new DateTimeOffset(2026, 11, 4, 6, 0, 0, TimeSpan.Zero));

        var inserted = await WithAsync<IHoldOutStore, bool>(host, s => s.InsertBlockAsync(block, Ct).AsTask());
        var again = await WithAsync<IHoldOutStore, bool>(host, s => s.InsertBlockAsync(block, Ct).AsTask());
        var current = await WithAsync<IHoldOutStore, HoldOutBlock?>(host, s => s.CurrentAsync(Ct).AsTask());
        var fresh = Unlock("HO", 1, false);
        var stored = await WithAsync<IHoldOutStore, bool>(host, s => s.InsertUnlockAsync(fresh, Ct).AsTask());
        var secondFresh = await WithAsync<IHoldOutStore, bool>(host, s => s.InsertUnlockAsync(Unlock("HO", 2, false), Ct).AsTask());
        var reused = await WithAsync<IHoldOutStore, bool>(host, s => s.InsertUnlockAsync(Unlock("HO", 2, true), Ct).AsTask());
        var sameVersion = await WithAsync<IHoldOutStore, bool>(host, s => s.InsertUnlockAsync(Unlock("HO", 1, true), Ct).AsTask());
        var read = await WithAsync<IHoldOutStore, StoredUnlock?>(host, s => s.GetUnlockAsync("HO", 1, "strategy", Ct).AsTask());
        var any = await WithAsync<IHoldOutStore, bool>(host, s => s.AnyUnlockAsync(block.Start, Ct).AsTask());
        var list = await WithAsync<IHoldOutStore, IReadOnlyList<StoredUnlock>>(host, s => s.ListUnlocksAsync(10, Ct).AsTask());

        (inserted, again).ShouldBe((true, false));
        current.ShouldBe(block);
        (stored, secondFresh, reused, sameVersion).ShouldBe((true, false, true, false), "the database decides what is fresh");
        read.ShouldNotBeNull().Result.ShouldBe(fresh.Result with { Notes = read.Result.Notes }, "the jsonb result comes back as it went in");
        read.Result.Notes.ShouldBe(fresh.Result.Notes);
        any.ShouldBeTrue();
        list.Count.ShouldBe(2);
    }
}
