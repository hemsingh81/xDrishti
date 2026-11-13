using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropDepthAndTickRecording : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR 0288: P13 (tick and depth features) is dropped and the two recorders are removed. Raw SQL on purpose, as in the two migrations that created these tables (nothing here is in the EF model, so the snapshot does not change).
            // Dropping a hypertable removes its chunks, its compression policy and its grants with it. The recorded rows go: both recorders were OFF by default and the data was never readable by any feature.
            migrationBuilder.Sql("DROP TABLE IF EXISTS market.depth_recorder_switch");
            migrationBuilder.Sql("DROP TABLE IF EXISTS market.depth_sessions");
            migrationBuilder.Sql("DROP TABLE IF EXISTS market.depth_session_instruments");
            migrationBuilder.Sql("DROP TABLE IF EXISTS market.depth_snapshots");
            migrationBuilder.Sql("DROP TABLE IF EXISTS market.tick_recorder_switch");
            migrationBuilder.Sql("DROP TABLE IF EXISTS market.tick_sessions");
            migrationBuilder.Sql("DROP TABLE IF EXISTS market.tick_session_instruments");
            migrationBuilder.Sql("DROP TABLE IF EXISTS market.trade_ticks");

            // The two recorders' service rows and heartbeats (the services screen would list an id nobody runs).
            migrationBuilder.Sql("DELETE FROM platform.service_runs WHERE service_id IN ('depth-recorder', 'tick-recorder')");
            migrationBuilder.Sql("DELETE FROM platform.service_schedules WHERE service_id IN ('depth-recorder', 'tick-recorder')");
            migrationBuilder.Sql("DELETE FROM platform.service_heartbeats WHERE service_name IN ('xd-depth', 'xd-ticks')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible: the dropped tables held recordings that are gone. The tables are created by AddDepthRecording and AddTickRecording if ever needed again.
        }
    }
}
