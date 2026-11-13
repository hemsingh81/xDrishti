using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Per-kind hold-out looks (spec HO-01b, ADR 0283): the fresh slot becomes one per (block, kind), a version may look once per block, the kind check admits <c>system</c>, a nullable <c>reason</c> holds <c>not_predated</c>, and
    /// <c>research.cell_stats</c> gains the nullable <c>seen_holdout</c> label. Looks are permanent: <c>Down</c> refuses while a <c>system</c> row exists or while the narrower unique keys would be violated.
    /// </summary>
    public partial class WidenHoldOutLooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_holdout_unlocks_fresh",
                schema: "lab",
                table: "holdout_unlocks");

            migrationBuilder.DropIndex(
                name: "uq_holdout_unlocks_version",
                schema: "lab",
                table: "holdout_unlocks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_holdout_unlocks_shape",
                schema: "lab",
                table: "holdout_unlocks");

            migrationBuilder.AddColumn<string>(
                name: "reason",
                schema: "lab",
                table: "holdout_unlocks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "seen_holdout",
                schema: "research",
                table: "cell_stats",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "uq_holdout_unlocks_fresh",
                schema: "lab",
                table: "holdout_unlocks",
                columns: new[] { "block_start", "kind" },
                unique: true,
                filter: "NOT reused_window");

            migrationBuilder.CreateIndex(
                name: "uq_holdout_unlocks_version",
                schema: "lab",
                table: "holdout_unlocks",
                columns: new[] { "strategy_id", "version", "kind", "block_start" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_holdout_unlocks_shape",
                schema: "lab",
                table: "holdout_unlocks",
                sql: "version >= 1 AND kind IN ('strategy','model','system') AND mode IN ('owner','auto') AND setting_hash ~ '^[0-9a-f]{64}$'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM lab.holdout_unlocks WHERE kind = 'system' OR reason IS NOT NULL)
                       OR EXISTS (SELECT 1 FROM lab.holdout_unlocks GROUP BY strategy_id, version, kind HAVING count(*) > 1)
                       OR EXISTS (SELECT 1 FROM lab.holdout_unlocks WHERE NOT reused_window GROUP BY block_start HAVING count(*) > 1) THEN
                        RAISE EXCEPTION 'Looks of kind system, a not_predated reason, a second look of a version, or two fresh looks at a block exist and are permanent; WidenHoldOutLooks cannot be reverted.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "uq_holdout_unlocks_fresh",
                schema: "lab",
                table: "holdout_unlocks");

            migrationBuilder.DropIndex(
                name: "uq_holdout_unlocks_version",
                schema: "lab",
                table: "holdout_unlocks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_holdout_unlocks_shape",
                schema: "lab",
                table: "holdout_unlocks");

            migrationBuilder.DropColumn(
                name: "reason",
                schema: "lab",
                table: "holdout_unlocks");

            migrationBuilder.DropColumn(
                name: "seen_holdout",
                schema: "research",
                table: "cell_stats");

            migrationBuilder.CreateIndex(
                name: "uq_holdout_unlocks_fresh",
                schema: "lab",
                table: "holdout_unlocks",
                column: "block_start",
                unique: true,
                filter: "NOT reused_window");

            migrationBuilder.CreateIndex(
                name: "uq_holdout_unlocks_version",
                schema: "lab",
                table: "holdout_unlocks",
                columns: new[] { "strategy_id", "version", "kind" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_holdout_unlocks_shape",
                schema: "lab",
                table: "holdout_unlocks",
                sql: "version >= 1 AND kind IN ('strategy','model') AND mode IN ('owner','auto') AND setting_hash ~ '^[0-9a-f]{64}$'");
        }
    }
}
