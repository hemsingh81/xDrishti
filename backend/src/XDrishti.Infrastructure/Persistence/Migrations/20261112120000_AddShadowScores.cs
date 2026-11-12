using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// CF-10e part 10e-1 (ADR 0281): <c>paper.shadow_scores</c>, one row per shadow candidate and registered model, written with the session before the 09:00 India cut-off. Insert-only for the runtime role; the database stamps <c>scored_at</c> and refuses a score at or after the open.
    /// Raw SQL on purpose: no EF entity maps the table (the runtime uses Npgsql), so the model snapshot does not change. The hashed columns of <c>shadow_entries</c> are not touched.
    /// </summary>
    public partial class AddShadowScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE paper.shadow_scores (
                    id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
                    entry_id bigint NOT NULL,
                    model_id bigint NOT NULL,
                    model_sha256 char(64) NOT NULL,
                    feature_version varchar(40) NOT NULL,
                    p_win double precision NOT NULL,
                    features_hash char(64) NOT NULL,
                    scored_at timestamptz NOT NULL DEFAULT now(),
                    CONSTRAINT pk_shadow_scores PRIMARY KEY (id),
                    CONSTRAINT fk_shadow_scores_shadow_entries_entry_id FOREIGN KEY (entry_id) REFERENCES paper.shadow_entries (id) ON DELETE RESTRICT,
                    CONSTRAINT fk_shadow_scores_model_versions_model_id FOREIGN KEY (model_id) REFERENCES research.model_versions (id) ON DELETE RESTRICT,
                    CONSTRAINT ck_shadow_scores_shape CHECK (p_win >= 0 AND p_win <= 1 AND model_sha256 ~ '^[0-9a-f]{64}$' AND features_hash ~ '^[0-9a-f]{64}$' AND length(btrim(feature_version)) > 0)
                )
                """);
            migrationBuilder.Sql("CREATE UNIQUE INDEX uq_shadow_scores_entry_model ON paper.shadow_scores (entry_id, model_id)");
            migrationBuilder.Sql("CREATE INDEX ix_shadow_scores_model_id ON paper.shadow_scores (model_id)");

            // The time is the database's, and a score cannot be written at or after 09:00 India time of the session of its entry (as for the entry itself).
            migrationBuilder.Sql("""
                CREATE FUNCTION paper.guard_shadow_score() RETURNS trigger LANGUAGE plpgsql AS $fn$
                DECLARE
                    entry_session date;
                BEGIN
                    NEW.scored_at := paper.shadow_now();
                    SELECT session_date INTO entry_session FROM paper.shadow_entries WHERE id = NEW.entry_id;
                    IF entry_session IS NULL OR NEW.scored_at >= (entry_session + time '09:00') AT TIME ZONE 'Asia/Kolkata' THEN
                        RAISE EXCEPTION 'A shadow score is written before 09:00 India time of the session of its entry' USING ERRCODE = '23514';
                    END IF;

                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.Sql("CREATE TRIGGER trg_shadow_scores_guard BEFORE INSERT ON paper.shadow_scores FOR EACH ROW EXECUTE FUNCTION paper.guard_shadow_score()");

            migrationBuilder.GrantTableToAppRole(Schemas.Paper, "shadow_scores");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Paper, "shadow_scores");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_shadow_scores_guard ON paper.shadow_scores");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS paper.guard_shadow_score()");
            migrationBuilder.Sql("DROP TABLE IF EXISTS paper.shadow_scores");
        }
    }
}
