using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The proof of a recovered step (spec AU-03, ADR 0240): <c>platform.recovery_items.detail</c> holds the per-session state (jsonb) and the state <c>partial</c> joins the allowed ones. Raw SQL: no EF entity maps the table, so the model
    /// snapshot does not change.
    /// </summary>
    public partial class AddRecoveryItemDetail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE platform.recovery_items ADD COLUMN detail jsonb NULL;
                ALTER TABLE platform.recovery_items DROP CONSTRAINT ck_recovery_items_state;
                ALTER TABLE platform.recovery_items ADD CONSTRAINT ck_recovery_items_state CHECK (state IN ('running', 'succeeded', 'failed', 'blocked', 'waiting', 'superseded', 'partial') AND attempts >= 0 AND session_from <= session_to);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE platform.recovery_items SET state = 'failed', reason = 'partial (downgraded)' WHERE state = 'partial';
                ALTER TABLE platform.recovery_items DROP CONSTRAINT ck_recovery_items_state;
                ALTER TABLE platform.recovery_items ADD CONSTRAINT ck_recovery_items_state CHECK (state IN ('running', 'succeeded', 'failed', 'blocked', 'waiting', 'superseded') AND attempts >= 0 AND session_from <= session_to);
                ALTER TABLE platform.recovery_items DROP COLUMN detail;
                """);
        }
    }
}
