using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExitSelections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "exit_selections",
                schema: "research",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    selection_key = table.Column<string>(type: "character(64)", nullable: false),
                    comparison_id = table.Column<long>(type: "bigint", nullable: false),
                    strategy_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    timeframe = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    side = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    style = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    regime = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    chosen_candidate_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    stable = table.Column<bool>(type: "boolean", nullable: true),
                    record_json = table.Column<string>(type: "text", nullable: false),
                    record_sha256 = table.Column<string>(type: "character(64)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exit_selections", x => x.id);
                    table.CheckConstraint("ck_exit_selections_keys", "selection_key ~ '^[0-9a-f]{64}$' AND record_sha256 ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "fk_exit_selections_comparison",
                        column: x => x.comparison_id,
                        principalSchema: "research",
                        principalTable: "sim_comparisons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exit_selection_folds",
                schema: "research",
                columns: table => new
                {
                    selection_id = table.Column<long>(type: "bigint", nullable: false),
                    fold_index = table.Column<int>(type: "integer", nullable: false),
                    candidate_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    test_start = table.Column<DateOnly>(type: "date", nullable: false),
                    test_end = table.Column<DateOnly>(type: "date", nullable: false),
                    qualified = table.Column<bool>(type: "boolean", nullable: false),
                    train_trades = table.Column<int>(type: "integer", nullable: false),
                    train_score = table.Column<double>(type: "double precision", nullable: true),
                    test_trades = table.Column<int>(type: "integer", nullable: false),
                    test_score = table.Column<double>(type: "double precision", nullable: true),
                    is_winner = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exit_selection_folds", x => new { x.selection_id, x.fold_index, x.candidate_id });
                    table.CheckConstraint("ck_exit_selection_folds_counts", "fold_index >= 1 AND train_trades >= 0 AND test_trades >= 0 AND test_end >= test_start");
                    table.ForeignKey(
                        name: "fk_exit_selection_folds_selection",
                        column: x => x.selection_id,
                        principalSchema: "research",
                        principalTable: "exit_selections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_exit_selections_comparison_id",
                schema: "research",
                table: "exit_selections",
                column: "comparison_id");

            migrationBuilder.CreateIndex(
                name: "uq_exit_selections_selection_key",
                schema: "research",
                table: "exit_selections",
                column: "selection_key",
                unique: true);

            // insert-only for the runtime role, like the other research tables (ADR 0045 D7; ResearchGrantsTests holds every research table to it)
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Research, "exit_selections");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Research, "exit_selection_folds");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exit_selection_folds",
                schema: "research");

            migrationBuilder.DropTable(
                name: "exit_selections",
                schema: "research");
        }
    }
}
