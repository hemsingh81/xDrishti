using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPortfolioHoldings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // P2-01 / P2-05 / ADR 0012: holdings with owner-dated lots (schema portfolio) and the automatic refresh settings of an account.
            // The three new portfolio_settings columns get database defaults only to fill the rows that exist already (refresh off, 15 minutes, 16:00 IST);
            // the application always writes all three, so the model carries no default values.
            migrationBuilder.EnsureSchema(
                name: "portfolio");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "refresh_daily_time_ist",
                schema: "accounts",
                table: "portfolio_settings",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(16, 0, 0));

            migrationBuilder.AddColumn<int>(
                name: "refresh_interval_minutes",
                schema: "accounts",
                table: "portfolio_settings",
                type: "integer",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<string>(
                name: "refresh_mode",
                schema: "accounts",
                table: "portfolio_settings",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "off");

            migrationBuilder.CreateTable(
                name: "holding_syncs",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trigger = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    requested_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    new_count = table.Column<int>(type: "integer", nullable: false),
                    increased_count = table.Column<int>(type: "integer", nullable: false),
                    decreased_count = table.Column<int>(type: "integer", nullable: false),
                    closed_count = table.Column<int>(type: "integer", nullable: false),
                    unchanged_count = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holding_syncs", x => x.id);
                    table.CheckConstraint("ck_holding_syncs_status", "status IN ('running', 'succeeded', 'failed', 'skipped')");
                    table.CheckConstraint("ck_holding_syncs_trigger", "trigger IN ('manual', 'auto', 'import')");
                    table.ForeignKey(
                        name: "fk_holding_syncs_broker_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "broker_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "holdings",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bucket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: true),
                    exchange = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    security_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    isin = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    trading_symbol = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    qty = table.Column<long>(type: "bigint", nullable: false),
                    avg_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    last_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    last_price_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holdings", x => x.id);
                    table.CheckConstraint("ck_holdings_qty", "qty >= 0");
                    table.CheckConstraint("ck_holdings_status", "status IN ('open', 'closed')");
                    table.ForeignKey(
                        name: "fk_holdings_broker_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "broker_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_holdings_portfolio_buckets_bucket_id",
                        column: x => x.bucket_id,
                        principalSchema: "accounts",
                        principalTable: "portfolio_buckets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "holding_lots",
                schema: "portfolio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    holding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    acquired_on = table.Column<DateOnly>(type: "date", nullable: false),
                    qty = table.Column<long>(type: "bigint", nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    source = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    uses_default_date = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    close_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    estimated = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holding_lots", x => x.id);
                    table.CheckConstraint("ck_holding_lots_closed", "(closed_on IS NULL) = (close_price IS NULL)");
                    table.CheckConstraint("ck_holding_lots_closed_after_acquired", "closed_on IS NULL OR closed_on >= acquired_on");
                    table.CheckConstraint("ck_holding_lots_qty", "qty > 0");
                    table.CheckConstraint("ck_holding_lots_source", "source IN ('import', 'added', 'manual')");
                    table.CheckConstraint("ck_holding_lots_unit_cost", "unit_cost >= 0");
                    table.ForeignKey(
                        name: "fk_holding_lots_holdings_holding_id",
                        column: x => x.holding_id,
                        principalSchema: "portfolio",
                        principalTable: "holdings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

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

            migrationBuilder.CreateIndex(
                name: "ix_holding_lots_holding_id",
                schema: "portfolio",
                table: "holding_lots",
                column: "holding_id");

            migrationBuilder.CreateIndex(
                name: "ix_holding_syncs_account_started",
                schema: "portfolio",
                table: "holding_syncs",
                columns: new[] { "account_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_holding_syncs_account_trigger_started",
                schema: "portfolio",
                table: "holding_syncs",
                columns: new[] { "account_id", "trigger", "started_at" })
                .Annotation("Npgsql:IndexInclude", new[] { "status" });

            migrationBuilder.CreateIndex(
                name: "ux_holding_syncs_one_running",
                schema: "portfolio",
                table: "holding_syncs",
                column: "account_id",
                unique: true,
                filter: "status = 'running'");

            migrationBuilder.CreateIndex(
                name: "ix_holdings_account_status",
                schema: "portfolio",
                table: "holdings",
                columns: new[] { "account_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_holdings_bucket_id",
                schema: "portfolio",
                table: "holdings",
                column: "bucket_id");

            migrationBuilder.CreateIndex(
                name: "ix_holdings_instrument_id",
                schema: "portfolio",
                table: "holdings",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "ux_holdings_account_security",
                schema: "portfolio",
                table: "holdings",
                columns: new[] { "account_id", "security_id", "exchange" },
                unique: true);

            migrationBuilder.GrantSchemaUsageToAppRole(Schemas.Portfolio);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "holding_lots",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "holding_syncs",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "holdings",
                schema: "portfolio");

            migrationBuilder.DropCheckConstraint(
                name: "ck_portfolio_settings_refresh_interval",
                schema: "accounts",
                table: "portfolio_settings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_portfolio_settings_refresh_mode",
                schema: "accounts",
                table: "portfolio_settings");

            migrationBuilder.DropColumn(
                name: "refresh_daily_time_ist",
                schema: "accounts",
                table: "portfolio_settings");

            migrationBuilder.DropColumn(
                name: "refresh_interval_minutes",
                schema: "accounts",
                table: "portfolio_settings");

            migrationBuilder.DropColumn(
                name: "refresh_mode",
                schema: "accounts",
                table: "portfolio_settings");
        }
    }
}
