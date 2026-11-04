using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExpressionLegs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // P14-10, ADR 0200: one row per leg of a paired structure (debit spread, credit spread, iron condor). Raw SQL on purpose (nothing maps these tables in the EF model). Insert-only for the runtime role, granted table by table;
            // the execution role and the read-only roles are granted nothing.
            migrationBuilder.Sql("""
                CREATE TABLE research.expression_legs (
                    run_id char(64) NOT NULL,
                    trade_key varchar(200) NOT NULL,
                    leg smallint NOT NULL,
                    side char(2) NOT NULL,
                    position char(5) NOT NULL,
                    strike_px integer NOT NULL,
                    entry_premium_px integer NOT NULL,
                    exit_premium_px integer NOT NULL,
                    labels text[] NOT NULL,
                    CONSTRAINT pk_expression_legs PRIMARY KEY (run_id, trade_key, leg),
                    CONSTRAINT fk_expression_legs_trade FOREIGN KEY (run_id, trade_key) REFERENCES research.expression_trades (run_id, trade_key) ON DELETE RESTRICT,
                    CONSTRAINT ck_expression_legs_leg CHECK (leg BETWEEN 0 AND 3),
                    CONSTRAINT ck_expression_legs_side CHECK (side IN ('CE', 'PE')),
                    CONSTRAINT ck_expression_legs_position CHECK (position IN ('long', 'short')),
                    CONSTRAINT ck_expression_legs_prices CHECK (strike_px > 0 AND entry_premium_px > 0 AND exit_premium_px >= 0)
                )
                """);
            migrationBuilder.GrantTableToAppRole(Schemas.Research, "expression_legs");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Research, "expression_legs");

            // The trade rows of a structure differ from a single option in four ways, so four checks of AddExpressionTrades are replaced (constraints only; no row changes): the three fates a structure adds, the condor's side word, a structure that
            // can be worth nothing at the exit (a zero exit value), and a condor that has no cash twin (cash_r is then null and the row says so in its labels).
            migrationBuilder.Sql("ALTER TABLE research.expression_trades DROP CONSTRAINT ck_expression_trades_fate, DROP CONSTRAINT ck_expression_trades_side, DROP CONSTRAINT ck_expression_trades_paired, DROP CONSTRAINT ck_expression_trades_numbers");
            migrationBuilder.Sql("""
                ALTER TABLE research.expression_trades
                    ADD CONSTRAINT ck_expression_trades_fate CHECK (fate IN ('paired', 'no_series', 'no_series_for_expiry', 'expiry_unknown', 'strike_left_window', 'archive_unusable', 'no_iv', 'illiquid', 'liquidity_unknown', 'basis_mismatch', 'open_at_end', 'spread_unavailable', 'risk_invalid', 'not_range', 'structure_invalid', 'lot_unknown', 'structure_inconsistent')),
                    ADD CONSTRAINT ck_expression_trades_side CHECK (side IN ('CE', 'PE', 'IC')),
                    ADD CONSTRAINT ck_expression_trades_paired CHECK ((fate = 'paired') = (strike_px IS NOT NULL AND expiry IS NOT NULL AND entry_premium_px IS NOT NULL AND exit_premium_px IS NOT NULL AND units IS NOT NULL AND gross_inr IS NOT NULL AND r_equiv IS NOT NULL AND (cash_r IS NOT NULL OR 'no_cash_twin' = ANY (labels)))),
                    ADD CONSTRAINT ck_expression_trades_numbers CHECK ((entry_premium_px IS NULL OR entry_premium_px > 0) AND (exit_premium_px IS NULL OR exit_premium_px >= 0) AND (units IS NULL OR (units > 0 AND units < 'Infinity'::double precision))
                        AND (r_equiv IS NULL OR (r_equiv > '-Infinity'::double precision AND r_equiv < 'Infinity'::double precision)) AND (cash_r IS NULL OR (cash_r > '-Infinity'::double precision AND cash_r < 'Infinity'::double precision)))
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS research.expression_legs");

            // The original checks are put back as NOT VALID: they bind every later insert and update, and no stored row is deleted or rewritten (a structure's row stays readable; this table is insert-only evidence).
            migrationBuilder.Sql("ALTER TABLE research.expression_trades DROP CONSTRAINT ck_expression_trades_fate, DROP CONSTRAINT ck_expression_trades_side, DROP CONSTRAINT ck_expression_trades_paired, DROP CONSTRAINT ck_expression_trades_numbers");
            migrationBuilder.Sql("""
                ALTER TABLE research.expression_trades
                    ADD CONSTRAINT ck_expression_trades_fate CHECK (fate IN ('paired', 'no_series', 'no_series_for_expiry', 'expiry_unknown', 'strike_left_window', 'archive_unusable', 'no_iv', 'illiquid', 'liquidity_unknown', 'basis_mismatch', 'open_at_end', 'spread_unavailable', 'risk_invalid')) NOT VALID,
                    ADD CONSTRAINT ck_expression_trades_side CHECK (side IN ('CE', 'PE')) NOT VALID,
                    ADD CONSTRAINT ck_expression_trades_paired CHECK ((fate = 'paired') = (strike_px IS NOT NULL AND expiry IS NOT NULL AND entry_premium_px IS NOT NULL AND exit_premium_px IS NOT NULL AND units IS NOT NULL AND gross_inr IS NOT NULL AND r_equiv IS NOT NULL AND cash_r IS NOT NULL)) NOT VALID,
                    ADD CONSTRAINT ck_expression_trades_numbers CHECK ((entry_premium_px IS NULL OR entry_premium_px > 0) AND (exit_premium_px IS NULL OR exit_premium_px > 0) AND (units IS NULL OR (units > 0 AND units < 'Infinity'::double precision))
                        AND (r_equiv IS NULL OR (r_equiv > '-Infinity'::double precision AND r_equiv < 'Infinity'::double precision)) AND (cash_r IS NULL OR (cash_r > '-Infinity'::double precision AND cash_r < 'Infinity'::double precision))) NOT VALID
                """);
        }
    }
}
