using System;
using Microsoft.EntityFrameworkCore.Migrations;
using XDrishti.Infrastructure.Persistence;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// CSV preview and undo (ADR 0017 §6, P1-07a): the range of bars each loaded CSV file wrote and when its load was undone (<c>csv_files</c>), the files a job was asked to load and the file a job undoes
    /// (<c>data_jobs</c>), and <c>market.undo_csv_bars</c>, the one door through which the runtime role can remove bars.
    /// </summary>
    public partial class AddCsvPreviewAndUndo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "csv_files",
                schema: "market",
                table: "data_jobs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "undo_csv_file",
                schema: "market",
                table: "data_jobs",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "first_bar",
                schema: "market",
                table: "csv_files",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_bar",
                schema: "market",
                table: "csv_files",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "undone_at",
                schema: "market",
                table: "csv_files",
                type: "timestamp with time zone",
                nullable: true);

            // The runtime role (xd_app) has no DELETE on the bars, the aggregates or the coverage (ADR 0016) - and keeps none. Undoing a CSV load needs to remove the bars that file wrote, so this one function
            // does it with its owner's rights (SECURITY DEFINER; the migration runs as xd_owner, which owns the tables and so the chunks, compressed ones included: TimescaleDB decompresses what a DELETE touches).
            // It deletes only rows with source 'csv' of one instrument within [p_from, p_to] - never a Dhan bar, never another instrument - then the derived buckets that covered that range (the aggregate phase
            // rebuilds them from the bars that remain), and brings the coverage up to date with the range marked as changed (the row goes when no bar is left). The search path is pinned (system schema first,
            // temporary objects last) and every object is schema-qualified, so a caller's own search_path cannot make it run anything else. Only xd_app may execute it.
            migrationBuilder.Sql("""
                CREATE FUNCTION market.undo_csv_bars(p_instrument uuid, p_from timestamptz, p_to timestamptz)
                RETURNS bigint
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = pg_catalog, market, pg_temp
                AS $$
                DECLARE
                    v_removed bigint;
                    v_day_from timestamptz;
                    v_last_day timestamptz;
                    v_origin_day constant timestamptz := TIMESTAMPTZ '2000-01-01 18:30:00+00';
                    v_origin_week constant timestamptz := TIMESTAMPTZ '2000-01-02 18:30:00+00';
                BEGIN
                    IF p_instrument IS NULL OR p_from IS NULL OR p_to IS NULL OR p_to < p_from THEN
                        RAISE EXCEPTION 'undo_csv_bars needs an instrument and a range with from <= to' USING ERRCODE = '22023';
                    END IF;

                    WITH gone AS (
                        DELETE FROM market.bars_1m
                        WHERE instrument_id = p_instrument AND ts >= p_from AND ts <= p_to AND source = 'csv'
                        RETURNING 1)
                    SELECT count(*) INTO v_removed FROM gone;

                    IF v_removed = 0 THEN
                        RETURN 0;
                    END IF;

                    -- The buckets the range touched, in India time (UTC+05:30): days from midnight, weeks from Monday, months from the 1st - what the aggregate build recomputes for the same range.
                    v_day_from := date_bin(interval '1 day', p_from, v_origin_day);
                    v_last_day := date_bin(interval '1 day', p_to, v_origin_day);
                    DELETE FROM market.bars_agg
                    WHERE instrument_id = p_instrument AND timeframe NOT IN ('1W', '1M') AND ts >= v_day_from AND ts < v_last_day + interval '24 hours';
                    DELETE FROM market.bars_agg
                    WHERE instrument_id = p_instrument AND timeframe = '1W'
                      AND ts >= date_bin(interval '7 days', v_day_from, v_origin_week) AND ts < date_bin(interval '7 days', v_last_day, v_origin_week) + interval '168 hours';
                    DELETE FROM market.bars_agg
                    WHERE instrument_id = p_instrument AND timeframe = '1M'
                      AND ts >= (date_trunc('month', (v_day_from AT TIME ZONE 'UTC') + interval '330 minutes') - interval '330 minutes') AT TIME ZONE 'UTC'
                      AND ts < (date_trunc('month', (v_last_day AT TIME ZONE 'UTC') + interval '330 minutes') + interval '1 month' - interval '330 minutes') AT TIME ZONE 'UTC';

                    IF EXISTS (SELECT 1 FROM market.bars_1m WHERE instrument_id = p_instrument) THEN
                        INSERT INTO market.bar_coverage AS c
                            (instrument_id, first_bar, last_bar, bars, csv_bars, dhan_bars, updated_at, dirty_from, dirty_to, aggregates)
                        SELECT p_instrument, min(ts), max(ts), count(*), count(*) FILTER (WHERE source = 'csv'), count(*) FILTER (WHERE source = 'dhan'), now(), p_from, p_to, '{}'::jsonb
                        FROM market.bars_1m
                        WHERE instrument_id = p_instrument
                        ON CONFLICT (instrument_id) DO UPDATE SET
                            first_bar = EXCLUDED.first_bar, last_bar = EXCLUDED.last_bar, bars = EXCLUDED.bars,
                            csv_bars = EXCLUDED.csv_bars, dhan_bars = EXCLUDED.dhan_bars, updated_at = EXCLUDED.updated_at,
                            dirty_from = LEAST(c.dirty_from, EXCLUDED.dirty_from), dirty_to = GREATEST(c.dirty_to, EXCLUDED.dirty_to);
                    ELSE
                        DELETE FROM market.bar_coverage WHERE instrument_id = p_instrument;
                    END IF;

                    RETURN v_removed;
                END
                $$;
                """);
            migrationBuilder.RestrictFunctionToAppRole("market.undo_csv_bars(uuid, timestamptz, timestamptz)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION market.undo_csv_bars(uuid, timestamptz, timestamptz)");

            migrationBuilder.DropColumn(
                name: "csv_files",
                schema: "market",
                table: "data_jobs");

            migrationBuilder.DropColumn(
                name: "undo_csv_file",
                schema: "market",
                table: "data_jobs");

            migrationBuilder.DropColumn(
                name: "first_bar",
                schema: "market",
                table: "csv_files");

            migrationBuilder.DropColumn(
                name: "last_bar",
                schema: "market",
                table: "csv_files");

            migrationBuilder.DropColumn(
                name: "undone_at",
                schema: "market",
                table: "csv_files");
        }
    }
}
