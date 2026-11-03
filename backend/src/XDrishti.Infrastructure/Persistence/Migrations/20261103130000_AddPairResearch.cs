using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPairResearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // P14-13, ADR 0196: the formations and the trades of the pairs study. Raw SQL on purpose: nothing maps these tables in the EF model; the store writes with SQL. Insert-only for the runtime role, granted table by table
            // (the research schema is already usable by it; a schema-wide re-grant would give back UPDATE and DELETE). The execution role and the read-only roles are granted nothing.
            migrationBuilder.Sql("""
                CREATE TABLE research.pair_formations (
                    formation_id char(64) NOT NULL,
                    family varchar(32) NOT NULL,
                    quarter varchar(8) NOT NULL,
                    window_from date NOT NULL,
                    window_to date NOT NULL,
                    candidates integer NOT NULL,
                    tested integer NOT NULL,
                    unavailable integer NOT NULL,
                    selected integer NOT NULL,
                    detail jsonb NOT NULL,
                    created_at timestamptz NOT NULL DEFAULT now(),
                    CONSTRAINT pk_pair_formations PRIMARY KEY (formation_id),
                    CONSTRAINT ck_pair_formations_id CHECK (formation_id ~ '^[0-9a-f]{64}$'),
                    CONSTRAINT ck_pair_formations_window CHECK (window_from <= window_to),
                    CONSTRAINT ck_pair_formations_counts CHECK (candidates >= 0 AND tested >= 0 AND unavailable >= 0 AND selected >= 0 AND tested + unavailable = candidates AND selected <= tested),
                    CONSTRAINT ck_pair_formations_detail CHECK (jsonb_typeof(detail) = 'object')
                )
                """);
            migrationBuilder.Sql("CREATE INDEX ix_pair_formations_family_quarter ON research.pair_formations (family, quarter)");

            migrationBuilder.Sql("""
                CREATE TABLE research.pair_trades (
                    run_id char(64) NOT NULL,
                    pair_key varchar(200) NOT NULL,
                    ordinal integer NOT NULL,
                    entry_ts timestamptz NOT NULL,
                    exit_ts timestamptz NOT NULL,
                    side varchar(12) NOT NULL,
                    z_entry double precision NOT NULL,
                    z_exit double precision NULL,
                    exit_reason varchar(16) NOT NULL,
                    qty_y integer NOT NULL,
                    qty_x integer NOT NULL,
                    gross_inr numeric(18,2) NOT NULL,
                    net_inr numeric(18,2) NULL,
                    r double precision NOT NULL,
                    labels text[] NOT NULL,
                    CONSTRAINT pk_pair_trades PRIMARY KEY (run_id, pair_key, ordinal),
                    CONSTRAINT ck_pair_trades_run CHECK (run_id ~ '^[0-9a-f]{64}$'),
                    CONSTRAINT ck_pair_trades_ordinal CHECK (ordinal >= 1),
                    CONSTRAINT ck_pair_trades_times CHECK (entry_ts <= exit_ts),
                    CONSTRAINT ck_pair_trades_side CHECK (side IN ('long_spread', 'short_spread')),
                    CONSTRAINT ck_pair_trades_reason CHECK (exit_reason IN ('exit', 'stop', 'square_off', 'end_of_data')),
                    CONSTRAINT ck_pair_trades_qty CHECK (qty_y >= 1 AND qty_x >= 1),
                    CONSTRAINT ck_pair_trades_numbers CHECK (z_entry > '-Infinity'::double precision AND z_entry < 'Infinity'::double precision AND (z_exit IS NULL OR (z_exit > '-Infinity'::double precision AND z_exit < 'Infinity'::double precision))
                        AND r > '-Infinity'::double precision AND r < 'Infinity'::double precision)
                )
                """);
            migrationBuilder.Sql("CREATE INDEX ix_pair_trades_entry ON research.pair_trades (entry_ts)");

            migrationBuilder.GrantTableToAppRole(Schemas.Research, "pair_formations");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Research, "pair_formations");
            migrationBuilder.GrantTableToAppRole(Schemas.Research, "pair_trades");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Research, "pair_trades");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS research.pair_trades");
            migrationBuilder.Sql("DROP TABLE IF EXISTS research.pair_formations");
        }
    }
}
