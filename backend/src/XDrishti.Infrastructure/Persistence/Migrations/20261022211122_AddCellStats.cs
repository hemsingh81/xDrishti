using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCellStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cell_stats",
                schema: "research",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    snapshot_key = table.Column<string>(type: "character(64)", nullable: false),
                    as_of = table.Column<DateOnly>(type: "date", nullable: false),
                    cell_key = table.Column<string>(type: "text", nullable: false),
                    strategy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    timeframe = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    side = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    style = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    exit_policy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    regime = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    window_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    costed = table.Column<bool>(type: "boolean", nullable: false),
                    n = table.Column<int>(type: "integer", nullable: false),
                    wins = table.Column<int>(type: "integer", nullable: false),
                    effective_n = table.Column<double>(type: "double precision", nullable: false),
                    win_rate_raw = table.Column<double>(type: "double precision", nullable: true),
                    prior_rate = table.Column<double>(type: "double precision", nullable: false),
                    win_rate_posterior = table.Column<double>(type: "double precision", nullable: false),
                    win_rate_lower = table.Column<double>(type: "double precision", nullable: false),
                    win_rate_upper = table.Column<double>(type: "double precision", nullable: false),
                    avg_win_r = table.Column<double>(type: "double precision", nullable: true),
                    avg_loss_r = table.Column<double>(type: "double precision", nullable: true),
                    expectancy_r = table.Column<double>(type: "double precision", nullable: true),
                    profit_factor = table.Column<double>(type: "double precision", nullable: true),
                    max_drawdown_r = table.Column<double>(type: "double precision", nullable: false),
                    mae_p50 = table.Column<double>(type: "double precision", nullable: true),
                    mae_p90 = table.Column<double>(type: "double precision", nullable: true),
                    mfe_p50 = table.Column<double>(type: "double precision", nullable: true),
                    mfe_p90 = table.Column<double>(type: "double precision", nullable: true),
                    enough = table.Column<bool>(type: "boolean", nullable: false),
                    half_life_days = table.Column<double>(type: "double precision", nullable: false),
                    prior_strength = table.Column<double>(type: "double precision", nullable: false),
                    min_trades = table.Column<int>(type: "integer", nullable: false),
                    credible_level = table.Column<double>(type: "double precision", nullable: false),
                    sources = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cell_stats", x => x.id);
                    table.CheckConstraint("ck_cell_stats_shape", "snapshot_key ~ '^[0-9a-f]{64}$' AND window_code IN ('all','250d','60d') AND n >= 0 AND wins >= 0 AND wins <= n");
                });

            migrationBuilder.CreateIndex(
                name: "ix_cell_stats_as_of_created",
                schema: "research",
                table: "cell_stats",
                columns: new[] { "as_of", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_cell_stats_cell_key",
                schema: "research",
                table: "cell_stats",
                column: "cell_key");

            migrationBuilder.CreateIndex(
                name: "uq_cell_stats_snapshot_cell_window",
                schema: "research",
                table: "cell_stats",
                columns: new[] { "snapshot_key", "cell_key", "window_code" },
                unique: true);

            // Insert-only for the runtime role (spec P6-01, ADR 0064).
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Research, "cell_stats");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cell_stats",
                schema: "research");
        }
    }
}
