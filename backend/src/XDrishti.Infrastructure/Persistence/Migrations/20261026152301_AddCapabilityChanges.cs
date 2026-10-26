using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCapabilityChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "capability_changes",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    capability = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    previous_id = table.Column<long>(type: "bigint", nullable: true),
                    snapshot_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_capability_changes", x => x.id);
                    table.CheckConstraint("ck_capability_changes_shape", "capability ~ '^[a-z][a-z0-9_]{0,39}$' AND actor LIKE 'owner:%' AND length(btrim(actor)) > 6 AND length(btrim(reason)) > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_capability_changes_capability_id",
                schema: "platform",
                table: "capability_changes",
                columns: new[] { "capability", "id" });

            // The owner's switches of the capabilities (spec P8-18 Part A, ADR 0128). The database, not the code, stamps a row: the id is drawn AFTER the exclusive lock (so id order is commit order and the newest row of a name is its highest id whatever the clock does),
            // the time is the database's, the previous id is the newest row of the same name, and the snapshot hash is the SHA-256 of "capability|enabled|reason|actor|previous_id". A value the caller chose for any of these is overwritten.
            migrationBuilder.Sql("""
                CREATE FUNCTION platform.stamp_capability_change() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    PERFORM pg_advisory_xact_lock(hashtextextended('platform.capability_changes', 0));
                    NEW.id := nextval(pg_get_serial_sequence('platform.capability_changes', 'id'));
                    NEW.changed_at := clock_timestamp();
                    SELECT c.id INTO NEW.previous_id FROM platform.capability_changes c WHERE c.capability = NEW.capability ORDER BY c.id DESC LIMIT 1;
                    NEW.snapshot_hash := encode(sha256(convert_to(format('%s|%s|%s|%s|%s', NEW.capability, NEW.enabled::text, NEW.reason, NEW.actor, COALESCE(NEW.previous_id::text, '')), 'UTF8')), 'hex');
                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.Sql("CREATE TRIGGER trg_capability_changes_stamp BEFORE INSERT ON platform.capability_changes FOR EACH ROW EXECUTE FUNCTION platform.stamp_capability_change()");

            // Insert-only for the runtime role, granted on this table alone: a schema-wide grant would give back UPDATE and DELETE on the other platform tables.
            migrationBuilder.GrantTableToAppRole(Schemas.Platform, "capability_changes");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Platform, "capability_changes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_capability_changes_stamp ON platform.capability_changes");
            migrationBuilder.DropTable(
                name: "capability_changes",
                schema: "platform");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS platform.stamp_capability_change()");
        }
    }
}
