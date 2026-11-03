using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOptionSpreadModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // P14-07, ADR 0194: the half-spread models of the option cost model. Content-addressed (model_id is the SHA-256 of the inputs) and insert-only. Raw SQL on purpose: nothing maps these tables in the EF model; the store writes with SQL.
            migrationBuilder.Sql("""
                CREATE TABLE research.option_spread_models (
                    model_id char(64) NOT NULL,
                    underlying_id uuid NOT NULL,
                    sessions integer NOT NULL,
                    first_session date NOT NULL,
                    last_session date NOT NULL,
                    settings jsonb NOT NULL,
                    built_at timestamptz NOT NULL DEFAULT now(),
                    CONSTRAINT pk_option_spread_models PRIMARY KEY (model_id),
                    CONSTRAINT ck_option_spread_models_id CHECK (model_id ~ '^[0-9a-f]{64}$'),
                    CONSTRAINT ck_option_spread_models_sessions CHECK (sessions > 0 AND first_session <= last_session),
                    CONSTRAINT ck_option_spread_models_settings CHECK (jsonb_typeof(settings) = 'object')
                )
                """);
            migrationBuilder.Sql("""
                CREATE TABLE research.option_spread_buckets (
                    model_id char(64) NOT NULL,
                    moneyness varchar(8) NOT NULL,
                    dte varchar(8) NOT NULL,
                    tod varchar(16) NOT NULL,
                    n integer NOT NULL,
                    half_spread_pct double precision NOT NULL,
                    CONSTRAINT pk_option_spread_buckets PRIMARY KEY (model_id, moneyness, dte, tod),
                    CONSTRAINT fk_option_spread_buckets_model FOREIGN KEY (model_id) REFERENCES research.option_spread_models (model_id) ON DELETE RESTRICT,
                    CONSTRAINT ck_option_spread_buckets_n CHECK (n > 0),
                    CONSTRAINT ck_option_spread_buckets_pct CHECK (half_spread_pct >= 0 AND half_spread_pct < 'Infinity'::double precision)
                )
                """);

            // Insert-only for the runtime role, granted table by table (a schema-wide re-grant would give back UPDATE and DELETE on the append-only tables of research). The execution role and the MCP roles are granted nothing.
            migrationBuilder.GrantTableToAppRole(Schemas.Research, "option_spread_models");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Research, "option_spread_models");
            migrationBuilder.GrantTableToAppRole(Schemas.Research, "option_spread_buckets");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Research, "option_spread_buckets");

            // The enum CostSegment gains Options and Futures (P14-07); the check on the stored cost segment of a simulated trade is generated from the enum, so the model snapshot widens it. The replay writes neither: a cash trade is IntradayEquity or Delivery.
            migrationBuilder.DropCheckConstraint(
                name: "ck_sim_trades_cost_segment",
                schema: "research",
                table: "sim_trades");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sim_trades_cost_segment",
                schema: "research",
                table: "sim_trades",
                sql: "cost_segment IS NULL OR cost_segment IN ('intradayequity', 'delivery', 'options', 'futures')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS research.option_spread_buckets");
            migrationBuilder.Sql("DROP TABLE IF EXISTS research.option_spread_models");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sim_trades_cost_segment",
                schema: "research",
                table: "sim_trades");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sim_trades_cost_segment",
                schema: "research",
                table: "sim_trades",
                sql: "cost_segment IS NULL OR cost_segment IN ('intradayequity', 'delivery')");
        }
    }
}
