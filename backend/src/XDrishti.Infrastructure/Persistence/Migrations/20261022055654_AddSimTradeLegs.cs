using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSimTradeLegs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sim_trade_leg_charges",
                schema: "research",
                columns: table => new
                {
                    run_id = table.Column<long>(type: "bigint", nullable: false),
                    open_at_end = table.Column<bool>(type: "boolean", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    charge_ordinal = table.Column<int>(type: "integer", nullable: false),
                    epoch_seconds = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<long>(type: "bigint", nullable: false),
                    turnover = table.Column<decimal>(type: "numeric", nullable: false),
                    brokerage = table.Column<decimal>(type: "numeric", nullable: false),
                    stt = table.Column<decimal>(type: "numeric", nullable: false),
                    exchange_txn = table.Column<decimal>(type: "numeric", nullable: false),
                    sebi_fee = table.Column<decimal>(type: "numeric", nullable: false),
                    stamp_duty = table.Column<decimal>(type: "numeric", nullable: false),
                    other_charges = table.Column<decimal>(type: "numeric", nullable: false),
                    dp_charge = table.Column<decimal>(type: "numeric", nullable: false),
                    gst = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sim_trade_leg_charges", x => new { x.run_id, x.open_at_end, x.ordinal, x.charge_ordinal });
                    table.CheckConstraint("ck_sim_trade_leg_charges_ordinals", "ordinal >= 0 AND charge_ordinal >= 0");
                    table.CheckConstraint("ck_sim_trade_leg_charges_quantity", "quantity >= 0");
                    table.ForeignKey(
                        name: "fk_sim_trade_leg_charges_trade",
                        columns: x => new { x.run_id, x.open_at_end, x.ordinal },
                        principalSchema: "research",
                        principalTable: "sim_trades",
                        principalColumns: new[] { "run_id", "open_at_end", "ordinal" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sim_trade_legs",
                schema: "research",
                columns: table => new
                {
                    run_id = table.Column<long>(type: "bigint", nullable: false),
                    open_at_end = table.Column<bool>(type: "boolean", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    leg_ordinal = table.Column<int>(type: "integer", nullable: false),
                    epoch_seconds = table.Column<long>(type: "bigint", nullable: false),
                    price = table.Column<double>(type: "double precision", nullable: false),
                    filled_price = table.Column<double>(type: "double precision", nullable: false),
                    share = table.Column<decimal>(type: "numeric", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sim_trade_legs", x => new { x.run_id, x.open_at_end, x.ordinal, x.leg_ordinal });
                    table.CheckConstraint("ck_sim_trade_legs_kind", "kind IN ('entry', 'add', 'exit')");
                    table.CheckConstraint("ck_sim_trade_legs_ordinals", "ordinal >= 0 AND leg_ordinal >= 0");
                    table.CheckConstraint("ck_sim_trade_legs_share", "share > 0");
                    table.ForeignKey(
                        name: "fk_sim_trade_legs_trade",
                        columns: x => new { x.run_id, x.open_at_end, x.ordinal },
                        principalSchema: "research",
                        principalTable: "sim_trades",
                        principalColumns: new[] { "run_id", "open_at_end", "ordinal" },
                        onDelete: ReferentialAction.Restrict);
                });

            // insert-only for the runtime role, like the tables they hang on (ADR 0045 D7; ResearchGrantsTests holds every research table to it)
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Research, "sim_trade_legs");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Research, "sim_trade_leg_charges");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sim_trade_leg_charges",
                schema: "research");

            migrationBuilder.DropTable(
                name: "sim_trade_legs",
                schema: "research");
        }
    }
}
