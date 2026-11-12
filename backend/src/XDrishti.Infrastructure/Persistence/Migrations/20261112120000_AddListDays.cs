using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddListDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DATA-01, ADR 0274: the dated rows of the back-filled F&O-ban list. A day is one coverage row (an empty ban list is a covered day with no member rows) plus one member row per banned instrument.
            // Raw SQL on purpose: nothing maps these tables in the EF model. Insert-only for the runtime role: SELECT and INSERT, never UPDATE or DELETE.
            migrationBuilder.Sql("""
                CREATE TABLE market.list_day_coverage (
                    basket_id uuid NOT NULL,
                    trade_date date NOT NULL,
                    source varchar(10) NOT NULL,
                    member_count integer NOT NULL,
                    loaded_at timestamptz NOT NULL,
                    CONSTRAINT pk_list_day_coverage PRIMARY KEY (basket_id, trade_date),
                    CONSTRAINT fk_list_day_coverage_baskets FOREIGN KEY (basket_id) REFERENCES market.baskets (id),
                    CONSTRAINT ck_list_day_coverage_source CHECK (source IN ('archive', 'daily')),
                    CONSTRAINT ck_list_day_coverage_count CHECK (member_count >= 0)
                )
                """);
            migrationBuilder.Sql("""
                CREATE TABLE market.list_day_members (
                    basket_id uuid NOT NULL,
                    trade_date date NOT NULL,
                    instrument_id uuid NOT NULL,
                    CONSTRAINT pk_list_day_members PRIMARY KEY (basket_id, trade_date, instrument_id),
                    CONSTRAINT fk_list_day_members_day FOREIGN KEY (basket_id, trade_date) REFERENCES market.list_day_coverage (basket_id, trade_date),
                    CONSTRAINT fk_list_day_members_instruments FOREIGN KEY (instrument_id) REFERENCES market.instruments (id)
                )
                """);
            migrationBuilder.GrantTableToAppRole("market", "list_day_coverage");
            migrationBuilder.GrantTableToAppRole("market", "list_day_members");
            migrationBuilder.MakeAppendOnlyForAppRole("market", "list_day_coverage");
            migrationBuilder.MakeAppendOnlyForAppRole("market", "list_day_members");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS market.list_day_members");
            migrationBuilder.Sql("DROP TABLE IF EXISTS market.list_day_coverage");
        }
    }
}
