using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// Criteria 1 and 2 of P3-10: the migration <c>AddResearchResultStore</c> (and the later <c>AddSimTradeLegs</c> and <c>AddSimComparisons</c> of P4-05) applies on a database built up to the migration before it (a row of <c>platform.app_settings</c> is the same before and after), creates the two tables with the columns, types,
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
        (await ListAsync(connection, "SELECT indexname FROM pg_indexes WHERE schemaname = 'research' ORDER BY 1")).Order(StringComparer.Ordinal).ShouldBe(
        [
            "ix_cell_stats_as_of_created", "ix_cell_stats_cell_key", "ix_exit_selections_comparison_id", "ix_experiments_created_at", "ix_hypotheses_experiment_id", "ix_hypotheses_hypothesis_id_id", "ix_learning_changelog_subject_id", "ix_lessons_lesson_id_id", "ix_model_designations_model_id_id", "ix_model_versions_sha256", "ix_model_versions_training_set_id", "ix_sim_comparisons_entries_key", "ix_sim_runs_strategy_created", "ix_training_sets_created_at", "pk_cell_stats", "pk_digests", "pk_exit_selection_folds", "pk_exit_selections", "pk_experiments", "pk_hypotheses", "pk_learning_changelog", "pk_lessons", "pk_model_designations", "pk_model_versions", "pk_sim_comparisons", "pk_sim_runs", "pk_sim_trade_leg_charges", "pk_sim_trade_legs", "pk_sim_trades", "pk_trade_features", "pk_training_sets",
            "uq_cell_stats_snapshot_cell_window", "uq_digests_iso_week", "uq_exit_selections_selection_key", "uq_experiments_experiment_key", "uq_hypotheses_id_is_seed", "uq_learning_changelog_reverts_id", "uq_lessons_finding", "uq_lessons_id_is_seed", "uq_model_versions_version_key", "uq_sim_comparisons_comparison_key", "uq_sim_runs_run_key", "uq_sim_trades_run_trade_key", "uq_trade_features_run_trade_version", "uq_training_sets_set_id",
        ]);
        (await ListAsync(connection, "SELECT indexdef FROM pg_indexes WHERE indexname = 'ix_sim_runs_strategy_created'")).Single().ShouldContain("strategy_id, created_at DESC");
        (await ListAsync(connection, "SELECT indexdef FROM pg_indexes WHERE indexname = 'pk_sim_trades'")).Single().ShouldContain("(run_id, open_at_end, ordinal)");
        (await ListAsync(connection, "SELECT indexdef FROM pg_indexes WHERE indexname = 'uq_sim_trades_run_trade_key'")).Single().ShouldContain("UNIQUE");
        (await ListAsync(connection, "SELECT conname FROM pg_constraint WHERE connamespace = 'research'::regnamespace AND contype = 'c' ORDER BY 1")).Order(StringComparer.Ordinal).ShouldBe(
        [
            "ck_cell_stats_shape", "ck_digests_shape", "ck_exit_selection_folds_counts", "ck_exit_selections_keys", "ck_experiments_shape", "ck_hypotheses_links", "ck_hypotheses_shape", "ck_hypotheses_state", "ck_learning_changelog_evidence", "ck_learning_changelog_kind", "ck_learning_changelog_revert", "ck_learning_changelog_states", "ck_lessons_closed", "ck_lessons_links", "ck_lessons_result", "ck_lessons_shape", "ck_lessons_state", "ck_model_designations_role", "ck_model_versions_shape", "ck_sim_comparisons_counts", "ck_sim_comparisons_keys", "ck_sim_comparisons_members", "ck_sim_comparisons_unit",
            "ck_sim_runs_costs", "ck_sim_runs_counts", "ck_sim_runs_run_key", "ck_sim_runs_universe_kind",
            "ck_sim_trade_leg_charges_ordinals", "ck_sim_trade_leg_charges_quantity", "ck_sim_trade_legs_kind", "ck_sim_trade_legs_ordinals", "ck_sim_trade_legs_share",
            "ck_sim_trades_cost_segment", "ck_sim_trades_costing_all_or_nothing", "ck_sim_trades_costs", "ck_sim_trades_costs_charged", "ck_sim_trades_entry_fill",
            "ck_sim_trades_exit_reason", "ck_sim_trades_fills", "ck_sim_trades_ordinal", "ck_sim_trades_side", "ck_sim_trades_trigger_timeframe", "ck_trade_features_shape", "ck_training_sets_shape",
        ]);
        (await ListAsync(connection, "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = 'ck_sim_trades_exit_reason'")).Single().ShouldContain("'timestop'", Case.Sensitive, "AddTimeStopExitReason (P4-02) lets the store hold the reason of the time stop");
        (await ListAsync(connection, "SELECT conname || '|' || confdeltype::text FROM pg_constraint WHERE connamespace = 'research'::regnamespace AND contype = 'f'")).Order(StringComparer.Ordinal).ShouldBe(["fk_exit_selection_folds_selection|r", "fk_exit_selections_comparison|r", "fk_experiments_sim_runs_baseline_run_key|r", "fk_experiments_sim_runs_candidate_run_key|r", "fk_hypotheses_experiments_experiment_id|r", "fk_hypotheses_hypotheses_hypothesis_id|r", "fk_hypotheses_seed|r", "fk_hypotheses_sim_runs_baseline_run_key|r", "fk_hypotheses_sim_runs_candidate_run_key|r", "fk_learning_changelog_learning_changelog_reverts_id|r", "fk_lessons_experiments_experiment_id|r", "fk_lessons_hypotheses_hypothesis_ref|r", "fk_lessons_lessons_lesson_id|r", "fk_lessons_seed|r", "fk_model_designations_model_versions_model_id|r", "fk_model_versions_training_sets_training_set_id|r", "fk_sim_trade_leg_charges_trade|r", "fk_sim_trade_legs_trade|r", "fk_sim_trades_run|r"]);
        (await ListAsync(connection, "SELECT attidentity::text FROM pg_attribute WHERE attrelid = 'research.sim_runs'::regclass AND attname = 'id'")).ShouldBe(["a"]);
        (await ListAsync(connection, "SELECT count(*)::text FROM timescaledb_information.hypertables WHERE hypertable_schema = 'research'")).ShouldBe(["0"]);
        db.Database.HasPendingModelChanges().ShouldBeFalse("the model snapshot agrees with the model");
    }

    private const string BeforeLegs = "20261021175309_AddEmaExitReason";

    [Fact]
    public async Task The_P4_05_migrations_add_the_leg_and_comparison_tables_to_a_store_that_holds_a_run_and_Down_removes_only_them()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_research_legs_probe", Ct, migrate: false);
        using var scope = scratch.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeLegs, Ct);
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        await ExecuteAsync(connection, "INSERT INTO research.sim_runs (run_key, engine_version, record_schema_version, trade_schema_version, strategy_id, definition_sha256, from_date, to_date, universe_kind, universe_label, instruments, instruments_run, trades_in_window, candidates_in_window, trade_count, open_at_end_count, acknowledge_csv_adjusted, fills, record_json, record_sha256, trades_sha256, created_at, created_by) VALUES (repeat('a', 64), 'sim-6', 6, 5, 'ORB', repeat('b', 64), '2026-01-01', '2026-02-01', 'basket', 'probe', 1, 1, 0, 0, 0, 0, false, '{}', '{}', repeat('c', 64), repeat('d', 64), now(), 'probe')");
        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'research' ORDER BY 1")).ShouldBe(["sim_runs", "sim_trades"]);

        await migrator.MigrateAsync(null, Ct);

        string[] legColumns = [C("run_id", "bigint"), C("open_at_end", "boolean"), C("ordinal", "integer"), C("leg_ordinal", "integer"), C("epoch_seconds", "bigint"), C("price", "double precision"), C("filled_price", "double precision"), C("share", "numeric"), C("kind", "character varying", 16), C("reason", "character varying", 64)];
        string[] chargeColumns = [C("run_id", "bigint"), C("open_at_end", "boolean"), C("ordinal", "integer"), C("charge_ordinal", "integer"), C("epoch_seconds", "bigint"), C("quantity", "bigint"), C("turnover", "numeric"), .. Lines.Select(line => C(line, "numeric"))];
        string[] comparisonColumns =
        [
            C("id", "bigint"), C("comparison_key", "character", 64), C("entries_key", "character", 64), C("strategy_id", "character varying", 64), C("unit", "character varying", 8), C("common_entries", "integer"),
            C("member_run_keys", "ARRAY"), C("member_candidate_ids", "ARRAY"), C("member_gated_blocked", "ARRAY"), C("comparison_csv", "text"), C("diagnostics_csv", "text"), C("record_sha256", "character", 64),
            C("created_at", "timestamp with time zone"), C("created_by", "character varying", 100),
        ];
        (await ListAsync(connection, Columns, ("t", "sim_trade_legs"))).Order(StringComparer.Ordinal).ShouldBe(legColumns.Order(StringComparer.Ordinal));
        (await ListAsync(connection, Columns, ("t", "sim_trade_leg_charges"))).Order(StringComparer.Ordinal).ShouldBe(chargeColumns.Order(StringComparer.Ordinal));
        (await ListAsync(connection, Columns, ("t", "sim_comparisons"))).Order(StringComparer.Ordinal).ShouldBe(comparisonColumns.Order(StringComparer.Ordinal));
        var legsKey = (await ListAsync(connection, "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = 'fk_sim_trade_legs_trade'")).Single();
        legsKey.ShouldContain("(run_id, open_at_end, ordinal)");
        legsKey.ShouldContain("REFERENCES research.sim_trades");
        (await ListAsync(connection, "SELECT count(*)::text FROM research.sim_runs")).ShouldBe(["1"]);
        db.Database.HasPendingModelChanges().ShouldBeFalse("the model snapshot agrees with the model");

        await migrator.MigrateAsync(BeforeLegs, Ct);

        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'research' ORDER BY 1")).ShouldBe(["sim_runs", "sim_trades"]);
        (await ListAsync(connection, "SELECT count(*)::text FROM research.sim_runs")).ShouldBe(["1"], "Down removes the tables of P4-05 and nothing else");

        await migrator.MigrateAsync(null, Ct);

        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'research' ORDER BY 1")).Order(StringComparer.Ordinal).ShouldBe(["cell_stats", "digests", "exit_selection_folds", "exit_selections", "experiments", "hypotheses", "learning_changelog", "lessons", "model_designations", "model_versions", "sim_comparisons", "sim_runs", "sim_trade_leg_charges", "sim_trade_legs", "sim_trades", "trade_features", "training_sets"]);
    }

    private const string BeforeExitSelections = "20261022055710_AddSimComparisons";

    [Fact]
    public async Task The_P4_06_migration_adds_the_exit_choice_tables_to_a_store_that_holds_a_comparison_and_Down_removes_only_them()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_research_selection_probe", Ct, migrate: false);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeExitSelections, Ct);
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        await ExecuteAsync(connection, "INSERT INTO research.sim_comparisons (comparison_key, entries_key, strategy_id, unit, common_entries, member_run_keys, member_candidate_ids, member_gated_blocked, comparison_csv, diagnostics_csv, record_sha256, created_at, created_by) VALUES (repeat('a', 64), repeat('b', 64), 'ORB', 'RFilled', 0, ARRAY[repeat('c', 64), repeat('d', 64)], ARRAY['x', 'y'], ARRAY[0, 0], 'c', 'd', repeat('e', 64), now(), 'probe')");
        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'research' ORDER BY 1")).Order(StringComparer.Ordinal).ShouldBe(["sim_comparisons", "sim_runs", "sim_trade_leg_charges", "sim_trade_legs", "sim_trades"]);

        await migrator.MigrateAsync(null, Ct);

        string[] selectionColumns =
        [
            C("id", "bigint"), C("selection_key", "character", 64), C("comparison_id", "bigint"), C("strategy_id", "character varying", 64), C("timeframe", "character varying", 8), C("side", "character varying", 8),
            C("style", "character varying", 16), C("regime", "character varying", 32), C("chosen_candidate_id", "character varying", 200, true), C("stable", "boolean", null, true), C("record_json", "text"), C("record_sha256", "character", 64),
            C("created_at", "timestamp with time zone"), C("created_by", "character varying", 100),
        ];
        string[] foldColumns =
        [
            C("selection_id", "bigint"), C("fold_index", "integer"), C("candidate_id", "character varying", 200), C("test_start", "date"), C("test_end", "date"), C("qualified", "boolean"), C("train_trades", "integer"),
            C("train_score", "double precision", null, true), C("test_trades", "integer"), C("test_score", "double precision", null, true), C("is_winner", "boolean"),
        ];
        (await ListAsync(connection, Columns, ("t", "exit_selections"))).Order(StringComparer.Ordinal).ShouldBe(selectionColumns.Order(StringComparer.Ordinal));
        (await ListAsync(connection, Columns, ("t", "exit_selection_folds"))).Order(StringComparer.Ordinal).ShouldBe(foldColumns.Order(StringComparer.Ordinal));

        await migrator.MigrateAsync(BeforeExitSelections, Ct);

        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'research' ORDER BY 1")).Order(StringComparer.Ordinal).ShouldBe(["sim_comparisons", "sim_runs", "sim_trade_leg_charges", "sim_trade_legs", "sim_trades"]);
        (await ListAsync(connection, "SELECT count(*)::text FROM research.sim_comparisons")).ShouldBe(["1"], "Down removes the tables of P4-06 and nothing else");
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

        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'research' ORDER BY 1")).Order(StringComparer.Ordinal).ShouldBe(["cell_stats", "digests", "exit_selection_folds", "exit_selections", "experiments", "hypotheses", "learning_changelog", "lessons", "model_designations", "model_versions", "sim_comparisons", "sim_runs", "sim_trade_leg_charges", "sim_trade_legs", "sim_trades", "trade_features", "training_sets"]);
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
        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'research' ORDER BY 1")).Order(StringComparer.Ordinal).ShouldBe(["cell_stats", "digests", "exit_selection_folds", "exit_selections", "experiments", "hypotheses", "learning_changelog", "lessons", "model_designations", "model_versions", "sim_comparisons", "sim_runs", "sim_trade_leg_charges", "sim_trade_legs", "sim_trades", "trade_features", "training_sets"]);
    }
}
