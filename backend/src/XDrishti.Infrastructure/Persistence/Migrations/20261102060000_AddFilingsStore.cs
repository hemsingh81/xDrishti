using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFilingsStore : Migration
    {
        private static readonly string[] Tables = ["filings", "result_facts", "shareholding", "announcements", "calendar"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // P15-02, ADR 0180: the point-in-time filing store. Raw SQL on purpose (nothing maps these tables in the EF model; the ingest writes with SQL).
            migrationBuilder.Sql("CREATE SCHEMA IF NOT EXISTS filings");

            // The envelope: one row per stored file, immutable. broadcast_ts is NULL exactly when the exchange time is unknown (state 'unavailable', with a closed reason): the date is never substituted (D3).
            // The id is deterministic (first 16 bytes of SHA-256 over instrument | kind | period | content hash), so replays and any insert order give the same id. A row is superseded only by a later revision of the same
            // instrument, kind and period (the trigger below), and by one revision only (the partial unique index): the chain is linear.
            migrationBuilder.Sql("""
                CREATE TABLE filings.filings (
                    id uuid NOT NULL,
                    instrument_id uuid NOT NULL,
                    kind varchar(14) NOT NULL,
                    period_end date NULL,
                    broadcast_ts timestamptz NULL,
                    broadcast_state varchar(11) NOT NULL,
                    unavailable_reason varchar(30) NULL,
                    ingested_at timestamptz NOT NULL,
                    source varchar(12) NOT NULL,
                    content_sha256 char(64) NOT NULL,
                    file_bytes bigint NOT NULL,
                    file_name varchar(120) NOT NULL,
                    source_ref varchar(200) NULL,
                    supersedes uuid NULL,
                    CONSTRAINT pk_filings PRIMARY KEY (id),
                    CONSTRAINT fk_filings_instrument FOREIGN KEY (instrument_id) REFERENCES market.instruments (id) ON DELETE RESTRICT,
                    CONSTRAINT fk_filings_supersedes FOREIGN KEY (supersedes) REFERENCES filings.filings (id) ON DELETE RESTRICT,
                    CONSTRAINT ck_filings_kind CHECK (kind IN ('results', 'shareholding', 'board_meeting', 'announcement')),
                    CONSTRAINT ck_filings_broadcast_state CHECK (broadcast_state IN ('ok', 'unavailable')),
                    CONSTRAINT ck_filings_state_ts CHECK ((broadcast_state = 'ok') = (broadcast_ts IS NOT NULL)),
                    CONSTRAINT ck_filings_reason CHECK ((broadcast_state = 'unavailable') = (unavailable_reason IS NOT NULL)),
                    CONSTRAINT ck_filings_reason_word CHECK (unavailable_reason IS NULL OR unavailable_reason IN ('missing', 'date_only', 'no_offset', 'unparsable', 'before_2000', 'broadcast_in_future')),
                    CONSTRAINT ck_filings_source CHECK (source IN ('folder_drop')),
                    CONSTRAINT ck_filings_sha CHECK (content_sha256 ~ '^[0-9a-f]{64}$'),
                    CONSTRAINT ck_filings_file_bytes CHECK (file_bytes >= 0),
                    CONSTRAINT ck_filings_self_supersede CHECK (supersedes IS NULL OR supersedes <> id),
                    CONSTRAINT ck_filings_supersede_ts CHECK (supersedes IS NULL OR (broadcast_state = 'ok' AND period_end IS NOT NULL))
                )
                """);
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_filings_content_sha256 ON filings.filings (content_sha256)");
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_filings_supersedes ON filings.filings (supersedes) WHERE supersedes IS NOT NULL");
            migrationBuilder.Sql("CREATE INDEX ix_filings_instrument_kind_broadcast ON filings.filings (instrument_id, kind, broadcast_ts)");
            migrationBuilder.Sql("CREATE INDEX ix_filings_kind_broadcast ON filings.filings (kind, broadcast_ts)");

            // A revision must belong to the filing it replaces: same instrument, kind and period, both times known, strictly later. The function reads only an immutable row, runs as the inserting role (which may read the table) and
            // pins its search_path; every name in it is schema-qualified.
            migrationBuilder.Sql("""
                CREATE FUNCTION filings.check_supersede() RETURNS trigger
                LANGUAGE plpgsql
                SET search_path = pg_catalog, pg_temp
                AS $$
                DECLARE
                    prior record;
                BEGIN
                    IF NEW.supersedes IS NULL THEN
                        RETURN NEW;
                    END IF;
                    SELECT f.instrument_id, f.kind, f.period_end, f.broadcast_ts INTO prior FROM filings.filings f WHERE f.id = NEW.supersedes;
                    IF NOT FOUND OR prior.instrument_id <> NEW.instrument_id OR prior.kind <> NEW.kind OR prior.period_end IS DISTINCT FROM NEW.period_end
                       OR prior.broadcast_ts IS NULL OR NEW.broadcast_ts IS NULL OR NEW.broadcast_ts <= prior.broadcast_ts THEN
                        RAISE EXCEPTION 'filings.bad_supersede: a revision must replace an earlier filing of the same instrument, kind and period' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$
                """);
            migrationBuilder.Sql("CREATE TRIGGER trg_filings_check_supersede BEFORE INSERT ON filings.filings FOR EACH ROW EXECUTE FUNCTION filings.check_supersede()");

            // The fact tables are created empty and are written by later slices (P15-03 result facts, P15-04 shareholding and calendar); this slice inserts nothing into them.
            migrationBuilder.Sql("""
                CREATE TABLE filings.result_facts (
                    filing_id uuid NOT NULL,
                    field_code varchar(40) NOT NULL,
                    value numeric NULL,
                    unit varchar(12) NOT NULL,
                    basis varchar(16) NOT NULL,
                    state varchar(11) NOT NULL,
                    CONSTRAINT pk_result_facts PRIMARY KEY (filing_id, field_code, basis),
                    CONSTRAINT fk_result_facts_filing FOREIGN KEY (filing_id) REFERENCES filings.filings (id) ON DELETE RESTRICT,
                    CONSTRAINT ck_result_facts_state CHECK (state IN ('value', 'unavailable')),
                    CONSTRAINT ck_result_facts_value CHECK ((state = 'value') = (value IS NOT NULL))
                )
                """);
            migrationBuilder.Sql("""
                CREATE TABLE filings.shareholding (
                    filing_id uuid NOT NULL,
                    category varchar(30) NOT NULL,
                    pct numeric NULL,
                    pledged_pct numeric NULL,
                    state varchar(11) NOT NULL,
                    CONSTRAINT pk_shareholding PRIMARY KEY (filing_id, category),
                    CONSTRAINT fk_shareholding_filing FOREIGN KEY (filing_id) REFERENCES filings.filings (id) ON DELETE RESTRICT,
                    CONSTRAINT ck_shareholding_state CHECK (state IN ('value', 'unavailable')),
                    CONSTRAINT ck_shareholding_value CHECK ((state = 'value') = (pct IS NOT NULL))
                )
                """);
            migrationBuilder.Sql("""
                CREATE TABLE filings.announcements (
                    filing_id uuid NOT NULL,
                    category varchar(60) NULL,
                    headline varchar(300) NULL,
                    CONSTRAINT pk_announcements PRIMARY KEY (filing_id),
                    CONSTRAINT fk_announcements_filing FOREIGN KEY (filing_id) REFERENCES filings.filings (id) ON DELETE RESTRICT
                )
                """);
            migrationBuilder.Sql("""
                CREATE TABLE filings.calendar (
                    filing_id uuid NOT NULL,
                    meeting_date date NOT NULL,
                    purpose varchar(20) NOT NULL,
                    status varchar(12) NOT NULL,
                    period_end date NOT NULL,
                    CONSTRAINT pk_calendar PRIMARY KEY (filing_id),
                    CONSTRAINT fk_calendar_filing FOREIGN KEY (filing_id) REFERENCES filings.filings (id) ON DELETE RESTRICT
                )
                """);

            // Insert-only for the runtime role, granted table by table (a schema-wide grant would give back UPDATE and DELETE; `filings` is not re-granted as a whole). The execution role and the read-only roles are granted nothing.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                        GRANT USAGE ON SCHEMA filings TO xd_app;
                    END IF;
                END $$;
                """);
            foreach (var table in Tables)
            {
                migrationBuilder.GrantTableToAppRole(Schemas.Filings, table);
                migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Filings, table);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS filings.calendar");
            migrationBuilder.Sql("DROP TABLE IF EXISTS filings.announcements");
            migrationBuilder.Sql("DROP TABLE IF EXISTS filings.shareholding");
            migrationBuilder.Sql("DROP TABLE IF EXISTS filings.result_facts");
            migrationBuilder.Sql("DROP TABLE IF EXISTS filings.filings");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS filings.check_supersede()");
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS filings");
        }
    }
}
