using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_approvals_kind",
                schema: "workflow",
                table: "approvals");

            migrationBuilder.DropCheckConstraint(
                name: "ck_approvals_shape",
                schema: "workflow",
                table: "approvals");

            migrationBuilder.AddColumn<double>(
                name: "p_win_lower",
                schema: "trading",
                table: "plan_items",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "policy_version",
                schema: "workflow",
                table: "approvals",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "settings_hash",
                schema: "workflow",
                table: "approvals",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "settings_id",
                schema: "workflow",
                table: "approvals",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "autonomy_settings",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    auto_approve_tickets = table.Column<bool>(type: "boolean", nullable: false),
                    promote_from_reserve = table.Column<bool>(type: "boolean", nullable: false),
                    min_p_win = table.Column<decimal>(type: "numeric(4,3)", precision: 4, scale: 3, nullable: false),
                    max_auto_per_day = table.Column<int>(type: "integer", nullable: false),
                    snapshot_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_autonomy_settings", x => x.id);
                    table.CheckConstraint("ck_autonomy_settings_shape", "min_p_win >= 0.5 AND min_p_win <= 0.99 AND max_auto_per_day >= 1 AND max_auto_per_day <= 20 AND actor LIKE 'owner:%' AND length(btrim(actor)) > 6 AND length(btrim(reason)) > 0");
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_plan_items_p_win_lower",
                schema: "trading",
                table: "plan_items",
                sql: "p_win_lower IS NULL OR (p_win_lower >= 0 AND p_win_lower <= 1)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_approvals_kind",
                schema: "workflow",
                table: "approvals",
                sql: "kind IN ('owner', 'auto')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_approvals_shape",
                schema: "workflow",
                table: "approvals",
                sql: "decision IN ('approve', 'modify', 'reject') AND (decision = 'modify') = (modifications IS NOT NULL) AND (confirm_reason IS NULL OR decision = 'approve') AND length(btrim(reason)) > 0 AND (decision = 'reject' OR decided_at < cutoff) AND ((kind = 'owner' AND actor LIKE 'owner:%' AND settings_id IS NULL AND settings_hash IS NULL AND policy_version IS NULL) OR (kind = 'auto' AND actor = 'rule:autonomy' AND decision = 'approve' AND modifications IS NULL AND confirm_reason IS NULL AND settings_id IS NOT NULL AND settings_hash IS NOT NULL AND policy_version IS NOT NULL AND item_id IS NOT NULL))");

            migrationBuilder.CreateIndex(
                name: "uq_autonomy_settings_id_hash",
                schema: "workflow",
                table: "autonomy_settings",
                columns: new[] { "id", "snapshot_hash" },
                unique: true);

            // Automatic approval (spec P7-12, ADR 0091). The database, not the code, decides what an auto approval may be.
            // 1. An auto approval names the settings row it rests on (id and hash together).
            migrationBuilder.Sql("ALTER TABLE workflow.approvals ADD CONSTRAINT fk_approvals_settings FOREIGN KEY (settings_id, settings_hash) REFERENCES workflow.autonomy_settings (id, snapshot_hash) ON DELETE RESTRICT");

            // 2. A settings row is stamped by the database: the time, and the hash of its canonical text (so a direct insert cannot choose either). The exclusive lock makes a change wait for in-flight automatic approvals and the other way round.
            migrationBuilder.Sql("""
                CREATE FUNCTION workflow.stamp_autonomy_setting() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    PERFORM pg_advisory_xact_lock(hashtextextended('workflow.autonomy_settings', 0));
                    NEW.changed_at := clock_timestamp();
                    NEW.snapshot_hash := encode(sha256(convert_to(format('autonomy:v1|auto=%s|reserve=%s|min_p_win=%s|max_per_day=%s', NEW.auto_approve_tickets::text, NEW.promote_from_reserve::text, to_char(NEW.min_p_win, 'FM0.000'), NEW.max_auto_per_day::text), 'UTF8')), 'hex');
                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.Sql("CREATE TRIGGER trg_autonomy_settings_stamp BEFORE INSERT ON workflow.autonomy_settings FOR EACH ROW EXECUTE FUNCTION workflow.stamp_autonomy_setting()");

            // 3. An auto approval is allowed only: against the NEWEST settings row, with the switch on in it; for a primary item; while its plan is published or in review; and within the row's cap for the planned session.
            //    (Before the cutoff, approve only, the rule as actor and no owner fields are the checks of ck_approvals_shape.) The owner's rows pass through unchanged.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION workflow.stamp_approval() RETURNS trigger LANGUAGE plpgsql AS $fn$
                DECLARE
                    cur record;
                    grp text;
                    plan_state text;
                    session date;
                    used bigint;
                BEGIN
                    NEW.decided_at := clock_timestamp();
                    IF NEW.kind = 'auto' THEN
                        PERFORM pg_advisory_xact_lock_shared(hashtextextended('workflow.autonomy_settings', 0));
                        SELECT id, auto_approve_tickets, max_auto_per_day INTO cur FROM workflow.autonomy_settings ORDER BY changed_at DESC, id DESC LIMIT 1;
                        IF NOT FOUND OR cur.id IS DISTINCT FROM NEW.settings_id OR NOT cur.auto_approve_tickets THEN
                            RAISE EXCEPTION 'An automatic approval needs the current settings row, with automatic approval switched on' USING ERRCODE = '23514';
                        END IF;

                        SELECT item_group INTO grp FROM trading.plan_items WHERE id = NEW.item_id AND plan_id = NEW.plan_id;
                        IF grp IS DISTINCT FROM 'primary' THEN
                            RAISE EXCEPTION 'An automatic approval is for a primary ticket only' USING ERRCODE = '23514';
                        END IF;

                        SELECT COALESCE((SELECT e.to_state FROM trading.plan_events e WHERE e.plan_id = NEW.plan_id AND e.item_id IS NULL ORDER BY e.id DESC LIMIT 1), 'draft') INTO plan_state;
                        IF plan_state NOT IN ('published', 'inreview') THEN
                            RAISE EXCEPTION 'An automatic approval needs a plan that is published or in review' USING ERRCODE = '23514';
                        END IF;

                        SELECT for_date INTO session FROM trading.plans WHERE id = NEW.plan_id;
                        PERFORM pg_advisory_xact_lock(hashtextextended('workflow.auto_approvals:' || session::text, 0));
                        SELECT count(*) INTO used FROM workflow.approvals a JOIN trading.plans p ON p.id = a.plan_id WHERE a.kind = 'auto' AND p.for_date = session;
                        IF used >= cur.max_auto_per_day THEN
                            RAISE EXCEPTION 'The daily cap of automatic approvals is reached' USING ERRCODE = '23514';
                        END IF;
                    END IF;
                    RETURN NEW;
                END
                $fn$
                """);

            // 4. The chain trigger also knows the auto kind (see the function).
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION trading.check_plan_event() RETURNS trigger LANGUAGE plpgsql AS $fn$
                DECLARE
                    grp text;
                    open_n bigint;
                    approved_n bigint;
                    armed_n bigint;
                    rejected_n bigint;
                BEGIN
                    IF NEW.item_id IS NOT NULL THEN
                        SELECT item_group INTO grp FROM trading.plan_items WHERE id = NEW.item_id;
                        IF grp = 'rejected' THEN
                            RAISE EXCEPTION 'A rejected candidate is not a ticket and has no state rows' USING ERRCODE = '23514';
                        END IF;
                        IF NEW.approval_id IS NOT NULL AND EXISTS (SELECT 1 FROM workflow.approvals a WHERE a.id = NEW.approval_id AND a.kind = 'auto')
                           AND (NEW.from_state <> 'proposed' OR NEW.actor <> 'rule:autonomy' OR grp <> 'primary') THEN
                            RAISE EXCEPTION 'An automatic approval moves a primary ticket from proposed, as the rule' USING ERRCODE = '23514';
                        END IF;
                        RETURN NEW;
                    END IF;

                    IF NEW.to_state IN ('inreview', 'approved', 'rejected', 'lapsed', 'armed') THEN
                        SELECT count(*) FILTER (WHERE st IN ('proposed', 'modified')), count(*) FILTER (WHERE st = 'approved'), count(*) FILTER (WHERE st = 'armed'), count(*) FILTER (WHERE st = 'rejected')
                        INTO open_n, approved_n, armed_n, rejected_n
                        FROM (SELECT COALESCE((SELECT e.to_state FROM trading.plan_events e WHERE e.item_id = i.id ORDER BY e.id DESC LIMIT 1), 'proposed') AS st
                              FROM trading.plan_items i WHERE i.plan_id = NEW.plan_id AND i.item_group <> 'rejected') x;
                        IF (NEW.to_state = 'approved' AND (open_n > 0 OR approved_n = 0))
                           OR (NEW.to_state IN ('rejected', 'lapsed') AND (open_n > 0 OR approved_n > 0))
                           OR (NEW.to_state = 'armed' AND armed_n = 0)
                           OR (NEW.to_state = 'inreview' AND approved_n + rejected_n = 0) THEN
                            RAISE EXCEPTION 'The tickets of plan % do not support moving it to %', NEW.plan_id, NEW.to_state USING ERRCODE = '23514';
                        END IF;
                    END IF;

                    RETURN NEW;
                END
                $fn$
                """);

            // 5. Insert-only for the runtime role, like the rest of the record. Never re-grant the schema (PlanGrantsTests, an architecture test).
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Workflow, "autonomy_settings");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down refuses (the database says so) while an auto approval exists: the old table only knows the owner.
            migrationBuilder.Sql("ALTER TABLE workflow.approvals DROP CONSTRAINT fk_approvals_settings");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_autonomy_settings_stamp ON workflow.autonomy_settings");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS workflow.stamp_autonomy_setting()");
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION workflow.stamp_approval() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    NEW.decided_at := clock_timestamp();
                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION trading.check_plan_event() RETURNS trigger LANGUAGE plpgsql AS $fn$
                DECLARE
                    grp text;
                    open_n bigint;
                    approved_n bigint;
                    armed_n bigint;
                    rejected_n bigint;
                BEGIN
                    IF NEW.item_id IS NOT NULL THEN
                        SELECT item_group INTO grp FROM trading.plan_items WHERE id = NEW.item_id;
                        IF grp = 'rejected' THEN
                            RAISE EXCEPTION 'A rejected candidate is not a ticket and has no state rows' USING ERRCODE = '23514';
                        END IF;
                        RETURN NEW;
                    END IF;

                    IF NEW.to_state IN ('inreview', 'approved', 'rejected', 'lapsed', 'armed') THEN
                        SELECT count(*) FILTER (WHERE st IN ('proposed', 'modified')), count(*) FILTER (WHERE st = 'approved'), count(*) FILTER (WHERE st = 'armed'), count(*) FILTER (WHERE st = 'rejected')
                        INTO open_n, approved_n, armed_n, rejected_n
                        FROM (SELECT COALESCE((SELECT e.to_state FROM trading.plan_events e WHERE e.item_id = i.id ORDER BY e.id DESC LIMIT 1), 'proposed') AS st
                              FROM trading.plan_items i WHERE i.plan_id = NEW.plan_id AND i.item_group <> 'rejected') x;
                        IF (NEW.to_state = 'approved' AND (open_n > 0 OR approved_n = 0))
                           OR (NEW.to_state IN ('rejected', 'lapsed') AND (open_n > 0 OR approved_n > 0))
                           OR (NEW.to_state = 'armed' AND armed_n = 0)
                           OR (NEW.to_state = 'inreview' AND approved_n + rejected_n = 0) THEN
                            RAISE EXCEPTION 'The tickets of plan % do not support moving it to %', NEW.plan_id, NEW.to_state USING ERRCODE = '23514';
                        END IF;
                    END IF;

                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.DropTable(
                name: "autonomy_settings",
                schema: "workflow");

            migrationBuilder.DropCheckConstraint(
                name: "ck_plan_items_p_win_lower",
                schema: "trading",
                table: "plan_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_approvals_kind",
                schema: "workflow",
                table: "approvals");

            migrationBuilder.DropCheckConstraint(
                name: "ck_approvals_shape",
                schema: "workflow",
                table: "approvals");

            migrationBuilder.DropColumn(
                name: "p_win_lower",
                schema: "trading",
                table: "plan_items");

            migrationBuilder.DropColumn(
                name: "policy_version",
                schema: "workflow",
                table: "approvals");

            migrationBuilder.DropColumn(
                name: "settings_hash",
                schema: "workflow",
                table: "approvals");

            migrationBuilder.DropColumn(
                name: "settings_id",
                schema: "workflow",
                table: "approvals");

            migrationBuilder.AddCheckConstraint(
                name: "ck_approvals_kind",
                schema: "workflow",
                table: "approvals",
                sql: "kind = 'owner'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_approvals_shape",
                schema: "workflow",
                table: "approvals",
                sql: "decision IN ('approve', 'modify', 'reject') AND (decision = 'modify') = (modifications IS NOT NULL) AND (confirm_reason IS NULL OR decision = 'approve') AND actor LIKE 'owner:%' AND length(btrim(reason)) > 0 AND (decision = 'reject' OR decided_at < cutoff)");
        }
    }
}
