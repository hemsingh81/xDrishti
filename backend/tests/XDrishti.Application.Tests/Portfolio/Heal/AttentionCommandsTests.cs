using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Attention;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.Heal;

/// <summary>
/// The one list of the commands a notice may carry (ADR 0030 §6): the deriver builds its targets only from it, every command the deriver can emit is on the list, and nothing on the list is dead - so the list the client is generated from is neither too short (a button that
/// does nothing) nor too long (a command nobody can emit).
/// </summary>
public sealed class AttentionCommandsTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly Guid Account = new("00000000-0000-4000-8000-000000000001");

    [Fact]
    public void The_list_names_the_three_commands_with_whole_path_patterns()
    {
        AttentionCommands.Allowed.Select(c => (c.Name, c.Method, c.Template)).ShouldBe(
        [
            ("holdings_sync", "POST", "/api/accounts/{account}/holdings/sync"),
            ("broker_history_sync", "POST", "/api/accounts/{account}/sync/broker-history"),
            ("service_run", "POST", "/api/services/{service}/run"),
        ]);
        AttentionCommands.Allowed.ShouldAllBe(c => c.Pattern.StartsWith("^/api/", StringComparison.Ordinal) && c.Pattern.EndsWith('$'));
    }

    [Fact]
    public void The_builders_give_the_target_text_of_each_command()
    {
        AttentionCommands.HoldingsSyncOf(Account).ShouldBe("POST /api/accounts/00000000-0000-4000-8000-000000000001/holdings/sync");
        AttentionCommands.BrokerHistorySyncOf(Account).ShouldBe("POST /api/accounts/00000000-0000-4000-8000-000000000001/sync/broker-history");
        AttentionCommands.ServiceRunOf("holdings-quotes").ShouldBe("POST /api/services/holdings-quotes/run");
    }

    [Theory]
    [InlineData("")]
    [InlineData("../admin")]
    [InlineData("a/b")]
    [InlineData("a%2fb")]
    [InlineData("a b")]
    [InlineData("-leading-hyphen")]
    public void A_service_id_that_is_not_letters_digits_and_hyphens_cannot_be_built(string id)
    {
        Should.Throw<ArgumentException>(() => AttentionCommands.ServiceRunOf(id));
    }

    [Fact]
    public void The_targets_the_builders_give_are_allowed()
    {
        AttentionCommands.IsAllowed(AttentionCommands.HoldingsSyncOf(Account)).ShouldBeTrue();
        AttentionCommands.IsAllowed(AttentionCommands.BrokerHistorySyncOf(Account)).ShouldBeTrue();
        AttentionCommands.IsAllowed(AttentionCommands.ServiceRunOf("holdings-quotes")).ShouldBeTrue();
    }

    [Theory]
    [InlineData("GET /api/accounts")]
    [InlineData("POST https://example.org/api/accounts")]
    [InlineData("POST /api/../admin")]
    [InlineData("POST /api/accounts/not-a-guid/holdings/sync")]
    [InlineData("POST /api/accounts/00000000-0000-4000-8000-000000000001/holdings/sync/extra")]
    [InlineData("POST /api/accounts/00000000-0000-4000-8000-000000000001/holdings/sync?confirm=true")]
    [InlineData("POST /api/accounts/00000000-0000-4000-8000-000000000001/%2e%2e/sync/broker-history")]
    [InlineData("PUT /api/services/holdings-quotes/run")]
    [InlineData("POST /api/services//run")]
    [InlineData("POST /api/services/a%2fb/run")]
    [InlineData("POST /api/services/holdings-quotes/run\n")]
    [InlineData("")]
    public void Anything_else_is_not_allowed(string target)
    {
        AttentionCommands.IsAllowed(target).ShouldBeFalse(target);
    }

    [Fact]
    public async Task Every_command_the_deriver_can_emit_is_on_the_list_and_every_command_on_the_list_is_emitted()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 12, 17, 0, 0, TimeSpan.FromMinutes(330)));
        var facts = new StaticAttentionFacts(clock);
        var account = facts.Add("Example One");
        facts.DataAccountId = account.Id;
        facts.DataAccountName = "Example One";
        foreach (var kind in new[] { SyncHealthKind.Holdings, SyncHealthKind.Trades, SyncHealthKind.Ledger, SyncHealthKind.Quotes })
        {
            facts.Health[(account.Id, kind)] = new SyncHealth(account.Id, kind, 3, "rejected", clock.GetUtcNow().AddHours(-1), 4, null, false, null, 0);
        }

        var items = AttentionDeriver.Derive(await facts.LoadAsync(Ct), new FixedHealSettings());
        var targets = items.SelectMany(i => i.Actions).Where(a => a.Kind == AttentionActionKinds.Command).Select(a => a.Target).ToList();

        targets.Count.ShouldBe(4); // holdings, trades, ledger, prices
        targets.ShouldAllBe(t => AttentionCommands.IsAllowed(t));
        foreach (var command in AttentionCommands.Allowed)
        {
            targets.Any(t => t.StartsWith(command.Method + " ", StringComparison.Ordinal) && System.Text.RegularExpressions.Regex.IsMatch(t[(command.Method.Length + 1)..], command.Pattern)).ShouldBeTrue(command.Name);
        }
    }
}
