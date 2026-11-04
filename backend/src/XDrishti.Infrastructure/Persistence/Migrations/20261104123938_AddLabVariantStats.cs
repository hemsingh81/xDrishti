using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The per-trade statistics of each variant of a strategy family (spec CF-03, ADR 0205): <c>lab.variant_stats</c>, insert-only for the runtime role (granted table by table; a schema-wide re-grant would give back UPDATE and DELETE). The execution role and the MCP roles are granted nothing.
    /// No existing row is touched.
    /// </summary>
    public partial class AddLabVariantStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "variant_stats",
                schema: "lab",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    strategy_id = table.Column<string>(type: "text", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_hash = table.Column<string>(type: "character(64)", nullable: false),
                    n = table.Column<int>(type: "integer", nullable: false),
                    mean_r = table.Column<double>(type: "double precision", nullable: true),
                    sd_r = table.Column<double>(type: "double precision", nullable: true),
                    skew = table.Column<double>(type: "double precision", nullable: true),
                    kurt = table.Column<double>(type: "double precision", nullable: true),
                    sr_per_trade = table.Column<double>(type: "double precision", nullable: true),
                    method = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_variant_stats", x => x.id);
                    table.CheckConstraint("ck_variant_stats_finite", "(mean_r IS NULL OR (mean_r <> 'NaN'::float8 AND abs(mean_r) <> 'Infinity'::float8)) AND (sd_r IS NULL OR (sd_r <> 'NaN'::float8 AND abs(sd_r) <> 'Infinity'::float8)) AND (skew IS NULL OR (skew <> 'NaN'::float8 AND abs(skew) <> 'Infinity'::float8)) AND (kurt IS NULL OR (kurt <> 'NaN'::float8 AND abs(kurt) <> 'Infinity'::float8)) AND (sr_per_trade IS NULL OR (sr_per_trade <> 'NaN'::float8 AND abs(sr_per_trade) <> 'Infinity'::float8))");
                    table.CheckConstraint("ck_variant_stats_shape", "variant_hash ~ '^[0-9a-f]{64}$' AND n >= 0 AND (sd_r IS NULL OR sd_r >= 0) AND method IN ('full','robust','sweep')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_variant_stats_strategy_created",
                schema: "lab",
                table: "variant_stats",
                columns: new[] { "strategy_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "uq_variant_stats_run_variant",
                schema: "lab",
                table: "variant_stats",
                columns: new[] { "run_id", "variant_hash" },
                unique: true);

            migrationBuilder.GrantTableToAppRole(Schemas.Lab, "variant_stats");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Lab, "variant_stats");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "variant_stats",
                schema: "lab");
        }
    }
}
