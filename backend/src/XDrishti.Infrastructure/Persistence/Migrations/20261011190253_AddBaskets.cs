using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBaskets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "baskets",
                schema: "market",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    colour = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    purpose = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    position = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_baskets", x => x.id);
                    table.CheckConstraint("ck_baskets_colour", "colour ~ '^#[0-9a-f]{6}$'");
                    table.CheckConstraint("ck_baskets_position", "position >= 0");
                    table.CheckConstraint("ck_baskets_purpose", "purpose IN ('trading', 'long_term', 'watch')");
                });

            migrationBuilder.CreateTable(
                name: "basket_members",
                schema: "market",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    basket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    added_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    removed_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_basket_members", x => x.id);
                    table.CheckConstraint("ck_basket_members_dates", "valid_to IS NULL OR valid_to >= valid_from");
                    table.CheckConstraint("ck_basket_members_position", "position >= 0");
                    table.CheckConstraint("ck_basket_members_removed_by", "(valid_to IS NULL) = (removed_by IS NULL)");
                    table.ForeignKey(
                        name: "fk_basket_members_baskets_basket_id",
                        column: x => x.basket_id,
                        principalSchema: "market",
                        principalTable: "baskets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_basket_members_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalSchema: "market",
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_basket_members_instrument_id",
                schema: "market",
                table: "basket_members",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "ux_basket_members_active",
                schema: "market",
                table: "basket_members",
                columns: new[] { "basket_id", "instrument_id" },
                unique: true,
                filter: "valid_to IS NULL");

            // Names of active baskets are unique ignoring case; an archived basket frees its name. EF Core cannot model expression indexes.
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_baskets_active_name_lower ON market.baskets (lower(name)) WHERE archived_at IS NULL");

            // The runtime role (xd_app) reads and writes baskets and members with plain DML; it never changes the tables.
            migrationBuilder.GrantTableToAppRole(Schemas.Market, "baskets");
            migrationBuilder.GrantTableToAppRole(Schemas.Market, "basket_members");

            // The baskets named in docs/design/06 §3, empty. Idempotent: skipped when the id exists or an active basket has the name. The values are
            // copied from Domain DefaultBaskets (a migration never changes; a test keeps the two equal). Colours are from the prototype's palette.
            migrationBuilder.Sql("""
                INSERT INTO market.baskets (id, name, colour, purpose, description, position, created_at, archived_at, revision)
                SELECT seed.id, seed.name, seed.colour, seed.purpose, NULL, seed.position, now(), NULL, 0
                FROM (VALUES
                    ('0199c1a0-0000-7000-8000-000000000001'::uuid, 'MyChoice', '#6366f1', 'trading', 0),
                    ('0199c1a0-0000-7000-8000-000000000002'::uuid, 'MyLongTerm', '#10b981', 'long_term', 1),
                    ('0199c1a0-0000-7000-8000-000000000003'::uuid, 'Best stocks', '#f59e0b', 'trading', 2)
                ) AS seed (id, name, colour, purpose, position)
                WHERE NOT EXISTS (
                    SELECT 1 FROM market.baskets b
                    WHERE b.id = seed.id OR (b.archived_at IS NULL AND lower(b.name) = lower(seed.name)))
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "basket_members",
                schema: "market");

            migrationBuilder.DropTable(
                name: "baskets",
                schema: "market");
        }
    }
}
