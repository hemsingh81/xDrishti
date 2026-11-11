using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// NSE's lot file as a fourth source of <c>options.lot_sizes</c> (spec BK-05b, ADR 0256): <c>nse_mktlots</c> joins the allowed values of <c>ck_lot_sizes_source</c>. The confirmed check is untouched, so such a row is always unconfirmed. Raw SQL on purpose
    /// (the table has no EF entity), so the model snapshot does not change. Down refuses while any <c>nse_mktlots</c> row exists and never deletes a row.
    /// </summary>
    public partial class AllowNseLotSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE options.lot_sizes DROP CONSTRAINT ck_lot_sizes_source");
            migrationBuilder.Sql("ALTER TABLE options.lot_sizes ADD CONSTRAINT ck_lot_sizes_source CHECK (source IN ('circular', 'secondary', 'scrip_master', 'nse_mktlots'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $do$
                BEGIN
                    IF EXISTS (SELECT 1 FROM options.lot_sizes WHERE source = 'nse_mktlots') THEN
                        RAISE EXCEPTION 'options.lot_sizes holds nse_mktlots rows; they are never deleted, so this migration cannot be reverted';
                    END IF;
                END
                $do$
                """);
            migrationBuilder.Sql("ALTER TABLE options.lot_sizes DROP CONSTRAINT ck_lot_sizes_source");
            migrationBuilder.Sql("ALTER TABLE options.lot_sizes ADD CONSTRAINT ck_lot_sizes_source CHECK (source IN ('circular', 'secondary', 'scrip_master'))");
        }
    }
}
