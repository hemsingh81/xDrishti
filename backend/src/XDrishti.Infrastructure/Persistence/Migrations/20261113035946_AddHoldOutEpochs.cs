using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Hold-out epochs (spec HO-01, ADR 0283): the stored row stays block 1 (<c>prev_start</c> null, nothing else changes); a second row left by the ADR 0207 race is marked <c>orphan</c> and never read; <c>lab.holdout_releases</c> is insert-only for the runtime role.
    /// <c>Down</c> refuses while a release exists (a release is permanent).
    /// </summary>
    public partial class AddHoldOutEpochs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "orphan",
                schema: "lab",
                table: "holdout_blocks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "prev_start",
                schema: "lab",
                table: "holdout_blocks",
                type: "date",
                nullable: true);

            // the earliest-created first block is block 1 (the one every reader saw); any other row of the ADR 0207 race is an orphan
            migrationBuilder.Sql("""
                UPDATE lab.holdout_blocks SET orphan = true
                WHERE prev_start IS NULL AND id <> (SELECT id FROM lab.holdout_blocks ORDER BY created_at ASC, id ASC LIMIT 1)
                """);

            migrationBuilder.CreateTable(
                name: "holdout_releases",
                schema: "lab",
                columns: table => new
                {
                    block_start = table.Column<DateOnly>(type: "date", nullable: false),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    mode = table.Column<string>(type: "text", nullable: false),
                    actor = table.Column<string>(type: "text", nullable: false),
                    setting_hash = table.Column<string>(type: "character(64)", nullable: false),
                    looked = table.Column<bool>(type: "boolean", nullable: false),
                    next_block_start = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holdout_releases", x => x.block_start);
                    table.CheckConstraint("ck_holdout_releases_shape", "mode IN ('owner','auto') AND setting_hash ~ '^[0-9a-f]{64}$' AND next_block_start > block_start AND length(actor) > 0");
                });

            migrationBuilder.CreateIndex(
                name: "uq_holdout_blocks_first",
                schema: "lab",
                table: "holdout_blocks",
                column: "orphan",
                unique: true,
                filter: "prev_start IS NULL AND NOT orphan");

            migrationBuilder.CreateIndex(
                name: "uq_holdout_blocks_prev",
                schema: "lab",
                table: "holdout_blocks",
                column: "prev_start",
                unique: true);

            // insert-only for the runtime role, granted table by table; the execution role and the MCP roles get nothing
            migrationBuilder.GrantTableToAppRole(Schemas.Lab, "holdout_releases");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Lab, "holdout_releases");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM lab.holdout_releases) THEN
                        RAISE EXCEPTION 'A hold-out release exists and is permanent; AddHoldOutEpochs cannot be reverted.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropTable(
                name: "holdout_releases",
                schema: "lab");

            migrationBuilder.DropIndex(
                name: "uq_holdout_blocks_first",
                schema: "lab",
                table: "holdout_blocks");

            migrationBuilder.DropIndex(
                name: "uq_holdout_blocks_prev",
                schema: "lab",
                table: "holdout_blocks");

            migrationBuilder.DropColumn(
                name: "orphan",
                schema: "lab",
                table: "holdout_blocks");

            migrationBuilder.DropColumn(
                name: "prev_start",
                schema: "lab",
                table: "holdout_blocks");
        }
    }
}
