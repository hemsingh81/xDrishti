using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLearningReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "learning_reports",
                schema: "research",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    input_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    outcome = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    as_of = table.Column<DateOnly>(type: "date", nullable: false),
                    basis = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    sources_json = table.Column<string>(type: "text", nullable: false),
                    options_json = table.Column<string>(type: "text", nullable: false),
                    report_json = table.Column<string>(type: "text", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    built_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_learning_reports", x => x.id);
                    table.CheckConstraint("ck_learning_reports_shape", "kind ~ '^[a-z][a-z_]{1,38}$' AND input_hash ~ '^[0-9a-f]{64}$' AND length(outcome) > 0 AND basis IN ('gross','costed_unconfirmed','net_confirmed') AND position('limit:' IN text) > 0 AND length(built_by) > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_learning_reports_kind_created",
                schema: "research",
                table: "learning_reports",
                columns: new[] { "kind", "created_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "uq_learning_reports_kind_hash",
                schema: "research",
                table: "learning_reports",
                columns: new[] { "kind", "input_hash" },
                unique: true);

            // Insert-only for the runtime role: a report is written once per input and never changed (ADR 0110). Granted on its own table, never by re-granting the schema.
            migrationBuilder.GrantTableToAppRole(Schemas.Research, "learning_reports");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Research, "learning_reports");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "learning_reports",
                schema: "research");
        }
    }
}
