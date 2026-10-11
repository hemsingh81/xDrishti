using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PortfolioDailyRefreshAndFirstImportDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_portfolio_settings_refresh_interval",
                schema: "accounts",
                table: "portfolio_settings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_portfolio_settings_refresh_mode",
                schema: "accounts",
                table: "portfolio_settings");

            // ADR 0013: the interval mode is gone; accounts that used it are switched off (the owner chooses a daily time again).
            migrationBuilder.Sql("UPDATE accounts.portfolio_settings SET refresh_mode = 'off' WHERE refresh_mode = 'interval';");

            migrationBuilder.DropColumn(
                name: "refresh_interval_minutes",
                schema: "accounts",
                table: "portfolio_settings");

            migrationBuilder.AddColumn<DateOnly>(
                name: "first_import_date",
                schema: "accounts",
                table: "portfolio_settings",
                type: "date",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_portfolio_settings_refresh_mode",
                schema: "accounts",
                table: "portfolio_settings",
                sql: "refresh_mode IN ('off', 'daily')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_portfolio_settings_refresh_mode",
                schema: "accounts",
                table: "portfolio_settings");

            migrationBuilder.DropColumn(
                name: "first_import_date",
                schema: "accounts",
                table: "portfolio_settings");

            migrationBuilder.AddColumn<int>(
                name: "refresh_interval_minutes",
                schema: "accounts",
                table: "portfolio_settings",
                type: "integer",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddCheckConstraint(
                name: "ck_portfolio_settings_refresh_interval",
                schema: "accounts",
                table: "portfolio_settings",
                sql: "refresh_interval_minutes IN (5, 15, 30, 60)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_portfolio_settings_refresh_mode",
                schema: "accounts",
                table: "portfolio_settings",
                sql: "refresh_mode IN ('off', 'interval', 'daily')");
        }
    }
}
