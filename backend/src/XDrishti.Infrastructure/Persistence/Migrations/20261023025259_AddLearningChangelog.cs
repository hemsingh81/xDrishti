using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLearningChangelog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "learning_changelog",
                schema: "research",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    subject_kind = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    from_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    to_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    evidence_ids = table.Column<string[]>(type: "text[]", nullable: false),
                    before_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    after_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    paper_days = table.Column<int>(type: "integer", nullable: true),
                    reverts_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_learning_changelog", x => x.id);
                    table.CheckConstraint("ck_learning_changelog_evidence", "cardinality(evidence_ids) <= 20 AND (paper_days IS NULL OR paper_days >= 0)");
                    table.CheckConstraint("ck_learning_changelog_kind", "kind IN ('transition', 'designation', 'revert') AND subject_kind IN ('cell', 'model')");
                    table.CheckConstraint("ck_learning_changelog_revert", "(kind = 'revert') = (reverts_id IS NOT NULL) AND (kind <> 'revert' OR subject_kind = 'cell')");
                    table.CheckConstraint("ck_learning_changelog_states", "from_state <> to_state AND (subject_kind <> 'cell' OR (from_state IN ('research', 'candidate', 'paper', 'active', 'paused', 'retired') AND to_state IN ('research', 'candidate', 'paper', 'active', 'paused', 'retired'))) AND (subject_kind <> 'model' OR (from_state IN ('challenger', 'champion', 'retired') AND to_state IN ('challenger', 'champion', 'retired')))");
                    table.ForeignKey(
                        name: "fk_learning_changelog_learning_changelog_reverts_id",
                        column: x => x.reverts_id,
                        principalSchema: "research",
                        principalTable: "learning_changelog",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_learning_changelog_subject_id",
                schema: "research",
                table: "learning_changelog",
                columns: new[] { "subject", "id" });

            migrationBuilder.CreateIndex(
                name: "uq_learning_changelog_reverts_id",
                schema: "research",
                table: "learning_changelog",
                column: "reverts_id",
                unique: true,
                filter: "reverts_id IS NOT NULL");

            // Insert-only for the runtime role (spec P6-08, ADR 0073): a change is a row, a revert is another row.
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Research, "learning_changelog");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "learning_changelog",
                schema: "research");
        }
    }
}
