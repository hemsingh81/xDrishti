using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanBasisFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "basis_flags_json",
                schema: "trading",
                table: "plans",
                type: "text",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_plans_flags_json",
                schema: "trading",
                table: "plans",
                sql: "basis_flags_json IS NULL OR left(basis_flags_json, 1) = '{'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_plans_flags_json",
                schema: "trading",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "basis_flags_json",
                schema: "trading",
                table: "plans");
        }
    }
}
