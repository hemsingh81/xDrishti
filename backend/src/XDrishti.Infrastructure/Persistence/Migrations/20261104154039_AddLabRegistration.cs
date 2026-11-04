using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Pre-registration, the plan freeze and the locked hold-out (spec CF-06, ADR 0207): <c>lab.registrations</c>, <c>lab.plan_freezes</c>, <c>lab.holdout_blocks</c> and <c>lab.holdout_unlocks</c> (insert-only for the runtime role) and two columns on <c>lab.test_ledger</c>
    /// (<c>trial_kind</c>, <c>extra_trials</c>; every existing row reads <c>run</c> and 0, so no count changes). No existing row is touched.
    /// </summary>
    public partial class AddLabRegistration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "extra_trials",
                schema: "lab",
                table: "test_ledger",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "trial_kind",
                schema: "lab",
                table: "test_ledger",
                type: "text",
                nullable: false,
                defaultValue: "run");

            migrationBuilder.CreateTable(
                name: "holdout_blocks",
                schema: "lab",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    history_first = table.Column<DateOnly>(type: "date", nullable: false),
                    history_last = table.Column<DateOnly>(type: "date", nullable: false),
                    length_months = table.Column<int>(type: "integer", nullable: false),
                    setting_hash = table.Column<string>(type: "character(64)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holdout_blocks", x => x.id);
                    table.CheckConstraint("ck_holdout_blocks_shape", "history_first < start_date AND start_date <= history_last AND length_months >= 1 AND setting_hash ~ '^[0-9a-f]{64}$'");
                });

            migrationBuilder.CreateTable(
                name: "holdout_unlocks",
                schema: "lab",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    strategy_id = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    unlocked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    mode = table.Column<string>(type: "text", nullable: false),
                    actor = table.Column<string>(type: "text", nullable: false),
                    setting_hash = table.Column<string>(type: "character(64)", nullable: false),
                    reused_window = table.Column<bool>(type: "boolean", nullable: false),
                    block_start = table.Column<DateOnly>(type: "date", nullable: false),
                    lab_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    result = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holdout_unlocks", x => x.id);
                    table.CheckConstraint("ck_holdout_unlocks_shape", "version >= 1 AND kind IN ('strategy','model') AND mode IN ('owner','auto') AND setting_hash ~ '^[0-9a-f]{64}$'");
                });

            migrationBuilder.CreateTable(
                name: "plan_freezes",
                schema: "lab",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    strategy_id = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    plan_hash = table.Column<string>(type: "character(64)", nullable: false),
                    first_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_freezes", x => x.id);
                    table.CheckConstraint("ck_plan_freezes_shape", "version >= 1 AND plan_hash ~ '^[0-9a-f]{64}$'");
                });

            migrationBuilder.CreateTable(
                name: "registrations",
                schema: "lab",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    strategy_id = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    rationale = table.Column<string>(type: "jsonb", nullable: false),
                    text_hash = table.Column<string>(type: "character(64)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_registrations", x => x.id);
                    table.CheckConstraint("ck_registrations_shape", "version >= 1 AND text_hash ~ '^[0-9a-f]{64}$'");
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_test_ledger_trial",
                schema: "lab",
                table: "test_ledger",
                sql: "trial_kind IN ('run','replan') AND extra_trials >= 0");

            migrationBuilder.CreateIndex(
                name: "uq_holdout_blocks_start",
                schema: "lab",
                table: "holdout_blocks",
                column: "start_date",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_holdout_unlocks_block",
                schema: "lab",
                table: "holdout_unlocks",
                column: "block_start");

            migrationBuilder.CreateIndex(
                name: "uq_holdout_unlocks_fresh",
                schema: "lab",
                table: "holdout_unlocks",
                column: "block_start",
                unique: true,
                filter: "NOT reused_window");

            migrationBuilder.CreateIndex(
                name: "uq_holdout_unlocks_version",
                schema: "lab",
                table: "holdout_unlocks",
                columns: new[] { "strategy_id", "version", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_plan_freezes_plan",
                schema: "lab",
                table: "plan_freezes",
                columns: new[] { "strategy_id", "version", "plan_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_registrations_version",
                schema: "lab",
                table: "registrations",
                columns: new[] { "strategy_id", "version" },
                unique: true);

            // the four new tables are insert-only for the runtime role, granted table by table; the execution role and the MCP roles get nothing. test_ledger keeps the insert-only grant of its own migration.
            foreach (var table in new[] { "registrations", "plan_freezes", "holdout_blocks", "holdout_unlocks" })
            {
                migrationBuilder.GrantTableToAppRole(Schemas.Lab, table);
                migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Lab, table);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "holdout_blocks",
                schema: "lab");

            migrationBuilder.DropTable(
                name: "holdout_unlocks",
                schema: "lab");

            migrationBuilder.DropTable(
                name: "plan_freezes",
                schema: "lab");

            migrationBuilder.DropTable(
                name: "registrations",
                schema: "lab");

            migrationBuilder.DropCheckConstraint(
                name: "ck_test_ledger_trial",
                schema: "lab",
                table: "test_ledger");

            migrationBuilder.DropColumn(
                name: "extra_trials",
                schema: "lab",
                table: "test_ledger");

            migrationBuilder.DropColumn(
                name: "trial_kind",
                schema: "lab",
                table: "test_ledger");
        }
    }
}
