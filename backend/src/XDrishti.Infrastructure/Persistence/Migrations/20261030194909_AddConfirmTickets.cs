using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The one-tap confirm record (spec P12-08b, ADR 0166). Raw SQL, not mapped in the application's model. Three insert-only tables (a trigger refuses UPDATE, DELETE and TRUNCATE even for the owner):
    /// <list type="bullet">
    /// <item><c>execution.confirm_tickets</c>: the card the execution service issues for a checking entry intent, with the exact text and its SHA-256 (checked here), the client order id, the reference price, the password rule and the Soft codes
    /// not passed at issue (never a Hard code). One per intent and attempt (at most two; a second only after a refused first), issued now by the database clock and living 15 to 300 seconds.</item>
    /// <item><c>execution.ticket_outcomes</c>: the one decision per ticket; the primary key on <c>ticket_id</c> is the one-decision guarantee (twenty parallel taps leave one row; a tap racing the expiry leaves one row). The trigger
    /// <c>execution.check_ticket_outcome</c> stamps the database time and admits <c>confirmed</c> only from the owner through the API, before <c>expires_at</c>, with a valid day unlock, no active halt, the account's effective mode
    /// <c>semi_auto</c> or <c>auto_live</c> and the global switch ON; <c>skipped</c> only from the owner before expiry; <c>expired</c> only from the execution service at or after expiry; <c>refused</c> only from the execution service.
    /// A confirmed ticket with Soft codes writes P12-03's <c>check_confirmations</c> row in the same transaction.</item>
    /// <item><c>execution.day_unlocks</c>: the owner's daily unlock per account and India date, stamped by the database and valid until 15:30 India time.</item>
    /// </list>
    /// Grants: xd_app SELECT on all three and INSERT on <c>ticket_outcomes</c> (the trigger lets it write confirmed and skipped only) and <c>day_unlocks</c>; xd_exec SELECT on all three and INSERT on <c>confirm_tickets</c> and
    /// <c>ticket_outcomes</c> (expired and refused only), plus the capital columns of <c>accounts.trading_settings</c> for the password rule. No UPDATE, DELETE or TRUNCATE. No account can be <c>semi_auto</c> (ADR 0163), so no
    /// confirmed row can be written today. Nothing here can send an order.
    /// </summary>
    public partial class AddConfirmTickets : Migration
    {
        private const string SoftCodes = "'sector_exposure','holdings_conflict','adv_share','results_within_days','fno_ban','surveillance','correlation','data_quality'";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                CREATE TABLE execution.confirm_tickets (
                    id uuid NOT NULL DEFAULT gen_random_uuid(),
                    intent_public_id uuid NOT NULL,
                    account_id uuid NOT NULL,
                    plan_item_id bigint NOT NULL,
                    session_date date NOT NULL,
                    attempt smallint NOT NULL,
                    kind varchar(16) NOT NULL,
                    params jsonb NOT NULL,
                    text text NOT NULL,
                    text_hash char(64) NOT NULL,
                    client_order_id varchar(30) NOT NULL,
                    ref_price numeric(18,4) NOT NULL,
                    notional numeric(18,2) NOT NULL,
                    risk_at_stop numeric(18,2) NOT NULL,
                    requires_password boolean NOT NULL,
                    password_reasons jsonb NOT NULL,
                    soft_codes text[] NOT NULL DEFAULT '{}',
                    check_run_id bigint NULL,
                    issued_at timestamptz NOT NULL,
                    expires_at timestamptz NOT NULL,
                    CONSTRAINT pk_exec_confirm_tickets PRIMARY KEY (id),
                    CONSTRAINT uq_exec_confirm_tickets_attempt UNIQUE (intent_public_id, attempt),
                    CONSTRAINT fk_exec_confirm_tickets_intent FOREIGN KEY (intent_public_id) REFERENCES execution.intents (public_id) ON DELETE RESTRICT,
                    CONSTRAINT fk_exec_confirm_tickets_run FOREIGN KEY (check_run_id) REFERENCES execution.check_runs (id) ON DELETE RESTRICT,
                    CONSTRAINT ck_exec_confirm_tickets_attempt CHECK (attempt IN (1, 2)),
                    CONSTRAINT ck_exec_confirm_tickets_kind CHECK (kind = 'entry'),
                    CONSTRAINT ck_exec_confirm_tickets_text CHECK (length(text) BETWEEN 1 AND 2000 AND text_hash = encode(sha256(convert_to(text, 'UTF8')), 'hex')),
                    CONSTRAINT ck_exec_confirm_tickets_coid CHECK (client_order_id ~ '^XD[0-9]{6}[0-9A-Z]{6}E1$'),
                    CONSTRAINT ck_exec_confirm_tickets_money CHECK (ref_price > 0 AND notional > 0 AND risk_at_stop >= 0),
                    CONSTRAINT ck_exec_confirm_tickets_reasons CHECK (jsonb_typeof(password_reasons) = 'array' AND jsonb_typeof(params) = 'object' AND (requires_password = (jsonb_array_length(password_reasons) > 0))),
                    CONSTRAINT ck_exec_confirm_tickets_soft CHECK (soft_codes <@ ARRAY[{{SoftCodes}}]::text[] AND (cardinality(soft_codes) = 0 OR (check_run_id IS NOT NULL AND requires_password))),
                    CONSTRAINT ck_exec_confirm_tickets_ttl CHECK (expires_at - issued_at BETWEEN interval '15 seconds' AND interval '300 seconds')
                );
                CREATE INDEX ix_exec_confirm_tickets_account ON execution.confirm_tickets (account_id, session_date);
                CREATE TABLE execution.ticket_outcomes (
                    ticket_id uuid NOT NULL,
                    outcome varchar(10) NOT NULL,
                    reason varchar(40) NOT NULL,
                    actor varchar(100) NOT NULL,
                    note varchar(200) NULL,
                    decided_at timestamptz NOT NULL DEFAULT now(),
                    CONSTRAINT pk_exec_ticket_outcomes PRIMARY KEY (ticket_id),
                    CONSTRAINT fk_exec_ticket_outcomes_ticket FOREIGN KEY (ticket_id) REFERENCES execution.confirm_tickets (id) ON DELETE RESTRICT,
                    CONSTRAINT ck_exec_ticket_outcomes_outcome CHECK (outcome IN ('confirmed','skipped','expired','refused')),
                    CONSTRAINT ck_exec_ticket_outcomes_reason CHECK (reason ~ '^[a-z_]{1,40}$'),
                    CONSTRAINT ck_exec_ticket_outcomes_note CHECK (note IS NULL OR (length(btrim(note)) BETWEEN 1 AND 200 AND note !~ '[[:cntrl:]]'))
                );
                CREATE TABLE execution.day_unlocks (
                    id bigint GENERATED BY DEFAULT AS IDENTITY NOT NULL,
                    account_id uuid NOT NULL,
                    session_date date NOT NULL,
                    unlocked_at timestamptz NOT NULL DEFAULT now(),
                    expires_at timestamptz NOT NULL,
                    actor varchar(100) NOT NULL,
                    CONSTRAINT pk_exec_day_unlocks PRIMARY KEY (id),
                    CONSTRAINT uq_exec_day_unlocks_session UNIQUE (account_id, session_date),
                    CONSTRAINT ck_exec_day_unlocks_actor CHECK (actor LIKE 'owner:%' AND length(actor) > 6)
                )
                """);

            migrationBuilder.Sql("""
                CREATE FUNCTION execution.check_confirm_ticket() RETURNS trigger LANGUAGE plpgsql AS $fn$
                DECLARE i record; s text; prior text;
                BEGIN
                    IF abs(extract(epoch FROM (NEW.issued_at - clock_timestamp()))) > 5 THEN
                        RAISE EXCEPTION 'execution.ticket_refused: a ticket is issued now by the database clock' USING ERRCODE = '23514';
                    END IF;
                    SELECT x.id, x.account_id, x.plan_item_id, x.session_date, x.side INTO i FROM execution.intents x WHERE x.public_id = NEW.intent_public_id;
                    IF NOT FOUND OR i.account_id <> NEW.account_id OR i.plan_item_id <> NEW.plan_item_id OR i.session_date <> NEW.session_date OR i.side <> 'buy' THEN
                        RAISE EXCEPTION 'execution.ticket_refused: the ticket does not match a BUY intent of its account, plan ticket and session' USING ERRCODE = '23514';
                    END IF;
                    SELECT st.state INTO s FROM execution.intent_state st WHERE st.intent_id = i.id;
                    IF s IS DISTINCT FROM 'checking' THEN
                        RAISE EXCEPTION 'execution.ticket_refused: a ticket is issued only for an intent in checking' USING ERRCODE = '23514';
                    END IF;
                    IF NEW.attempt = 2 THEN
                        SELECT o.outcome INTO prior FROM execution.confirm_tickets t JOIN execution.ticket_outcomes o ON o.ticket_id = t.id WHERE t.intent_public_id = NEW.intent_public_id AND t.attempt = 1;
                        IF prior IS DISTINCT FROM 'refused' THEN
                            RAISE EXCEPTION 'execution.ticket_refused: a second ticket follows only a refused first one' USING ERRCODE = '23514';
                        END IF;
                    END IF;
                    IF NEW.check_run_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM execution.check_runs r WHERE r.id = NEW.check_run_id AND r.intent_id = i.id AND r.kind = 'full') THEN
                        RAISE EXCEPTION 'execution.ticket_refused: the check run is not a full run of the intent' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END
                $fn$;
                CREATE FUNCTION execution.check_ticket_outcome() RETURNS trigger LANGUAGE plpgsql AS $fn$
                DECLARE t execution.confirm_tickets%ROWTYPE;
                BEGIN
                    NEW.decided_at := clock_timestamp();
                    SELECT * INTO t FROM execution.confirm_tickets WHERE id = NEW.ticket_id;
                    IF NOT FOUND THEN
                        RAISE EXCEPTION 'execution.not_found: there is no such ticket' USING ERRCODE = '23514';
                    END IF;
                    IF NEW.outcome IN ('confirmed', 'skipped') THEN
                        IF session_user = 'xd_exec' OR NEW.actor NOT LIKE 'owner:_%' THEN
                            RAISE EXCEPTION 'execution.owner_only: a tap or a skip is the owner''s act, written by the API' USING ERRCODE = '23514';
                        END IF;
                        IF NEW.decided_at >= t.expires_at THEN
                            RAISE EXCEPTION 'execution.ticket_expired: the card expired before the answer' USING ERRCODE = '23514';
                        END IF;
                    ELSE
                        IF session_user <> 'xd_exec' OR NEW.actor NOT LIKE 'system:_%' THEN
                            RAISE EXCEPTION 'execution.owner_only: an expiry or a refusal is written by the execution service' USING ERRCODE = '23514';
                        END IF;
                        IF NEW.outcome = 'expired' AND NEW.decided_at < t.expires_at THEN
                            RAISE EXCEPTION 'execution.ticket_refused: a ticket expires only at or after its expiry' USING ERRCODE = '23514';
                        END IF;
                    END IF;
                    IF NEW.outcome = 'confirmed' THEN
                        IF NOT COALESCE((SELECT s.execution_enabled FROM execution.settings s ORDER BY s.id DESC LIMIT 1), false) THEN
                            RAISE EXCEPTION 'execution.disabled: the global execution switch is OFF' USING ERRCODE = '23514';
                        END IF;
                        IF execution.effective_mode(t.account_id) NOT IN ('semi_auto', 'auto_live') THEN
                            RAISE EXCEPTION 'execution.mode_changed: the account is not in semi_auto' USING ERRCODE = '23514';
                        END IF;
                        IF EXISTS (SELECT 1 FROM execution.halts h WHERE (h.scope = 'system' OR h.account_id = t.account_id)
                                   AND NOT EXISTS (SELECT 1 FROM execution.halt_releases r WHERE r.halt_id = h.id) AND NOT EXISTS (SELECT 1 FROM execution.halt_auto_clears c WHERE c.halt_id = h.id)) THEN
                            RAISE EXCEPTION 'execution.halted: a halt is in force' USING ERRCODE = '23514';
                        END IF;
                        IF NOT EXISTS (SELECT 1 FROM execution.day_unlocks u WHERE u.account_id = t.account_id AND u.session_date = t.session_date AND u.expires_at > NEW.decided_at) THEN
                            RAISE EXCEPTION 'execution.not_unlocked: the day is not unlocked for this account' USING ERRCODE = '23514';
                        END IF;
                        IF cardinality(t.soft_codes) > 0 AND NEW.note IS NULL THEN
                            RAISE EXCEPTION 'execution.reason_required: a Soft check that did not pass is confirmed with a reason' USING ERRCODE = '23514';
                        END IF;
                    END IF;
                    RETURN NEW;
                END
                $fn$;
                CREATE FUNCTION execution.after_ticket_outcome() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $fn$
                DECLARE t execution.confirm_tickets%ROWTYPE;
                BEGIN
                    IF NEW.outcome = 'confirmed' THEN
                        SELECT * INTO t FROM execution.confirm_tickets WHERE id = NEW.ticket_id;
                        IF cardinality(t.soft_codes) > 0 THEN
                            INSERT INTO execution.check_confirmations (run_id, actor, reason, codes, at) VALUES (t.check_run_id, left(NEW.actor, 64), NEW.note, t.soft_codes, NEW.decided_at);
                        END IF;
                    END IF;
                    PERFORM pg_notify('execution_ticket', '');
                    RETURN NULL;
                END
                $fn$;
                CREATE FUNCTION execution.notify_ticket() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    PERFORM pg_notify('execution_ticket', '');
                    RETURN NULL;
                END
                $fn$;
                CREATE FUNCTION execution.check_day_unlock() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF session_user = 'xd_exec' THEN
                        RAISE EXCEPTION 'execution.owner_only: a day unlock is the owner''s act, written by the API' USING ERRCODE = '23514';
                    END IF;
                    NEW.unlocked_at := clock_timestamp();
                    NEW.expires_at := (NEW.session_date + time '15:30') AT TIME ZONE 'Asia/Kolkata';
                    IF (NEW.unlocked_at AT TIME ZONE 'Asia/Kolkata')::date <> NEW.session_date THEN
                        RAISE EXCEPTION 'execution.unlock_closed: an unlock is for the current India date only' USING ERRCODE = '23514';
                    END IF;
                    IF NEW.unlocked_at >= NEW.expires_at THEN
                        RAISE EXCEPTION 'execution.unlock_closed: the session closed at 15:30 India time' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END
                $fn$;
                CREATE TRIGGER trg_exec_confirm_tickets_check BEFORE INSERT ON execution.confirm_tickets FOR EACH ROW EXECUTE FUNCTION execution.check_confirm_ticket();
                CREATE TRIGGER trg_exec_confirm_tickets_notify AFTER INSERT ON execution.confirm_tickets FOR EACH ROW EXECUTE FUNCTION execution.notify_ticket();
                CREATE TRIGGER trg_exec_ticket_outcomes_check BEFORE INSERT ON execution.ticket_outcomes FOR EACH ROW EXECUTE FUNCTION execution.check_ticket_outcome();
                CREATE TRIGGER trg_exec_ticket_outcomes_after AFTER INSERT ON execution.ticket_outcomes FOR EACH ROW EXECUTE FUNCTION execution.after_ticket_outcome();
                CREATE TRIGGER trg_exec_day_unlocks_check BEFORE INSERT ON execution.day_unlocks FOR EACH ROW EXECUTE FUNCTION execution.check_day_unlock();
                CREATE TRIGGER trg_exec_confirm_tickets_insert_only BEFORE UPDATE OR DELETE ON execution.confirm_tickets FOR EACH ROW EXECUTE FUNCTION execution.refuse_halt_change();
                CREATE TRIGGER trg_exec_confirm_tickets_no_truncate BEFORE TRUNCATE ON execution.confirm_tickets FOR EACH STATEMENT EXECUTE FUNCTION execution.refuse_halt_change();
                CREATE TRIGGER trg_exec_ticket_outcomes_insert_only BEFORE UPDATE OR DELETE ON execution.ticket_outcomes FOR EACH ROW EXECUTE FUNCTION execution.refuse_halt_change();
                CREATE TRIGGER trg_exec_ticket_outcomes_no_truncate BEFORE TRUNCATE ON execution.ticket_outcomes FOR EACH STATEMENT EXECUTE FUNCTION execution.refuse_halt_change();
                CREATE TRIGGER trg_exec_day_unlocks_insert_only BEFORE UPDATE OR DELETE ON execution.day_unlocks FOR EACH ROW EXECUTE FUNCTION execution.refuse_halt_change();
                CREATE TRIGGER trg_exec_day_unlocks_no_truncate BEFORE TRUNCATE ON execution.day_unlocks FOR EACH STATEMENT EXECUTE FUNCTION execution.refuse_halt_change()
                """);

            // The SECURITY DEFINER trigger function is never callable directly: a trigger fires it whatever EXECUTE says, so nobody holds EXECUTE.
            migrationBuilder.Sql("REVOKE ALL ON FUNCTION execution.after_ticket_outcome() FROM PUBLIC");
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_exec') THEN
                        GRANT INSERT, SELECT ON execution.confirm_tickets, execution.ticket_outcomes TO xd_exec;
                        GRANT SELECT ON execution.day_unlocks TO xd_exec;
                        GRANT SELECT (account_id, capital_source, capital_amount) ON accounts.trading_settings TO xd_exec;
                    END IF;
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                        REVOKE ALL ON execution.confirm_tickets, execution.ticket_outcomes, execution.day_unlocks FROM xd_app;
                        REVOKE ALL ON SEQUENCE execution.day_unlocks_id_seq FROM xd_app;
                        GRANT SELECT ON execution.confirm_tickets TO xd_app;
                        GRANT SELECT, INSERT ON execution.ticket_outcomes, execution.day_unlocks TO xd_app;
                        GRANT USAGE ON SEQUENCE execution.day_unlocks_id_seq TO xd_app;
                    END IF;
                END $$
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_exec') THEN
                        REVOKE SELECT (account_id, capital_source, capital_amount) ON accounts.trading_settings FROM xd_exec;
                    END IF;
                END $$
                """);
            migrationBuilder.Sql("DROP TABLE IF EXISTS execution.ticket_outcomes");
            migrationBuilder.Sql("DROP TABLE IF EXISTS execution.confirm_tickets");
            migrationBuilder.Sql("DROP TABLE IF EXISTS execution.day_unlocks");
            migrationBuilder.Sql("""
                DROP FUNCTION IF EXISTS execution.check_day_unlock();
                DROP FUNCTION IF EXISTS execution.notify_ticket();
                DROP FUNCTION IF EXISTS execution.after_ticket_outcome();
                DROP FUNCTION IF EXISTS execution.check_ticket_outcome();
                DROP FUNCTION IF EXISTS execution.check_confirm_ticket()
                """);
        }
    }
}
