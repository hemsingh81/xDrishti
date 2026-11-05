using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The record of how the influence predicate stood for a model (spec CF-10 part 10c-2, ADR 0217): <c>research.ml_influence_checks</c>, insert-only for the runtime role. <c>rollback_proposals</c>, the <c>auto</c> marker on changelog rows and the <c>calibration_kind</c> column are not here (part 10d; the kind is in the artifact header since 10a).
    /// </summary>
    public partial class AddMlSafety : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ml_influence_checks",
                schema: "research",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    model_id = table.Column<long>(type: "bigint", nullable: false),
                    evaluated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    may_influence = table.Column<bool>(type: "boolean", nullable: false),
                    terms = table.Column<string>(type: "jsonb", nullable: false),
                    setting_hash = table.Column<string>(type: "character(64)", nullable: false),
                    inputs_hash = table.Column<string>(type: "character(64)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ml_influence_checks", x => x.id);
                    table.CheckConstraint("ck_ml_influence_checks_hashes", "setting_hash ~ '^[0-9a-f]{64}$' AND inputs_hash ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "fk_ml_influence_checks_model_versions_model_id",
                        column: x => x.model_id,
                        principalSchema: "research",
                        principalTable: "model_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ml_influence_checks_model_id_id",
                schema: "research",
                table: "ml_influence_checks",
                columns: new[] { "model_id", "id" });

            // Insert-only for the runtime role: a check is evidence of how the gate answered and is never changed or deleted.
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Research, "ml_influence_checks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ml_influence_checks",
                schema: "research");
        }
    }
}
