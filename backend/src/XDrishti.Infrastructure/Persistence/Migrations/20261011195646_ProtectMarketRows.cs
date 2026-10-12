using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProtectMarketRows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The tracked instruments carry a row version (PostgreSQL's system column xmin, mapped like the other row versions): it needs no DDL, the
            // model snapshot records it. Rows of these tables are kept for good (docs/design/06 §3.4): baskets are archived, members get a valid_to, an
            // instrument is untracked by switching it off. The runtime role therefore keeps SELECT, INSERT and UPDATE but loses DELETE.
            migrationBuilder.NeverDeleteForAppRole(Schemas.Market, "baskets");
            migrationBuilder.NeverDeleteForAppRole(Schemas.Market, "basket_members");
            migrationBuilder.NeverDeleteForAppRole(Schemas.Market, "tracked_instruments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.GrantTableToAppRole(Schemas.Market, "baskets");
            migrationBuilder.GrantTableToAppRole(Schemas.Market, "basket_members");
            migrationBuilder.GrantTableToAppRole(Schemas.Market, "tracked_instruments");
        }
    }
}
