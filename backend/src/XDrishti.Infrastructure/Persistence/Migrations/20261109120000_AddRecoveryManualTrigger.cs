using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// A pass the owner starts on the Recovery screen (spec AU-04) has the trigger <c>manual</c>: the check on <c>platform.recovery_passes.trigger</c> admits it. Raw SQL: no EF entity maps the table, so the model snapshot does not change.
    /// </summary>
    public partial class AddRecoveryManualTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE platform.recovery_passes DROP CONSTRAINT ck_recovery_passes_trigger;
                ALTER TABLE platform.recovery_passes ADD CONSTRAINT ck_recovery_passes_trigger CHECK (trigger IN ('startup', 'wake', 'broker_reconnect', 'resume', 'manual'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE platform.recovery_passes SET trigger = 'resume' WHERE trigger = 'manual';
                ALTER TABLE platform.recovery_passes DROP CONSTRAINT ck_recovery_passes_trigger;
                ALTER TABLE platform.recovery_passes ADD CONSTRAINT ck_recovery_passes_trigger CHECK (trigger IN ('startup', 'wake', 'broker_reconnect', 'resume'));
                """);
        }
    }
}
