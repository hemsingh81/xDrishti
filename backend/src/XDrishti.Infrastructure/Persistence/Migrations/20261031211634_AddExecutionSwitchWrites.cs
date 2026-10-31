using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The owner's switch writes (P12-07 criterion 13 in the reduced form of ADR 0174). Raw SQL, not mapped in the application's model.
    /// <list type="bullet">
    /// <item><c>execution.settings</c> becomes an owner-act table: <c>xd_app</c> gains INSERT (after the endpoint checked the session, the CSRF token and a fresh password). The trigger <c>execution.check_settings</c> (search_path pinned to
    /// <c>pg_catalog, pg_temp</c>, every catalog name qualified, so a temporary table cannot stand in for a catalog) binds every role but the table's owner (migrations and fixtures; it owns the trigger and could switch it off anyway): it takes one
    /// advisory lock, then draws the row's id (so id order is commit order), stamps <c>changed_at</c> by the database clock, refuses <c>auto_live_enabled</c> ON, carries the <c>values</c> of the row in force over unchanged (this route changes the two
    /// switches only), lets <c>execution_enabled</c> go from OFF to ON only with an <c>effective_from</c> not before 09:00 India time of the next calendar day, stamps every other row in force at once and stamps the snapshot hash. UPDATE, DELETE and
    /// TRUNCATE are refused for every role. A CHECK keeps <c>auto_live_enabled</c> OFF for every role, the owner included; the decision that allows auto_live drops it.</item>
    /// <item>The row in force is the newest whose <c>effective_from</c> has passed. Four trigger functions that read the global switch read the newest row regardless of its time; they are rewritten here, text for text, to read the row in force, so a
    /// raise waiting for its cooling-off is never read early (the functions' text is taken from the catalog and only that one phrase is replaced; the migration fails if the phrase is not found).</item>
    /// <item><c>check_limits</c> and <c>check_mode_trail</c> also took their per-account lock after the id was drawn; they now draw the id after the lock, the same way.</item>
    /// </list>
    /// Nothing here can send an order, store <c>semi_auto</c> or <c>auto_live</c>, or turn <c>auto_live_enabled</c> on.
    /// </summary>
    public partial class AddExecutionSwitchWrites : Migration
    {
        /// <summary>
        /// The first step of Down. After Down the readers take the newest row whatever its time, so a switch-on still waiting would be read at once: when one exists, an OFF row is written first as the owner (newest id, in force now) so that every
        /// reader reads OFF. Public for its test.
        /// </summary>
        public const string DownFailClosed = """
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM execution.settings WHERE effective_from > clock_timestamp()) THEN
                    INSERT INTO execution.settings (execution_enabled, auto_live_enabled, "values", actor, reason)
                    SELECT false, false, COALESCE((SELECT s."values" FROM execution.settings s WHERE s.effective_from <= clock_timestamp() ORDER BY s.id DESC LIMIT 1), '{}'::jsonb), 'owner:' || current_user,
                           'migration AddExecutionSwitchWrites rolled back while a switch-on was waiting: execution is off';
                END IF;
            END $$
            """;

        /// <summary>
        /// The first statement of every trigger here that judges a row against the row in force or newest row read after its lock: at REPEATABLE READ or SERIALIZABLE that read would use the transaction's first snapshot, so a writer could judge
        /// against a row that a later commit has replaced (for example switch execution back ON against a stale ON row, with no cooling-off). Only READ COMMITTED reads fresh after the lock.
        /// </summary>
        private const string ReadCommittedOnly =
            "IF pg_catalog.current_setting('transaction_isolation') <> 'read committed' THEN RAISE EXCEPTION 'execution.isolation_refused: this record is written only at READ COMMITTED, so the row in force is read after the lock' USING ERRCODE = '23514'; END IF;";

        /// <summary>The trigger functions whose search_path this migration pins (check_settings is created pinned).</summary>
        private static readonly string[] Pinned = ["execution.check_limits()", "execution.check_mode_trail()", "execution.check_ledger_entry()"];

        private const string InForceSettings = "WHERE s.effective_from <= clock_timestamp() ORDER BY s.id DESC LIMIT 1";

        private static readonly (string Function, string From, string To)[] Rewrites =
        [
            ("execution.check_promotion_request()", "FROM execution.settings s ORDER BY s.id DESC LIMIT 1", "FROM execution.settings s " + InForceSettings),
            ("execution.check_mode_trail()", "FROM execution.settings s ORDER BY s.id DESC LIMIT 1", "FROM execution.settings s " + InForceSettings),
            ("execution.check_ticket_outcome()", "FROM execution.settings s ORDER BY s.id DESC LIMIT 1", "FROM execution.settings s " + InForceSettings),
            ("execution.check_ledger_entry()", "FROM execution.settings x ORDER BY x.id DESC LIMIT 1", "FROM execution.settings x WHERE x.effective_from <= clock_timestamp() ORDER BY x.id DESC LIMIT 1"),
            ("execution.check_limits()", "PERFORM pg_advisory_xact_lock(hashtextextended('execution.limits:' || NEW.account_id::text, 0));",
                ReadCommittedOnly + " PERFORM pg_catalog.pg_advisory_xact_lock(pg_catalog.hashtextextended('execution.limits:' || NEW.account_id::text, 0)); NEW.id := pg_catalog.nextval(pg_catalog.pg_get_serial_sequence('execution.limits', 'id'));"),
            ("execution.check_mode_trail()", "PERFORM execution.lock_account_modes(NEW.account_id);",
                ReadCommittedOnly + " PERFORM execution.lock_account_modes(NEW.account_id); NEW.id := pg_catalog.nextval(pg_catalog.pg_get_serial_sequence('execution.mode_trail', 'id'));"),
            ("execution.check_ledger_entry()", "PERFORM execution.lock_account_ledger(NEW.account_id);", ReadCommittedOnly + " PERFORM execution.lock_account_ledger(NEW.account_id);"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Shortcut (ADR 0174 limits): the database floor for a raise is 09:00 India time of the next CALENDAR day; the API sets the stricter next TRADING day after one full closed session, which the database has no calendar to prove.
            migrationBuilder.Sql("""
                CREATE FUNCTION execution.check_settings() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog, pg_temp AS $fn$
                DECLARE cur execution.settings%ROWTYPE;
                BEGIN
                    IF session_user = (SELECT pg_catalog.pg_get_userbyid(c.relowner) FROM pg_catalog.pg_class c WHERE c.oid = 'execution.settings'::pg_catalog.regclass) THEN
                        RETURN NEW;
                    END IF;
                    IF pg_catalog.current_setting('transaction_isolation') <> 'read committed' THEN
                        RAISE EXCEPTION 'execution.isolation_refused: this record is written only at READ COMMITTED, so the row in force is read after the lock' USING ERRCODE = '23514';
                    END IF;
                    PERFORM pg_catalog.pg_advisory_xact_lock(pg_catalog.hashtextextended('execution.settings', 0));
                    NEW.id := pg_catalog.nextval(pg_catalog.pg_get_serial_sequence('execution.settings', 'id'));
                    NEW.changed_at := pg_catalog.clock_timestamp();
                    IF NEW.auto_live_enabled THEN
                        RAISE EXCEPTION 'execution.settings_refused: auto_live_enabled stays OFF; no route turns it on' USING ERRCODE = '23514';
                    END IF;
                    IF NEW.actor NOT LIKE 'owner:_%' OR pg_catalog.length(pg_catalog.btrim(NEW.reason)) NOT BETWEEN 1 AND 300 THEN
                        RAISE EXCEPTION 'execution.settings_refused: a switch change is the owner''s act with a reason of 1 to 300 characters' USING ERRCODE = '23514';
                    END IF;
                    SELECT * INTO cur FROM execution.settings s WHERE s.effective_from <= NEW.changed_at ORDER BY s.id DESC LIMIT 1;
                    NEW."values" := COALESCE(cur."values", '{}'::jsonb);
                    IF NEW.execution_enabled AND NOT COALESCE(cur.execution_enabled, false) THEN
                        IF NEW.effective_from < (((NEW.changed_at AT TIME ZONE 'Asia/Kolkata')::date + 1) + time '09:00') AT TIME ZONE 'Asia/Kolkata' THEN
                            RAISE EXCEPTION 'execution.settings_refused: switching execution on takes effect after one full closed session, never before 09:00 India time of the next day' USING ERRCODE = '23514';
                        END IF;
                    ELSE
                        NEW.effective_from := NEW.changed_at;
                    END IF;
                    NEW.snapshot_hash := pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(NEW.execution_enabled::text || '|' || NEW.auto_live_enabled::text || '|' || NEW."values"::text, 'UTF8')), 'hex');
                    RETURN NEW;
                END
                $fn$;
                CREATE TRIGGER trg_exec_settings_check BEFORE INSERT ON execution.settings FOR EACH ROW EXECUTE FUNCTION execution.check_settings();
                CREATE TRIGGER trg_exec_settings_insert_only BEFORE UPDATE OR DELETE ON execution.settings FOR EACH ROW EXECUTE FUNCTION execution.refuse_halt_change();
                CREATE TRIGGER trg_exec_settings_no_truncate BEFORE TRUNCATE ON execution.settings FOR EACH STATEMENT EXECUTE FUNCTION execution.refuse_halt_change();
                ALTER TABLE execution.settings ADD CONSTRAINT ck_exec_settings_auto_live_off CHECK (NOT auto_live_enabled)
                """);

            foreach (var (function, from, to) in Rewrites)
            {
                migrationBuilder.Sql(Rewrite(function, from, to));
            }

            foreach (var function in Pinned)
            {
                migrationBuilder.Sql($"ALTER FUNCTION {function} SET search_path = pg_catalog, pg_temp");
            }

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                        GRANT INSERT ON execution.settings TO xd_app;
                        GRANT USAGE ON SEQUENCE execution.settings_id_seq TO xd_app;
                    END IF;
                END $$
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DownFailClosed);
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                        REVOKE INSERT ON execution.settings FROM xd_app;
                        REVOKE USAGE ON SEQUENCE execution.settings_id_seq FROM xd_app;
                    END IF;
                END $$
                """);

            foreach (var function in Pinned)
            {
                migrationBuilder.Sql($"ALTER FUNCTION {function} RESET search_path");
            }

            foreach (var (function, from, to) in Enumerable.Reverse(Rewrites))
            {
                migrationBuilder.Sql(Rewrite(function, to, from));
            }

            migrationBuilder.Sql("""
                ALTER TABLE execution.settings DROP CONSTRAINT IF EXISTS ck_exec_settings_auto_live_off;
                DROP TRIGGER IF EXISTS trg_exec_settings_no_truncate ON execution.settings;
                DROP TRIGGER IF EXISTS trg_exec_settings_insert_only ON execution.settings;
                DROP TRIGGER IF EXISTS trg_exec_settings_check ON execution.settings;
                DROP FUNCTION IF EXISTS execution.check_settings()
                """);
        }

        // Replaces one phrase in the stored text of a function and re-creates it; refuses when the phrase is missing, so a function changed by a later migration is never rewritten blind.
        private static string Rewrite(string function, string from, string to)
        {
            var f = from.Replace("'", "''", StringComparison.Ordinal);
            var t = to.Replace("'", "''", StringComparison.Ordinal);
            return $"""
                DO $$
                DECLARE d text;
                BEGIN
                    d := pg_get_functiondef('{function}'::regprocedure);
                    IF position('{f}' IN d) = 0 THEN
                        RAISE EXCEPTION 'AddExecutionSwitchWrites: % does not contain the expected text', '{function}';
                    END IF;
                    EXECUTE replace(d, '{f}', '{t}');
                END $$
                """;
        }
    }
}
