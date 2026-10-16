using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AttentionRetentionGrant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR 0030: the attention items are working memory (they remember "since" and the 24 hours of "resolved"), not a record, so the healer tick deletes the resolved ones after the retention. This is the one table of the portfolio schema whose DELETE the runtime role gets back
            // (it was taken away by AddSyncHealCounters); every other table keeps its NeverDelete. The delete statement itself only touches resolved rows.
            migrationBuilder.GrantTableToAppRole(Schemas.Portfolio, "attention_items");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.NeverDeleteForAppRole(Schemas.Portfolio, "attention_items");
        }
    }
}
