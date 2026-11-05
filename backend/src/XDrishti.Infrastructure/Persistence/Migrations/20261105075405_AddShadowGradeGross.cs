using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The gross R of a shadow grade (spec CF-08 part B, ADR 0212): <c>r_gross</c> is the replay's filled R of the same trade (what <c>TicketGrader</c> uses when the trade is not charged), stored beside <c>r_sim</c> (net when the replay charged the trade) so that the summary can show the gross figures
    /// while the charge sheet is not confirmed as the owner's. Nullable; the table is empty and the switch is OFF when this is added, so nothing is back-filled. Part A's migration is not edited.
    /// </summary>
    public partial class AddShadowGradeGross : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "r_gross",
                schema: "paper",
                table: "shadow_grades",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "r_gross",
                schema: "paper",
                table: "shadow_grades");
        }
    }
}
