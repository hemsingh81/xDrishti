using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExtensionTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // P13-05: the two tables shared by the feature-set comparison of P13 (md-1) and P15 (ev-1, fu-1, llm-1). Raw SQL on purpose (nothing maps these tables in the EF model). NaN in feature_values means unknown; it is never filled.
            // `values` is a reserved word, so the column is feature_values, as in research.trade_features.
            migrationBuilder.Sql("""
                CREATE TABLE research.trade_features_ext (
                    run_key char(64) NOT NULL,
                    trade_key text NOT NULL,
                    extension varchar(8) NOT NULL,
                    version varchar(16) NOT NULL,
                    decision_at timestamptz NOT NULL,
                    feature_values double precision[] NOT NULL,
                    created_at timestamptz NOT NULL DEFAULT now(),
                    CONSTRAINT pk_trade_features_ext PRIMARY KEY (run_key, trade_key, extension, version),
                    CONSTRAINT ck_trade_features_ext_run_key CHECK (run_key ~ '^[0-9a-f]{64}$'),
                    CONSTRAINT ck_trade_features_ext_extension CHECK (extension ~ '^[a-z]{2,8}$'),
                    CONSTRAINT ck_trade_features_ext_version CHECK (version ~ ('^' || extension || '-[0-9]{1,3}$')),
                    CONSTRAINT ck_trade_features_ext_values CHECK (cardinality(feature_values) > 0)
                )
                """);
            migrationBuilder.Sql("CREATE INDEX ix_trade_features_ext_version_run ON research.trade_features_ext (version, run_key)");

            // One row per evaluation; protocol and inputs let P15-10 store protocols F, P and L here as well. The hold-out is read once per family and version key: a partial unique index, not a trigger (so there is no code to pin).
            migrationBuilder.Sql("""
                CREATE TABLE research.extension_evaluations (
                    evaluation_id char(64) NOT NULL,
                    family varchar(32) NOT NULL,
                    protocol varchar(8) NOT NULL,
                    extension_version varchar(16) NULL,
                    version_key varchar(200) NOT NULL,
                    base_version varchar(16) NOT NULL,
                    recipe_hash char(64) NOT NULL,
                    training_data_id char(64) NULL,
                    trial_hash char(64) NULL,
                    trials integer NOT NULL,
                    factor double precision NOT NULL,
                    fold_unit varchar(8) NOT NULL,
                    folds_total integer NOT NULL,
                    folds_evaluable integer NOT NULL,
                    rows integer NOT NULL,
                    flips integer NULL,
                    estimates jsonb NOT NULL,
                    verdict varchar(16) NOT NULL,
                    missing text[] NOT NULL,
                    limits text[] NOT NULL,
                    evidence jsonb NOT NULL,
                    inputs jsonb NOT NULL,
                    holdout_read boolean NOT NULL,
                    created_at timestamptz NOT NULL DEFAULT now(),
                    CONSTRAINT pk_extension_evaluations PRIMARY KEY (evaluation_id),
                    CONSTRAINT ck_extension_evaluations_id CHECK (evaluation_id ~ '^[0-9a-f]{64}$'),
                    CONSTRAINT ck_extension_evaluations_recipe CHECK (recipe_hash ~ '^[0-9a-f]{64}$'),
                    CONSTRAINT ck_extension_evaluations_data CHECK (training_data_id IS NULL OR training_data_id ~ '^[0-9a-f]{64}$'),
                    CONSTRAINT ck_extension_evaluations_trial CHECK (trial_hash IS NULL OR trial_hash ~ '^[0-9a-f]{64}$'),
                    CONSTRAINT ck_extension_evaluations_verdict CHECK (verdict IN ('KEEP', 'DROP', 'INCONCLUSIVE', 'NOT_EVALUABLE')),
                    CONSTRAINT ck_extension_evaluations_fold_unit CHECK (fold_unit IN ('month', 'quarter', 'none')),
                    CONSTRAINT ck_extension_evaluations_counts CHECK (folds_total >= 0 AND folds_evaluable >= 0 AND rows >= 0 AND (flips IS NULL OR flips >= 0)),
                    CONSTRAINT ck_extension_evaluations_trials CHECK (trials >= 1)
                )
                """);
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_extension_evaluations_holdout ON research.extension_evaluations (family, version_key) WHERE holdout_read");
            migrationBuilder.Sql("CREATE INDEX ix_extension_evaluations_family_created ON research.extension_evaluations (family, created_at)");

            // Insert-only for the runtime role, granted table by table (the schema is already usable by it; a schema-wide re-grant would give back UPDATE and DELETE). The execution role and the read-only roles are granted nothing.
            migrationBuilder.GrantTableToAppRole(Schemas.Research, "trade_features_ext");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Research, "trade_features_ext");
            migrationBuilder.GrantTableToAppRole(Schemas.Research, "extension_evaluations");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Research, "extension_evaluations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS research.extension_evaluations");
            migrationBuilder.Sql("DROP TABLE IF EXISTS research.trade_features_ext");
        }
    }
}
