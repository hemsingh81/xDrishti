using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLabStrategies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "lab");

            migrationBuilder.CreateTable(
                name: "strategies",
                schema: "lab",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    style = table.Column<string>(type: "text", nullable: false),
                    built_in = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_strategies", x => x.id);
                    table.CheckConstraint("ck_strategies_id", "id ~ '^[A-Za-z][A-Za-z0-9_]{0,63}$'");
                    table.CheckConstraint("ck_strategies_style", "style IN ('intraday', 'swing')");
                });

            migrationBuilder.CreateTable(
                name: "strategy_versions",
                schema: "lab",
                columns: table => new
                {
                    strategy_id = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    schema_version = table.Column<short>(type: "smallint", nullable: false),
                    hash = table.Column<string>(type: "character(64)", nullable: false),
                    source_text = table.Column<string>(type: "text", nullable: false),
                    canonical_json = table.Column<string>(type: "text", nullable: false),
                    parent_hash = table.Column<string>(type: "character(64)", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_strategy_versions", x => new { x.strategy_id, x.version });
                    table.UniqueConstraint("uq_strategy_versions_hash", x => x.hash);
                    table.CheckConstraint("ck_strategy_versions_hash", "hash ~ '^[0-9a-f]{64}$' AND (parent_hash IS NULL OR parent_hash ~ '^[0-9a-f]{64}$')");
                    table.CheckConstraint("ck_strategy_versions_sizes", "octet_length(source_text) <= 262144 AND (note IS NULL OR char_length(note) <= 500)");
                    table.CheckConstraint("ck_strategy_versions_version", "version >= 1");
                    table.ForeignKey(
                        name: "fk_strategy_versions_parent",
                        column: x => x.parent_hash,
                        principalSchema: "lab",
                        principalTable: "strategy_versions",
                        principalColumn: "hash",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_strategy_versions_strategy",
                        column: x => x.strategy_id,
                        principalSchema: "lab",
                        principalTable: "strategies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_strategy_versions_parent_hash",
                schema: "lab",
                table: "strategy_versions",
                column: "parent_hash");

            // two ids that differ only in case are one id to a person (spec P3-02d, question 6); EF cannot express an index on an expression
            migrationBuilder.Sql("CREATE UNIQUE INDEX uq_strategies_id_lower ON lab.strategies (lower(id));");

            // insert-only for the runtime role, like the research tables (ADR 0045 D7, ADR 0056 D3); a later migration that re-grants the schema must call MakeAppendOnlyForAppRole for both again
            migrationBuilder.GrantSchemaUsageToAppRole(Schemas.Lab);
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Lab, "strategies");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Lab, "strategy_versions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "strategy_versions",
                schema: "lab");

            migrationBuilder.DropTable(
                name: "strategies",
                schema: "lab");

            // only if empty and without CASCADE: a later table in the schema makes this fail and removes nothing
            migrationBuilder.Sql("DROP SCHEMA lab;");
        }
    }
}
