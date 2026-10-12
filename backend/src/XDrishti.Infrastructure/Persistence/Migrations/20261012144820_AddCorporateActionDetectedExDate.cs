using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCorporateActionDetectedExDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "detected_ex_date",
                schema: "market",
                table: "corporate_actions",
                type: "date",
                nullable: true);

            // What the detection found keeps the date it found it on, so that a suggestion the owner moved to another ex-date is not suggested again on the old one.
            migrationBuilder.Sql("UPDATE market.corporate_actions SET detected_ex_date = ex_date WHERE source = 'detected'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "detected_ex_date",
                schema: "market",
                table: "corporate_actions");
        }
    }
}
