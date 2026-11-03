using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFilingExtractions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // P15-08, ADR 0189: what the local model said about one stored announcement, with the hashes of the model file and the prompt it said it with. Raw SQL on purpose (nothing maps this table in the EF model; the extraction verb writes with SQL).
            // Only the checked answer or a closed reason is stored: never the raw answer and never the announcement text. state 'ok' holds the whole answer, every other state holds none of it and a closed reason. One row per filing, model, prompt and schema.
            migrationBuilder.Sql("""
                CREATE TABLE filings.extractions (
                    filing_id uuid NOT NULL,
                    model_sha256 char(64) NOT NULL,
                    prompt_name varchar(40) NOT NULL,
                    prompt_version varchar(16) NOT NULL,
                    prompt_hash char(64) NOT NULL,
                    schema_version varchar(8) NOT NULL,
                    state varchar(17) NOT NULL,
                    reason varchar(30) NULL,
                    material boolean NULL,
                    category varchar(30) NULL,
                    rationale varchar(200) NULL,
                    order_value_inr numeric NULL,
                    guidance varchar(12) NULL,
                    rating_action varchar(12) NULL,
                    ungrounded boolean NOT NULL,
                    latency_ms integer NOT NULL,
                    prompt_tokens integer NULL,
                    completion_tokens integer NULL,
                    created_at timestamptz NOT NULL,
                    CONSTRAINT pk_extractions PRIMARY KEY (filing_id, model_sha256, prompt_hash, schema_version),
                    CONSTRAINT fk_extractions_filing FOREIGN KEY (filing_id) REFERENCES filings.filings (id) ON DELETE RESTRICT,
                    CONSTRAINT ck_extractions_model CHECK (model_sha256 ~ '^[0-9a-f]{64}$'),
                    CONSTRAINT ck_extractions_prompt CHECK (prompt_hash ~ '^[0-9a-f]{64}$'),
                    CONSTRAINT ck_extractions_state CHECK (state IN ('ok', 'invalid_output', 'model_unavailable', 'too_long')),
                    CONSTRAINT ck_extractions_ok_fields CHECK ((state = 'ok') = (material IS NOT NULL AND category IS NOT NULL)),
                    CONSTRAINT ck_extractions_ok_answer CHECK ((state = 'ok') = (rationale IS NOT NULL AND guidance IS NOT NULL AND rating_action IS NOT NULL)),
                    CONSTRAINT ck_extractions_reason CHECK ((state = 'ok') = (reason IS NULL)),
                    CONSTRAINT ck_extractions_empty_unless_ok CHECK (state = 'ok' OR (material IS NULL AND category IS NULL AND rationale IS NULL AND guidance IS NULL AND rating_action IS NULL AND order_value_inr IS NULL)),
                    CONSTRAINT ck_extractions_category CHECK (category IS NULL OR category IN ('results', 'order_win', 'acquisition_or_merger', 'fund_raising', 'credit_rating', 'guidance', 'management_change', 'litigation_or_regulatory', 'default_or_delay', 'dividend_or_buyback', 'procedural', 'other')),
                    CONSTRAINT ck_extractions_guidance CHECK (guidance IS NULL OR guidance IN ('raised', 'lowered', 'maintained', 'none')),
                    CONSTRAINT ck_extractions_rating CHECK (rating_action IS NULL OR rating_action IN ('upgrade', 'downgrade', 'reaffirmed', 'withdrawn', 'none')),
                    CONSTRAINT ck_extractions_counts CHECK (latency_ms >= 0 AND (prompt_tokens IS NULL OR prompt_tokens >= 0) AND (completion_tokens IS NULL OR completion_tokens >= 0))
                )
                """);

            // Insert-only for the runtime role, granted table by table (a schema-wide re-grant would give back UPDATE and DELETE). The execution role, the read-only roles and the MCP roles (xd_mcp_read, xd_mcp_sql) are granted nothing.
            migrationBuilder.GrantTableToAppRole(Schemas.Filings, "extractions");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Filings, "extractions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS filings.extractions");
        }
    }
}
