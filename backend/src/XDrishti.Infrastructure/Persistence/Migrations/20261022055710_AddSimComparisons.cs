using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSimComparisons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sim_comparisons",
                schema: "research",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    comparison_key = table.Column<string>(type: "character(64)", nullable: false),
                    entries_key = table.Column<string>(type: "character(64)", nullable: false),
                    strategy_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    unit = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    common_entries = table.Column<int>(type: "integer", nullable: false),
                    member_run_keys = table.Column<string[]>(type: "character(64)[]", nullable: false),
                    member_candidate_ids = table.Column<string[]>(type: "character varying(200)[]", nullable: false),
                    member_gated_blocked = table.Column<long[]>(type: "bigint[]", nullable: false),
                    comparison_csv = table.Column<string>(type: "text", nullable: false),
                    diagnostics_csv = table.Column<string>(type: "text", nullable: false),
                    record_sha256 = table.Column<string>(type: "character(64)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sim_comparisons", x => x.id);
                    table.CheckConstraint("ck_sim_comparisons_counts", "common_entries >= 0");
                    table.CheckConstraint("ck_sim_comparisons_keys", "comparison_key ~ '^[0-9a-f]{64}$' AND entries_key ~ '^[0-9a-f]{64}$' AND record_sha256 ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_sim_comparisons_members", "cardinality(member_run_keys) >= 2 AND cardinality(member_run_keys) = cardinality(member_candidate_ids) AND cardinality(member_run_keys) = cardinality(member_gated_blocked)");
                    table.CheckConstraint("ck_sim_comparisons_unit", "unit IN ('RFilled', 'RNet')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_sim_comparisons_entries_key",
                schema: "research",
                table: "sim_comparisons",
                column: "entries_key");

            migrationBuilder.CreateIndex(
                name: "uq_sim_comparisons_comparison_key",
                schema: "research",
                table: "sim_comparisons",
                column: "comparison_key",
                unique: true);

            // insert-only for the runtime role, like the other research tables (ADR 0045 D7; ResearchGrantsTests holds every research table to it)
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Research, "sim_comparisons");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sim_comparisons",
                schema: "research");
        }
    }
}
