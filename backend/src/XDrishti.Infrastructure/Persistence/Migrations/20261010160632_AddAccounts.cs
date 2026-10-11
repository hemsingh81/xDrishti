using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "accounts");

            migrationBuilder.CreateTable(
                name: "broker_accounts",
                schema: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    broker = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    client_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    credential_ref = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    data_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    data_primary = table.Column<bool>(type: "boolean", nullable: false),
                    data_standby = table.Column<bool>(type: "boolean", nullable: false),
                    live_feed_budget = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_broker_accounts", x => x.id);
                    table.CheckConstraint("ck_broker_accounts_data_role", "NOT (data_primary AND data_standby) AND (data_enabled OR NOT (data_primary OR data_standby))");
                });

            migrationBuilder.CreateTable(
                name: "portfolio_buckets",
                schema: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_portfolio_buckets", x => x.id);
                    table.UniqueConstraint("ak_portfolio_buckets_name", x => x.name);
                });

            migrationBuilder.CreateTable(
                name: "risk_profiles",
                schema: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    is_seeded = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    risk_per_trade_pct = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    max_capital_per_trade_pct = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    max_new_entries_per_day = table.Column<int>(type: "integer", nullable: false),
                    max_open_positions = table.Column<int>(type: "integer", nullable: false),
                    max_total_risk_pct = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    max_per_sector = table.Column<int>(type: "integer", nullable: false),
                    max_pair_correlation = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    max_intraday = table.Column<int>(type: "integer", nullable: false),
                    max_swing = table.Column<int>(type: "integer", nullable: false),
                    daily_loss_r = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    daily_loss_pct = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    weekly_loss_r = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    weekly_loss_action = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_risk_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "account_events",
                schema: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    @event = table.Column<string>(name: "event", type: "character varying(48)", maxLength: 48, nullable: false),
                    detail = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    actor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_account_events_broker_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "broker_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "portfolio_settings",
                schema: "accounts",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    default_bucket = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    history_from = table.Column<DateOnly>(type: "date", nullable: true),
                    include_in_totals = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_portfolio_settings", x => x.account_id);
                    table.ForeignKey(
                        name: "fk_portfolio_settings_broker_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "broker_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_portfolio_settings_portfolio_buckets_default_bucket",
                        column: x => x.default_bucket,
                        principalSchema: "accounts",
                        principalTable: "portfolio_buckets",
                        principalColumn: "name",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trading_settings",
                schema: "accounts",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    risk_profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    capital_source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    capital_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    intraday = table.Column<bool>(type: "boolean", nullable: false),
                    swing = table.Column<bool>(type: "boolean", nullable: false),
                    intraday_instruments = table.Column<int>(type: "integer", nullable: false),
                    swing_buy_instruments = table.Column<int>(type: "integer", nullable: false),
                    swing_sell_instruments = table.Column<int>(type: "integer", nullable: false),
                    override_risk_per_trade_pct = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    override_max_new_entries_per_day = table.Column<int>(type: "integer", nullable: true),
                    override_max_open_positions = table.Column<int>(type: "integer", nullable: true),
                    override_daily_loss_pct = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trading_settings", x => x.account_id);
                    table.ForeignKey(
                        name: "fk_trading_settings_broker_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "broker_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_trading_settings_risk_profiles_risk_profile_id",
                        column: x => x.risk_profile_id,
                        principalSchema: "accounts",
                        principalTable: "risk_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_events_account_id_at",
                schema: "accounts",
                table: "account_events",
                columns: new[] { "account_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ux_broker_accounts_credential_ref_active",
                schema: "accounts",
                table: "broker_accounts",
                column: "credential_ref",
                unique: true,
                filter: "archived_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_broker_accounts_data_primary",
                schema: "accounts",
                table: "broker_accounts",
                column: "data_primary",
                unique: true,
                filter: "data_primary AND archived_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_broker_accounts_data_standby",
                schema: "accounts",
                table: "broker_accounts",
                column: "data_standby",
                unique: true,
                filter: "data_standby AND archived_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_broker_accounts_slug_active",
                schema: "accounts",
                table: "broker_accounts",
                column: "slug",
                unique: true,
                filter: "archived_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_portfolio_buckets_name",
                schema: "accounts",
                table: "portfolio_buckets",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_portfolio_settings_default_bucket",
                schema: "accounts",
                table: "portfolio_settings",
                column: "default_bucket");

            migrationBuilder.CreateIndex(
                name: "ix_trading_settings_risk_profile_id",
                schema: "accounts",
                table: "trading_settings",
                column: "risk_profile_id");

            // Names are unique ignoring case (EF Core cannot model expression indexes).
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_portfolio_buckets_name_lower ON accounts.portfolio_buckets (lower(name))");
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_risk_profiles_name_lower ON accounts.risk_profiles (lower(name))");

            // Seed data (docs/design/16-configuration.md, risk_profiles). Idempotent: a row that exists already is left alone.
            migrationBuilder.Sql("""
                INSERT INTO accounts.risk_profiles
                    (id, name, is_seeded, created_at, updated_at, risk_per_trade_pct, max_capital_per_trade_pct, max_new_entries_per_day, max_open_positions,
                     max_total_risk_pct, max_per_sector, max_pair_correlation, max_intraday, max_swing, daily_loss_r, daily_loss_pct, weekly_loss_r, weekly_loss_action)
                VALUES
                    ('00000000-0000-7000-8000-000000000001', 'Standard', true, now(), now(), 0.5, 25, 3, 5, 3.0, 2, 0.70, 2, 2, 2, 1.5, 5, 'HalveSize'),
                    ('00000000-0000-7000-8000-000000000002', 'Conservative', true, now(), now(), 0.25, 25, 2, 3, 3.0, 2, 0.70, 2, 2, 2, 1.0, 5, 'HalveSize')
                ON CONFLICT DO NOTHING;
                """);
            migrationBuilder.Sql("""
                INSERT INTO accounts.portfolio_buckets (id, name, created_at)
                VALUES
                    ('00000000-0000-7000-8000-000000000101', 'Trading', now()),
                    ('00000000-0000-7000-8000-000000000102', 'MyLongTerm', now()),
                    ('00000000-0000-7000-8000-000000000103', 'MyChoice', now())
                ON CONFLICT DO NOTHING;
                """);

            migrationBuilder.GrantSchemaUsageToAppRole(Schemas.Accounts);

            // Like the sign-in audit trail, the account audit trail is append-only for the runtime role.
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Accounts, "account_events");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_events",
                schema: "accounts");

            migrationBuilder.DropTable(
                name: "portfolio_settings",
                schema: "accounts");

            migrationBuilder.DropTable(
                name: "trading_settings",
                schema: "accounts");

            migrationBuilder.DropTable(
                name: "portfolio_buckets",
                schema: "accounts");

            migrationBuilder.DropTable(
                name: "broker_accounts",
                schema: "accounts");

            migrationBuilder.DropTable(
                name: "risk_profiles",
                schema: "accounts");
        }
    }
}
