using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiWriteSwitches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "allow_draft_confirm",
                schema: "ai",
                table: "settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "allow_proposal_accept",
                schema: "ai",
                table: "settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "auto_accept_hypothesis",
                schema: "ai",
                table: "settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "auto_lab_quick_test",
                schema: "ai",
                table: "settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // The hash of a settings row must cover EVERY value column (P9-02 criterion 4). A row with the four new switches all false keeps the text of v1, so every old row still verifies; a row with any of them true adds them, so a flipped switch changes the hash.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION ai.stamp_settings() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    PERFORM pg_advisory_xact_lock(hashtextextended('ai.settings', 0));
                    NEW.id := nextval(pg_get_serial_sequence('ai.settings', 'id'));
                    NEW.changed_at := clock_timestamp();
                    NEW.snapshot_hash := encode(sha256(convert_to(
                        format('ai-settings:v1|enabled=%s|portfolio_context=%s|max_tool_calls=%s', NEW.enabled::text, NEW.portfolio_context::text, NEW.max_tool_calls::text)
                        || CASE WHEN NEW.allow_draft_confirm OR NEW.auto_lab_quick_test OR NEW.allow_proposal_accept OR NEW.auto_accept_hypothesis
                                THEN format('|allow_draft_confirm=%s|auto_lab_quick_test=%s|allow_proposal_accept=%s|auto_accept_hypothesis=%s', NEW.allow_draft_confirm::text, NEW.auto_lab_quick_test::text, NEW.allow_proposal_accept::text, NEW.auto_accept_hypothesis::text)
                                ELSE '' END, 'UTF8')), 'hex');
                    RETURN NEW;
                END
                $fn$
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION ai.stamp_settings() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    PERFORM pg_advisory_xact_lock(hashtextextended('ai.settings', 0));
                    NEW.id := nextval(pg_get_serial_sequence('ai.settings', 'id'));
                    NEW.changed_at := clock_timestamp();
                    NEW.snapshot_hash := encode(sha256(convert_to(format('ai-settings:v1|enabled=%s|portfolio_context=%s|max_tool_calls=%s', NEW.enabled::text, NEW.portfolio_context::text, NEW.max_tool_calls::text), 'UTF8')), 'hex');
                    RETURN NEW;
                END
                $fn$
                """);

            migrationBuilder.DropColumn(
                name: "allow_draft_confirm",
                schema: "ai",
                table: "settings");

            migrationBuilder.DropColumn(
                name: "allow_proposal_accept",
                schema: "ai",
                table: "settings");

            migrationBuilder.DropColumn(
                name: "auto_accept_hypothesis",
                schema: "ai",
                table: "settings");

            migrationBuilder.DropColumn(
                name: "auto_lab_quick_test",
                schema: "ai",
                table: "settings");
        }
    }
}
