using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The ledger accepts the trial kind <c>null</c> (CF-07, ADR 0208): one row per family-level random-entry null. Only the check constraint changes; no row is touched. Down removes the null rows (the old constraint refuses them) and restores the old constraint.
    /// </summary>
    public partial class AddLabNullTrialKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_test_ledger_trial",
                schema: "lab",
                table: "test_ledger");

            migrationBuilder.AddCheckConstraint(
                name: "ck_test_ledger_trial",
                schema: "lab",
                table: "test_ledger",
                sql: "trial_kind IN ('run','replan','null') AND extra_trials >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM lab.test_ledger WHERE trial_kind = 'null';");

            migrationBuilder.DropCheckConstraint(
                name: "ck_test_ledger_trial",
                schema: "lab",
                table: "test_ledger");

            migrationBuilder.AddCheckConstraint(
                name: "ck_test_ledger_trial",
                schema: "lab",
                table: "test_ledger",
                sql: "trial_kind IN ('run','replan') AND extra_trials >= 0");
        }
    }
}
