using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDividendImportConcurrencyTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The row version of the dividend batches and aliases (PostgreSQL's system column xmin) needs no DDL: it exists on every table, the model only maps it. Two undos of one batch cannot both
            // mark it undone (and audit it twice), and an alias pointed elsewhere by two imports at once cannot lose an update (optimistic concurrency, ADR 0028). The model snapshot records it.
            // (A separate migration, not part of AddDividendImport, because that one may have been applied to a development database already.)
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // nothing to undo: xmin is a system column
        }
    }
}
