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
            // Shortcut (ADR 0117): plain CREATE INDEX takes a SHARE lock and blocks writes to these four insert-only tables while it builds.
            // The tables are small (tens of rows a day, single owner, migrations run with the stack stopped), so CREATE INDEX CONCURRENTLY
            // (which cannot run inside the migration transaction) is not worth its cost here. Revisit if a store passes millions of rows.
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
