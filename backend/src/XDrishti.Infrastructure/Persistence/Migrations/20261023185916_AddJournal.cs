using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "stop_price",
                schema: "trading",
                table: "grades",
                type: "double precision",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "journal_entries",
                schema: "trading",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    plan_id = table.Column<long>(type: "bigint", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    for_date = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_journal_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_journal_entries_broker_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "broker_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_journal_entries_plan_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "trading",
                        principalTable: "plan_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_journal_entries_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "trading",
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fill_grades",
                schema: "trading",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    entry_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    qty = table.Column<long>(type: "bigint", nullable: false),
                    entry_price = table.Column<double>(type: "double precision", nullable: false),
                    exit_price = table.Column<double>(type: "double precision", nullable: false),
                    plan_entry_price = table.Column<double>(type: "double precision", nullable: true),
                    plan_stop_price = table.Column<double>(type: "double precision", nullable: true),
                    plan_r = table.Column<double>(type: "double precision", nullable: true),
                    slippage_price = table.Column<double>(type: "double precision", nullable: true),
                    slippage_r = table.Column<double>(type: "double precision", nullable: true),
                    gross_pnl = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    charges = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    r = table.Column<double>(type: "double precision", nullable: true),
                    basis = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    graded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fill_grades", x => x.id);
                    table.CheckConstraint("ck_fill_grades_shape", "status IN ('graded', 'no_plan_levels') AND basis = 'realised fills, not simulated' AND qty > 0 AND ((status = 'graded') = (r IS NOT NULL)) AND ((status = 'graded') = (slippage_r IS NOT NULL)) AND length(detail) > 0");
                    table.ForeignKey(
                        name: "fk_fill_grades_journal_entries_entry_id",
                        column: x => x.entry_id,
                        principalSchema: "trading",
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "journal_fills",
                schema: "trading",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_id = table.Column<long>(type: "bigint", nullable: true),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    qty = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    for_date = table.Column<DateOnly>(type: "date", nullable: false),
                    journaled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_journal_fills", x => x.id);
                    table.CheckConstraint("ck_journal_fills_shape", "role IN ('entry', 'exit', 'discretionary') AND (role = 'discretionary') = (entry_id IS NULL) AND qty > 0 AND length(reason) > 0");
                    table.ForeignKey(
                        name: "fk_journal_fills_journal_entries_entry_id",
                        column: x => x.entry_id,
                        principalSchema: "trading",
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_journal_fills_transactions_transaction_id",
                        column: x => x.transaction_id,
                        principalSchema: "portfolio",
                        principalTable: "transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "uq_fill_grades_entry_id",
                schema: "trading",
                table: "fill_grades",
                column: "entry_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_account_id",
                schema: "trading",
                table: "journal_entries",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_plan_id",
                schema: "trading",
                table: "journal_entries",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "uq_journal_entries_item_account",
                schema: "trading",
                table: "journal_entries",
                columns: new[] { "item_id", "account_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_journal_fills_entry_id",
                schema: "trading",
                table: "journal_fills",
                column: "entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_journal_fills_for_date",
                schema: "trading",
                table: "journal_fills",
                column: "for_date");

            migrationBuilder.CreateIndex(
                name: "uq_journal_fills_transaction_id",
                schema: "trading",
                table: "journal_fills",
                column: "transaction_id",
                unique: true);

            // The journal entry is a real allocation of a real item of its own plan (composite keys onto the unique pairs of plan_items and plan_item_allocations).
            migrationBuilder.Sql("ALTER TABLE trading.journal_entries ADD CONSTRAINT fk_journal_entries_item_in_plan FOREIGN KEY (plan_id, item_id) REFERENCES trading.plan_items (plan_id, id) ON DELETE RESTRICT");
            migrationBuilder.Sql("ALTER TABLE trading.journal_entries ADD CONSTRAINT fk_journal_entries_allocation FOREIGN KEY (item_id, account_id) REFERENCES trading.plan_item_allocations (item_id, account_id) ON DELETE RESTRICT");

            // Insert-only for the runtime role (ADR 0093): a link or a grade is written once and never changed. Granted per table, never by re-granting the schema (ADR 0083, 6a).
            // The new column of trading.grades is covered by the table-level grants of AddGrades (insert and select only).
            foreach (var table in new[] { "journal_entries", "journal_fills", "fill_grades" })
            {
                migrationBuilder.GrantTableToAppRole(Schemas.Trading, table);
                migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Trading, table);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fill_grades",
                schema: "trading");

            migrationBuilder.DropTable(
                name: "journal_fills",
                schema: "trading");

            migrationBuilder.DropTable(
                name: "journal_entries",
                schema: "trading");

            migrationBuilder.DropColumn(
                name: "stop_price",
                schema: "trading",
                table: "grades");
        }
    }
}
