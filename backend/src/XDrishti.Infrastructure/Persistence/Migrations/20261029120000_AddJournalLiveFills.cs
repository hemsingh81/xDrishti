using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The journal can link a captured live fill (spec P11-01b, ADR 0154): <c>journal_fills.live_fill_id</c> beside a now-nullable <c>transaction_id</c>, exactly one of the two (check), each unique, so a fill is journaled once per source. The trigger that keeps a link
    /// on the account of its entry now reads the account from whichever source the link names, and a second trigger refuses the same trade journaled through both sources (a stored trade and its live twin: same account, identity <c>t:yyyyMMdd:trade id</c>).
    /// </summary>
    public partial class AddJournalLiveFills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_journal_fills_shape",
                schema: "trading",
                table: "journal_fills");

            migrationBuilder.AlterColumn<Guid>(
                name: "transaction_id",
                schema: "trading",
                table: "journal_fills",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "live_fill_id",
                schema: "trading",
                table: "journal_fills",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "uq_journal_fills_live_fill_id",
                schema: "trading",
                table: "journal_fills",
                column: "live_fill_id",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_journal_fills_shape",
                schema: "trading",
                table: "journal_fills",
                sql: "role IN ('entry', 'exit', 'discretionary') AND (role = 'discretionary') = (entry_id IS NULL) AND qty > 0 AND length(reason) > 0 AND (transaction_id IS NULL) <> (live_fill_id IS NULL)");

            migrationBuilder.Sql("ALTER TABLE trading.journal_fills ADD CONSTRAINT fk_journal_fills_live_fills_live_fill_id FOREIGN KEY (live_fill_id) REFERENCES trading.live_fills (id) ON DELETE RESTRICT");

            // The account of the link's source must be the account of its entry (the source tables are not ours to add a composite key to).
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION trading.journal_fill_same_account() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW.entry_id IS NOT NULL AND (SELECT account_id FROM trading.journal_entries WHERE id = NEW.entry_id) IS DISTINCT FROM
                       COALESCE((SELECT account_id FROM portfolio.transactions WHERE id = NEW.transaction_id), (SELECT account_id FROM trading.live_fills WHERE id = NEW.live_fill_id)) THEN
                        RAISE EXCEPTION 'a journal fill must belong to the account of its entry' USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END $$;
                """);

            // Never the same trade through both sources, in either order of arrival. A voided stored twin does not count (the reads ignore it too, so the live fill is then the only live record).
            // Shortcut: the check-then-insert is not serialized, so two concurrent journal runs could both pass; the journal runs as one service at a time, and the unique keys still hold per source.
            migrationBuilder.Sql("""
                CREATE FUNCTION trading.journal_fill_single_source() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW.live_fill_id IS NOT NULL AND EXISTS (
                        SELECT 1 FROM trading.live_fills l JOIN portfolio.transactions t ON t.account_id = l.account_id AND t.external_id = 't:' || to_char(l.trade_date, 'YYYYMMDD') || ':' || l.broker_trade_id
                        JOIN trading.journal_fills j ON j.transaction_id = t.id WHERE l.id = NEW.live_fill_id AND t.voided_at IS NULL) THEN
                        RAISE EXCEPTION 'this trade is already journaled through the stored trade' USING ERRCODE = 'unique_violation';
                    END IF;
                    IF NEW.transaction_id IS NOT NULL AND EXISTS (
                        SELECT 1 FROM portfolio.transactions t JOIN trading.live_fills l ON l.account_id = t.account_id AND t.external_id = 't:' || to_char(l.trade_date, 'YYYYMMDD') || ':' || l.broker_trade_id
                        JOIN trading.journal_fills j ON j.live_fill_id = l.id WHERE t.id = NEW.transaction_id AND t.voided_at IS NULL) THEN
                        RAISE EXCEPTION 'this trade is already journaled through the live fill' USING ERRCODE = 'unique_violation';
                    END IF;
                    RETURN NEW;
                END $$;
                """);
            migrationBuilder.Sql("CREATE TRIGGER trg_journal_fills_single_source BEFORE INSERT ON trading.journal_fills FOR EACH ROW EXECUTE FUNCTION trading.journal_fill_single_source()");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Unsafe once live links exist unless they are removed: this Down deletes the live-linked rows (the live fills themselves stay), since transaction_id must be NOT NULL again.
            migrationBuilder.Sql("DELETE FROM trading.journal_fills WHERE live_fill_id IS NOT NULL");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_journal_fills_single_source ON trading.journal_fills");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS trading.journal_fill_single_source()");
            migrationBuilder.Sql("ALTER TABLE trading.journal_fills DROP CONSTRAINT IF EXISTS fk_journal_fills_live_fills_live_fill_id");
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION trading.journal_fill_same_account() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW.entry_id IS NOT NULL AND (SELECT account_id FROM trading.journal_entries WHERE id = NEW.entry_id) IS DISTINCT FROM (SELECT account_id FROM portfolio.transactions WHERE id = NEW.transaction_id) THEN
                        RAISE EXCEPTION 'a journal fill must belong to the account of its entry' USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END $$;
                """);
            migrationBuilder.DropIndex(
                name: "uq_journal_fills_live_fill_id",
                schema: "trading",
                table: "journal_fills");

            migrationBuilder.DropCheckConstraint(
                name: "ck_journal_fills_shape",
                schema: "trading",
                table: "journal_fills");

            migrationBuilder.DropColumn(
                name: "live_fill_id",
                schema: "trading",
                table: "journal_fills");

            migrationBuilder.AlterColumn<Guid>(
                name: "transaction_id",
                schema: "trading",
                table: "journal_fills",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_journal_fills_shape",
                schema: "trading",
                table: "journal_fills",
                sql: "role IN ('entry', 'exit', 'discretionary') AND (role = 'discretionary') = (entry_id IS NULL) AND qty > 0 AND length(reason) > 0");
        }
    }
}
