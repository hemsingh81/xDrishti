using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlanRunsOnePublishedPerSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "uq_plan_runs_published_for_date",
                schema: "trading",
                table: "plan_runs",
                column: "for_date",
                unique: true,
                filter: "outcome = 'published'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_plan_runs_published_for_date",
                schema: "trading",
                table: "plan_runs");
        }
    }
}
