using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGrades : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "grades",
                schema: "trading",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    plan_id = table.Column<long>(type: "bigint", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    for_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    r = table.Column<double>(type: "double precision", nullable: true),
                    mae_r = table.Column<double>(type: "double precision", nullable: true),
                    mfe_r = table.Column<double>(type: "double precision", nullable: true),
                    exit_reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    entry_price = table.Column<double>(type: "double precision", nullable: true),
                    exit_price = table.Column<double>(type: "double precision", nullable: true),
                    cost_basis = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    basis = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    run_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    graded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_grades", x => x.id);
                    table.CheckConstraint("ck_grades_status", "status IN ('graded', 'not_triggered', 'not_gradable') AND basis = 'simulated on plan prices' AND ((status = 'graded') = (r IS NOT NULL)) AND ((status = 'graded') = (exit_reason IS NOT NULL)) AND length(detail) > 0");
                    table.ForeignKey(
                        name: "fk_grades_plan_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "trading",
                        principalTable: "plan_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_grades_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "trading",
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_grades_for_date_plan_id",
                schema: "trading",
                table: "grades",
                columns: new[] { "for_date", "plan_id" });

            migrationBuilder.CreateIndex(
                name: "ix_grades_plan_id",
                schema: "trading",
                table: "grades",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "uq_grades_item_id",
                schema: "trading",
                table: "grades",
                column: "item_id",
                unique: true);

            // The grade's plan is the ticket's own plan (a composite key onto the unique pair of plan_items).
            migrationBuilder.Sql("ALTER TABLE trading.grades ADD CONSTRAINT fk_grades_item_in_plan FOREIGN KEY (plan_id, item_id) REFERENCES trading.plan_items (plan_id, id) ON DELETE RESTRICT");

            // Insert-only for the runtime role: a grade is written once and never changed (ADR 0090). Granted on its own table, never by re-granting the schema (ADR 0083, 6a).
            migrationBuilder.GrantTableToAppRole(Schemas.Trading, "grades");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Trading, "grades");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "grades",
                schema: "trading");
        }
    }
}
