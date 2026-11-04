using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The shrunk expectancy of each cell row (spec CF-02, ADR 0204): nullable columns on <c>research.cell_stats</c>, so a row written before this reads "not computed". Adding columns changes no grant: the table stays insert-only for <c>xd_app</c>
    /// (ADR 0045; <c>ResearchGrantsTests</c>). Beyond the spec's ten columns, <c>shrink_n</c> (the n the precision rests on), <c>shrink_mode</c> and <c>shrink_note</c> (why a figure is unavailable) are kept so a stored row says what it was computed for.
    /// </summary>
    public partial class AddCellStatsShrinkage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "blend",
                schema: "research",
                table: "cell_stats",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "estimated_strength",
                schema: "research",
                table: "cell_stats",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "parent_level",
                schema: "research",
                table: "cell_stats",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "parent_mean_r",
                schema: "research",
                table: "cell_stats",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shrink_mode",
                schema: "research",
                table: "cell_stats",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "shrink_n",
                schema: "research",
                table: "cell_stats",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shrink_note",
                schema: "research",
                table: "cell_stats",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "shrunk_lower_r",
                schema: "research",
                table: "cell_stats",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "shrunk_mean_r",
                schema: "research",
                table: "cell_stats",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "shrunk_win_rate",
                schema: "research",
                table: "cell_stats",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "strength_source",
                schema: "research",
                table: "cell_stats",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tau_source",
                schema: "research",
                table: "cell_stats",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "tau_squared",
                schema: "research",
                table: "cell_stats",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_cell_stats_shrinkage",
                schema: "research",
                table: "cell_stats",
                sql: "(parent_level IS NULL OR parent_level IN ('regime','star','strategy','system')) AND (tau_source IS NULL OR tau_source IN ('estimated','floor')) AND (strength_source IS NULL OR strength_source IN ('estimated','default')) AND (shrink_mode IS NULL OR shrink_mode IN ('ReportOnly','Gate'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_cell_stats_shrinkage",
                schema: "research",
                table: "cell_stats");

            migrationBuilder.DropColumn(
                name: "blend",
                schema: "research",
                table: "cell_stats");

            migrationBuilder.DropColumn(
                name: "estimated_strength",
                schema: "research",
                table: "cell_stats");

            migrationBuilder.DropColumn(
                name: "parent_level",
                schema: "research",
                table: "cell_stats");

            migrationBuilder.DropColumn(
                name: "parent_mean_r",
                schema: "research",
                table: "cell_stats");

            migrationBuilder.DropColumn(
                name: "shrink_mode",
                schema: "research",
                table: "cell_stats");

            migrationBuilder.DropColumn(
                name: "shrink_n",
                schema: "research",
                table: "cell_stats");

            migrationBuilder.DropColumn(
                name: "shrink_note",
                schema: "research",
                table: "cell_stats");

            migrationBuilder.DropColumn(
                name: "shrunk_lower_r",
                schema: "research",
                table: "cell_stats");

            migrationBuilder.DropColumn(
                name: "shrunk_mean_r",
                schema: "research",
                table: "cell_stats");

            migrationBuilder.DropColumn(
                name: "shrunk_win_rate",
                schema: "research",
                table: "cell_stats");

            migrationBuilder.DropColumn(
                name: "strength_source",
                schema: "research",
                table: "cell_stats");

            migrationBuilder.DropColumn(
                name: "tau_source",
                schema: "research",
                table: "cell_stats");

            migrationBuilder.DropColumn(
                name: "tau_squared",
                schema: "research",
                table: "cell_stats");
        }
    }
}
