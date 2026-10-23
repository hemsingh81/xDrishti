using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;

namespace XDrishti.Api.IntegrationTests.Planning;

/// <summary>
/// Spec P7-03 (ADR 0083): what the runtime role <c>xd_app</c> may do to the plan record. Runs on the dedicated throw-away server of <see cref="ResearchGrantsTests"/>, where the role has the default privileges of the init script and the chain is migrated by the owner.
/// Every table of <c>trading</c> and <c>workflow</c> is insert-only: a state change is a new row, a decision a new approval, nothing is edited or removed.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class PlanGrantsTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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
    public async Task On_every_plan_table_the_runtime_role_reads_and_inserts_and_can_neither_change_nor_delete()
    {
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);
        await using var tables = new NpgsqlCommand("SELECT schemaname || '.' || tablename FROM pg_tables WHERE schemaname IN ('trading', 'workflow') ORDER BY 1", connection);
        var names = new List<string>();
        await using (var reader = await tables.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                names.Add(reader.GetString(0));
            }
        }

        names.ShouldBe(["trading.plan_events", "trading.plan_item_allocations", "trading.plan_items", "trading.plan_runs", "trading.plans", "workflow.approvals"]);
        foreach (var name in names)
        {
            foreach (var (privilege, expected) in new[] { ("SELECT", true), ("INSERT", true), ("UPDATE", false), ("DELETE", false), ("TRUNCATE", false) })
            {
                await using var check = new NpgsqlCommand("SELECT has_table_privilege('xd_app', @t, @p)", connection);
                check.Parameters.AddWithValue("t", name);
                check.Parameters.AddWithValue("p", privilege);
                ((bool)(await check.ExecuteScalarAsync(Ct))!).ShouldBe(expected, $"{privilege} on {name}");
            }
        }
    }

    [Fact]
    public async Task As_xd_app_inserting_works_and_updating_deleting_and_truncating_fail_with_insufficient_privilege()
    {
        const string plan = "INSERT INTO trading.plans (plan_key, for_date, version, input_hash, basis, provisional, notice, limits, created_by, created_at) VALUES ('grants-probe', '2026-10-13', 1, repeat('a', 64), 'b', true, 'n', 'l', 'probe', now())";

        (await SqlStateAsync(server.App, plan)).ShouldBeNull();
        (await SqlStateAsync(server.App, "INSERT INTO workflow.approvals (plan_id, kind, decision, actor, reason, decided_at) VALUES (1, 'owner', 'approve', 'a', 'r', now())")).ShouldNotBe(PostgresErrorCodes.InsufficientPrivilege, "the privilege to insert an approval is there");
        foreach (var statement in new[]
        {
            "UPDATE trading.plans SET basis = 'x'",
            "DELETE FROM trading.plans",
            "TRUNCATE trading.plans",
            "UPDATE trading.plan_items SET symbol = 'x' WHERE false",
            "DELETE FROM trading.plan_items WHERE false",
            "TRUNCATE trading.plan_items",
            "UPDATE trading.plan_item_allocations SET quantity = 1 WHERE false",
            "DELETE FROM trading.plan_item_allocations WHERE false",
            "UPDATE trading.plan_events SET to_state = 'approved' WHERE false",
            "DELETE FROM trading.plan_events WHERE false",
            "TRUNCATE trading.plan_events",
            "UPDATE trading.plan_runs SET reason = 'x' WHERE false",
            "DELETE FROM trading.plan_runs WHERE false",
            "UPDATE workflow.approvals SET decision = 'approve' WHERE false",
            "DELETE FROM workflow.approvals WHERE false",
            "TRUNCATE workflow.approvals",
        })
        {
            (await SqlStateAsync(server.App, statement)).ShouldBe(PostgresErrorCodes.InsufficientPrivilege, statement);
        }

        (await StoreKit.CountAsync(server.App, "SELECT count(*) FROM trading.plans", Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task As_xd_app_creating_or_altering_a_plan_object_fails_with_insufficient_privilege()
    {
        foreach (var statement in new[] { "CREATE TABLE trading.x (i integer)", "ALTER TABLE trading.plans ADD COLUMN x integer", "DROP TABLE workflow.approvals", "CREATE TABLE workflow.x (i integer)" })
        {
            (await SqlStateAsync(server.App, statement)).ShouldBe(PostgresErrorCodes.InsufficientPrivilege, statement);
        }
    }
}
