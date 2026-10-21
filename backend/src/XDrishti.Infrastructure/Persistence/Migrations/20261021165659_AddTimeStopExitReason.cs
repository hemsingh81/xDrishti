using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTimeStopExitReason : Migration
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
                sql: "exit_reason IN ('stop', 'target', 'squareoff', 'endofdata', 'maxhold', 'timestop')");
        }

        /// <inheritdoc />
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
                sql: "exit_reason IN ('stop', 'target', 'squareoff', 'endofdata', 'maxhold')");
        }
    }
}
