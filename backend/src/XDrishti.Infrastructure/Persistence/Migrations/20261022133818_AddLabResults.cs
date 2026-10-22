using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLabResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lab_results",
                schema: "lab",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    lab_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    strategy_id = table.Column<string>(type: "text", nullable: false),
                    strategy_hash = table.Column<string>(type: "character(64)", nullable: false),
                    method = table.Column<string>(type: "text", nullable: false),
                    outcome = table.Column<string>(type: "text", nullable: false),
                    variant_count = table.Column<int>(type: "integer", nullable: false),
                    adjustment_factor = table.Column<double>(type: "double precision", nullable: false),
                    result = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lab_results", x => x.id);
                    table.CheckConstraint("ck_lab_results_outcome", "outcome IN ('PASS', 'MARGINAL', 'FAIL')");
                    table.ForeignKey(
                        name: "fk_lab_results_run",
                        column: x => x.lab_run_id,
                        principalSchema: "lab",
                        principalTable: "lab_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "uq_lab_results_run",
                schema: "lab",
                table: "lab_results",
                column: "lab_run_id",
                unique: true);

            // Insert-only for the runtime role (spec P5-04, ADR 0060): see AddLabTestLedger.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                        REVOKE UPDATE, DELETE, TRUNCATE ON lab.lab_results FROM xd_app;
                        GRANT SELECT, INSERT ON lab.lab_results TO xd_app;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lab_results",
                schema: "lab");
        }
    }
}
