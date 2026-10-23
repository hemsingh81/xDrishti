using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWeeklyDigests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "digests",
                schema: "research",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    iso_week = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    week_start = table.Column<DateOnly>(type: "date", nullable: false),
                    week_end = table.Column<DateOnly>(type: "date", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    sections_json = table.Column<string>(type: "text", nullable: false),
                    built_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_digests", x => x.id);
                    table.CheckConstraint("ck_digests_shape", "iso_week ~ '^[0-9]{4}-W[0-9]{2}$' AND week_end = week_start + 6 AND position('Nothing was applied' IN text) > 0 AND length(built_by) > 0");
                });

            migrationBuilder.CreateIndex(
                name: "uq_digests_iso_week",
                schema: "research",
                table: "digests",
                column: "iso_week",
                unique: true);

            // Insert-only for the runtime role: a digest is written once per week and never changed (ADR 0094). Granted on its own table, never by re-granting the schema.
            migrationBuilder.GrantTableToAppRole(Schemas.Research, "digests");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Research, "digests");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "digests",
                schema: "research");
        }
    }
}
