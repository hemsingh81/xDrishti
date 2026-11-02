using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReshapeResultFacts : Migration
    {
        private static readonly string[] Tables = ["result_parses", "result_facts"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // P15-03, ADR 0184: the fact table of P15-02 was never written (a test pins that). It is replaced, not altered, by a table keyed on the parser version, the span and the basis. The step refuses, changing nothing, when the old table has a row.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM filings.result_facts) THEN
                        RAISE EXCEPTION 'filings.result_facts is not empty: ReshapeResultFacts changes nothing';
                    END IF;
                END $$;
                """);
            migrationBuilder.Sql("DROP TABLE filings.result_facts");

            // One row per (filing, parser version): the outcome of the parse. A rejected or not-xbrl file has a row and no fact. The facts of a version are written with their parse row in one transaction.
            migrationBuilder.Sql("""
                CREATE TABLE filings.result_parses (
                    filing_id uuid NOT NULL,
                    parser_version varchar(24) NOT NULL,
                    outcome varchar(10) NOT NULL,
                    reason varchar(30) NULL,
                    facts integer NOT NULL,
                    flags jsonb NOT NULL,
                    parsed_at timestamptz NOT NULL,
                    CONSTRAINT pk_result_parses PRIMARY KEY (filing_id, parser_version),
                    CONSTRAINT fk_result_parses_filing FOREIGN KEY (filing_id) REFERENCES filings.filings (id) ON DELETE RESTRICT,
                    CONSTRAINT ck_result_parses_outcome CHECK (outcome IN ('parsed', 'rejected', 'not_xbrl', 'no_facts')),
                    CONSTRAINT ck_result_parses_facts CHECK (facts >= 0 AND (outcome = 'parsed' OR facts = 0))
                )
                """);
            migrationBuilder.Sql("""
                CREATE TABLE filings.result_facts (
                    filing_id uuid NOT NULL,
                    parser_version varchar(24) NOT NULL,
                    field_code varchar(40) NOT NULL,
                    span varchar(4) NOT NULL,
                    basis varchar(16) NOT NULL,
                    value numeric NULL,
                    unit varchar(16) NOT NULL,
                    state varchar(11) NOT NULL,
                    unavailable_reason varchar(30) NULL,
                    CONSTRAINT pk_result_facts PRIMARY KEY (filing_id, parser_version, field_code, span, basis),
                    CONSTRAINT fk_result_facts_filing FOREIGN KEY (filing_id) REFERENCES filings.filings (id) ON DELETE RESTRICT,
                    CONSTRAINT fk_result_facts_parse FOREIGN KEY (filing_id, parser_version) REFERENCES filings.result_parses (filing_id, parser_version) ON DELETE RESTRICT,
                    CONSTRAINT ck_result_facts_span CHECK (span IN ('q', 'ytd', 'fy')),
                    CONSTRAINT ck_result_facts_basis CHECK (basis IN ('consolidated', 'standalone')),
                    CONSTRAINT ck_result_facts_state CHECK (state IN ('value', 'unavailable')),
                    CONSTRAINT ck_result_facts_value CHECK ((state = 'value') = (value IS NOT NULL)),
                    CONSTRAINT ck_result_facts_reason CHECK ((state = 'unavailable') = (unavailable_reason IS NOT NULL))
                )
                """);
            Grants(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to the P15-02 shape; refuses, changing nothing, when anything was parsed.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM filings.result_facts) OR EXISTS (SELECT 1 FROM filings.result_parses) THEN
                        RAISE EXCEPTION 'filings.result_facts or filings.result_parses is not empty: the Down step of ReshapeResultFacts changes nothing';
                    END IF;
                END $$;
                """);
            migrationBuilder.Sql("DROP TABLE filings.result_facts");
            migrationBuilder.Sql("DROP TABLE filings.result_parses");
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
            migrationBuilder.GrantTableToAppRole(Schemas.Filings, "result_facts");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Filings, "result_facts");
        }

        // Insert-only for the runtime role, table by table; the execution role and the read-only roles are granted nothing.
        private static void Grants(MigrationBuilder migrationBuilder)
        {
            foreach (var table in Tables)
            {
                migrationBuilder.GrantTableToAppRole(Schemas.Filings, table);
                migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Filings, table);
            }
        }
    }
}
