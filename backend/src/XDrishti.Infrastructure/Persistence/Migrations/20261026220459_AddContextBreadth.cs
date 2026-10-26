using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContextBreadth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "breadth",
                schema: "market",
                table: "context_daily",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "breadth_computed_at",
                schema: "market",
                table: "context_daily",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "breadth_excluded",
                schema: "market",
                table: "context_daily",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "breadth_state",
                schema: "market",
                table: "context_daily",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "breadth_universe",
                schema: "market",
                table: "context_daily",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "breadth_voters",
                schema: "market",
                table: "context_daily",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_context_daily_breadth",
                schema: "market",
                table: "context_daily",
                sql: "breadth IS NULL OR (breadth >= 0 AND breadth <= 100)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_context_daily_breadth_state",
                schema: "market",
                table: "context_daily",
                sql: "breadth_state IS NULL OR breadth_state IN ('value', 'insufficient')");

            // The table stays insert-only for the runtime role (no table-level UPDATE): only these six columns may be updated, a Postgres column privilege (P3-01e). The index and VIX figures still refuse UPDATE.
            migrationBuilder.GrantColumnUpdateToAppRole("market", "context_daily", "breadth", "breadth_voters", "breadth_universe", "breadth_excluded", "breadth_state", "breadth_computed_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_context_daily_breadth",
                schema: "market",
                table: "context_daily");

            migrationBuilder.DropCheckConstraint(
                name: "ck_context_daily_breadth_state",
                schema: "market",
                table: "context_daily");

            migrationBuilder.DropColumn(
                name: "breadth",
                schema: "market",
                table: "context_daily");

            migrationBuilder.DropColumn(
                name: "breadth_computed_at",
                schema: "market",
                table: "context_daily");

            migrationBuilder.DropColumn(
                name: "breadth_excluded",
                schema: "market",
                table: "context_daily");

            migrationBuilder.DropColumn(
                name: "breadth_state",
                schema: "market",
                table: "context_daily");

            migrationBuilder.DropColumn(
                name: "breadth_universe",
                schema: "market",
                table: "context_daily");

            migrationBuilder.DropColumn(
                name: "breadth_voters",
                schema: "market",
                table: "context_daily");
        }
    }
}
