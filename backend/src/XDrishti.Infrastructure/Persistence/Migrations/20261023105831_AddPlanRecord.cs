using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "workflow");

            migrationBuilder.EnsureSchema(
                name: "trading");

            migrationBuilder.CreateTable(
                name: "plans",
                schema: "trading",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    plan_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    for_date = table.Column<DateOnly>(type: "date", nullable: false),
                    review_cutoff = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    input_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    basis = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    provisional = table.Column<bool>(type: "boolean", nullable: false),
                    notice = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    limits = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plans", x => x.id);
                    table.CheckConstraint("ck_plans_shape", "version >= 1 AND length(plan_key) > 0 AND length(input_hash) = 64");
                });

            migrationBuilder.CreateTable(
                name: "plan_items",
                schema: "trading",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    plan_id = table.Column<long>(type: "bigint", nullable: false),
                    item_rank = table.Column<int>(type: "integer", nullable: false),
                    item_group = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    symbol = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    side = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    strategy_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    strategy_version_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    trigger_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    stop_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    target_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    validity_text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    reasons = table.Column<string[]>(type: "text[]", nullable: false),
                    cell_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    trades = table.Column<int>(type: "integer", nullable: false),
                    data_range = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    p_win_basis = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    p_win = table.Column<double>(type: "double precision", nullable: true),
                    conservative_expectancy_r = table.Column<double>(type: "double precision", nullable: true),
                    unavailable = table.Column<string[]>(type: "text[]", nullable: false),
                    advisory_note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_items", x => x.id);
                    table.CheckConstraint("ck_plan_items_group", "item_group IN ('primary', 'reserve', 'rejected') AND (item_group = 'primary' OR cardinality(reasons) > 0)");
                    table.CheckConstraint("ck_plan_items_shape", "item_rank >= 1 AND side IN ('BUY', 'SELL') AND trades >= 0 AND (p_win IS NULL OR (p_win >= 0 AND p_win <= 1))");
                    table.ForeignKey(
                        name: "fk_plan_items_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "trading",
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "plan_runs",
                schema: "trading",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    for_date = table.Column<DateOnly>(type: "date", nullable: false),
                    plan_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    failed_gate = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    plan_id = table.Column<long>(type: "bigint", nullable: true),
                    manual = table.Column<bool>(type: "boolean", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_runs", x => x.id);
                    table.CheckConstraint("ck_plan_runs_outcome", "outcome IN ('published', 'failed_closed', 'dry_run') AND (outcome = 'published') = (plan_id IS NOT NULL) AND (outcome = 'failed_closed') = (failed_gate IS NOT NULL) AND finished_at >= started_at");
                    table.ForeignKey(
                        name: "fk_plan_runs_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "trading",
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "approvals",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    plan_id = table.Column<long>(type: "bigint", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: true),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    decision = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    confirm_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    modifications = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    cutoff = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approvals", x => x.id);
                    table.CheckConstraint("ck_approvals_kind", "kind = 'owner'");
                    table.CheckConstraint("ck_approvals_shape", "decision IN ('approve', 'modify', 'reject') AND (decision = 'modify') = (modifications IS NOT NULL) AND (confirm_reason IS NULL OR decision = 'approve') AND actor LIKE 'owner:%' AND length(btrim(reason)) > 0 AND (decision = 'reject' OR decided_at < cutoff)");
                    table.ForeignKey(
                        name: "fk_approvals_plan_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "trading",
                        principalTable: "plan_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_approvals_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "trading",
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "plan_item_allocations",
                schema: "trading",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_item_allocations", x => x.id);
                    table.CheckConstraint("ck_plan_item_allocations_shape", "quantity > 0 AND mode = 'paper'");
                    table.ForeignKey(
                        name: "fk_plan_item_allocations_broker_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "broker_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plan_item_allocations_plan_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "trading",
                        principalTable: "plan_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "plan_events",
                schema: "trading",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    plan_id = table.Column<long>(type: "bigint", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: true),
                    from_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    to_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    approval_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_events", x => x.id);
                    table.CheckConstraint("ck_plan_events_approval", "(item_id IS NULL AND approval_id IS NULL) OR (item_id IS NOT NULL AND ((to_state IN ('approved', 'modified', 'rejected')) = (approval_id IS NOT NULL)))");
                    table.CheckConstraint("ck_plan_events_legal", "(item_id IS NOT NULL AND (from_state, to_state) IN (('proposed', 'approved'), ('proposed', 'modified'), ('proposed', 'rejected'), ('proposed', 'lapsed'), ('modified', 'approved'), ('modified', 'rejected'), ('modified', 'lapsed'), ('approved', 'armed'), ('approved', 'invalidated'), ('approved', 'expired'), ('armed', 'triggered'), ('armed', 'expired'), ('triggered', 'closed'))) OR (item_id IS NULL AND (from_state, to_state) IN (('draft', 'published'), ('published', 'inreview'), ('published', 'approved'), ('published', 'rejected'), ('published', 'lapsed'), ('inreview', 'approved'), ('inreview', 'rejected'), ('inreview', 'lapsed'), ('approved', 'armed'), ('approved', 'expired'), ('armed', 'expired')))");
                    table.CheckConstraint("ck_plan_events_shape", "length(btrim(actor)) > 0 AND length(btrim(reason)) > 0");
                    table.ForeignKey(
                        name: "fk_plan_events_approvals_approval_id",
                        column: x => x.approval_id,
                        principalSchema: "workflow",
                        principalTable: "approvals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plan_events_plan_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "trading",
                        principalTable: "plan_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plan_events_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "trading",
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_approvals_item_id",
                schema: "workflow",
                table: "approvals",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_approvals_plan_id_id",
                schema: "workflow",
                table: "approvals",
                columns: new[] { "plan_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_plan_events_approval_id",
                schema: "trading",
                table: "plan_events",
                column: "approval_id");

            migrationBuilder.CreateIndex(
                name: "ix_plan_events_item_id_id",
                schema: "trading",
                table: "plan_events",
                columns: new[] { "item_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_plan_events_plan_id_id",
                schema: "trading",
                table: "plan_events",
                columns: new[] { "plan_id", "id" });

            migrationBuilder.CreateIndex(
                name: "uq_plan_events_item_from",
                schema: "trading",
                table: "plan_events",
                columns: new[] { "item_id", "from_state" },
                unique: true,
                filter: "item_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_plan_events_plan_from",
                schema: "trading",
                table: "plan_events",
                columns: new[] { "plan_id", "from_state" },
                unique: true,
                filter: "item_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_plan_item_allocations_account_id",
                schema: "trading",
                table: "plan_item_allocations",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "uq_plan_item_allocations_item_account",
                schema: "trading",
                table: "plan_item_allocations",
                columns: new[] { "item_id", "account_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_plan_items_plan_rank",
                schema: "trading",
                table: "plan_items",
                columns: new[] { "plan_id", "item_rank" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plan_runs_for_date_id",
                schema: "trading",
                table: "plan_runs",
                columns: new[] { "for_date", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_plan_runs_plan_id",
                schema: "trading",
                table: "plan_runs",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "uq_plans_for_date_version",
                schema: "trading",
                table: "plans",
                columns: new[] { "for_date", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_plans_plan_key",
                schema: "trading",
                table: "plans",
                column: "plan_key",
                unique: true);

            // Composite keys (spec P7-03 review): an event, an approval and the item they name belong to the same plan, and an owner-decision event names an approval of the same item and plan.
            migrationBuilder.Sql("ALTER TABLE trading.plan_items ADD CONSTRAINT uq_plan_items_plan_id_id UNIQUE (plan_id, id)");
            migrationBuilder.Sql("ALTER TABLE workflow.approvals ADD CONSTRAINT uq_approvals_id_plan_item UNIQUE (id, plan_id, item_id)");
            migrationBuilder.Sql("ALTER TABLE workflow.approvals ADD CONSTRAINT fk_approvals_item_in_plan FOREIGN KEY (plan_id, item_id) REFERENCES trading.plan_items (plan_id, id) ON DELETE RESTRICT");
            migrationBuilder.Sql("ALTER TABLE trading.plan_events ADD CONSTRAINT fk_plan_events_item_in_plan FOREIGN KEY (plan_id, item_id) REFERENCES trading.plan_items (plan_id, id) ON DELETE RESTRICT");
            migrationBuilder.Sql("ALTER TABLE trading.plan_events ADD CONSTRAINT fk_plan_events_approval_of_item FOREIGN KEY (approval_id, plan_id, item_id) REFERENCES workflow.approvals (id, plan_id, item_id) ON DELETE RESTRICT");

            // Insert-only for the runtime role (spec P7-03, ADR 0083): a state change is a new plan_events row, a decision a new approvals row.
            // A later migration must NOT call GrantSchemaUsageToAppRole for trading or workflow (it would give UPDATE and DELETE back); to add a table here, grant that table and make it append-only. PlanGrantsTests and an architecture test enforce it.
            migrationBuilder.GrantSchemaUsageToAppRole(Schemas.Trading);
            migrationBuilder.GrantSchemaUsageToAppRole(Schemas.Workflow);
            foreach (var table in new[] { "plans", "plan_items", "plan_item_allocations", "plan_events", "plan_runs" })
            {
                migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Trading, table);
            }

            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Workflow, "approvals");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "plan_events",
                schema: "trading");

            migrationBuilder.DropTable(
                name: "plan_item_allocations",
                schema: "trading");

            migrationBuilder.DropTable(
                name: "plan_runs",
                schema: "trading");

            migrationBuilder.DropTable(
                name: "approvals",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "plan_items",
                schema: "trading");

            migrationBuilder.DropTable(
                name: "plans",
                schema: "trading");
        }
    }
}
