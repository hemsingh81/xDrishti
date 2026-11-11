using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpeningBarsRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // BK-03b criterion 8: the daily retention of market.bars_1m_live (OpeningBars:KeepDays). The runtime role stays insert-only on the table; the delete is a function owned by the migration role
            // (SECURITY DEFINER, search_path pg_catalog and pg_temp only, every name qualified) that removes only rows older than the cutoff and that the runtime role may only execute. Not callable by PUBLIC. A keep value under 1 day is refused.
            migrationBuilder.Sql("""
                CREATE FUNCTION market.prune_opening_bars(keep_days integer) RETURNS integer
                LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $$
                DECLARE removed integer;
                BEGIN
                    IF keep_days IS NULL OR keep_days < 1 THEN
                        RAISE EXCEPTION 'keep_days must be at least 1';
                    END IF;
                    DELETE FROM market.bars_1m_live WHERE minute_start < now() - make_interval(days => keep_days);
                    GET DIAGNOSTICS removed = ROW_COUNT;
                    RETURN removed;
                END
                $$
                """);
            migrationBuilder.Sql("REVOKE ALL ON FUNCTION market.prune_opening_bars(integer) FROM PUBLIC");
            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = '{MigrationBuilderExtensions.AppRole}') THEN
                        GRANT EXECUTE ON FUNCTION market.prune_opening_bars(integer) TO {MigrationBuilderExtensions.AppRole};
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS market.prune_opening_bars(integer)");
        }
    }
}
