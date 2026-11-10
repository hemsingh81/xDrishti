using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Derived baskets (BK-03, ADR 0258): the immutable dated versions (<c>basket_versions</c>, insert-only for the runtime role), their scores, the parameter sets (insert-only) and the switch per basket (<c>derived_definitions</c>, all OFF).
    /// The fourteen derived baskets are seeded here as data (kind derived, system) with a disabled definition each; the migration holds its own copy of the list.
    /// </summary>
    public partial class AddDerivedBasketVersions : Migration
    {
        // number, name, cadence; basket 13 needs BK-05's flags and 4 and 12 need live minute bars, so none of them can be switched on in this build.
        private static readonly (int No, string Name, string Cadence)[] Catalogue =
        [
            (1, "Most volatile", "daily"),
            (2, "Most moving", "daily"),
            (3, "Most liquid", "daily"),
            (4, "Gappers", "frozen_intraday"),
            (5, "High beta", "weekly"),
            (6, "Momentum 12-1", "monthly"),
            (7, "Mean-reverting", "weekly"),
            (8, "Trending", "daily"),
            (9, "Near 52-week high", "daily"),
            (10, "Low volatility", "monthly"),
            (11, "High relative volume", "daily"),
            (12, "Opening-range wide", "frozen_intraday"),
            (13, "Intraday-tradable (proxy)", "daily"),
            (14, "Return-correlation clusters", "monthly"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "basket_param_sets",
                schema: "market",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_no = table.Column<int>(type: "integer", nullable: false),
                    @params = table.Column<string>(name: "params", type: "jsonb", nullable: false),
                    params_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_basket_param_sets", x => x.id);
                    table.CheckConstraint("ck_basket_param_sets_no", "definition_no BETWEEN 1 AND 14");
                });

            migrationBuilder.CreateTable(
                name: "basket_versions",
                schema: "market",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    basket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    as_of = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    formed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    @params = table.Column<string>(name: "params", type: "jsonb", nullable: false),
                    params_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    universe_size = table.Column<int>(type: "integer", nullable: false),
                    member_count = table.Column<int>(type: "integer", nullable: false),
                    below_threshold = table.Column<int>(type: "integer", nullable: false),
                    excluded = table.Column<string>(type: "jsonb", nullable: false),
                    outcome = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    provenance = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_basket_versions", x => x.id);
                    table.CheckConstraint("ck_basket_versions_counts", "universe_size >= 0 AND member_count >= 0 AND below_threshold >= 0");
                    table.CheckConstraint("ck_basket_versions_dates", "effective_from > as_of");
                    table.CheckConstraint("ck_basket_versions_outcome", "outcome IN ('applied', 'unchanged', 'not_formed')");
                    table.CheckConstraint("ck_basket_versions_provenance", "provenance IN ('derived', 'recomputed')");
                    table.ForeignKey(
                        name: "fk_basket_versions_baskets_basket_id",
                        column: x => x.basket_id,
                        principalSchema: "market",
                        principalTable: "baskets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "derived_definitions",
                schema: "market",
                columns: table => new
                {
                    basket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_no = table.Column<int>(type: "integer", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    cadence = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    params_set_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_derived_definitions", x => x.basket_id);
                    table.CheckConstraint("ck_derived_definitions_cadence", "cadence IN ('daily', 'weekly', 'monthly', 'frozen_intraday')");
                    table.CheckConstraint("ck_derived_definitions_no", "definition_no BETWEEN 1 AND 14");
                    table.ForeignKey(
                        name: "fk_derived_definitions_basket_param_sets_params_set_id",
                        column: x => x.params_set_id,
                        principalSchema: "market",
                        principalTable: "basket_param_sets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_derived_definitions_baskets_basket_id",
                        column: x => x.basket_id,
                        principalSchema: "market",
                        principalTable: "baskets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "basket_member_scores",
                schema: "market",
                columns: table => new
                {
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    score = table.Column<double>(type: "double precision", nullable: false),
                    rank = table.Column<int>(type: "integer", nullable: false),
                    is_member = table.Column<bool>(type: "boolean", nullable: false),
                    sessions_held = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_basket_member_scores", x => new { x.version_id, x.instrument_id });
                    table.CheckConstraint("ck_basket_member_scores_rank", "rank >= 0 AND sessions_held >= 0");
                    table.ForeignKey(
                        name: "fk_basket_member_scores_basket_versions_version_id",
                        column: x => x.version_id,
                        principalSchema: "market",
                        principalTable: "basket_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_basket_member_scores_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalSchema: "market",
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_basket_member_scores_instrument_id",
                schema: "market",
                table: "basket_member_scores",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "ix_basket_versions_basket_id_as_of",
                schema: "market",
                table: "basket_versions",
                columns: new[] { "basket_id", "as_of" });

            migrationBuilder.CreateIndex(
                name: "ix_derived_definitions_params_set_id",
                schema: "market",
                table: "derived_definitions",
                column: "params_set_id");

            migrationBuilder.CreateIndex(
                name: "ux_derived_definitions_definition_no",
                schema: "market",
                table: "derived_definitions",
                column: "definition_no",
                unique: true);

            // The point-in-time record is insert-only for the runtime role (the schema grant gave it UPDATE and DELETE).
            migrationBuilder.MakeAppendOnlyForAppRole("market", "basket_versions");
            migrationBuilder.MakeAppendOnlyForAppRole("market", "basket_member_scores");
            migrationBuilder.MakeAppendOnlyForAppRole("market", "basket_param_sets");

            // One version per basket, as_of, parameters and formation time: an expression index, which EF Core cannot model.
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_basket_versions_basket_as_of_params ON market.basket_versions (basket_id, as_of, params_hash, coalesce(formed_at, '-infinity'::timestamptz))");

            foreach (var (no, name, cadence) in Catalogue)
            {
                var id = "md5('market.basket.derived:" + no + "')::uuid";
                migrationBuilder.Sql(
                    "INSERT INTO market.baskets (id, name, colour, purpose, description, position, created_at, revision, kind, system) VALUES ("
                    + id + ", 'Derived: " + name + "', '#64748b', 'watch', 'Formed from our own daily bars; read-only, membership from the dated versions.', 0, now(), 0, 'derived', true)");
                migrationBuilder.Sql(
                    "INSERT INTO market.derived_definitions (basket_id, definition_no, enabled, cadence) VALUES (" + id + ", " + no + ", false, '" + cadence + "')");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "basket_member_scores",
                schema: "market");

            migrationBuilder.DropTable(
                name: "derived_definitions",
                schema: "market");

            migrationBuilder.DropTable(
                name: "basket_versions",
                schema: "market");

            migrationBuilder.DropTable(
                name: "basket_param_sets",
                schema: "market");

            migrationBuilder.Sql("DELETE FROM market.basket_members WHERE basket_id IN (SELECT id FROM market.baskets WHERE kind = 'derived')");
            migrationBuilder.Sql("DELETE FROM market.baskets WHERE kind = 'derived'");
        }
    }
}
