using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// Criteria 1 and 2 of P3-10: the migration <c>AddResearchResultStore</c> applies on a database built up to the migration before it (a row of <c>platform.app_settings</c> is the same before and after), creates the two tables with the columns, types,
/// checks, keys and indexes of D2 and D3, agrees with the model snapshot, and is reversible: <c>Down</c> removes both tables and the schema, and fails, removing nothing, when the schema holds another object.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ResearchStoreMigrationTests(ApiFactory factory)
{
    private const string Before = "20261018223826_WidenAppSettingValue";

    private const string Columns = "SELECT column_name || '|' || data_type || '|' || coalesce(character_maximum_length::text, '') || '|' || is_nullable FROM information_schema.columns WHERE table_schema = 'research' AND table_name = @t";

    private static readonly string[] Legs = ["entry", "exit"];

    private static readonly string[] Lines = ["brokerage", "stt", "exchange_txn", "sebi_fee", "stamp_duty", "other_charges", "dp_charge", "gst"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string C(string name, string type, int? length = null, bool nullable = false) => $"{name}|{type}|{length}|{(nullable ? "YES" : "NO")}";

    private static string[] RunColumns() =>
    [
        C("id", "bigint"), C("run_key", "character", 64), C("engine_version", "character varying", 32), C("record_schema_version", "smallint"), C("trade_schema_version", "smallint"),
        C("strategy_id", "character varying", 64), C("definition_sha256", "character", 64), C("from_date", "date"), C("to_date", "date"), C("universe_kind", "character varying", 16),
        C("universe_label", "character varying", 200), C("instruments", "integer"), C("instruments_run", "integer"), C("trades_in_window", "bigint"), C("candidates_in_window", "bigint"),
        C("trade_count", "integer"), C("open_at_end_count", "integer"), C("acknowledge_csv_adjusted", "boolean"), C("fills", "character varying", 2000),
        C("costs_sheet_id", "character varying", 64, true), C("costs_rates_sha256", "character", 64, true), C("record_json", "text"), C("record_sha256", "character", 64),
        C("trades_sha256", "character", 64), C("created_at", "timestamp with time zone"), C("created_by", "character varying", 100),
    ];

    private static string[] TradeColumns()
    {
        var columns = new List<string>
        {
            C("run_id", "bigint"), C("open_at_end", "boolean"), C("ordinal", "integer"), C("trade_key", "character varying", 160),
            C("strategy_id", "character varying", 64), C("instrument_id", "uuid"), C("symbol", "character varying", 40), C("side", "character varying", 16), C("trade_date", "date"),
            C("decision_epoch_seconds", "bigint"), C("trigger_timeframe", "character varying", 16), C("trigger_bar_epoch", "bigint"), C("trigger_bar_open", "double precision"),
            C("trigger_bar_high", "double precision"), C("trigger_bar_low", "double precision"), C("trigger_bar_close", "double precision"), C("trigger_bar_volume", "bigint"),
            C("trigger_bar_minutes", "integer"), C("trigger_bar_complete", "boolean"), C("entry_epoch_seconds", "bigint"), C("entry_price", "double precision"), C("stop_price", "double precision"),
            C("target_price", "double precision", null, true), C("exit_epoch_seconds", "bigint"), C("exit_price", "double precision"), C("exit_reason", "character varying", 16),
            C("r_exact", "double precision"), C("mae_r", "double precision"), C("mfe_r", "double precision"), C("fills", "character varying", 16), C("costs", "character varying", 16),
            C("entry_snapshot_json", "text", null, true), C("r_filled", "double precision"), C("slippage_r", "double precision"), C("entry_filled_price", "double precision"),
            C("exit_filled_price", "double precision"), C("entry_fill", "character varying", 16), C("tick_fallback", "boolean"), C("sessions_held", "integer"), C("sessions_unseen", "integer"),
            C("basket_ids", "ARRAY"), C("cost_segment", "character varying", 16, true), C("cost_quantity", "bigint", null, true), C("cost_risk_taken", "numeric", null, true),
            C("cost_entry_turnover", "numeric", null, true), C("cost_exit_turnover", "numeric", null, true),
        };
        foreach (var leg in Legs)
        {
            columns.AddRange(Lines.Select(line => C($"{leg}_{line}", "numeric", null, true)));
        }

        columns.AddRange([C("cost_total", "numeric", null, true), C("cost_r", "double precision", null, true), C("r_net", "double precision", null, true)]);
        return [.. columns];
    }

    private static async Task<List<string>> ListAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await using var reader = await command.ExecuteReaderAsync(Ct);
        var rows = new List<string>();
        while (await reader.ReadAsync(Ct))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }

    [Fact]
    public async Task The_migration_creates_the_two_tables_their_checks_keys_and_indexes_and_leaves_the_settings_rows_as_they_were()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_research_migration_probe", Ct, migrate: false);
        using var scope = scratch.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Before, Ct);
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        await ExecuteAsync(connection, "INSERT INTO platform.app_settings (key, value, updated_by, updated_at) VALUES ('portfolio.benchmark_instrument', '6f9619ff-8b86-d011-b42d-00c04fc964ff', 'owner@example.invalid', '2026-10-02T00:00:00Z')");
        var settings = await ListAsync(connection, "SELECT key || '|' || value || '|' || updated_by || '|' || updated_at::text FROM platform.app_settings ORDER BY key");
        (await ListAsync(connection, "SELECT schema_name FROM information_schema.schemata WHERE schema_name = 'research'")).ShouldBeEmpty();

        await migrator.MigrateAsync(null, Ct);

        (await ListAsync(connection, "SELECT key || '|' || value || '|' || updated_by || '|' || updated_at::text FROM platform.app_settings ORDER BY key")).ShouldBe(settings);
        (await ListAsync(connection, Columns, ("t", "sim_runs"))).Order(StringComparer.Ordinal).ShouldBe(RunColumns().Order(StringComparer.Ordinal));
        (await ListAsync(connection, Columns, ("t", "sim_trades"))).Order(StringComparer.Ordinal).ShouldBe(TradeColumns().Order(StringComparer.Ordinal));
        (await ListAsync(connection, "SELECT indexname FROM pg_indexes WHERE schemaname = 'research' ORDER BY 1")).ShouldBe(["ix_sim_runs_strategy_created", "pk_sim_runs", "pk_sim_trades", "uq_sim_runs_run_key", "uq_sim_trades_run_trade_key"]);
        (await ListAsync(connection, "SELECT indexdef FROM pg_indexes WHERE indexname = 'ix_sim_runs_strategy_created'")).Single().ShouldContain("strategy_id, created_at DESC");
        (await ListAsync(connection, "SELECT indexdef FROM pg_indexes WHERE indexname = 'pk_sim_trades'")).Single().ShouldContain("(run_id, open_at_end, ordinal)");
        (await ListAsync(connection, "SELECT indexdef FROM pg_indexes WHERE indexname = 'uq_sim_trades_run_trade_key'")).Single().ShouldContain("UNIQUE");
        (await ListAsync(connection, "SELECT conname FROM pg_constraint WHERE connamespace = 'research'::regnamespace AND contype = 'c' ORDER BY 1")).ShouldBe(
        [
            "ck_sim_runs_costs", "ck_sim_runs_counts", "ck_sim_runs_run_key", "ck_sim_runs_universe_kind",
            "ck_sim_trades_cost_segment", "ck_sim_trades_costing_all_or_nothing", "ck_sim_trades_costs", "ck_sim_trades_costs_charged", "ck_sim_trades_entry_fill",
            "ck_sim_trades_exit_reason", "ck_sim_trades_fills", "ck_sim_trades_ordinal", "ck_sim_trades_side", "ck_sim_trades_trigger_timeframe",
        ]);
        (await ListAsync(connection, "SELECT conname || '|' || confdeltype::text FROM pg_constraint WHERE connamespace = 'research'::regnamespace AND contype = 'f'")).ShouldBe(["fk_sim_trades_run|r"]);
        (await ListAsync(connection, "SELECT attidentity::text FROM pg_attribute WHERE attrelid = 'research.sim_runs'::regclass AND attname = 'id'")).ShouldBe(["a"]);
        (await ListAsync(connection, "SELECT count(*)::text FROM timescaledb_information.hypertables WHERE hypertable_schema = 'research'")).ShouldBe(["0"]);
        db.Database.HasPendingModelChanges().ShouldBeFalse("the model snapshot agrees with the model");
    }

    [Fact]
    public async Task Down_removes_both_tables_and_the_schema_and_up_again_is_clean()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_research_down_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        await migrator.MigrateAsync(Before, Ct);

        (await ListAsync(connection, "SELECT schema_name FROM information_schema.schemata WHERE schema_name = 'research'")).ShouldBeEmpty();
        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'research'")).ShouldBeEmpty();

        await migrator.MigrateAsync(null, Ct);

        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'research' ORDER BY 1")).ShouldBe(["sim_runs", "sim_trades"]);
    }

    [Fact]
    public async Task Down_fails_and_removes_nothing_when_the_schema_holds_another_object()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_research_down_extra_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        await ExecuteAsync(connection, "CREATE TABLE research.later_table (x integer)");

        var failure = await Should.ThrowAsync<Exception>(() => migrator.MigrateAsync(Before, Ct));

        (failure as PostgresException ?? failure.InnerException as PostgresException).ShouldNotBeNull().SqlState.ShouldBe(PostgresErrorCodes.DependentObjectsStillExist);
        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'research' ORDER BY 1")).ShouldBe(["later_table", "sim_runs", "sim_trades"]);

        await ExecuteAsync(connection, "DROP TABLE research.later_table");
        await migrator.MigrateAsync(Before, Ct);
        (await ListAsync(connection, "SELECT schema_name FROM information_schema.schemata WHERE schema_name = 'research'")).ShouldBeEmpty();
        await migrator.MigrateAsync(null, Ct);
        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'research' ORDER BY 1")).ShouldBe(["sim_runs", "sim_trades"]);
    }
}
