using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCorporateActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "corporate_actions",
                schema: "market",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ex_date = table.Column<DateOnly>(type: "date", nullable: false),
                    kind = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    numerator = table.Column<int>(type: "integer", nullable: false),
                    denominator = table.Column<int>(type: "integer", nullable: false),
                    factor = table.Column<decimal>(type: "numeric(24,12)", nullable: false),
                    status = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    source = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_corporate_actions", x => x.id);
                    table.CheckConstraint("ck_corporate_actions_decision", "(status = 'suggested' AND decided_at IS NULL AND decided_by IS NULL) OR (status <> 'suggested' AND decided_at IS NOT NULL AND decided_by IS NOT NULL)");
                    table.CheckConstraint("ck_corporate_actions_kind", "kind IN ('split', 'bonus')");
                    table.CheckConstraint("ck_corporate_actions_ratio", "numerator > 0 AND denominator > 0 AND factor > 0");
                    table.CheckConstraint("ck_corporate_actions_source", "source IN ('manual', 'detected', 'csv')");
                    table.CheckConstraint("ck_corporate_actions_status", "status IN ('suggested', 'applied', 'ignored')");
                    table.ForeignKey(
                        name: "fk_corporate_actions_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalSchema: "market",
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_corporate_actions_status_ex_date",
                schema: "market",
                table: "corporate_actions",
                columns: new[] { "status", "ex_date" });

            migrationBuilder.CreateIndex(
                name: "ux_corporate_actions_instrument_ex_date_kind",
                schema: "market",
                table: "corporate_actions",
                columns: new[] { "instrument_id", "ex_date", "kind" },
                unique: true);

            // The runtime role records suggestions and the owner's decisions (insert, update) but never deletes an action: a declined one is 'ignored', so the detection does not suggest it again.
            migrationBuilder.GrantTableToAppRole(Schemas.Market, "corporate_actions");
            migrationBuilder.NeverDeleteForAppRole(Schemas.Market, "corporate_actions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "corporate_actions",
                schema: "market");
        }
    }
}
