using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The switch of the shadow record is stamped in commit order (spec CF-08 part B, ADR 0212; the P7-12 pattern of <c>workflow.stamp_autonomy_setting</c>). Part A's stamp set only the time; the id came from the sequence before the commit, so two changes that
    /// raced could commit in the other order and the "newest row" (highest id) would not be the last change. The new function draws the id AFTER an exclusive advisory lock held to the end of the transaction, so id order is commit order. No table changes; an applied
    /// migration is not edited.
    /// </summary>
    public partial class StampShadowSettingInCommitOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION paper.stamp_shadow_setting() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    PERFORM pg_advisory_xact_lock(hashtextextended('paper.shadow_settings', 0));
                    NEW.id := nextval(pg_get_serial_sequence('paper.shadow_settings', 'id'));
                    NEW.changed_at := paper.shadow_now();
                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                        EXECUTE format('GRANT USAGE ON SEQUENCE %s TO xd_app', pg_get_serial_sequence('paper.shadow_settings', 'id'));
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                        EXECUTE format('REVOKE USAGE ON SEQUENCE %s FROM xd_app', pg_get_serial_sequence('paper.shadow_settings', 'id'));
                    END IF;
                END $$;
                """);
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION paper.stamp_shadow_setting() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    NEW.changed_at := paper.shadow_now();
                    RETURN NEW;
                END
                $fn$
                """);
        }
    }
}
