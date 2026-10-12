using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "proposal_status",
                schema: "market",
                table: "trading_days",
                type: "character varying(9)",
                maxLength: 9,
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "proposed_from",
                schema: "market",
                table: "trading_days",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "proposed_kind",
                schema: "market",
                table: "trading_days",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "proposed_note",
                schema: "market",
                table: "trading_days",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "proposed_to",
                schema: "market",
                table: "trading_days",
                type: "time without time zone",
                nullable: true);

            // The row version (PostgreSQL's system column xmin) needs no DDL: it exists on every table, the model only maps it (optimistic concurrency of the calendar rows).

            migrationBuilder.AddCheckConstraint(
                name: "ck_trading_days_proposal",
                schema: "market",
                table: "trading_days",
                sql: "(proposal_status IS NULL AND proposed_kind IS NULL AND proposed_from IS NULL AND proposed_to IS NULL AND proposed_note IS NULL) OR (proposal_status = 'dismissed' AND proposed_kind IS NULL AND proposed_from IS NULL AND proposed_to IS NULL AND proposed_note IS NULL) OR (proposal_status = 'proposed' AND ((proposed_kind = 'holiday' AND proposed_from IS NULL AND proposed_to IS NULL) OR (proposed_kind = 'special' AND proposed_from IS NOT NULL AND proposed_to IS NOT NULL AND proposed_from < proposed_to)))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_trading_days_proposal_status",
                schema: "market",
                table: "trading_days",
                sql: "proposal_status IS NULL OR proposal_status IN ('proposed', 'dismissed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_trading_days_proposal",
                schema: "market",
                table: "trading_days");

            migrationBuilder.DropCheckConstraint(
                name: "ck_trading_days_proposal_status",
                schema: "market",
                table: "trading_days");

            migrationBuilder.DropColumn(
                name: "proposal_status",
                schema: "market",
                table: "trading_days");

            migrationBuilder.DropColumn(
                name: "proposed_from",
                schema: "market",
                table: "trading_days");

            migrationBuilder.DropColumn(
                name: "proposed_kind",
                schema: "market",
                table: "trading_days");

            migrationBuilder.DropColumn(
                name: "proposed_note",
                schema: "market",
                table: "trading_days");

            migrationBuilder.DropColumn(
                name: "proposed_to",
                schema: "market",
                table: "trading_days");
        }
    }
}
