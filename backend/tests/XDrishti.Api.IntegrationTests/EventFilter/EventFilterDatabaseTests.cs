using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Selection;
using XDrishti.Domain.Selection;

namespace XDrishti.Api.IntegrationTests.EventFilter;

/// <summary>
/// P15-06 criteria 8 and 9 on a real TimescaleDB: the check constraints of <c>filings.event_filter_decisions</c> (the owner and auto shapes, the window, the approval, the reason), the trigger (the database clock, the limits of the automatic path under an advisory lock), the effective value
/// from the newest row, and a cross-check of the trigger against the pure rule (the owner's OFF included) <see cref="EventFilterAutoDecision"/> on every state and both actions, proved not vacuous by two mutants of the rule that must disagree with the database.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class EventFilterDatabaseTests(ApiFactory factory) : DataTestBase(factory)
{
    private const string OwnerActor = "owner:hem@example.invalid";

    private const string AutoActor = "auto:event-filter";

    private static string ConfirmedKeep(int days) => "{\"verdict.state\": \"KEEP\", \"verdict.hold_out_confirmed\": true, \"verdict.results_within_days\": " + days + "}";

    private const string Insert = "INSERT INTO filings.event_filter_decisions (enabled, results_within_days, approval, decided_by, actor, reason, setting_snapshot, verdict_ref) VALUES (@e, @d, @a, @by, @actor, 'r', @snap::jsonb, @v)";

    private async Task<string?> TryAsync(bool enabled, int days, string approval, string decidedBy, string actor, string? verdict, string? reason = null, string? snapshotOverride = null)
    {
        await using var connection = await OpenAsync();
        try
        {
            var sql = reason is null ? Insert : Insert.Replace("'r'", "@reason", StringComparison.Ordinal);
            var snapshot = snapshotOverride ?? (decidedBy == "auto" && enabled ? ConfirmedKeep(days) : "{}");
            var parameters = new List<(string, object)> { ("e", enabled), ("d", (short)days), ("a", approval), ("by", decidedBy), ("actor", actor), ("v", (object?)verdict ?? DBNull.Value), ("snap", snapshot) };
            if (reason is not null)
            {
                parameters.Add(("reason", reason));
            }

            await ExecuteAsync(connection, sql, [.. parameters]);
            return null;
        }
        catch (PostgresException failure)
        {
            return failure.SqlState + ":" + failure.MessageText;
        }
    }

    private Task<string?> OwnerAsync(bool enabled, string approval = "manual", int days = 2) => TryAsync(enabled, days, approval, "owner", OwnerActor, null);

    private Task<string?> AutoAsync(bool enabled, string approval = "auto", int days = 2) => TryAsync(enabled, days, approval, "auto", AutoActor, "pf-1");

    private async Task ResetAsync()
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "TRUNCATE filings.event_filter_decisions RESTART IDENTITY");
    }

    [Theory]
    [InlineData("owner", "auto:event-filter", null)]
    [InlineData("owner", "owner:x@example.invalid", "pf-1")]
    [InlineData("owner", "someone", null)]
    [InlineData("auto", "owner:x@example.invalid", "pf-1")]
    [InlineData("auto", "auto:event-filter", null)]
    [InlineData("auto", "auto:other", "pf-1")]
    [InlineData("robot", "auto:event-filter", "pf-1")]
    public async Task The_decided_by_actor_and_verdict_shapes_are_checked_by_the_database(string decidedBy, string actor, string? verdict)
    {
        await ResetAsync();

        var refused = await TryAsync(false, 2, "manual", decidedBy, actor, verdict);

        refused.ShouldNotBeNull();
        refused.ShouldStartWith("23514");
        (await ScalarAsync<long>("SELECT count(*) FROM filings.event_filter_decisions")).ShouldBe(0);
    }

    [Theory]
    [InlineData(-1, "manual")]
    [InlineData(11, "manual")]
    [InlineData(2, "other")]
    [InlineData(2, "AUTO")]
    public async Task The_window_and_the_approval_are_checked_by_the_database(int days, string approval)
    {
        await ResetAsync();

        (await TryAsync(false, days, approval, "owner", OwnerActor, null)).ShouldNotBeNull().ShouldStartWith("23514");
    }

    [Fact]
    public async Task The_window_ends_0_and_10_and_a_reason_of_300_characters_are_accepted_and_an_empty_or_longer_one_is_not()
    {
        await ResetAsync();

        (await OwnerAsync(false, days: 0)).ShouldBeNull();
        (await OwnerAsync(true, days: 10)).ShouldBeNull();
        (await TryAsync(false, 2, "manual", "owner", OwnerActor, null, new string('r', 300))).ShouldBeNull();
        (await TryAsync(false, 2, "manual", "owner", OwnerActor, null, "   ")).ShouldNotBeNull().ShouldStartWith("23514");
        (await TryAsync(false, 2, "manual", "owner", OwnerActor, null, new string('r', 301))).ShouldNotBeNull();
    }

    [Fact]
    public async Task The_database_stamps_the_time_and_a_time_from_the_caller_is_not_a_column_it_can_set_to_the_past()
    {
        await ResetAsync();
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "INSERT INTO filings.event_filter_decisions (enabled, results_within_days, approval, decided_by, actor, reason, setting_snapshot, changed_at) VALUES (false, 2, 'manual', 'owner', 'owner:x@example.invalid', 'r', '{}', '2001-01-01T00:00:00Z')");
        }

        (await ScalarAsync<bool>("SELECT changed_at > now() - interval '1 minute' FROM filings.event_filter_decisions")).ShouldBeTrue();
    }

    [Fact]
    public async Task An_automatic_row_needs_approval_auto_in_force_and_changes_neither_the_window_nor_the_approval()
    {
        await ResetAsync();
        (await AutoAsync(true)).ShouldNotBeNull().ShouldContain("filings.event_filter_refused", Case.Sensitive);   // no row: manual
        await OwnerAsync(false, "manual");
        (await AutoAsync(true)).ShouldNotBeNull().ShouldContain("filings.event_filter_refused", Case.Sensitive);   // approval manual in force
        await OwnerAsync(false, "auto", 4);
        (await AutoAsync(true, days: 5)).ShouldNotBeNull().ShouldContain("filings.event_filter_refused", Case.Sensitive);   // the window is the owner's
        (await AutoAsync(true, "manual", 4)).ShouldNotBeNull().ShouldContain("filings.event_filter_refused", Case.Sensitive);   // so is the approval
        (await AutoAsync(true, days: 4)).ShouldBeNull();

        (await ScalarAsync<string>("SELECT string_agg(decided_by || ':' || enabled::text, ',' ORDER BY id) FROM filings.event_filter_decisions")).ShouldBe("owner:false,owner:false,auto:true");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"verdict.state\": \"KEEP\"}")]
    [InlineData("{\"verdict.state\": \"KEEP\", \"verdict.hold_out_confirmed\": false, \"verdict.results_within_days\": 2}")]
    [InlineData("{\"verdict.state\": \"KEEP\", \"verdict.hold_out_confirmed\": true}")]
    [InlineData("{\"verdict.state\": \"DROP\", \"verdict.hold_out_confirmed\": true, \"verdict.results_within_days\": 2}")]
    public async Task An_automatic_turn_on_without_a_hold_out_confirmed_keep_in_its_snapshot_is_refused_like_the_pure_rule_refuses_it(string snapshot)
    {
        await ResetAsync();
        (await OwnerAsync(false, "auto")).ShouldBeNull();

        var refused = await TryAsync(true, 2, "auto", "auto", AutoActor, "pf-1", snapshotOverride: snapshot);
        var rule = EventFilterAutoDecision.Decide(new EventFilterVerdict("KEEP", "evf-1", "pf-1", false, 2), new EventFilterState(false, 2, "auto", "owner"), "evf-1");

        refused.ShouldNotBeNull().ShouldStartWith("23514:");
        refused.ShouldContain("hold-out confirmed");
        rule.ShouldBe(new EventFilterAutoDecisionResult(EventFilterAutoAction.None, "keep_not_confirmed", null));
        (await AutoAsync(true)).ShouldBeNull();
    }

    [Fact]
    public async Task An_automatic_turn_on_in_a_window_other_than_its_verdicts_is_refused_by_the_trigger_and_by_the_rule()
    {
        await ResetAsync();
        (await OwnerAsync(false, "auto", days: 3)).ShouldBeNull();

        var refused = await TryAsync(true, 3, "auto", "auto", AutoActor, "pf-1", snapshotOverride: ConfirmedKeep(2));
        var rule = EventFilterAutoDecision.Decide(new EventFilterVerdict("KEEP", "evf-1", "pf-1", true, 2), new EventFilterState(false, 3, "auto", "owner"), "evf-1");

        refused.ShouldNotBeNull().ShouldStartWith("23514:");
        rule.Reason.ShouldBe("verdict_window_mismatch");
        (await TryAsync(true, 3, "auto", "auto", AutoActor, "pf-1", snapshotOverride: ConfirmedKeep(3))).ShouldBeNull();
    }

    [Fact]
    public async Task The_trigger_agrees_with_the_pure_rule_on_every_state_and_both_actions_and_two_mutants_of_the_rule_disagree()
    {
        var states = new (string Name, Func<Task> Setup, EventFilterState State)[]
        {
            ("no row", ResetAsync, EventFilterState.Off),
            ("owner off manual", async () => { await ResetAsync(); await OwnerAsync(false, "manual"); }, new EventFilterState(false, 2, "manual", "owner")),
            ("owner off auto", async () => { await ResetAsync(); await OwnerAsync(false, "auto"); }, new EventFilterState(false, 2, "auto", "owner")),
            ("owner on manual", async () => { await ResetAsync(); await OwnerAsync(true, "manual"); }, new EventFilterState(true, 2, "manual", "owner")),
            ("owner on auto", async () => { await ResetAsync(); await OwnerAsync(true, "auto"); }, new EventFilterState(true, 2, "auto", "owner")),
            ("auto on", async () => { await ResetAsync(); await OwnerAsync(false, "auto"); await AutoAsync(true); }, new EventFilterState(true, 2, "auto", "auto")),
            ("auto off", async () => { await ResetAsync(); await OwnerAsync(false, "auto"); await AutoAsync(true); await AutoAsync(false); }, new EventFilterState(false, 2, "auto", "auto")),
            ("owner switched off", async () => { await ResetAsync(); await OwnerAsync(true, "auto"); await OwnerAsync(false, "auto"); }, new EventFilterState(false, 2, "auto", "owner", OwnerSwitchedOff: true)),
            ("owner on again after off", async () => { await ResetAsync(); await OwnerAsync(true, "auto"); await OwnerAsync(false, "auto"); await OwnerAsync(true, "auto"); }, new EventFilterState(true, 2, "auto", "owner")),
            ("owner re-saves off", async () => { await ResetAsync(); await OwnerAsync(true, "auto"); await OwnerAsync(false, "auto"); await OwnerAsync(false, "auto"); }, new EventFilterState(false, 2, "auto", "owner")),
        };

        // The pure rule's answer for "may the automatic path turn it on / off from this state": KEEP is the only verdict that turns on, DROP the one that turns off.
        static bool Real(EventFilterState s, bool turnOn) =>
            EventFilterAutoDecision.Decide(new EventFilterVerdict(turnOn ? "KEEP" : "DROP", "evf-1", "pf-1", true, 2), s, "evf-1").Action == (turnOn ? EventFilterAutoAction.TurnOn : EventFilterAutoAction.TurnOff);

        // Mutant 1 forgets the approval setting; mutant 2 lets the auto path turn off a filter the owner turned on.
        static bool NoApproval(EventFilterState s, bool turnOn) => turnOn ? !s.Enabled && !s.OwnerSwitchedOff : s.Enabled && s.DecidedBy == "auto";
        static bool OwnerOff(EventFilterState s, bool turnOn) => turnOn ? s.Approval == "auto" && !s.Enabled && !s.OwnerSwitchedOff : s.Approval == "auto" && s.Enabled;
        static bool IgnoresOwnerOff(EventFilterState s, bool turnOn) => turnOn ? s.Approval == "auto" && !s.Enabled : s.Approval == "auto" && s.Enabled && s.DecidedBy == "auto";

        var real = new List<string>();
        var mutant1 = new List<string>();
        var mutant2 = new List<string>();
        var mutant3 = new List<string>();
        foreach (var (name, setup, state) in states)
        {
            foreach (var turnOn in new[] { true, false })
            {
                await setup();
                var accepted = await AutoAsync(turnOn, "auto") is null;
                var label = name + (turnOn ? " -> on" : " -> off");
                if (accepted != Real(state, turnOn))
                {
                    real.Add(label + ": database " + accepted + ", rule " + Real(state, turnOn));
                }

                if (accepted != NoApproval(state, turnOn))
                {
                    mutant1.Add(label);
                }

                if (accepted != OwnerOff(state, turnOn))
                {
                    mutant2.Add(label);
                }

                if (accepted != IgnoresOwnerOff(state, turnOn))
                {
                    mutant3.Add(label);
                }
            }
        }

        real.ShouldBeEmpty("the trigger and the pure rule must give the same answer on every state");
        mutant1.ShouldNotBeEmpty("a rule that forgets the approval is caught by the database");
        mutant2.ShouldNotBeEmpty("a rule that lets the auto path turn off an owner's filter is caught by the database");
        mutant3.ShouldBe(["owner switched off -> on"], "a rule that forgets the owner's OFF is caught by the database, and by that state alone");
    }

    [Fact]
    public async Task Two_automatic_turn_ons_at_once_leave_exactly_one_row_because_the_writers_are_serialised()
    {
        await ResetAsync();
        await OwnerAsync(false, "auto");

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => AutoAsync(true)));

        results.Count(r => r is null).ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM filings.event_filter_decisions WHERE decided_by = 'auto'")).ShouldBe(1);
    }

    [Fact]
    public async Task The_effective_value_is_the_newest_row_through_the_port_and_the_port_reads_what_the_runtime_role_wrote()
    {
        await ResetAsync();
        using var scope = Factory.Services.CreateScope();
        var decisions = scope.ServiceProvider.GetRequiredService<IEventFilterDecisions>();
        var sw = scope.ServiceProvider.GetRequiredService<IEventFilterSwitch>();

        (await sw.CurrentAsync(Ct)).ShouldBe(EventFilterState.Off);
        var (id, refused) = await decisions.AppendAsync(new NewEventFilterRow(true, 6, "manual", "owner", OwnerActor, "r", "{\"a\":1}", null), Ct);
        var (autoId, autoRefused) = await decisions.AppendAsync(new NewEventFilterRow(true, 6, "auto", "auto", AutoActor, "r", "{}", "pf-1"), Ct);

        (id, refused).ShouldBe((1L, null));
        autoId.ShouldBeNull();
        autoRefused.ShouldNotBeNull().ShouldContain("approval auto");
        (await sw.CurrentAsync(Ct)).ShouldBe(new EventFilterState(true, 6, "manual", "owner"));
        var rows = await decisions.RowsAsync(10, Ct);
        rows.ShouldHaveSingleItem().ChangedAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1));
        rows[0].SettingSnapshot.Replace(" ", string.Empty, StringComparison.Ordinal).ShouldBe("{\"a\":1}");
        string.Create(CultureInfo.InvariantCulture, $"{rows[0].Id}").ShouldBe("1");
    }
}
