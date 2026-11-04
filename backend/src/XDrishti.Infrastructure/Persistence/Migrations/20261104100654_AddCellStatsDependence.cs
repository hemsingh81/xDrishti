using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Effective n, power and minimum track record on each cell row (spec CF-01, ADR 0203): nullable columns on <c>research.cell_stats</c>, so a row written before this reads "not computed". Adding columns changes no grant: the table stays insert-only for
    /// <c>xd_app</c> (ADR 0045; <c>ResearchGrantsTests</c>). Beyond the spec's seven columns, <c>span_years</c> (years-to-evidence), <c>target_edge_r</c> (the edge the needed trades are about), <c>n_eff_mode</c> and <c>n_eff_note</c> are kept so a stored row
    /// says what it was computed for, in which mode, and why a figure is unavailable.
    /// </summary>
    public partial class AddCellStatsDependence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "design_effect",
                schema: "research",
                table: "cell_stats",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "enough_by_n_eff",
                schema: "research",
                table: "cell_stats",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "min_detectable_edge_r",
                schema: "research",
                table: "cell_stats",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "min_trl",
                schema: "research",
                table: "cell_stats",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "n_eff",
                schema: "research",
                table: "cell_stats",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "n_eff_method",
                schema: "research",
                table: "cell_stats",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "n_eff_mode",
                schema: "research",
                table: "cell_stats",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "n_eff_note",
                schema: "research",
                table: "cell_stats",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "needed_trades",
                schema: "research",
                table: "cell_stats",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "span_years",
                schema: "research",
                table: "cell_stats",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "target_edge_r",
                schema: "research",
                table: "cell_stats",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_cell_stats_n_eff_method",
                schema: "research",
                table: "cell_stats",
                sql: "n_eff_method IS NULL OR n_eff_method IN ('cluster','serial','none')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_cell_stats_n_eff_method",
                schema: "research",
                table: "cell_stats");

            migrationBuilder.DropColumn(
                name: "design_effect",
                schema: "research",
                table: "cell_stats");

            migrationBuilder.DropColumn(
                name: "enough_by_n_eff",
                schema: "research",
                table: "cell_stats");

            migrationBuilder.DropColumn(
                name: "min_detectable_edge_r",
                schema: "research",
                table: "cell_stats");

            migrationBuilder.DropColumn(
                name: "min_trl",
                schema: "research",
                table: "cell_stats");

            migrationBuilder.DropColumn(
                name: "n_eff",
                schema: "research",
                table: "cell_stats");

            migrationBuilder.DropColumn(
                name: "n_eff_method",
                schema: "research",
                table: "cell_stats");

            migrationBuilder.DropColumn(
                name: "n_eff_mode",
                schema: "research",
                table: "cell_stats");

            migrationBuilder.DropColumn(
                name: "n_eff_note",
                schema: "research",
                table: "cell_stats");

            migrationBuilder.DropColumn(
                name: "needed_trades",
                schema: "research",
                table: "cell_stats");

            migrationBuilder.DropColumn(
                name: "span_years",
                schema: "research",
                table: "cell_stats");

            migrationBuilder.DropColumn(
                name: "target_edge_r",
                schema: "research",
                table: "cell_stats");
        }
    }
}
