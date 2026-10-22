using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTradeFeaturesAndTrainingSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trade_features",
                schema: "research",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    run_key = table.Column<string>(type: "character(64)", nullable: false),
                    trade_key = table.Column<string>(type: "text", nullable: false),
                    feature_version = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    strategy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    timeframe = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    side = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    trade_date = table.Column<DateOnly>(type: "date", nullable: false),
                    decision_epoch_seconds = table.Column<long>(type: "bigint", nullable: false),
                    feature_values = table.Column<double[]>(type: "double precision[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trade_features", x => x.id);
                    table.CheckConstraint("ck_trade_features_shape", "run_key ~ '^[0-9a-f]{64}$' AND cardinality(feature_values) > 0");
                });

            migrationBuilder.CreateTable(
                name: "training_sets",
                schema: "research",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    set_id = table.Column<string>(type: "character(64)", nullable: false),
                    label_set_id = table.Column<string>(type: "character(64)", nullable: false),
                    rule_version = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    feature_version = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    basis = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    sources = table.Column<string>(type: "text", nullable: false),
                    required_features = table.Column<string>(type: "text", nullable: false),
                    half_life_days = table.Column<double>(type: "double precision", nullable: false),
                    prior_strength = table.Column<double>(type: "double precision", nullable: false),
                    min_trades = table.Column<int>(type: "integer", nullable: false),
                    credible_level = table.Column<double>(type: "double precision", nullable: false),
                    from_date = table.Column<DateOnly>(type: "date", nullable: false),
                    to_date = table.Column<DateOnly>(type: "date", nullable: false),
                    labelled = table.Column<int>(type: "integer", nullable: false),
                    row_count = table.Column<int>(type: "integer", nullable: false),
                    positives = table.Column<int>(type: "integer", nullable: false),
                    negatives = table.Column<int>(type: "integer", nullable: false),
                    unresolved = table.Column<int>(type: "integer", nullable: false),
                    no_target = table.Column<int>(type: "integer", nullable: false),
                    dropped_no_features = table.Column<int>(type: "integer", nullable: false),
                    dropped_unknown = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_training_sets", x => x.id);
                    table.CheckConstraint("ck_training_sets_shape", "set_id ~ '^[0-9a-f]{64}$' AND label_set_id ~ '^[0-9a-f]{64}$' AND row_count > 0 AND positives >= 0 AND negatives >= 0 AND positives + negatives = row_count AND labelled >= row_count AND unresolved >= 0 AND no_target >= 0 AND dropped_no_features >= 0 AND dropped_unknown >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "uq_trade_features_run_trade_version",
                schema: "research",
                table: "trade_features",
                columns: new[] { "run_key", "trade_key", "feature_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_training_sets_created_at",
                schema: "research",
                table: "training_sets",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "uq_training_sets_set_id",
                schema: "research",
                table: "training_sets",
                column: "set_id",
                unique: true);

            // Insert-only for the runtime role (spec P6-04, ADR 0066).
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Research, "trade_features");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Research, "training_sets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trade_features",
                schema: "research");

            migrationBuilder.DropTable(
                name: "training_sets",
                schema: "research");
        }
    }
}
