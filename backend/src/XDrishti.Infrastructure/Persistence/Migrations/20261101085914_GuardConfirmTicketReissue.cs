using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The database half of the P12-03 Soft-only alignment (ADR 0166 amendment 2026-11-01). The trigger function <c>execution.check_confirm_ticket</c> is rewritten text for text (its text is taken from the catalog and only the named
    /// phrases are replaced; the migration fails if a phrase is not found):
    /// <list type="bullet">
    /// <item>A second card follows only a first one refused for a reason other than <c>intent_rejected</c>: a card the rejection step closed because its intent was rejected is final, even while a failed move leaves the intent in
    /// <c>checking</c>.</item>
    /// <item>The intent's state is read <c>FOR SHARE</c>, so a card insert waits for a move that holds the state row (the event chain locks it <c>FOR UPDATE</c>) and then reads the state after it; the insert runs only at READ COMMITTED (at a
    /// higher isolation the read after the lock would use the transaction's first snapshot), as in ADR 0174.</item>
    /// <item>The function's search_path is pinned to <c>pg_catalog, pg_temp</c> (every name in it is schema-qualified or a catalog built-in).</item>
    /// </list>
    /// Down fails closed: it refuses while an intent is still in <c>checking</c> with a newest card refused <c>intent_rejected</c>, because the old function would let a second card follow it. Nothing here can send an order.
    /// </summary>
    public partial class GuardConfirmTicketReissue : Migration
    {
        private const string Function = "execution.check_confirm_ticket()";

        /// <summary>The first step of Down. Public for its test.</summary>
        public const string DownFailClosed = """
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM execution.intents i
                      JOIN execution.intent_state st ON st.intent_id = i.id AND st.state = 'checking'
                      JOIN LATERAL (SELECT k.id FROM execution.confirm_tickets k WHERE k.intent_public_id = i.public_id ORDER BY k.attempt DESC LIMIT 1) t ON true
                      JOIN execution.ticket_outcomes o ON o.ticket_id = t.id AND o.outcome = 'refused' AND o.reason = 'intent_rejected') THEN
                    RAISE EXCEPTION 'GuardConfirmTicketReissue: an intent in checking has a card closed intent_rejected; rolling back would let a second card follow it. Finish or cancel those intents first.';
                END IF;
            END $$
            """;

        private const string ReadCommittedOnly =
            "IF pg_catalog.current_setting('transaction_isolation') <> 'read committed' THEN RAISE EXCEPTION 'execution.isolation_refused: a card is written only at READ COMMITTED, so the intent''s state is read after its lock' USING ERRCODE = '23514'; END IF;";

        private static readonly (string From, string To)[] Rewrites =
        [
            ("SELECT st.state INTO s FROM execution.intent_state st WHERE st.intent_id = i.id;",
                ReadCommittedOnly + " SELECT st.state INTO s FROM execution.intent_state st WHERE st.intent_id = i.id FOR SHARE;"),
            ("SELECT o.outcome INTO prior FROM execution.confirm_tickets t",
                "SELECT CASE WHEN o.reason = 'intent_rejected' THEN 'intent_rejected' ELSE o.outcome END INTO prior FROM execution.confirm_tickets t"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (from, to) in Rewrites)
            {
                migrationBuilder.Sql(Rewrite(from, to));
            }

            migrationBuilder.Sql($"ALTER FUNCTION {Function} SET search_path = pg_catalog, pg_temp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DownFailClosed);
            migrationBuilder.Sql($"ALTER FUNCTION {Function} RESET search_path");
            foreach (var (from, to) in Enumerable.Reverse(Rewrites))
            {
                migrationBuilder.Sql(Rewrite(to, from));
            }
        }

        // Replaces one phrase in the stored text of the function and re-creates it; refuses when the phrase is missing, so a function changed by a later migration is never rewritten blind.
        private static string Rewrite(string from, string to)
        {
            var f = from.Replace("'", "''", StringComparison.Ordinal);
            var t = to.Replace("'", "''", StringComparison.Ordinal);
            return $"""
                DO $$
                DECLARE d text;
                BEGIN
                    d := pg_get_functiondef('{Function}'::regprocedure);
                    IF position('{f}' IN d) = 0 THEN
                        RAISE EXCEPTION 'GuardConfirmTicketReissue: % does not contain the expected text', '{Function}';
                    END IF;
                    EXECUTE replace(d, '{f}', '{t}');
                END $$
                """;
        }
    }
}
