using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The owner's reason for dismissing a kill proposal (spec CF-12 leftover, ADR 0234): one nullable column of <c>confidence.kill_proposals</c> that the runtime role may set in the same single proposed-to-dismissed update that sets the state and the actor. A check ties it
    /// to the state (a dismissed proposal has a reason of 1 to 400 characters, no other state has one) and a dismissal is always by an owner (an automatic decision can only apply). Raw SQL: no EF entity maps the table, so the model snapshot does not change.
    /// </summary>
    public partial class AddKillDismissReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE confidence.kill_proposals ADD COLUMN dismiss_reason varchar(400) NULL");
            migrationBuilder.Sql("""
                ALTER TABLE confidence.kill_proposals ADD CONSTRAINT ck_kill_proposals_dismiss_reason
                CHECK ((state = 'dismissed') = (dismiss_reason IS NOT NULL) AND (dismiss_reason IS NULL OR length(btrim(dismiss_reason)) > 0))
                """);
            migrationBuilder.GrantColumnUpdateToAppRole(Schemas.Confidence, "kill_proposals", "dismiss_reason");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE confidence.kill_proposals DROP CONSTRAINT IF EXISTS ck_kill_proposals_dismiss_reason");
            migrationBuilder.Sql("ALTER TABLE confidence.kill_proposals DROP COLUMN IF EXISTS dismiss_reason");
        }
    }
}
