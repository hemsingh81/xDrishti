using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDefaultPortfolioBuckets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // P1-16e. Structure first (new columns, links), then the existing data, then the old structure goes.
            migrationBuilder.AlterColumn<string>(
                name: "name",
                schema: "accounts",
                table: "portfolio_buckets",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "archived_at",
                schema: "accounts",
                table: "portfolio_buckets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "default_for_account_id",
                schema: "accounts",
                table: "portfolio_buckets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "default_bucket_id",
                schema: "accounts",
                table: "portfolio_settings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_portfolio_settings_default_bucket_id",
                schema: "accounts",
                table: "portfolio_settings",
                column: "default_bucket_id");

            migrationBuilder.CreateIndex(
                name: "ux_portfolio_buckets_default_for_account",
                schema: "accounts",
                table: "portfolio_buckets",
                column: "default_for_account_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_portfolio_buckets_broker_accounts_default_for_account_id",
                schema: "accounts",
                table: "portfolio_buckets",
                column: "default_for_account_id",
                principalSchema: "accounts",
                principalTable: "broker_accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_portfolio_settings_portfolio_buckets_default_bucket_id",
                schema: "accounts",
                table: "portfolio_settings",
                column: "default_bucket_id",
                principalSchema: "accounts",
                principalTable: "portfolio_buckets",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // 1. Every active account that has the Portfolio role gets its default bucket "{account name} · Portfolio" (a taken name gets " 2", " 3", …).
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    r record;
                    base_name text;
                    candidate text;
                    n integer;
                BEGIN
                    FOR r IN
                        SELECT a.id, a.name
                        FROM accounts.broker_accounts a
                        JOIN accounts.portfolio_settings p ON p.account_id = a.id
                        WHERE a.archived_at IS NULL AND p.enabled
                        ORDER BY a.created_at, a.id
                    LOOP
                        base_name := r.name || ' · Portfolio';
                        candidate := base_name;
                        n := 1;
                        WHILE EXISTS (SELECT 1 FROM accounts.portfolio_buckets b WHERE lower(b.name) = lower(candidate)) LOOP
                            n := n + 1;
                            candidate := base_name || ' ' || n;
                        END LOOP;
                        INSERT INTO accounts.portfolio_buckets (id, name, created_at, default_for_account_id)
                        VALUES (gen_random_uuid(), candidate, now(), r.id);
                        INSERT INTO accounts.account_events (id, at, account_id, event, detail, actor)
                        VALUES (gen_random_uuid(), now(), r.id, 'BucketCreated', 'Default portfolio bucket ''' || candidate || ''' created', 'migration');
                    END LOOP;
                END $$;
                """);

            // 2. The default bucket moves from a name to a reference, for custom buckets only (NULL means "the account's own default bucket").
            //    An account that had one of the three presets (Trading, MyLongTerm, MyChoice) now uses its own default bucket; that is audited for active
            //    accounts with the Portfolio role (the others are re-pointed silently: their role is off or they are archived).
            migrationBuilder.Sql("""
                INSERT INTO accounts.account_events (id, at, account_id, event, detail, actor)
                SELECT gen_random_uuid(), now(), p.account_id, 'PortfolioUpdated',
                       'Preset bucket ''' || p.default_bucket || ''' was removed; imports now go to the account''s own bucket', 'migration'
                FROM accounts.portfolio_settings p
                JOIN accounts.broker_accounts a ON a.id = p.account_id
                JOIN accounts.portfolio_buckets b ON b.name = p.default_bucket
                WHERE p.enabled AND a.archived_at IS NULL
                  AND b.id IN ('00000000-0000-7000-8000-000000000101', '00000000-0000-7000-8000-000000000102', '00000000-0000-7000-8000-000000000103');
                UPDATE accounts.portfolio_settings p
                SET default_bucket_id = b.id
                FROM accounts.portfolio_buckets b
                WHERE b.name = p.default_bucket
                  AND b.id NOT IN ('00000000-0000-7000-8000-000000000101', '00000000-0000-7000-8000-000000000102', '00000000-0000-7000-8000-000000000103');
                """);

            // 3. The old name-based reference and the history date go.
            migrationBuilder.DropForeignKey(
                name: "fk_portfolio_settings_portfolio_buckets_default_bucket",
                schema: "accounts",
                table: "portfolio_settings");

            migrationBuilder.DropIndex(
                name: "ix_portfolio_settings_default_bucket",
                schema: "accounts",
                table: "portfolio_settings");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_portfolio_buckets_name",
                schema: "accounts",
                table: "portfolio_buckets");

            migrationBuilder.DropIndex(
                name: "ix_portfolio_buckets_name",
                schema: "accounts",
                table: "portfolio_buckets");

            migrationBuilder.DropColumn(
                name: "default_bucket",
                schema: "accounts",
                table: "portfolio_settings");

            migrationBuilder.DropColumn(
                name: "history_from",
                schema: "accounts",
                table: "portfolio_settings");

            // 4. The presets are deleted when nothing references them (nothing does after step 2).
            migrationBuilder.Sql("""
                DELETE FROM accounts.portfolio_buckets b
                WHERE b.id IN ('00000000-0000-7000-8000-000000000101', '00000000-0000-7000-8000-000000000102', '00000000-0000-7000-8000-000000000103')
                  AND NOT EXISTS (SELECT 1 FROM accounts.portfolio_settings p WHERE p.default_bucket_id = b.id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_portfolio_buckets_broker_accounts_default_for_account_id",
                schema: "accounts",
                table: "portfolio_buckets");

            migrationBuilder.DropForeignKey(
                name: "fk_portfolio_settings_portfolio_buckets_default_bucket_id",
                schema: "accounts",
                table: "portfolio_settings");

            migrationBuilder.DropIndex(
                name: "ix_portfolio_settings_default_bucket_id",
                schema: "accounts",
                table: "portfolio_settings");

            migrationBuilder.DropIndex(
                name: "ux_portfolio_buckets_default_for_account",
                schema: "accounts",
                table: "portfolio_buckets");

            migrationBuilder.AddColumn<string>(
                name: "default_bucket",
                schema: "accounts",
                table: "portfolio_settings",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "history_from",
                schema: "accounts",
                table: "portfolio_settings",
                type: "date",
                nullable: true);

            // Custom bucket choices go back to their name; the default buckets of accounts cannot exist in the old shape (name length, no link) and are removed.
            migrationBuilder.Sql("""
                UPDATE accounts.portfolio_settings p SET default_bucket = b.name FROM accounts.portfolio_buckets b WHERE b.id = p.default_bucket_id AND b.default_for_account_id IS NULL;
                DELETE FROM accounts.portfolio_buckets WHERE default_for_account_id IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "default_bucket_id",
                schema: "accounts",
                table: "portfolio_settings");

            migrationBuilder.DropColumn(
                name: "archived_at",
                schema: "accounts",
                table: "portfolio_buckets");

            migrationBuilder.DropColumn(
                name: "default_for_account_id",
                schema: "accounts",
                table: "portfolio_buckets");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                schema: "accounts",
                table: "portfolio_buckets",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_portfolio_buckets_name",
                schema: "accounts",
                table: "portfolio_buckets",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ix_portfolio_settings_default_bucket",
                schema: "accounts",
                table: "portfolio_settings",
                column: "default_bucket");

            migrationBuilder.CreateIndex(
                name: "ix_portfolio_buckets_name",
                schema: "accounts",
                table: "portfolio_buckets",
                column: "name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_portfolio_settings_portfolio_buckets_default_bucket",
                schema: "accounts",
                table: "portfolio_settings",
                column: "default_bucket",
                principalSchema: "accounts",
                principalTable: "portfolio_buckets",
                principalColumn: "name",
                onDelete: ReferentialAction.Restrict);

            // The presets of the first version (P1-16) come back, as they were seeded.
            migrationBuilder.Sql("""
                INSERT INTO accounts.portfolio_buckets (id, name, created_at)
                VALUES
                    ('00000000-0000-7000-8000-000000000101', 'Trading', now()),
                    ('00000000-0000-7000-8000-000000000102', 'MyLongTerm', now()),
                    ('00000000-0000-7000-8000-000000000103', 'MyChoice', now())
                ON CONFLICT DO NOTHING;
                """);
        }
    }
}
