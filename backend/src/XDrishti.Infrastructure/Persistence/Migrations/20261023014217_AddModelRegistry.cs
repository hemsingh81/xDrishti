using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModelRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "model_versions",
                schema: "research",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    version_key = table.Column<string>(type: "character(64)", nullable: false),
                    sha256 = table.Column<string>(type: "character(64)", nullable: false),
                    artifact_bytes = table.Column<int>(type: "integer", nullable: false),
                    trainer = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    training_set_id = table.Column<string>(type: "character(64)", nullable: false),
                    feature_version = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    feature_width = table.Column<int>(type: "integer", nullable: false),
                    fit_rows = table.Column<int>(type: "integer", nullable: false),
                    calibration_rows = table.Column<int>(type: "integer", nullable: false),
                    train_rows = table.Column<int>(type: "integer", nullable: false),
                    from_date = table.Column<DateOnly>(type: "date", nullable: false),
                    to_date = table.Column<DateOnly>(type: "date", nullable: false),
                    hold_out_start = table.Column<DateOnly>(type: "date", nullable: true),
                    verdict = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    used_folds = table.Column<int>(type: "integer", nullable: false),
                    folds_beating_both = table.Column<int>(type: "integer", nullable: false),
                    folds_needed = table.Column<int>(type: "integer", nullable: false),
                    pooled_beats_both = table.Column<bool>(type: "boolean", nullable: false),
                    pooled_ece_pp = table.Column<double>(type: "double precision", nullable: false),
                    pooled_log_loss = table.Column<double>(type: "double precision", nullable: false),
                    pooled_brier = table.Column<double>(type: "double precision", nullable: false),
                    reasons = table.Column<string>(type: "text", nullable: false),
                    report_text = table.Column<string>(type: "text", nullable: false),
                    parameters = table.Column<string>(type: "text", nullable: false),
                    calibration_scores = table.Column<double[]>(type: "double precision[]", nullable: false),
                    calibration_values = table.Column<double[]>(type: "double precision[]", nullable: false),
                    importance = table.Column<double[]>(type: "double precision[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_model_versions", x => x.id);
                    table.CheckConstraint("ck_model_versions_shape", "sha256 ~ '^[0-9a-f]{64}$' AND version_key ~ '^[0-9a-f]{64}$' AND training_set_id ~ '^[0-9a-f]{64}$' AND verdict IN ('Eligible', 'CannotBeatBaselines', 'Inconclusive') AND artifact_bytes > 0 AND feature_width > 0 AND cardinality(calibration_scores) > 0 AND cardinality(calibration_scores) = cardinality(calibration_values) AND cardinality(importance) = feature_width AND train_rows > 0 AND fit_rows >= 0 AND calibration_rows >= 0 AND used_folds >= 0");
                });

            migrationBuilder.CreateTable(
                name: "model_designations",
                schema: "research",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    model_id = table.Column<long>(type: "bigint", nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_model_designations", x => x.id);
                    table.CheckConstraint("ck_model_designations_role", "role IN ('challenger', 'champion', 'retired')");
                    table.ForeignKey(
                        name: "fk_model_designations_model_versions_model_id",
                        column: x => x.model_id,
                        principalSchema: "research",
                        principalTable: "model_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_model_designations_model_id_id",
                schema: "research",
                table: "model_designations",
                columns: new[] { "model_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_model_versions_sha256",
                schema: "research",
                table: "model_versions",
                column: "sha256");

            migrationBuilder.CreateIndex(
                name: "ix_model_versions_training_set_id",
                schema: "research",
                table: "model_versions",
                column: "training_set_id");

            migrationBuilder.CreateIndex(
                name: "uq_model_versions_version_key",
                schema: "research",
                table: "model_versions",
                column: "version_key",
                unique: true);

            // A model is made from a stored training set: the unique index on training_sets (set_id) is the key (spec P6-07).
            migrationBuilder.Sql("ALTER TABLE research.model_versions ADD CONSTRAINT fk_model_versions_training_sets_training_set_id FOREIGN KEY (training_set_id) REFERENCES research.training_sets (set_id) ON DELETE RESTRICT");

            // Insert-only for the runtime role (spec P6-07, ADR 0070): a model is never changed, a role is only appended.
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Research, "model_versions");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Research, "model_designations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "model_designations",
                schema: "research");

            migrationBuilder.DropTable(
                name: "model_versions",
                schema: "research");
        }
    }
}
