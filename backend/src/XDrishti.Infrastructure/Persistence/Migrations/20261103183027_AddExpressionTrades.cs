using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExpressionTrades : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // P14-08, ADR 0197: the runs and the rows of the option re-mark of a stored cash run. Raw SQL on purpose: nothing maps these tables in the EF model; the store writes with SQL. Insert-only for the runtime role, granted table by table
            // (the research schema is already usable by it; a schema-wide re-grant would give back UPDATE and DELETE). The execution role and the read-only roles are granted nothing.
            migrationBuilder.Sql("""
                CREATE TABLE research.expression_runs (
                    run_id char(64) NOT NULL,
                    cash_run_key char(64) NOT NULL,
                    profile_hash char(64) NOT NULL,
                    remark_version varchar(16) NOT NULL,
                    inputs jsonb NOT NULL,
                    trades integer NOT NULL,
                    paired integer NOT NULL,
                    unpaired jsonb NOT NULL,
                    bs_fallback_share double precision NULL,
                    labels text[] NOT NULL,
                    created_at timestamptz NOT NULL DEFAULT now(),
                    CONSTRAINT pk_expression_runs PRIMARY KEY (run_id),
                    CONSTRAINT ck_expression_runs_id CHECK (run_id ~ '^[0-9a-f]{64}$'),
                    CONSTRAINT ck_expression_runs_cash_run CHECK (cash_run_key ~ '^[0-9a-f]{64}$'),
                    CONSTRAINT ck_expression_runs_profile CHECK (profile_hash ~ '^[0-9a-f]{64}$'),
                    CONSTRAINT ck_expression_runs_counts CHECK (trades >= 0 AND paired >= 0 AND paired <= trades),
                    CONSTRAINT ck_expression_runs_json CHECK (jsonb_typeof(inputs) = 'object' AND jsonb_typeof(unpaired) = 'object'),
                    CONSTRAINT ck_expression_runs_share CHECK ((bs_fallback_share IS NULL) = (paired = 0) AND (bs_fallback_share IS NULL OR (bs_fallback_share >= 0 AND bs_fallback_share <= 1)))
                )
                """);
            migrationBuilder.Sql("CREATE INDEX ix_expression_runs_cash_run ON research.expression_runs (cash_run_key, profile_hash)");

            migrationBuilder.Sql("""
                CREATE TABLE research.expression_trades (
                    run_id char(64) NOT NULL,
                    trade_key varchar(200) NOT NULL,
                    fate varchar(24) NOT NULL,
                    side char(2) NOT NULL,
                    expiry_flag char(1) NULL,
                    expiry_code smallint NULL,
                    expiry date NULL,
                    expiry_source varchar(8) NOT NULL,
                    strike_px integer NULL,
                    entry_ts timestamptz NOT NULL,
                    exit_ts timestamptz NOT NULL,
                    entry_premium_px integer NULL,
                    exit_premium_px integer NULL,
                    units double precision NULL,
                    gross_inr numeric(18,2) NULL,
                    net_inr numeric(18,2) NULL,
                    r_equiv double precision NULL,
                    cash_r double precision NULL,
                    exit_reason varchar(24) NOT NULL,
                    lot integer NULL,
                    sizable varchar(24) NOT NULL,
                    labels text[] NOT NULL,
                    CONSTRAINT pk_expression_trades PRIMARY KEY (run_id, trade_key),
                    CONSTRAINT fk_expression_trades_run FOREIGN KEY (run_id) REFERENCES research.expression_runs (run_id) ON DELETE RESTRICT,
                    CONSTRAINT ck_expression_trades_fate CHECK (fate IN ('paired', 'no_series', 'no_series_for_expiry', 'expiry_unknown', 'strike_left_window', 'archive_unusable', 'no_iv', 'illiquid', 'liquidity_unknown', 'basis_mismatch', 'open_at_end', 'spread_unavailable', 'risk_invalid')),
                    CONSTRAINT ck_expression_trades_side CHECK (side IN ('CE', 'PE')),
                    CONSTRAINT ck_expression_trades_expiry CHECK ((expiry_flag IS NULL OR expiry_flag IN ('W', 'M')) AND (expiry_code IS NULL OR expiry_code BETWEEN 1 AND 9) AND expiry_source IN ('contract', 'derived', 'none')),
                    CONSTRAINT ck_expression_trades_times CHECK (entry_ts <= exit_ts),
                    CONSTRAINT ck_expression_trades_paired CHECK ((fate = 'paired') = (strike_px IS NOT NULL AND expiry IS NOT NULL AND entry_premium_px IS NOT NULL AND exit_premium_px IS NOT NULL AND units IS NOT NULL AND gross_inr IS NOT NULL AND r_equiv IS NOT NULL AND cash_r IS NOT NULL)),
                    CONSTRAINT ck_expression_trades_unpaired CHECK (fate = 'paired' OR (strike_px IS NULL AND entry_premium_px IS NULL AND exit_premium_px IS NULL AND units IS NULL AND gross_inr IS NULL AND net_inr IS NULL AND r_equiv IS NULL AND cash_r IS NULL)),
                    CONSTRAINT ck_expression_trades_numbers CHECK ((entry_premium_px IS NULL OR entry_premium_px > 0) AND (exit_premium_px IS NULL OR exit_premium_px > 0) AND (units IS NULL OR (units > 0 AND units < 'Infinity'::double precision))
                        AND (r_equiv IS NULL OR (r_equiv > '-Infinity'::double precision AND r_equiv < 'Infinity'::double precision)) AND (cash_r IS NULL OR (cash_r > '-Infinity'::double precision AND cash_r < 'Infinity'::double precision))),
                    CONSTRAINT ck_expression_trades_sizable CHECK (sizable IN ('sizable', 'not_sizable', 'lot_unknown', 'delta_unavailable', 'risk_invalid', 'n/a'))
                )
                """);
            migrationBuilder.Sql("CREATE INDEX ix_expression_trades_fate ON research.expression_trades (run_id, fate)");

            migrationBuilder.GrantTableToAppRole(Schemas.Research, "expression_runs");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Research, "expression_runs");
            migrationBuilder.GrantTableToAppRole(Schemas.Research, "expression_trades");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Research, "expression_trades");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS research.expression_trades");
            migrationBuilder.Sql("DROP TABLE IF EXISTS research.expression_runs");
        }
    }
}
