using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Tool access of the assistant (spec P9-05, ADR 0139): the view <c>aiv.holding_groups</c> (account, symbol, bucket; portfolio context) with its two revokes, and the column-level grants of the read role on the four tables the report reader names. The tool server reads
    /// reports through that reader, so the grants are exactly its columns (a test runs the real reader as the role). <c>xd_mcp_sql</c> gets no table.
    /// </summary>
    public partial class AddAiToolAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. The group names of the holdings for get_portfolio. The bucket name comes through a helper view of schema ai, like the account label (the view definitions of aiv never name the accounts schema); nobody but the view owner may read it.
            migrationBuilder.Sql("CREATE VIEW ai.bucket_labels AS SELECT id, name AS label FROM accounts.portfolio_buckets");
            migrationBuilder.Sql("""
                CREATE VIEW aiv.holding_groups WITH (security_barrier = true) AS
                    SELECT
                        al.label AS account,
                        t.trading_symbol AS symbol,
                        bl.label AS bucket
                    FROM portfolio.holdings t LEFT JOIN ai.account_labels al ON al.id = t.account_id LEFT JOIN ai.bucket_labels bl ON bl.id = t.bucket_id
                    WHERE ai.context_allows('portfolio')
                """);

            // 2. The two revokes every new view of aiv ends with (P9-02 AC 13a): xd_app holds DML on every table the owner creates, and a simple view is auto-updatable.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                        REVOKE ALL ON aiv.holding_groups FROM xd_app;
                        REVOKE ALL ON ai.bucket_labels FROM xd_app;
                    END IF;
                END $$
                """);
            migrationBuilder.Sql("REVOKE ALL ON aiv.holding_groups FROM PUBLIC");
            migrationBuilder.Sql("REVOKE ALL ON ai.bucket_labels FROM PUBLIC");

            // 3. The report engine reads the stored runs through the very reader of the Reports screen (spec P9-05 AC 13): the read role gets column-level SELECT on exactly the columns that reader names, on four tables, and nothing else outside aiv and ai.settings.
            //    USAGE on the two schemas is needed to name a table; it gives no right on a table. xd_mcp_sql gets nothing here.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_mcp_read') THEN
                        GRANT SELECT ON aiv.holding_groups TO xd_mcp_read, xd_mcp_sql;
                        GRANT USAGE ON SCHEMA research, lab TO xd_mcp_read;
                        GRANT SELECT (id, run_key, strategy_id, universe_kind, universe_label, instruments, instruments_run, from_date, to_date, costs_sheet_id, trade_count, open_at_end_count, created_at, costs_rates_sha256, created_by) ON research.sim_runs TO xd_mcp_read;
                        GRANT SELECT (run_id, trade_key, strategy_id, trigger_timeframe, side, instrument_id, symbol, basket_ids, trade_date, exit_epoch_seconds, r_filled, r_net, sessions_held, ordinal, open_at_end, mae_r, mfe_r, entry_epoch_seconds, exit_reason) ON research.sim_trades TO xd_mcp_read;
                        GRANT SELECT (comparison_key, member_run_keys, member_candidate_ids) ON research.sim_comparisons TO xd_mcp_read;
                        GRANT SELECT (id, style) ON lab.strategies TO xd_mcp_read;
                    END IF;
                END $$
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_mcp_read') THEN
                        REVOKE SELECT ON research.sim_runs, research.sim_trades, research.sim_comparisons, lab.strategies FROM xd_mcp_read;
                        REVOKE USAGE ON SCHEMA research, lab FROM xd_mcp_read;
                    END IF;
                END $$
                """);
            migrationBuilder.Sql("DROP VIEW aiv.holding_groups");
            migrationBuilder.Sql("DROP VIEW ai.bucket_labels");
        }
    }
}
