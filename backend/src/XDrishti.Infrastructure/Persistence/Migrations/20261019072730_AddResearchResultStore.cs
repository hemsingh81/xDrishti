using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddResearchResultStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "research");

            migrationBuilder.CreateTable(
                name: "sim_runs",
                schema: "research",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    run_key = table.Column<string>(type: "character(64)", nullable: false),
                    engine_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_schema_version = table.Column<short>(type: "smallint", nullable: false),
                    trade_schema_version = table.Column<short>(type: "smallint", nullable: false),
                    strategy_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    definition_sha256 = table.Column<string>(type: "character(64)", nullable: false),
                    from_date = table.Column<DateOnly>(type: "date", nullable: false),
                    to_date = table.Column<DateOnly>(type: "date", nullable: false),
                    universe_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    universe_label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    instruments = table.Column<int>(type: "integer", nullable: false),
                    instruments_run = table.Column<int>(type: "integer", nullable: false),
                    trades_in_window = table.Column<long>(type: "bigint", nullable: false),
                    candidates_in_window = table.Column<long>(type: "bigint", nullable: false),
                    trade_count = table.Column<int>(type: "integer", nullable: false),
                    open_at_end_count = table.Column<int>(type: "integer", nullable: false),
                    acknowledge_csv_adjusted = table.Column<bool>(type: "boolean", nullable: false),
                    fills = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    costs_sheet_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    costs_rates_sha256 = table.Column<string>(type: "character(64)", nullable: true),
                    record_json = table.Column<string>(type: "text", nullable: false),
                    record_sha256 = table.Column<string>(type: "character(64)", nullable: false),
                    trades_sha256 = table.Column<string>(type: "character(64)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sim_runs", x => x.id);
                    table.CheckConstraint("ck_sim_runs_costs", "(costs_sheet_id IS NULL) = (costs_rates_sha256 IS NULL)");
                    table.CheckConstraint("ck_sim_runs_counts", "trade_count >= 0 AND open_at_end_count >= 0");
                    table.CheckConstraint("ck_sim_runs_run_key", "run_key ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_sim_runs_universe_kind", "universe_kind IN ('instrumentlist', 'basket')");
                });

            migrationBuilder.CreateTable(
                name: "sim_trades",
                schema: "research",
                columns: table => new
                {
                    run_id = table.Column<long>(type: "bigint", nullable: false),
                    open_at_end = table.Column<bool>(type: "boolean", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    trade_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    basket_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    cost_entry_turnover = table.Column<decimal>(type: "numeric", nullable: true),
                    cost_exit_turnover = table.Column<decimal>(type: "numeric", nullable: true),
                    cost_quantity = table.Column<long>(type: "bigint", nullable: true),
                    cost_r = table.Column<double>(type: "double precision", nullable: true),
                    cost_risk_taken = table.Column<decimal>(type: "numeric", nullable: true),
                    cost_segment = table.Column<string>(type: "character varying(16)", nullable: true),
                    cost_total = table.Column<decimal>(type: "numeric", nullable: true),
                    costs = table.Column<string>(type: "character varying(16)", nullable: false),
                    decision_epoch_seconds = table.Column<long>(type: "bigint", nullable: false),
                    entry_brokerage = table.Column<decimal>(type: "numeric", nullable: true),
                    entry_dp_charge = table.Column<decimal>(type: "numeric", nullable: true),
                    entry_epoch_seconds = table.Column<long>(type: "bigint", nullable: false),
                    entry_exchange_txn = table.Column<decimal>(type: "numeric", nullable: true),
                    entry_fill = table.Column<string>(type: "character varying(16)", nullable: false),
                    entry_filled_price = table.Column<double>(type: "double precision", nullable: false),
                    entry_gst = table.Column<decimal>(type: "numeric", nullable: true),
                    entry_other_charges = table.Column<decimal>(type: "numeric", nullable: true),
                    entry_price = table.Column<double>(type: "double precision", nullable: false),
                    entry_sebi_fee = table.Column<decimal>(type: "numeric", nullable: true),
                    entry_snapshot_json = table.Column<string>(type: "text", nullable: true),
                    entry_stamp_duty = table.Column<decimal>(type: "numeric", nullable: true),
                    entry_stt = table.Column<decimal>(type: "numeric", nullable: true),
                    exit_brokerage = table.Column<decimal>(type: "numeric", nullable: true),
                    exit_dp_charge = table.Column<decimal>(type: "numeric", nullable: true),
                    exit_epoch_seconds = table.Column<long>(type: "bigint", nullable: false),
                    exit_exchange_txn = table.Column<decimal>(type: "numeric", nullable: true),
                    exit_filled_price = table.Column<double>(type: "double precision", nullable: false),
                    exit_gst = table.Column<decimal>(type: "numeric", nullable: true),
                    exit_other_charges = table.Column<decimal>(type: "numeric", nullable: true),
                    exit_price = table.Column<double>(type: "double precision", nullable: false),
                    exit_reason = table.Column<string>(type: "character varying(16)", nullable: false),
                    exit_sebi_fee = table.Column<decimal>(type: "numeric", nullable: true),
                    exit_stamp_duty = table.Column<decimal>(type: "numeric", nullable: true),
                    exit_stt = table.Column<decimal>(type: "numeric", nullable: true),
                    fills = table.Column<string>(type: "character varying(16)", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mae_r = table.Column<double>(type: "double precision", nullable: false),
                    mfe_r = table.Column<double>(type: "double precision", nullable: false),
                    r_exact = table.Column<double>(type: "double precision", nullable: false),
                    r_filled = table.Column<double>(type: "double precision", nullable: false),
                    r_net = table.Column<double>(type: "double precision", nullable: true),
                    sessions_held = table.Column<int>(type: "integer", nullable: false),
                    sessions_unseen = table.Column<int>(type: "integer", nullable: false),
                    side = table.Column<string>(type: "character varying(16)", nullable: false),
                    slippage_r = table.Column<double>(type: "double precision", nullable: false),
                    stop_price = table.Column<double>(type: "double precision", nullable: false),
                    strategy_id = table.Column<string>(type: "character varying(64)", nullable: false),
                    symbol = table.Column<string>(type: "character varying(40)", nullable: false),
                    target_price = table.Column<double>(type: "double precision", nullable: true),
                    tick_fallback = table.Column<bool>(type: "boolean", nullable: false),
                    trade_date = table.Column<DateOnly>(type: "date", nullable: false),
                    trigger_bar_close = table.Column<double>(type: "double precision", nullable: false),
                    trigger_bar_complete = table.Column<bool>(type: "boolean", nullable: false),
                    trigger_bar_epoch = table.Column<long>(type: "bigint", nullable: false),
                    trigger_bar_high = table.Column<double>(type: "double precision", nullable: false),
                    trigger_bar_low = table.Column<double>(type: "double precision", nullable: false),
                    trigger_bar_minutes = table.Column<int>(type: "integer", nullable: false),
                    trigger_bar_open = table.Column<double>(type: "double precision", nullable: false),
                    trigger_bar_volume = table.Column<long>(type: "bigint", nullable: false),
                    trigger_timeframe = table.Column<string>(type: "character varying(16)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sim_trades", x => new { x.run_id, x.open_at_end, x.ordinal });
                    table.CheckConstraint("ck_sim_trades_cost_segment", "cost_segment IS NULL OR cost_segment IN ('intradayequity', 'delivery')");
                    table.CheckConstraint("ck_sim_trades_costing_all_or_nothing", "num_nonnulls(cost_segment, cost_quantity, cost_risk_taken, cost_entry_turnover, cost_exit_turnover, entry_brokerage, entry_stt, entry_exchange_txn, entry_sebi_fee, entry_stamp_duty, entry_other_charges, entry_dp_charge, entry_gst, exit_brokerage, exit_stt, exit_exchange_txn, exit_sebi_fee, exit_stamp_duty, exit_other_charges, exit_dp_charge, exit_gst, cost_total, cost_r, r_net) IN (0, 24)");
                    table.CheckConstraint("ck_sim_trades_costs", "costs IN ('none', 'charged')");
                    table.CheckConstraint("ck_sim_trades_costs_charged", "(costs = 'charged') = (cost_quantity IS NOT NULL)");
                    table.CheckConstraint("ck_sim_trades_entry_fill", "entry_fill IN ('atopen', 'atlimit')");
                    table.CheckConstraint("ck_sim_trades_exit_reason", "exit_reason IN ('stop', 'target', 'squareoff', 'endofdata', 'maxhold')");
                    table.CheckConstraint("ck_sim_trades_fills", "fills IN ('exact', 'modelled')");
                    table.CheckConstraint("ck_sim_trades_ordinal", "ordinal >= 0");
                    table.CheckConstraint("ck_sim_trades_side", "side IN ('buy', 'sell')");
                    table.CheckConstraint("ck_sim_trades_trigger_timeframe", "trigger_timeframe IN ('m1', 'm5', 'm10', 'm15', 'm30', 'h1', 'd1', 'w1', 'mo1')");
                    table.ForeignKey(
                        name: "fk_sim_trades_run",
                        column: x => x.run_id,
                        principalSchema: "research",
                        principalTable: "sim_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sim_runs_strategy_created",
                schema: "research",
                table: "sim_runs",
                columns: new[] { "strategy_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "uq_sim_runs_run_key",
                schema: "research",
                table: "sim_runs",
                column: "run_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_sim_trades_run_trade_key",
                schema: "research",
                table: "sim_trades",
                columns: new[] { "run_id", "trade_key" },
                unique: true);

            // The runtime role reads and inserts; it can neither change nor delete a stored result (spec P3-10, D13). Order matters: the schema grant gives DML on every table, the revoke then narrows these two.
            migrationBuilder.GrantSchemaUsageToAppRole(Schemas.Research);
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Research, "sim_runs");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Research, "sim_trades");
        }

        /// <inheritdoc />
        // Destroys the stored runs (each can be re-run from the stored bars, and the run key says whether the result is the same). The schema is dropped without CASCADE, so a later table in it makes this fail and removes nothing.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sim_trades",
                schema: "research");

            migrationBuilder.DropTable(
                name: "sim_runs",
                schema: "research");

            migrationBuilder.Sql("DROP SCHEMA research;");
        }
    }
}
