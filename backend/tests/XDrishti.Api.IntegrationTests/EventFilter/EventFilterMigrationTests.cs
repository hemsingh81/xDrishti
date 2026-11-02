using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.EventFilter;

/// <summary>P15-06 contract: <c>AddEventFilterDecisions</c> creates <c>filings.event_filter_decisions</c> with its checks and its trigger, agrees with the model, and <c>Down</c> drops the table and the function and leaves the rest of <c>filings</c> alone.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class EventFilterMigrationTests(ApiFactory factory)
{
    private const string Before = "20261102060000_AddFilingsStore";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<List<string>> ListAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var rows = new List<string>();
        while (await reader.ReadAsync(Ct))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    [Fact]
    public async Task Up_creates_the_table_its_checks_the_trigger_and_the_pinned_function_and_agrees_with_the_model()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_event_filter_up_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        (await ListAsync(connection, "SELECT column_name || '|' || data_type || '|' || is_nullable FROM information_schema.columns WHERE table_schema = 'filings' AND table_name = 'event_filter_decisions' ORDER BY ordinal_position")).ShouldBe(
        [
            "id|bigint|NO", "enabled|boolean|NO", "results_within_days|smallint|NO", "approval|character varying|NO", "decided_by|character varying|NO", "actor|character varying|NO", "reason|character varying|NO",
            "setting_snapshot|jsonb|NO", "verdict_ref|character varying|YES", "changed_at|timestamp with time zone|NO",
        ]);
        (await ListAsync(connection, "SELECT conname FROM pg_constraint WHERE conrelid = 'filings.event_filter_decisions'::regclass AND contype <> 'n' ORDER BY 1")).ShouldBe(
        [
            "ck_event_filter_decisions_approval", "ck_event_filter_decisions_auto", "ck_event_filter_decisions_days", "ck_event_filter_decisions_decided_by", "ck_event_filter_decisions_owner", "ck_event_filter_decisions_reason", "pk_event_filter_decisions",
        ]);
        (await ListAsync(connection, "SELECT tgname FROM pg_trigger WHERE tgrelid = 'filings.event_filter_decisions'::regclass AND NOT tgisinternal")).ShouldBe(["trg_event_filter_decisions_check"]);
        (await ListAsync(connection, "SELECT proconfig::text || '|' || prosecdef::text FROM pg_proc WHERE oid = 'filings.check_event_filter_decision()'::regprocedure")).ShouldBe(["{\"search_path=pg_catalog, pg_temp\"}|false"]);
        scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().Database.HasPendingModelChanges().ShouldBeFalse("the migration and the model snapshot agree");
    }

    [Fact]
    public async Task Down_drops_the_table_and_the_function_keeps_the_rest_of_the_schema_and_up_again_is_clean()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_event_filter_down_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        await migrator.MigrateAsync(Before, Ct);

        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'filings' ORDER BY 1")).ShouldBe(["announcements", "calendar", "filings", "result_facts", "shareholding"]);
        (await ListAsync(connection, "SELECT proname FROM pg_proc WHERE pronamespace = 'filings'::regnamespace AND proname = 'check_event_filter_decision'")).ShouldBeEmpty();

        await migrator.MigrateAsync(null, Ct);

        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'filings' ORDER BY 1")).ShouldBe(["announcements", "calendar", "event_filter_decisions", "filings", "result_facts", "shareholding"]);
    }
}
