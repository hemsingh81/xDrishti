using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSuggestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "suggestions",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    plan_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    subject = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suggestions", x => x.id);
                    table.CheckConstraint("ck_suggestions_shape", "length(btrim(kind)) > 0 AND length(btrim(subject)) > 0 AND length(btrim(text)) > 0");
                    table.ForeignKey(
                        name: "fk_suggestions_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "trading",
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "uq_suggestions_plan_kind_subject",
                schema: "workflow",
                table: "suggestions",
                columns: new[] { "plan_id", "kind", "subject" },
                unique: true);

            // Insert-only for the runtime role: a suggestion is appended once and never changed (ADR 0092). Granted on its own table, never by re-granting the schema (ADR 0083, 6a; PlanGrantsTests).
            migrationBuilder.GrantTableToAppRole(Schemas.Workflow, "suggestions");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Workflow, "suggestions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "suggestions",
                schema: "workflow");
        }
    }
}
