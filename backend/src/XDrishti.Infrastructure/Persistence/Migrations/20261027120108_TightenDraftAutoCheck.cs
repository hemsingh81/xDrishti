using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TightenDraftAutoCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_ai_strategy_draft_decisions_auto",
                schema: "ai",
                table: "strategy_draft_decisions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ai_strategy_draft_decisions_auto",
                schema: "ai",
                table: "strategy_draft_decisions",
                sql: "(actor = 'system:auto-draft') = auto AND (NOT auto OR (decision = 'confirmed' AND settings_id IS NOT NULL AND snapshot_hash IS NOT NULL))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_ai_strategy_draft_decisions_auto",
                schema: "ai",
                table: "strategy_draft_decisions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ai_strategy_draft_decisions_auto",
                schema: "ai",
                table: "strategy_draft_decisions",
                sql: "NOT auto OR (decision = 'confirmed' AND settings_id IS NOT NULL AND snapshot_hash IS NOT NULL AND actor = 'system:auto-draft')");
        }
    }
}
