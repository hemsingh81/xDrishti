using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RequireConfirmedKeepForAutoOn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // P15-10 (owner amendment): the automatic path may turn the filter ON only from a protocol-F KEEP that the hold-out confirmed. The pure rule asks for it; the trigger asks for the same of the row's own settings snapshot (verdict.state KEEP, verdict.hold_out_confirmed true and verdict.results_within_days equal to the row's own window),
            // so a caller that skips the rule cannot turn the filter on from a development KEEP. Same function, one more check; the rest of the limits are unchanged. No data changes.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION filings.check_event_filter_decision() RETURNS trigger
                LANGUAGE plpgsql
                SET search_path = pg_catalog, pg_temp
                AS $$
                DECLARE
                    prior record;
                    before record;
                BEGIN
                    PERFORM pg_catalog.pg_advisory_xact_lock(pg_catalog.hashtextextended('filings.event_filter_decisions', 0));
                    NEW.changed_at := pg_catalog.clock_timestamp();
                    IF NEW.decided_by = 'auto' THEN
                        SELECT d.enabled INTO before FROM filings.event_filter_decisions d ORDER BY d.id DESC LIMIT 1 OFFSET 1;
                        SELECT d.enabled, d.results_within_days, d.approval, d.decided_by INTO prior FROM filings.event_filter_decisions d ORDER BY d.id DESC LIMIT 1;   -- last: IF NOT FOUND below reads this one
                        IF NOT FOUND OR prior.approval <> 'auto' OR NEW.approval <> 'auto' OR NEW.results_within_days <> prior.results_within_days THEN
                            RAISE EXCEPTION 'filings.event_filter_refused: an automatic row needs approval auto in force and changes neither the window nor the approval' USING ERRCODE = '23514';
                        END IF;
                        IF NEW.enabled AND prior.enabled THEN
                            RAISE EXCEPTION 'filings.event_filter_refused: the filter is already on' USING ERRCODE = '23514';
                        END IF;
                        IF NEW.enabled AND NOT (COALESCE(NEW.setting_snapshot ->> 'verdict.state', '') = 'KEEP' AND COALESCE(NEW.setting_snapshot ->> 'verdict.hold_out_confirmed', '') = 'true'
                                      AND COALESCE(NEW.setting_snapshot ->> 'verdict.results_within_days', '') = NEW.results_within_days::text) THEN
                            RAISE EXCEPTION 'filings.event_filter_refused: an automatic turn-on needs a protocol F verdict KEEP that the hold-out confirmed for this window' USING ERRCODE = '23514';
                        END IF;
                        IF NEW.enabled AND NOT prior.enabled AND prior.decided_by = 'owner' AND COALESCE(before.enabled, false) THEN
                            RAISE EXCEPTION 'filings.event_filter_refused: the owner switched the filter off; it stays off until the owner turns it on or sets approval manual' USING ERRCODE = '23514';
                        END IF;
                        IF NOT NEW.enabled AND NOT (prior.enabled AND prior.decided_by = 'auto') THEN
                            RAISE EXCEPTION 'filings.event_filter_refused: only a filter that an automatic row turned on can be turned off automatically' USING ERRCODE = '23514';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $$

                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION filings.check_event_filter_decision() RETURNS trigger
                LANGUAGE plpgsql
                SET search_path = pg_catalog, pg_temp
                AS $$
                DECLARE
                    prior record;
                    before record;
                BEGIN
                    PERFORM pg_catalog.pg_advisory_xact_lock(pg_catalog.hashtextextended('filings.event_filter_decisions', 0));
                    NEW.changed_at := pg_catalog.clock_timestamp();
                    IF NEW.decided_by = 'auto' THEN
                        SELECT d.enabled INTO before FROM filings.event_filter_decisions d ORDER BY d.id DESC LIMIT 1 OFFSET 1;
                        SELECT d.enabled, d.results_within_days, d.approval, d.decided_by INTO prior FROM filings.event_filter_decisions d ORDER BY d.id DESC LIMIT 1;   -- last: IF NOT FOUND below reads this one
                        IF NOT FOUND OR prior.approval <> 'auto' OR NEW.approval <> 'auto' OR NEW.results_within_days <> prior.results_within_days THEN
                            RAISE EXCEPTION 'filings.event_filter_refused: an automatic row needs approval auto in force and changes neither the window nor the approval' USING ERRCODE = '23514';
                        END IF;
                        IF NEW.enabled AND prior.enabled THEN
                            RAISE EXCEPTION 'filings.event_filter_refused: the filter is already on' USING ERRCODE = '23514';
                        END IF;
                        IF NEW.enabled AND NOT prior.enabled AND prior.decided_by = 'owner' AND COALESCE(before.enabled, false) THEN
                            RAISE EXCEPTION 'filings.event_filter_refused: the owner switched the filter off; it stays off until the owner turns it on or sets approval manual' USING ERRCODE = '23514';
                        END IF;
                        IF NOT NEW.enabled AND NOT (prior.enabled AND prior.decided_by = 'auto') THEN
                            RAISE EXCEPTION 'filings.event_filter_refused: only a filter that an automatic row turned on can be turned off automatically' USING ERRCODE = '23514';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $$

                """);
        }
    }
}
