using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaperCapture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "paper");

            migrationBuilder.CreateTable(
                name: "capture_runs",
                schema: "paper",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    session_date = table.Column<DateOnly>(type: "date", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    outcome = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    seen = table.Column<int>(type: "integer", nullable: false),
                    entries_written = table.Column<int>(type: "integer", nullable: false),
                    exits_written = table.Column<int>(type: "integer", nullable: false),
                    skips_written = table.Column<int>(type: "integer", nullable: false),
                    pending = table.Column<int>(type: "integer", nullable: false),
                    integrity = table.Column<int>(type: "integer", nullable: false),
                    manual = table.Column<bool>(type: "boolean", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_capture_runs", x => x.id);
                    table.CheckConstraint("ck_capture_runs_shape", "outcome IN ('done', 'partial', 'failed') AND seen >= 0 AND pending >= 0 AND integrity >= 0 AND finished_at >= started_at");
                });

            migrationBuilder.CreateTable(
                name: "epochs",
                schema: "paper",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    started_on = table.Column<DateOnly>(type: "date", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fingerprint_hash = table.Column<string>(type: "char(64)", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    prev_epoch_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_epochs", x => x.id);
                    table.CheckConstraint("ck_epochs_shape", "kind IN ('first', 'detected_change', 'owner_restart') AND length(btrim(reason)) > 0 AND length(btrim(actor)) > 0 AND (fingerprint_hash IS NULL OR length(fingerprint_hash) = 64)");
                    table.ForeignKey(
                        name: "fk_epochs_epochs_prev_epoch_id",
                        column: x => x.prev_epoch_id,
                        principalSchema: "paper",
                        principalTable: "epochs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "entries",
                schema: "paper",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    plan_id = table.Column<long>(type: "bigint", nullable: false),
                    plan_item_id = table.Column<long>(type: "bigint", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    epoch_id = table.Column<long>(type: "bigint", nullable: false),
                    measure_status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ticket_group = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    approval_id = table.Column<long>(type: "bigint", nullable: false),
                    approval_kind = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    symbol = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    side = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    strategy_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    strategy_hash = table.Column<string>(type: "char(64)", nullable: false),
                    cell_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    trade_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    qty_allocated = table.Column<int>(type: "integer", nullable: false),
                    qty = table.Column<int>(type: "integer", nullable: false),
                    armed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    entry_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    entry_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    entry_exact = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    stop_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    target_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    swing = table.Column<bool>(type: "boolean", nullable: false),
                    run_key = table.Column<string>(type: "char(64)", nullable: false),
                    engine_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    basis = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entries", x => x.id);
                    table.CheckConstraint("ck_entries_shape", "measure_status IN ('measured', 'straddle') AND ticket_group IN ('primary', 'reserve') AND approval_kind IN ('owner', 'auto') AND side IN ('BUY', 'SELL') AND qty > 0 AND qty <= qty_allocated AND entry_at >= armed_at AND basis = 'simulated on the official 1-minute bars; no order was placed or exists'");
                    table.ForeignKey(
                        name: "fk_entries_approvals_approval_id",
                        column: x => x.approval_id,
                        principalSchema: "workflow",
                        principalTable: "approvals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_entries_epochs_epoch_id",
                        column: x => x.epoch_id,
                        principalSchema: "paper",
                        principalTable: "epochs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "epoch_accounts",
                schema: "paper",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    epoch_id = table.Column<long>(type: "bigint", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    capital = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    capital_source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    mode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_epoch_accounts", x => x.id);
                    table.CheckConstraint("ck_epoch_accounts_shape", "mode = 'paper' AND capital > 0");
                    table.ForeignKey(
                        name: "fk_epoch_accounts_broker_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "broker_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_epoch_accounts_epochs_epoch_id",
                        column: x => x.epoch_id,
                        principalSchema: "paper",
                        principalTable: "epochs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "skips",
                schema: "paper",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    plan_id = table.Column<long>(type: "bigint", nullable: false),
                    plan_item_id = table.Column<long>(type: "bigint", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    epoch_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_skips", x => x.id);
                    table.CheckConstraint("ck_skips_shape", "code IN ('not_triggered', 'not_gradable', 'dropped_by_sizing', 'legs_not_supported', 'entered_before_armed', 'modification_unreadable', 'approval_missing') AND length(btrim(detail)) > 0");
                    table.ForeignKey(
                        name: "fk_skips_epochs_epoch_id",
                        column: x => x.epoch_id,
                        principalSchema: "paper",
                        principalTable: "epochs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exits",
                schema: "paper",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    entry_id = table.Column<long>(type: "bigint", nullable: false),
                    entry_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    exit_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    exit_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    exit_reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    held_sessions = table.Column<int>(type: "integer", nullable: false),
                    gross_pnl = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    charges = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    charge_sheet_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    net_pnl = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    cost_basis = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    r_exact = table.Column<double>(type: "double precision", nullable: false),
                    r_gross = table.Column<double>(type: "double precision", nullable: false),
                    r_net = table.Column<double>(type: "double precision", nullable: true),
                    r_sim = table.Column<double>(type: "double precision", nullable: false),
                    r_basis = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    mae_r = table.Column<double>(type: "double precision", nullable: false),
                    mfe_r = table.Column<double>(type: "double precision", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exits", x => x.id);
                    table.CheckConstraint("ck_exits_shape", "exit_at >= entry_at AND held_sessions >= 1 AND r_basis IN ('net', 'gross') AND (charges IS NULL) = (r_net IS NULL) AND (charges IS NULL) = (net_pnl IS NULL) AND (charges IS NULL) = (charge_sheet_id IS NULL) AND ((r_basis = 'net') = (r_net IS NOT NULL))");
                    table.ForeignKey(
                        name: "fk_exits_entries_entry_id",
                        column: x => x.entry_id,
                        principalSchema: "paper",
                        principalTable: "entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_capture_runs_session_date_id",
                schema: "paper",
                table: "capture_runs",
                columns: new[] { "session_date", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_entries_approval_id",
                schema: "paper",
                table: "entries",
                column: "approval_id");

            migrationBuilder.CreateIndex(
                name: "ix_entries_entry_at_id",
                schema: "paper",
                table: "entries",
                columns: new[] { "entry_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_entries_epoch_id",
                schema: "paper",
                table: "entries",
                column: "epoch_id");

            migrationBuilder.CreateIndex(
                name: "ix_entries_plan_id",
                schema: "paper",
                table: "entries",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "uq_entries_id_entry_at",
                schema: "paper",
                table: "entries",
                columns: new[] { "id", "entry_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_entries_item_account",
                schema: "paper",
                table: "entries",
                columns: new[] { "plan_item_id", "account_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_epoch_accounts_account_id",
                schema: "paper",
                table: "epoch_accounts",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "uq_epoch_accounts_epoch_account",
                schema: "paper",
                table: "epoch_accounts",
                columns: new[] { "epoch_id", "account_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_epochs_prev_epoch_id",
                schema: "paper",
                table: "epochs",
                column: "prev_epoch_id");

            migrationBuilder.CreateIndex(
                name: "uq_epochs_first",
                schema: "paper",
                table: "epochs",
                column: "kind",
                unique: true,
                filter: "kind = 'first'");

            migrationBuilder.CreateIndex(
                name: "uq_exits_entry_id",
                schema: "paper",
                table: "exits",
                column: "entry_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_skips_epoch_id",
                schema: "paper",
                table: "skips",
                column: "epoch_id");

            migrationBuilder.CreateIndex(
                name: "uq_skips_item_account",
                schema: "paper",
                table: "skips",
                columns: new[] { "plan_item_id", "account_id" },
                unique: true);

            // The ticket is in its plan, and the allocation is a real one of that ticket (composite keys onto the plan record's unique pairs).
            migrationBuilder.Sql("ALTER TABLE paper.entries ADD CONSTRAINT fk_entries_item_in_plan FOREIGN KEY (plan_id, plan_item_id) REFERENCES trading.plan_items (plan_id, id) ON DELETE RESTRICT");
            migrationBuilder.Sql("ALTER TABLE paper.entries ADD CONSTRAINT fk_entries_allocation FOREIGN KEY (plan_item_id, account_id) REFERENCES trading.plan_item_allocations (item_id, account_id) ON DELETE RESTRICT");
            migrationBuilder.Sql("ALTER TABLE paper.skips ADD CONSTRAINT fk_skips_item_in_plan FOREIGN KEY (plan_id, plan_item_id) REFERENCES trading.plan_items (plan_id, id) ON DELETE RESTRICT");
            migrationBuilder.Sql("ALTER TABLE paper.skips ADD CONSTRAINT fk_skips_allocation FOREIGN KEY (plan_item_id, account_id) REFERENCES trading.plan_item_allocations (item_id, account_id) ON DELETE RESTRICT");

            // An exit is tied to the instant of its entry, so it cannot precede it (the check on exits) and cannot name another entry's instant.
            migrationBuilder.Sql("ALTER TABLE paper.exits ADD CONSTRAINT fk_exits_entry_at FOREIGN KEY (entry_id, entry_at) REFERENCES paper.entries (id, entry_at) ON DELETE RESTRICT");

            // Insert-only for the runtime role: the paper record is written once and never changed (ADR 0116). A later paper migration must not re-grant the schema without repeating the append-only step for every table.
            migrationBuilder.GrantSchemaUsageToAppRole(Schemas.Paper);
            foreach (var table in new[] { "epochs", "epoch_accounts", "entries", "exits", "skips", "capture_runs" })
            {
                migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Paper, table);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "capture_runs",
                schema: "paper");

            migrationBuilder.DropTable(
                name: "epoch_accounts",
                schema: "paper");

            migrationBuilder.DropTable(
                name: "exits",
                schema: "paper");

            migrationBuilder.DropTable(
                name: "skips",
                schema: "paper");

            migrationBuilder.DropTable(
                name: "entries",
                schema: "paper");

            migrationBuilder.DropTable(
                name: "epochs",
                schema: "paper");

            migrationBuilder.Sql("DROP SCHEMA IF EXISTS paper");
        }
    }
}
