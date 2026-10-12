using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEodRunAndBarGaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_data_jobs_status",
                schema: "market",
                table: "data_jobs");

            migrationBuilder.AddColumn<bool>(
                name: "refetch_gaps",
                schema: "market",
                table: "data_jobs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "trigger",
                schema: "market",
                table: "data_jobs",
                type: "character varying(6)",
                maxLength: 6,
                nullable: false,
                defaultValueSql: "'manual'");

            migrationBuilder.CreateTable(
                name: "bar_gaps",
                schema: "market",
                columns: table => new
                {
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    expected = table.Column<int>(type: "integer", nullable: false),
                    found = table.Column<int>(type: "integer", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bar_gaps", x => new { x.instrument_id, x.date });
                    table.CheckConstraint("ck_bar_gaps_counts", "expected >= 0 AND found >= 0 AND attempts >= 0");
                    table.CheckConstraint("ck_bar_gaps_status", "status IN ('open', 'filled', 'unfillable')");
                    table.ForeignKey(
                        name: "fk_bar_gaps_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalSchema: "market",
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_data_jobs_trigger_started_at",
                schema: "market",
                table: "data_jobs",
                columns: new[] { "trigger", "started_at" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_data_jobs_status",
                schema: "market",
                table: "data_jobs",
                sql: "status IN ('running', 'succeeded', 'failed', 'skipped')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_data_jobs_trigger",
                schema: "market",
                table: "data_jobs",
                sql: "trigger IN ('manual', 'daily')");

            migrationBuilder.CreateIndex(
                name: "ix_bar_gaps_status_date",
                schema: "market",
                table: "bar_gaps",
                columns: new[] { "status", "date" });

            // The runtime role records gaps (insert) and moves them along (update) but never deletes them: like the bars and the job records, they are kept for good.
            migrationBuilder.GrantTableToAppRole(Schemas.Market, "bar_gaps");
            migrationBuilder.NeverDeleteForAppRole(Schemas.Market, "bar_gaps");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bar_gaps",
                schema: "market");

            migrationBuilder.DropIndex(
                name: "ix_data_jobs_trigger_started_at",
                schema: "market",
                table: "data_jobs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_data_jobs_status",
                schema: "market",
                table: "data_jobs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_data_jobs_trigger",
                schema: "market",
                table: "data_jobs");

            migrationBuilder.DropColumn(
                name: "refetch_gaps",
                schema: "market",
                table: "data_jobs");

            migrationBuilder.DropColumn(
                name: "trigger",
                schema: "market",
                table: "data_jobs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_data_jobs_status",
                schema: "market",
                table: "data_jobs",
                sql: "status IN ('running', 'succeeded', 'failed')");
        }
    }
}
