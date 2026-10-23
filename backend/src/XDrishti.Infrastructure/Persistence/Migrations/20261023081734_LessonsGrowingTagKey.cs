using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LessonsGrowingTagKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_lessons_closed",
                schema: "research",
                table: "lessons",
                sql: "state <> 'closed' OR (experiment_outcome IS NOT NULL AND before_total_r IS NOT NULL AND after_total_r IS NOT NULL AND (result = 'improved') = (experiment_outcome = 'Better'))");

            // A growing tag's key is the tag kind alone and is reused once its lesson is closed or dismissed (ADR 0078), so the opening rows are unique for the other monitors only;
            // the log port checks growing tags under a lock.
            migrationBuilder.Sql("DROP INDEX research.uq_lessons_finding");
            migrationBuilder.Sql("CREATE UNIQUE INDEX uq_lessons_finding ON research.lessons (monitor_kind, subject, evidence_key) WHERE lesson_id IS NULL AND monitor_kind <> 'growing_tag'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Fails, removing nothing, when two opening rows of a growing tag now share a key: that is the data this migration allowed.
            migrationBuilder.Sql("DROP INDEX research.uq_lessons_finding");
            migrationBuilder.Sql("CREATE UNIQUE INDEX uq_lessons_finding ON research.lessons (monitor_kind, subject, evidence_key) WHERE lesson_id IS NULL");
            migrationBuilder.DropCheckConstraint(
                name: "ck_lessons_closed",
                schema: "research",
                table: "lessons");
        }
    }
}
