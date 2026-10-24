using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSuggestionSeen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "suggestion_seen",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    suggestion_id = table.Column<long>(type: "bigint", nullable: false),
                    actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suggestion_seen", x => x.id);
                    table.CheckConstraint("ck_suggestion_seen_actor", "actor LIKE 'owner:%'");
                    table.ForeignKey(
                        name: "fk_suggestion_seen_suggestions_suggestion_id",
                        column: x => x.suggestion_id,
                        principalSchema: "workflow",
                        principalTable: "suggestions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "uq_suggestion_seen_suggestion_id",
                schema: "workflow",
                table: "suggestion_seen",
                column: "suggestion_id",
                unique: true);

            // Insert-only for the runtime role: a mark is appended once and never changed (ADR 0095). Granted on its own table, never by re-granting the schema (ADR 0083, 6a; PlanGrantsTests).
            migrationBuilder.GrantTableToAppRole(Schemas.Workflow, "suggestion_seen");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Workflow, "suggestion_seen");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "suggestion_seen",
                schema: "workflow");
        }
    }
}
