using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Indexes for the simulation report API (spec AU-14, ADR 0250). Raw SQL: no EF entity maps these tables' new indexes, so the model snapshot does not change. A report of one stock reaches its few runs through the chunk index (job, instrument) and the trades through the
    /// primary key (run, ordinal); a report of one basket reaches its trades through the GIN index on the stored baskets and the chunk of each run through (run key); no report reads the trades of a job it did not ask for.
    /// </summary>
    public partial class AddSimReportIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE INDEX ix_sim_all_chunks_instrument ON research.sim_all_chunks (job_id, instrument_id) INCLUDE (run_key, finished_at) WHERE state = 'done'");
            migrationBuilder.Sql("CREATE INDEX ix_sim_all_chunks_run_key ON research.sim_all_chunks (run_key) INCLUDE (job_id, finished_at) WHERE state = 'done'");
            migrationBuilder.Sql("CREATE INDEX ix_sim_trades_basket_ids ON research.sim_trades USING gin (basket_ids)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS research.ix_sim_trades_basket_ids");
            migrationBuilder.Sql("DROP INDEX IF EXISTS research.ix_sim_all_chunks_run_key");
            migrationBuilder.Sql("DROP INDEX IF EXISTS research.ix_sim_all_chunks_instrument");
        }
    }
}
