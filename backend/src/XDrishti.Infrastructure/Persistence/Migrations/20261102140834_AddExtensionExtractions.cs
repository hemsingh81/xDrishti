using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExtensionExtractions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // P15-05: what an extension extraction (ev-1, fu-1) of a stored run was built from, so the comparison of P15-10 can tell when the inputs changed. Raw SQL on purpose (nothing maps this table in the EF model).
            // The rows themselves go to the feature table of P13-05. parser_version is null for an extension that reads no facts.
            migrationBuilder.Sql("""
                CREATE TABLE research.extension_extractions (
                    run_key char(64) NOT NULL,
                    extension varchar(8) NOT NULL,
                    version varchar(16) NOT NULL,
                    lag_minutes integer NOT NULL,
                    parser_version varchar(32) NULL,
                    calendar_sha256 char(64) NOT NULL,
                    filings_watermark timestamptz NULL,
                    rows integer NOT NULL,
                    nan_counts jsonb NOT NULL,
                    extracted_at timestamptz NOT NULL DEFAULT now(),
                    CONSTRAINT pk_extension_extractions PRIMARY KEY (run_key, extension, version),
                    CONSTRAINT ck_extension_extractions_run_key CHECK (run_key ~ '^[0-9a-f]{64}$'),
                    CONSTRAINT ck_extension_extractions_calendar CHECK (calendar_sha256 ~ '^[0-9a-f]{64}$'),
                    CONSTRAINT ck_extension_extractions_extension CHECK (extension ~ '^[a-z]{2,8}$'),
                    CONSTRAINT ck_extension_extractions_version CHECK (version ~ ('^' || extension || '-[0-9]{1,3}$')),
                    CONSTRAINT ck_extension_extractions_counts CHECK (lag_minutes >= 0 AND rows >= 0)
                )
                """);

            // Insert-only for the runtime role, granted table by table (a schema-wide re-grant would give back UPDATE and DELETE). The execution role and the read-only roles are granted nothing.
            migrationBuilder.GrantTableToAppRole(Schemas.Research, "extension_extractions");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Research, "extension_extractions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS research.extension_extractions");
        }
    }
}
