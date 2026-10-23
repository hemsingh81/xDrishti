using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ModelRegistryTrialsAndPurged : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_model_versions_shape",
                schema: "research",
                table: "model_versions");

            migrationBuilder.AddColumn<int>(
                name: "purged_rows",
                schema: "research",
                table: "model_versions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "trials",
                schema: "research",
                table: "model_versions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddCheckConstraint(
                name: "ck_model_versions_shape",
                schema: "research",
                table: "model_versions",
                sql: "sha256 ~ '^[0-9a-f]{64}$' AND version_key ~ '^[0-9a-f]{64}$' AND training_set_id ~ '^[0-9a-f]{64}$' AND verdict IN ('Eligible', 'CannotBeatBaselines', 'Inconclusive') AND artifact_bytes > 0 AND feature_width > 0 AND cardinality(calibration_scores) > 0 AND cardinality(calibration_scores) = cardinality(calibration_values) AND cardinality(importance) = feature_width AND train_rows > 0 AND fit_rows >= 0 AND calibration_rows >= 0 AND used_folds >= 0 AND trials >= 1 AND purged_rows >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_model_versions_shape",
                schema: "research",
                table: "model_versions");

            migrationBuilder.DropColumn(
                name: "purged_rows",
                schema: "research",
                table: "model_versions");

            migrationBuilder.DropColumn(
                name: "trials",
                schema: "research",
                table: "model_versions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_model_versions_shape",
                schema: "research",
                table: "model_versions",
                sql: "sha256 ~ '^[0-9a-f]{64}$' AND version_key ~ '^[0-9a-f]{64}$' AND training_set_id ~ '^[0-9a-f]{64}$' AND verdict IN ('Eligible', 'CannotBeatBaselines', 'Inconclusive') AND artifact_bytes > 0 AND feature_width > 0 AND cardinality(calibration_scores) > 0 AND cardinality(calibration_scores) = cardinality(calibration_values) AND cardinality(importance) = feature_width AND train_rows > 0 AND fit_rows >= 0 AND calibration_rows >= 0 AND used_folds >= 0");
        }
    }
}
