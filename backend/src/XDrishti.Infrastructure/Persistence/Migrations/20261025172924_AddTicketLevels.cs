using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketLevels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "entry_ref_price",
                schema: "trading",
                table: "plan_items",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "levels_session",
                schema: "trading",
                table: "plan_items",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "reward_risk",
                schema: "trading",
                table: "plan_items",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "stop_anchor",
                schema: "trading",
                table: "plan_items",
                type: "character varying(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "stop_level",
                schema: "trading",
                table: "plan_items",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "target_level",
                schema: "trading",
                table: "plan_items",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_plan_items_levels",
                schema: "trading",
                table: "plan_items",
                sql: "(entry_ref_price IS NULL AND levels_session IS NULL AND stop_level IS NULL AND stop_anchor IS NULL AND target_level IS NULL AND reward_risk IS NULL) OR (entry_ref_price IS NOT NULL AND entry_ref_price > 0 AND levels_session IS NOT NULL AND (stop_level IS NULL) = (stop_anchor IS NULL) AND (stop_anchor IS NULL OR stop_anchor IN ('level', 'close_of_d')) AND (stop_level IS NULL OR (stop_level > 0 AND ((side = 'BUY' AND stop_level < entry_ref_price) OR (side = 'SELL' AND stop_level > entry_ref_price)))) AND (target_level IS NULL) = (reward_risk IS NULL) AND (target_level IS NULL OR (stop_level IS NOT NULL AND target_level > 0 AND reward_risk > 0 AND ((side = 'BUY' AND target_level > entry_ref_price) OR (side = 'SELL' AND target_level < entry_ref_price)))))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_plan_items_levels",
                schema: "trading",
                table: "plan_items");

            migrationBuilder.DropColumn(
                name: "entry_ref_price",
                schema: "trading",
                table: "plan_items");

            migrationBuilder.DropColumn(
                name: "levels_session",
                schema: "trading",
                table: "plan_items");

            migrationBuilder.DropColumn(
                name: "reward_risk",
                schema: "trading",
                table: "plan_items");

            migrationBuilder.DropColumn(
                name: "stop_anchor",
                schema: "trading",
                table: "plan_items");

            migrationBuilder.DropColumn(
                name: "stop_level",
                schema: "trading",
                table: "plan_items");

            migrationBuilder.DropColumn(
                name: "target_level",
                schema: "trading",
                table: "plan_items");
        }
    }
}
