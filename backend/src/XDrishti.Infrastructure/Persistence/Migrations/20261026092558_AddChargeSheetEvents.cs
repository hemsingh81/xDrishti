using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChargeSheetEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "charge_sheet_events",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    sheet_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    rates_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    confirmed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_charge_sheet_events", x => x.id);
                    table.CheckConstraint("ck_charge_sheet_events_shape", "kind IN ('gate_on', 'gate_off', 'confirm', 'revoke') AND actor LIKE 'owner:%' AND length(btrim(actor)) > 6 AND length(btrim(reason)) > 0 AND ((kind IN ('gate_on', 'gate_off') AND sheet_id IS NULL AND rates_sha256 IS NULL AND confirmed_on IS NULL) OR (kind = 'confirm' AND sheet_id ~ '^[a-z0-9][a-z0-9._-]{0,39}$' AND sheet_id NOT LIKE 'test-%' AND rates_sha256 ~ '^[0-9a-f]{64}$' AND confirmed_on IS NOT NULL) OR (kind = 'revoke' AND sheet_id ~ '^[a-z0-9][a-z0-9._-]{0,39}$' AND rates_sha256 ~ '^[0-9a-f]{64}$' AND confirmed_on IS NULL))");
                });

            migrationBuilder.CreateIndex(
                name: "ix_charge_sheet_events_kind_id",
                schema: "workflow",
                table: "charge_sheet_events",
                columns: new[] { "kind", "id" });

            // The owner's acts on the charge-sheet confirmation (spec P8-23, ADR 0127). The database, not the code, stamps and guards a row.
            // 1. The id is drawn AFTER the exclusive lock (so id order is commit order and the newest row is the highest id whatever the clock does), the time is the database's, and a confirmation carries the India date the database drew.
            // 2. A confirmation is refused unless the newest capability row says "on" (no row means off): the application checks it too, this is the last line.
            migrationBuilder.Sql("""
                CREATE FUNCTION workflow.stamp_charge_sheet_event() RETURNS trigger LANGUAGE plpgsql AS $fn$
                DECLARE
                    gate text;
                BEGIN
                    PERFORM pg_advisory_xact_lock(hashtextextended('workflow.charge_sheet_events', 0));
                    NEW.id := nextval(pg_get_serial_sequence('workflow.charge_sheet_events', 'id'));
                    NEW.created_at := clock_timestamp();
                    IF NEW.kind = 'confirm' THEN
                        SELECT e.kind INTO gate FROM workflow.charge_sheet_events e WHERE e.kind IN ('gate_on', 'gate_off') ORDER BY e.id DESC LIMIT 1;
                        IF gate IS DISTINCT FROM 'gate_on' THEN
                            RAISE EXCEPTION 'A charge sheet can be confirmed only while confirming is switched on' USING ERRCODE = '23514';
                        END IF;
                        NEW.confirmed_on := (NEW.created_at AT TIME ZONE 'Asia/Kolkata')::date;
                    END IF;
                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.Sql("CREATE TRIGGER trg_charge_sheet_events_stamp BEFORE INSERT ON workflow.charge_sheet_events FOR EACH ROW EXECUTE FUNCTION workflow.stamp_charge_sheet_event()");

            // Insert-only for the runtime role, granted on this table alone: a schema-wide grant would give back UPDATE and DELETE on the other workflow tables.
            migrationBuilder.GrantTableToAppRole(Schemas.Workflow, "charge_sheet_events");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Workflow, "charge_sheet_events");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_charge_sheet_events_stamp ON workflow.charge_sheet_events");
            migrationBuilder.DropTable(
                name: "charge_sheet_events",
                schema: "workflow");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS workflow.stamp_charge_sheet_event()");
        }
    }
}
