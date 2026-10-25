using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditReadIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_plan_events_created_at",
                schema: "trading",
                table: "plan_events",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_learning_changelog_created_at",
                schema: "research",
                table: "learning_changelog",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_approvals_decided_at",
                schema: "workflow",
                table: "approvals",
                column: "decided_at");

            migrationBuilder.CreateIndex(
                name: "ix_account_events_at",
                schema: "accounts",
                table: "account_events",
                column: "at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_plan_events_created_at",
                schema: "trading",
                table: "plan_events");

            migrationBuilder.DropIndex(
                name: "ix_learning_changelog_created_at",
                schema: "research",
                table: "learning_changelog");

            migrationBuilder.DropIndex(
                name: "ix_approvals_decided_at",
                schema: "workflow",
                table: "approvals");

            migrationBuilder.DropIndex(
                name: "ix_account_events_at",
                schema: "accounts",
                table: "account_events");
        }
    }
}
