using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>The ledger's trial kind <c>cells_refresh</c> (spec LC-01, ADR 0277): one row per newly stored run of the scheduled cell-statistics refresh.</summary>
    public partial class AddCellsRefreshTrialKind : Migration
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
                sql: "trial_kind IN ('run','replan','null','training_sweep','system_replay','cells_refresh') AND extra_trials >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // the ledger is insert-only evidence of looks: Down refuses while a refresh row exists instead of deleting it
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM lab.test_ledger WHERE trial_kind = 'cells_refresh') THEN
                        RAISE EXCEPTION 'lab.test_ledger holds cells_refresh rows; they are kept as evidence, so this migration is not undone';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_test_ledger_trial",
                schema: "lab",
                table: "test_ledger");

            migrationBuilder.AddCheckConstraint(
                name: "ck_test_ledger_trial",
                schema: "lab",
                table: "test_ledger",
                sql: "trial_kind IN ('run','replan','null','training_sweep','system_replay') AND extra_trials >= 0");
        }
    }
}
