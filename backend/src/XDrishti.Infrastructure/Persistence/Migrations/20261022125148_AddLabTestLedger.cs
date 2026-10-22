using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLabTestLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "test_ledger",
                schema: "lab",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    strategy_id = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    strategy_hash = table.Column<string>(type: "character(64)", nullable: false),
                    parent_hash = table.Column<string>(type: "character(64)", nullable: true),
                    lab_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_key = table.Column<string>(type: "character(64)", nullable: false),
                    universe_label = table.Column<string>(type: "text", nullable: false),
                    window_from = table.Column<DateOnly>(type: "date", nullable: false),
                    window_to = table.Column<DateOnly>(type: "date", nullable: false),
                    method = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_test_ledger", x => x.id);
                    table.CheckConstraint("ck_test_ledger_hash", "strategy_hash ~ '^[0-9a-f]{64}$' AND window_from <= window_to");
                });

            migrationBuilder.CreateIndex(
                name: "ix_test_ledger_strategy_created",
                schema: "lab",
                table: "test_ledger",
                columns: new[] { "strategy_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "uq_test_ledger_run",
                schema: "lab",
                table: "test_ledger",
                column: "lab_run_id",
                unique: true);

            // Insert-only for the runtime role (spec P5-03, ADR 0059): default privileges give the new table plain DML, which is cut back here. Not GrantSchemaUsageToAppRole: that would re-grant UPDATE and DELETE
            // on the append-only tables of the schema.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                        REVOKE UPDATE, DELETE, TRUNCATE ON lab.test_ledger FROM xd_app;
                        GRANT SELECT, INSERT ON lab.test_ledger TO xd_app;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "test_ledger",
                schema: "lab");
        }
    }
}
