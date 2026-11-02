using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEventExitExitReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_sim_trades_exit_reason",
                schema: "research",
                table: "sim_trades");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sim_trades_exit_reason",
                schema: "research",
                table: "sim_trades",
                sql: "exit_reason IN ('stop', 'target', 'squareoff', 'endofdata', 'maxhold', 'emaexit', 'timestop', 'eventexit')");
        }

        /// <inheritdoc />
        // Down fails once a row with exit_reason 'eventexit' exists: research.sim_trades is append-only and nothing here rewrites or deletes a stored trade, so the old constraint cannot be re-added over it.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_sim_trades_exit_reason",
                schema: "research",
                table: "sim_trades");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sim_trades_exit_reason",
                schema: "research",
                table: "sim_trades",
                sql: "exit_reason IN ('stop', 'target', 'squareoff', 'endofdata', 'maxhold', 'emaexit', 'timestop')");
        }
    }
}
