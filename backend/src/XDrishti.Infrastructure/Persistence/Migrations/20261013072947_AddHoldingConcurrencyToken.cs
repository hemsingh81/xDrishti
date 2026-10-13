using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHoldingConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The row version of the holdings (PostgreSQL's system column xmin) needs no DDL: it exists on every table, the model only maps it, so that a holdings sync cannot
            // overwrite a quote written after it read the rows (optimistic concurrency, ADR 0019). The model snapshot records it.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // nothing to undo: xmin is a system column
        }
    }
}
