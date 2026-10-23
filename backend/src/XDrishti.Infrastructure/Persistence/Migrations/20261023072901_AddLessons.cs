using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLessons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lessons",
                schema: "research",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    lesson_id = table.Column<long>(type: "bigint", nullable: true),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    from_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    monitor_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    evidence_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    problem = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    hypothesis_ref = table.Column<long>(type: "bigint", nullable: true),
                    experiment_id = table.Column<long>(type: "bigint", nullable: true),
                    result = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    experiment_outcome = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    before_mean_r = table.Column<double>(type: "double precision", nullable: true),
                    after_mean_r = table.Column<double>(type: "double precision", nullable: true),
                    before_total_r = table.Column<double>(type: "double precision", nullable: true),
                    after_total_r = table.Column<double>(type: "double precision", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lessons", x => x.id);
                    table.CheckConstraint("ck_lessons_links", "(state <> 'open' OR (hypothesis_ref IS NULL AND experiment_id IS NULL)) AND (state <> 'linked' OR (hypothesis_ref IS NOT NULL) <> (experiment_id IS NOT NULL)) AND (state <> 'closed' OR experiment_id IS NOT NULL)");
                    table.CheckConstraint("ck_lessons_result", "(state = 'closed') = (result IS NOT NULL) AND (result IS NULL OR result IN ('improved', 'no_measurable_change')) AND (state = 'closed' OR (experiment_outcome IS NULL AND before_total_r IS NULL AND after_total_r IS NULL AND before_mean_r IS NULL AND after_mean_r IS NULL))");
                    table.CheckConstraint("ck_lessons_shape", "monitor_kind IN ('growing_tag', 'rolling_band', 'cusum', 'calibration')");
                    table.CheckConstraint("ck_lessons_state", "state IN ('open', 'linked', 'closed', 'dismissed') AND (from_state IS NULL OR from_state IN ('open', 'linked', 'closed', 'dismissed')) AND (state = 'open') = (from_state IS NULL) AND (state = 'open') = (lesson_id IS NULL) AND state IS DISTINCT FROM from_state");
                    table.ForeignKey(
                        name: "fk_lessons_lessons_lesson_id",
                        column: x => x.lesson_id,
                        principalSchema: "research",
                        principalTable: "lessons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lessons_lesson_id_id",
                schema: "research",
                table: "lessons",
                columns: new[] { "lesson_id", "id" });

            // A linked fix must exist (spec P6-16).
            migrationBuilder.Sql("ALTER TABLE research.lessons ADD CONSTRAINT fk_lessons_hypotheses_hypothesis_ref FOREIGN KEY (hypothesis_ref) REFERENCES research.hypotheses (id) ON DELETE RESTRICT");
            migrationBuilder.Sql("ALTER TABLE research.lessons ADD CONSTRAINT fk_lessons_experiments_experiment_id FOREIGN KEY (experiment_id) REFERENCES research.experiments (id) ON DELETE RESTRICT");

            // A state row must point at an opening row, never at another state row (as research.hypotheses).
            migrationBuilder.Sql("ALTER TABLE research.lessons ADD COLUMN is_seed boolean GENERATED ALWAYS AS (lesson_id IS NULL) STORED, ADD COLUMN points_at_seed boolean GENERATED ALWAYS AS (CASE WHEN lesson_id IS NULL THEN NULL ELSE true END) STORED");
            migrationBuilder.Sql("ALTER TABLE research.lessons ADD CONSTRAINT uq_lessons_id_is_seed UNIQUE (id, is_seed)");
            migrationBuilder.Sql("ALTER TABLE research.lessons ADD CONSTRAINT fk_lessons_seed FOREIGN KEY (lesson_id, points_at_seed) REFERENCES research.lessons (id, is_seed) ON DELETE RESTRICT");

            // The same finding opens one lesson only: monitor, subject and evidence key are unique among the opening rows.
            migrationBuilder.Sql("CREATE UNIQUE INDEX uq_lessons_finding ON research.lessons (monitor_kind, subject, evidence_key) WHERE lesson_id IS NULL");

            // Insert-only for the runtime role (spec P6-16, ADR 0078): a state change is a new row.
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Research, "lessons");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lessons",
                schema: "research");
        }
    }
}
