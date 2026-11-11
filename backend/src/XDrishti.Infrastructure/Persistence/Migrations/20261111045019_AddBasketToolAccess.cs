using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Tool access of the assistant to the stored basket context (spec BK-12, ADR 0264): column-level SELECT for the read role on the thirteen columns the context report names of <c>market.basket_context_daily</c> (not <c>created_at</c>), and USAGE on schema <c>market</c> to name the table.
    /// USAGE gives no right on any other table of the schema. <c>xd_mcp_sql</c> gets nothing; the basket list comes through the existing view <c>aiv.baskets</c>, so no new view is needed.
    /// </summary>
    public partial class AddBasketToolAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_mcp_read') THEN
                        GRANT USAGE ON SCHEMA market TO xd_mcp_read;
                        GRANT SELECT (basket_id, as_of, n_members, n_voters, b20, b50, b200, ad_share, new_high_share, dispersion, rho_bar, state, membership_basis) ON market.basket_context_daily TO xd_mcp_read;
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
                        REVOKE SELECT ON market.basket_context_daily FROM xd_mcp_read;
                        REVOKE USAGE ON SCHEMA market FROM xd_mcp_read;
                    END IF;
                END $$
                """);
        }
    }
}
